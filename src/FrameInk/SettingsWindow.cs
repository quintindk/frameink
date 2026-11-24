using System;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace FrameInk;

internal static class SettingsWindow
{
    internal sealed record Values(string Folder, Hotkey Shortcut);

    public static Values? Prompt(Window owner, Hotkey shortcut, string saveFolder)
    {
        var dialog = new Window { Owner = owner, Title = "FrameInk Settings", Icon = owner.Icon, ShowInTaskbar = false,
            AllowsTransparency = true, WindowStyle = WindowStyle.None, Background = Brushes.Transparent,
            SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, UseLayoutRounding = true };
        dialog.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Theme.xaml", UriKind.Relative) });
        Brush Colour(string key) => (Brush)dialog.FindResource(key);
        Style Look(string key) => (Style)dialog.FindResource(key);

        var layout = new StackPanel { Width = 512 };
        var header = new DockPanel { Margin = new(18, 14, 10, 10), LastChildFill = true };
        var close = new Button { Content = "×", Style = Look("DialogButton"), FontSize = 17, Padding = new(10, 0, 10, 0) };
        AutomationProperties.SetName(close, "Close settings");
        close.Click += (_, _) => dialog.DialogResult = false;
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        var title = new TextBlock { Text = "Settings", FontSize = 22, FontWeight = FontWeights.SemiBold,
            Foreground = Colour("TextBrush"), Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
        title.MouseLeftButtonDown += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed) dialog.DragMove(); };
        header.Children.Add(title);
        layout.Children.Add(header);
        layout.Children.Add(new Border { Background = Colour("AccentBrush"), Height = 2, Margin = new(18, 0, 18, 0), CornerRadius = new(1) });
        var panel = new StackPanel { Margin = new(18, 12, 18, 16) };
        layout.Children.Add(panel);
        dialog.Content = new Border { Background = Colour("SurfaceBrush"), BorderBrush = Colour("OutlineBrush"),
            BorderThickness = new(1), CornerRadius = new(11), Child = layout };

        var location = Section("Save location", "PNG and MP4 files are saved to this folder.");
        var folder = new TextBox { Text = saveFolder, Style = Look("DialogField") };
        AutomationProperties.SetName(folder, "Save folder");
        location.Children.Add(folder);
        var folderActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 8, 0, 0) };
        var error = new TextBlock { Foreground = Colour("ErrorBrush"), TextWrapping = TextWrapping.Wrap };
        ActionButton(folderActions, "Browse…", () =>
        {
            try
            {
                var picker = new OpenFolderDialog { Title = "Choose a local FrameInk save folder" };
                if (Directory.Exists(saveFolder)) picker.InitialDirectory = saveFolder;
                if (picker.ShowDialog(dialog) == true) folder.Text = picker.FolderName;
            }
            catch (Exception ex) { error.Text = ex.Message; }
        });
        ActionButton(folderActions, "Use default", () => folder.Text = CaptureFolder.Default);
        location.Children.Add(folderActions);

        var keyboard = Section("Global PNG shortcut", "Choose at least one modifier and a function key.");
        var modifiers = new StackPanel { Orientation = Orientation.Horizontal };
        var ctrl = Modifier("Ctrl", shortcut.Ctrl);
        var alt = Modifier("Alt", shortcut.Alt);
        var shift = Modifier("Shift", shortcut.Shift);
        keyboard.Children.Add(modifiers);
        var keys = new ListBox { ItemsSource = Hotkey.Keys, SelectedItem = shortcut.Key, Style = Look("ShortcutKeys"), Margin = new(0, 8, 0, 0) };
        AutomationProperties.SetName(keys, "Function key");
        keyboard.Children.Add(keys);

        panel.Children.Add(error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 6, 0, 0) };
        actions.Children.Add(new Button { Content = "Cancel", Style = Look("DialogButton"), IsCancel = true });
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
        }, "DialogPrimaryButton").IsDefault = true;
        panel.Children.Add(actions);

        // Do not allow the desktop's close animation to appear in the following capture.
        _ = new WindowInteropHelper(dialog).EnsureHandle();
        try { Native.ExcludeFromCapture(dialog); }
        catch { dialog.Close(); throw; }
        return dialog.ShowDialog() == true ? answer : null;

        StackPanel Section(string label, string description)
        {
            var contents = new StackPanel { Margin = new(12) };
            contents.Children.Add(new TextBlock { Text = label, Foreground = Colour("TextBrush"), FontSize = 15, FontWeight = FontWeights.SemiBold });
            contents.Children.Add(new TextBlock { Text = description, Foreground = Colour("MutedTextBrush"), Margin = new(0, 3, 0, 8) });
            panel.Children.Add(new Border { Child = contents, Background = Colour("FieldBrush"), BorderBrush = Colour("OutlineBrush"),
                BorderThickness = new(1), CornerRadius = new(8), Margin = new(0, 0, 0, 10) });
            return contents;
        }

        CheckBox Modifier(string label, bool value)
        {
            var box = new CheckBox { Content = label, IsChecked = value, Style = Look("ModifierChip") };
            modifiers.Children.Add(box);
            return box;
        }

        Button ActionButton(Panel buttons, string caption, Action action, string style = "DialogButton")
        {
            var button = new Button { Content = caption, Style = Look(style) };
            button.Click += (_, _) => action();
            buttons.Children.Add(button);
            return button;
        }
    }
}
