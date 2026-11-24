using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;
using System.Windows.Interop;

namespace FrameInk;

internal sealed record Hotkey(bool Ctrl, bool Alt, bool Shift, string Key)
{
    public static readonly Hotkey Default = new(true, false, true, "F10");
    private static readonly string PathName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FrameInk", "hotkey.json");
    [JsonIgnore] public bool Valid => (Ctrl || Alt || Shift) && Array.IndexOf(Keys, Key) >= 0;
    [JsonIgnore] public uint Modifiers => 0x4000u | (Ctrl ? 2u : 0) | (Alt ? 1u : 0) | (Shift ? 4u : 0);
    [JsonIgnore] public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Enum.Parse<Key>(Key));
    public override string ToString() => $"{(Ctrl ? "Ctrl+" : "")}{(Alt ? "Alt+" : "")}{(Shift ? "Shift+" : "")}{Key}";
    internal static readonly string[] Keys = ["F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11"];

    public static Hotkey Load()
    {
        if (!File.Exists(PathName)) return Default;
        var value = JsonSerializer.Deserialize<Hotkey>(File.ReadAllText(PathName));
        return value is { Valid: true } ? value : throw new InvalidDataException("Invalid hotkey settings.");
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        string temp = PathName + "." + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(this));
            File.Move(temp, PathName, true);
        }
        finally { File.Delete(temp); }
    }

}
