using System;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace FrameInk;

internal static class SettingsWindow
{
    internal sealed record Values(string Folder, Hotkey Shortcut);

    public static Values? Prompt(Window owner, Hotkey shortcut, string saveFolder)
    {
        var panel = new StackPanel { Margin = new(22), Width = 440 };
        var dialog = new Window { Owner = owner, Title = "FrameInk Settings", Content = panel, ShowInTaskbar = false,
            SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterScreen };

        panel.Children.Add(Heading("Save location"));
        panel.Children.Add(new TextBlock { Text = "PNG and MP4 files are saved to this folder.", Margin = new(0, 4, 0, 8) });
        var folder = new TextBox { Text = saveFolder, Padding = new(5) };
        AutomationProperties.SetName(folder, "Save folder");
        panel.Children.Add(folder);
        var folderActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 6, 0, 16) };
        ActionButton(folderActions, "Browse…", () =>
        {
            var picker = new OpenFolderDialog { Title = "Choose a local FrameInk save folder" };
            if (Directory.Exists(saveFolder)) picker.InitialDirectory = saveFolder;
            if (picker.ShowDialog(dialog) == true) folder.Text = picker.FolderName;
        });
        ActionButton(folderActions, "Use default", () => folder.Text = CaptureFolder.Default);
        panel.Children.Add(folderActions);

        panel.Children.Add(Heading("Global PNG shortcut"));
        panel.Children.Add(new TextBlock { Text = "Choose at least one modifier and a function key.", Margin = new(0, 4, 0, 8) });
        var modifiers = new StackPanel { Orientation = Orientation.Horizontal };
        var ctrl = new CheckBox { Content = "Ctrl", IsChecked = shortcut.Ctrl, Margin = new(0, 0, 12, 0) };
        var alt = new CheckBox { Content = "Alt", IsChecked = shortcut.Alt, Margin = new(0, 0, 12, 0) };
        var shift = new CheckBox { Content = "Shift", IsChecked = shortcut.Shift };
        foreach (var box in new[] { ctrl, alt, shift }) modifiers.Children.Add(box);
        panel.Children.Add(modifiers);
        var keys = new ListBox { ItemsSource = Hotkey.Keys, SelectedItem = shortcut.Key, Margin = new(0, 6, 0, 10) };
        panel.Children.Add(keys);

        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 6) };
        panel.Children.Add(error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Values? answer = null;
        ActionButton(actions, "Apply", () =>
        {
            try
            {
                var next = new Hotkey(ctrl.IsChecked == true, alt.IsChecked == true, shift.IsChecked == true, (string)keys.SelectedItem);
                if (!next.Valid) { error.Text = "Choose at least one modifier and a function key."; return; }
                answer = new(CaptureFolder.Ensure(folder.Text), next);
                dialog.DialogResult = true;
            }
            catch (Exception ex) { error.Text = ex.Message; }
        }).IsDefault = true;
        actions.Children.Add(new Button { Content = "Cancel", Padding = new(12, 4, 12, 4), IsCancel = true });
        panel.Children.Add(actions);

        // Do not allow the desktop's close animation to appear in the following capture.
        _ = new WindowInteropHelper(dialog).EnsureHandle();
        try { Native.ExcludeFromCapture(dialog); }
        catch { dialog.Close(); throw; }
        return dialog.ShowDialog() == true ? answer : null;
    }

    private static TextBlock Heading(string title) => new() { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold };

    private static Button ActionButton(Panel panel, string caption, Action action)
    {
        var button = new Button { Content = caption, Padding = new(12, 4, 12, 4), Margin = new(0, 0, 8, 0) };
        button.Click += (_, _) => action();
        panel.Children.Add(button);
        return button;
    }
}
