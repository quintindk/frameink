using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace FrameInk;

internal sealed partial class FrameWindow : Window
{
    private readonly Ink ink = new();
    private readonly InkSurface surface;
    private readonly Window overlay;
    private readonly TextBlock size = new() { Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox status = new() { Foreground = Brushes.White, BorderThickness = new(0),
        IsReadOnly = true, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden };
    private readonly Button interact, settings, screen;
    private readonly Border move, leftRail, rightRail;
    private readonly RowDefinition railRow = new() { Height = new(8) };
    private readonly TextBlock grip;
    private Crop crop, regionBeforeFullScreen;
    private Display display;
    private Tool selectedTool;
    private InkColour selectedColour = InkPalette.Default;
    private bool drawing, ready, placing, invalid, configuring, closing, savingPng, fullScreen;
    private (Native.Point Point, Crop Crop, bool Resize, UIElement Handle)? gesture;
    private Hotkey hotkey = Hotkey.Default;
    private string saveFolder = CaptureFolder.Default;
    private int hotkeyId;

    [STAThread]
    public static void Main() => new Application().Run(new FrameWindow());

    public FrameWindow()
    {
        Title = "FrameInk";
        Topmost = true;
        AllowsTransparency = true;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Background = Brushes.Transparent;
        Width = 556;
        Height = 360;
        UseLayoutRounding = true;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Theme.xaml", UriKind.Relative) });
        var edge = (Brush)FindResource("SurfaceBrush");
        var accent = (Brush)FindResource("AccentBrush");
        status.Background = edge;

        surface = new InkSurface(ink) { Cursor = Cursors.Pen };
        var inkHost = new Grid();
        inkHost.Children.Add(surface);
        inkHost.Children.Add(editorLayer);
        overlay = new Window { Title = "FrameInk ink", AllowsTransparency = true, WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize, Background = Brushes.Transparent, ShowInTaskbar = false,
            ShowActivated = false, Topmost = true, Content = inkHost, Width = 1, Height = 1 };
        surface.MouseLeftButtonDown += (_, _) =>
        {
            if (!drawing || !Native.TryCursor(out var pointer)) return;
            FinishText(keep: true);
            if (!surface.CaptureMouse()) return;
            var point = InkPoint(pointer);
            if (selectedTool == Tool.Text) textDrag = point;
            else ink.Begin(selectedTool, point, selectedColour);
            surface.InvalidateVisual();
        };
        surface.MouseMove += (_, e) =>
        {
            if (textDrag is not null || !surface.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed || !Native.TryCursor(out var pointer)) return;
            ink.Move(InkPoint(pointer));
            surface.InvalidateVisual();
        };
        surface.MouseLeftButtonUp += (_, _) =>
        {
            if (!surface.IsMouseCaptured) return;
            var start = textDrag;
            if (!Native.TryCursor(out var pointer)) { surface.ReleaseMouseCapture(); return; }
            var end = InkPoint(pointer);
            if (start is null) ink.Move(end);
            surface.ReleaseMouseCapture();
            if (start is { } corner) OpenTextEditor(corner, end);
        };
        surface.LostMouseCapture += (_, _) => { textDrag = null; ink.End(); surface.InvalidateVisual(); };

        var grid = new Grid();
        grid.RowDefinitions.Add(railRow);
        grid.RowDefinitions.Add(new());
        grid.RowDefinitions.Add(new() { Height = new(96) });
        grid.ColumnDefinitions.Add(new() { Width = new(8) });
        grid.ColumnDefinitions.Add(new());
        grid.ColumnDefinitions.Add(new() { Width = new(8) });
        move = new Border { Background = accent, Cursor = Cursors.SizeAll };
        Add(grid, move, 0, 0, 3);
        leftRail = new Border { Background = accent };
        rightRail = new Border { Background = accent };
        Add(grid, leftRail, 1, 0);
        Add(grid, rightRail, 1, 2);
        var bar = new Grid();
        for (int i = 0; i < 3; i++) bar.RowDefinitions.Add(new() { Height = new(31) });
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(8, 1, 5, 0) };
        interact = Button(top, "Interact", () => SetDrawing(!drawing));
        UpdateModeButton();
        foreach (Tool tool in Enum.GetValues<Tool>())
        {
            var button = new RadioButton { Content = tool == Tool.Rectangle ? "Rect" : tool.ToString(), IsChecked = tool == selectedTool,
                GroupName = "InkTools", Foreground = Brushes.White, Padding = new(2), Margin = new(0, 1, 6, 1), VerticalContentAlignment = VerticalAlignment.Center };
            void SelectTool()
            {
                if (selectedTool != tool) FinishText(keep: true);
                selectedTool = tool;
                surface.Cursor = tool == Tool.Text ? Cursors.Cross : Cursors.Pen;
                if (!drawing) SetDrawing(true);
                if (tool == Tool.Text && textEditor is null) ShowStatus("Drag an area for text, then type. Only the text will appear in captures.");
            }
            button.Checked += (_, _) => SelectTool();
            button.Click += (_, _) => SelectTool();
            top.Children.Add(button);
        }
        Button(top, "Undo", () => { FinishText(keep: true); ink.Undo(); surface.InvalidateVisual(); });
        Button(top, "Clear", () => { FinishText(keep: false); ink.Clear(); surface.InvalidateVisual(); });
        Button(top, "Save PNG", Snapshot, "PngButton");
        AddRecordButton(top);
        Add(bar, top, 0, 0);
        var info = new DockPanel { Margin = new(8, 0, 4, 0), LastChildFill = false };
        grip = new TextBlock { Text = "◢", Foreground = Brushes.White, FontSize = 22, Cursor = Cursors.SizeNWSE,
            Width = 28, Background = edge, TextAlignment = TextAlignment.Center };
        DockPanel.SetDock(grip, Dock.Right);
        info.Children.Add(grip);
        var secondary = new StackPanel { Orientation = Orientation.Horizontal };
        screen = Button(secondary, "Full screen", ToggleFullScreen);
        settings = Button(secondary, "Settings", Configure);
        Button(secondary, "Close", Close);
        info.Children.Add(secondary);
        size.Margin = new(6, 0, 0, 0);
        info.Children.Add(size);
        Add(bar, info, 1, 0);
        var footer = new DockPanel { LastChildFill = true };
        var palette = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 2, 6, 0) };
        DockPanel.SetDock(palette, Dock.Right);
        foreach (var colour in InkPalette.Colours)
        {
            var swatch = new RadioButton { GroupName = "InkPalette", Background = colour.Fill,
                Style = (Style)FindResource("ColourSwatch"), IsChecked = colour == selectedColour };
            AutomationProperties.SetName(swatch, $"{colour.Name} ink");
            swatch.Checked += (_, _) =>
            {
                FinishText(keep: true);
                selectedColour = colour;
                ShowStatus($"Ink colour: {colour.Name}");
            };
            palette.Children.Add(swatch);
        }
        footer.Children.Add(palette);
        status.Margin = new(8, 4, 8, 0);
        // No toolbar popups may overlap the region. The status path is selectable instead of using a tooltip.
        status.ContextMenuOpening += (_, e) => e.Handled = true;
        footer.Children.Add(status);
        Add(bar, footer, 2, 0);
        Add(grid, new Border { CornerRadius = new(8), Background = edge, Child = bar }, 2, 0, 3);
        Content = grid;
        AttachDrag(move, false);
        AttachDrag(grip, true);

        Loaded += (_, _) => Initialise();
        Closing += OnClosing;
    }

    private static void Add(Grid grid, UIElement element, int row, int column, int span = 1)
    {
        Grid.SetRow(element, row); Grid.SetColumn(element, column); Grid.SetColumnSpan(element, span);
        grid.Children.Add(element);
    }

    private Button Button(Panel panel, string text, Action action, string style = "ToolbarButton")
    {
        var button = new Button { Content = text, Style = (Style)FindResource(style) };
        button.Click += (_, _) => action();
        panel.Children.Add(button);
        return button;
    }

    private void Initialise()
    {
        try
        {
            var pointer = Native.Cursor();
            var monitor = Native.MonitorAt(pointer.X, pointer.Y);
            overlay.Owner = this;
            overlay.Left = Left;
            overlay.Top = Top;
            overlay.Show();
            Native.ExcludeFromCapture(overlay);
            Native.ExcludeFromCapture(this);
            Native.SetClickThrough(overlay, true);
            HwndSource.FromHwnd(Native.Handle(this)).AddHook(Message);
            Apply(monitor, new(monitor.Work.X + monitor.Pixels(75), monitor.Work.Y + monitor.Pixels(75), monitor.Pixels(600), monitor.Pixels(340)));
            ready = true;
            ShowStatus("Drag the top rail to move. Use the lower-right grip to resize.");
            string? settingsError = null;
            try { hotkey = Hotkey.Load(); } catch (Exception ex) { settingsError = ex.Message; }
            Register(hotkey, persist: false);
            if (settingsError is not null && hotkeyId != 0)
                ShowStatus($"Hotkey settings could not be loaded: {settingsError} Using {hotkey}.");
            try { saveFolder = CaptureFolder.Load(); }
            catch (Exception ex) { ShowStatus($"Save folder setting could not be loaded: {ex.Message} Using the default folder."); }
        }
        catch (Exception ex) { ShowStatus($"Frame unavailable: {ex.Message}"); }
    }

    private void Apply(Display target, Crop request, bool wholeMonitor = false)
    {
        Crop next = wholeMonitor ? target.Bounds : target.Fit(request);
        Crop chrome = target.Chrome(next, wholeMonitor);
        bool changingMode = fullScreen != wholeMonitor;
        placing = true;
        try
        {
            Native.Place(this, chrome);
            Native.Place(overlay, next);
            if (Native.Client(Native.Handle(overlay)) != next) throw new InvalidOperationException("Overlay position does not match the capture region.");
            if (changingMode) ink.Translate(crop.X - next.X, crop.Y - next.Y);
            fullScreen = wholeMonitor;
            railRow.Height = new(wholeMonitor ? 0 : 8);
            move.Visibility = leftRail.Visibility = rightRail.Visibility = grip.Visibility = wholeMonitor ? Visibility.Collapsed : Visibility.Visible;
            screen.Content = wholeMonitor ? "Restore region" : "Full screen";
            crop = next;
            display = target;
            surface.Crop = crop;
            surface.Toolbar = wholeMonitor ? chrome : null;
            surface.InvalidateVisual();
            invalid = false;
            UpdateSize();
        }
        finally { placing = false; }
    }

    private void ToggleFullScreen()
    {
        if (!ready || Locked || configuring) return;
        try
        {
            FinishText(keep: true);
            surface.ReleaseMouseCapture();
            if (fullScreen)
            {
                var previous = regionBeforeFullScreen;
                Apply(Native.MonitorAt(previous.X, previous.Y), previous);
                ShowStatus("Restored the previous capture region.");
            }
            else
            {
                regionBeforeFullScreen = crop;
                Apply(Native.MonitorAt(crop.X + crop.Width / 2, crop.Y + crop.Height / 2), crop, wholeMonitor: true);
                ShowStatus("Full screen: the toolbar stays available but is excluded from captures.");
            }
        }
        catch (Exception ex) { invalid = true; ShowStatus($"Cannot change capture area: {ex.Message}"); }
    }

    private void AttachDrag(UIElement handle, bool resize)
    {
        handle.MouseLeftButtonDown += (_, e) =>
        {
            if (!ready || Locked || fullScreen) return;
            FinishText(keep: true);
            try { if (handle.CaptureMouse()) gesture = (Native.Cursor(), crop, resize, handle); }
            catch (Exception ex) { ShowStatus(ex.Message); }
            e.Handled = true;
        };
        handle.MouseMove += (_, e) =>
        {
            if (!ready || Locked || gesture is not { } drag || drag.Handle != handle || e.LeftButton != MouseButtonState.Pressed) return;
            try
            {
                var p = Native.Cursor();
                int dx = p.X - drag.Point.X, dy = p.Y - drag.Point.Y;
                var c = drag.Crop;
                var monitor = drag.Resize ? Native.MonitorAt(c.X, c.Y) : Native.MonitorAt(p.X, p.Y);
                // Hold the upper-left corner fixed when the right/bottom grip is dragged.
                int width = Math.Min(c.Width + dx, monitor.Work.Right - monitor.Border - c.X);
                int height = Math.Min(c.Height + dy, monitor.Work.Bottom - monitor.Bar - c.Y);
                bool wasInvalid = invalid;
                Apply(monitor, drag.Resize ? c with { Width = width, Height = height } : c with { X = c.X + dx, Y = c.Y + dy });
                if (wasInvalid) ShowStatus("Capture region reselected.");
            }
            catch (Exception ex) { invalid = true; ShowStatus($"Cannot place frame: {ex.Message}"); }
        };
        handle.MouseLeftButtonUp += (_, _) => handle.ReleaseMouseCapture();
        handle.LostMouseCapture += (_, _) => gesture = null;
    }

    private Point InkPoint(Native.Point p) => new(Math.Clamp(p.X - crop.X, 0, crop.Width - 1),
                                                   Math.Clamp(p.Y - crop.Y, 0, crop.Height - 1));

    private void SetDrawing(bool enabled)
    {
        if (!ready) return;
        try
        {
            if (!enabled) FinishText(keep: true);
            surface.ReleaseMouseCapture();
            Native.SetClickThrough(overlay, !enabled);
            drawing = enabled;
            UpdateModeButton();
        }
        catch (Exception ex) { ShowStatus($"Cannot change mode: {ex.Message}"); }
    }

    private void UpdateModeButton()
    {
        interact.Content = drawing ? "✎  Draw" : "↗  Interact";
        AutomationProperties.SetName(interact, drawing ? "Draw" : "Interact");
        AutomationProperties.SetHelpText(interact, drawing ? "Drawing is active. Switch to click-through Interact mode."
            : "Clicks pass through the frame. Switch to Draw mode to annotate.");
        interact.Style = (Style)FindResource(drawing ? "DrawMode" : "InteractMode");
        move.Background = leftRail.Background = rightRail.Background = (Brush)FindResource(drawing ? "DrawAccentBrush" : "AccentBrush");
    }

    private void ValidateCrop()
    {
        if (!ready) throw new InvalidOperationException("Capture was not initialised. Close and reopen FrameInk.");
        nint frameHwnd = Native.Handle(this), inkHwnd = Native.Handle(overlay);
        if (invalid || Native.MonitorAt(crop.X, crop.Y) != display
            || Native.GetDpiForWindow(frameHwnd) != display.Dpi || Native.GetDpiForWindow(inkHwnd) != display.Dpi
            || Native.Client(inkHwnd) != crop || Native.Client(frameHwnd) != display.Chrome(crop, fullScreen))
        {
            invalid = true;
            throw new InvalidOperationException($"Capture region changed. {ReselectHint}");
        }
        if (!Native.IsCaptureExcludedAndVisible(this) || !Native.IsCaptureExcludedAndVisible(overlay))
            throw new InvalidOperationException("The frame must be visible with Windows capture exclusion enabled.");
    }

    private async void Snapshot()
    {
        if (configuring || closing || savingPng) return;
        savingPng = true;
        try
        {
            ValidateCrop();
            string path = Capture.OutputPath(".png", saveFolder);
            var frame = Capture.Grab(crop, ink);
            IncludeSnapshot(frame);
            await Task.Run(() => Capture.SavePng(frame, path));
            ShowStatus($"PNG saved: {path}");
        }
        catch (Exception ex) { ShowStatus($"PNG failed: {ex.Message}"); }
        finally { savingPng = false; }
    }

    private void Configure()
    {
        if (!ready || Locked) return;
        FinishText(keep: true);
        configuring = true;
        try
        {
            if (SettingsWindow.Prompt(this, hotkey, saveFolder) is not { } next) return;
            bool folderChanged = !string.Equals(saveFolder, next.Folder, StringComparison.OrdinalIgnoreCase);
            if (folderChanged)
            {
                CaptureFolder.Save(next.Folder);
                saveFolder = next.Folder;
            }
            if (next.Shortcut != hotkey || hotkeyId == 0) Register(next.Shortcut, persist: true);
            else ShowStatus($"Save folder: {saveFolder}");
        }
        catch (Exception ex) { ShowStatus($"Settings: {ex.Message}"); }
        finally { configuring = false; }
    }

    private void Register(Hotkey next, bool persist)
    {
        nint hwnd = Native.Handle(this);
        if (hotkeyId != 0 && next == hotkey) { ShowStatus($"PNG shortcut: {hotkey}"); return; }
        int slot = hotkeyId == 1 ? 2 : 1;
        if (!Native.RegisterHotKey(hwnd, slot, next.Modifiers, next.VirtualKey))
        {
            ShowStatus($"Cannot register {next}: {new Win32Exception().Message}. Choose another shortcut.");
            UpdateSize();
            return;
        }
        if (hotkeyId != 0) Native.UnregisterHotKey(hwnd, hotkeyId);
        hotkeyId = slot;
        hotkey = next;
        UpdateSize();
        if (persist)
        {
            try { next.Save(); }
            catch (Exception ex) { ShowStatus($"Shortcut works now but could not be saved: {ex.Message}"); return; }
        }
        ShowStatus($"PNG shortcut: {next}");
    }

    private nint Message(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == Native.HotkeyMessage && (int)wParam == hotkeyId)
        {
            handled = true;
            if (ready && !configuring && !closing) Dispatcher.BeginInvoke(Snapshot);
        }
        if (ready && !closing && !placing && (message == Native.DisplayChange || message == Native.DpiChange))
        {
            invalid = true;
            Dispatcher.BeginInvoke(() => DisplayInvalidated());
        }
        return 0;
    }

    private string ReselectHint => fullScreen ? "Restore the region, then select Full screen again." : "Drag the top rail to reselect it.";
    private void UpdateSize() => size.Text = $"{crop.Width} × {crop.Height} px  |  PNG: {(hotkeyId == 0 ? "no hotkey" : hotkey)}{RecordingCaption}";
    private void ShowStatus(string message) { status.Text = message; status.Select(0, 0); }

    private void OnClosing(object? sender, CancelEventArgs e) => PrepareClose(e);
}
