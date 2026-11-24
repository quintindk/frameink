"""Rebuild the Windows icon from its SVG. Requires FFmpeg with the librsvg decoder."""

import struct
import subprocess
from pathlib import Path


def main():
    assets = Path(__file__).resolve().parents[1] / "src" / "FrameInk" / "Assets"
    source = assets / "FrameInk.svg"
    sizes = (16, 20, 24, 32, 40, 48, 64, 128, 256)
    icon_header = struct.pack("<HHH", 0, 1, len(sizes))
    entries, frames = [], []
    offset = len(icon_header) + 16 * len(sizes)
    for size in sizes:
        png = subprocess.check_output([
            "ffmpeg", "-nostdin", "-v", "error", "-c:v", "librsvg",
            "-width", str(size), "-height", str(size), "-i", str(source),
            "-frames:v", "1", "-c:v", "png", "-pix_fmt", "rgba", "-threads", "1", "-f", "image2pipe", "pipe:1",
        ])
        if not png.startswith(b"\x89PNG\r\n\x1a\n") or struct.unpack_from(">II", png, 16) != (size, size):
            raise ValueError(f"Expected a {size}×{size} PNG from FFmpeg")
        edge = size % 256  # ICO represents 256 pixels with a byte value of zero.
        entries.append(struct.pack("<BBBBHHII", edge, edge, 0, 0, 1, 32, len(png), offset))
        frames.append(png)
        offset += len(png)
    (assets / "FrameInk.ico").write_bytes(icon_header + b"".join(entries + frames))


if __name__ == "__main__":
    main()
