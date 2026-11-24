"""Compare decoded synthetic smoke-test frames with the plain and mid-take PNGs."""

import argparse
import json
import subprocess


def streams(path):
    return json.loads(subprocess.check_output([
        "ffprobe", "-v", "error", "-show_streams", "-of", "json", path,
    ]))["streams"]


def decoder(path):
    return ["ffmpeg", "-nostdin", "-v", "error", "-i", path,
            "-map", "0:v:0", "-fps_mode", "passthrough", "-vf", "setpts=N", "-pix_fmt", "rgb24",
            "-threads", "1", "-f", "rawvideo", "pipe:1"]


def check(condition, message):
    if not condition:
        raise SystemExit(message)


def red(image, pixel):
    r, g, b = image[pixel:pixel + 3]
    return r > 155 and r - max(g, b) > 70


def verify(video, plain_path, mid_path):
    stream = streams(video)
    check(len(stream) == 1 and stream[0]["codec_name"] == "h264", "Expected one H.264 video stream and no audio.")
    w, h = stream[0]["width"], stream[0]["height"]
    duration = float(stream[0]["duration"])
    check(duration > 2, f"Take was too short: {duration}s.")
    frames = json.loads(subprocess.check_output([
        "ffprobe", "-v", "error", "-select_streams", "v:0", "-show_frames",
        "-show_entries", "frame=best_effort_timestamp_time", "-of", "json", video,
    ]))["frames"]
    times = [float(frame["best_effort_timestamp_time"]) for frame in frames]
    check(all(a < b for a, b in zip(times, times[1:])), "MP4 timestamps did not advance monotonically.")
    size = w * h * 3

    def png(path):
        image = streams(path)[0]
        check((image["width"], image["height"]) == (w, h), "PNG and video dimensions differ.")
        return subprocess.check_output(decoder(path))

    plain, mid = png(plain_path), png(mid_path)
    mask = [p for p in range(0, size, 3) if red(mid, p) and not red(plain, p)]
    check(len(mask) >= 100, "Mid-take PNG has no usable ink mask.")

    # Include every edge and sample across the interior. Compare against the clean PNG
    # everywhere except annotated pixels. Allow for the lossy H.264/NV12 conversion.
    edge = {3 * x for x in range(w)} | {3 * ((h - 1) * w + x) for x in range(w)}
    edge |= {3 * y * w for y in range(h)} | {3 * ((y + 1) * w - 1) for y in range(h)}
    interior = {3 * (y * w + x) for y in range(4, h, 11) for x in range(4, w, 13)}
    groups = [[p for p in group if plain[p:p + 3] == mid[p:p + 3]] for group in (edge, interior)]
    scores, worst = [], 0.0
    with subprocess.Popen(decoder(video), stdout=subprocess.PIPE) as player:
        while frame := player.stdout.read(size):
            check(len(frame) == size, "Short decoded frame.")
            for group in groups:
                mae = sum(abs(frame[p + c] - plain[p + c]) for p in group for c in range(3)) / (3 * len(group))
                worst = max(worst, mae)
            scores.append(sum(red(frame, p) for p in mask) / len(mask))
        check(player.wait() == 0, "FFmpeg did not decode the take through to the end.")

    check(len(scores) == len(times) and len(scores) >= 12 and worst < 16,
          f"Bad frame count or crop/edge mismatch: {len(scores)} frames, RGB error {worst:.2f}.")
    marked = [i for i, score in enumerate(scores) if score >= 0.75]
    check(len(marked) >= 2 and max(marked) < len(scores) - 2, "PNG ink was missing from the MP4 or there were no later frames.")
    check(all(score < 0.1 for score in scores[:2] + scores[-2:]), "Video did not show clean → marked → clean.")
    print(f"PASS {len(scores)} decoded frames, {w}x{h}, {duration:.2f}s, video only. "
          f"Worst crop/edge RGB MAE {worst:.2f}. PNG ink matched in {len(marked)} frames before the final clean frames.")


if __name__ == "__main__":
    args = argparse.ArgumentParser(description=__doc__)
    args.add_argument("video")
    args.add_argument("plain_png")
    args.add_argument("mid_take_png")
    opts = args.parse_args()
    verify(opts.video, opts.plain_png, opts.mid_take_png)
