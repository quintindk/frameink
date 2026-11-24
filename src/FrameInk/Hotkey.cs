using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
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
    private static readonly string[] Keys = ["F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11"];

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

    public static Hotkey? Prompt(Window owner, Hotkey current)
    {
        var panel = new StackPanel { Margin = new(16) };
        panel.Children.Add(new TextBlock { Text = "Global PNG shortcut. Pick at least one modifier." });
        var ctrl = new CheckBox { Content = "Ctrl", IsChecked = current.Ctrl };
        var alt = new CheckBox { Content = "Alt", IsChecked = current.Alt };
        var shift = new CheckBox { Content = "Shift", IsChecked = current.Shift };
        var keys = new ListBox { ItemsSource = Keys, SelectedItem = current.Key, Margin = new(0, 6, 0, 6) };
        var save = new Button { Content = "Apply", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new(0, 4, 0, 0) };
        foreach (var element in new UIElement[] { ctrl, alt, shift, keys, save, cancel }) panel.Children.Add(element);
        var dialog = new Window { Owner = owner, Title = "FrameInk PNG shortcut", Content = panel, ShowInTaskbar = false,
            SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var currentValue = () => new Hotkey(ctrl.IsChecked == true, alt.IsChecked == true, shift.IsChecked == true, (string)keys.SelectedItem);
        foreach (var box in new[] { ctrl, alt, shift }) box.Click += (_, _) => save.IsEnabled = currentValue().Valid;
        save.Click += (_, _) => dialog.DialogResult = true;
        // Exclude the modal itself so the desktop's close animation cannot appear in the next PNG.
        _ = new WindowInteropHelper(dialog).EnsureHandle();
        try { Native.ExcludeFromCapture(dialog); }
        catch { dialog.Close(); throw; }
        return dialog.ShowDialog() == true ? currentValue() : null;
    }
}
