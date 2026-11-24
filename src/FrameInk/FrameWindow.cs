using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace FrameInk;

internal sealed partial class FrameWindow : Window
{
    private static readonly Brush Edge = new SolidColorBrush(Color.FromRgb(43, 52, 65));
    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(48, 178, 230));
    private readonly Ink ink = new();
    private readonly InkSurface surface;
    private readonly Window overlay;
    private readonly TextBlock size = new() { Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox status = new() { Foreground = Brushes.White, Background = Edge, BorderThickness = new(0),
        IsReadOnly = true, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden };
    private readonly Button interact, settings;
    private readonly Border move;
    private readonly TextBlock grip;
    private Crop crop;
    private Display display;
    private Tool selectedTool;
    private bool drawing, ready, placing, invalid, configuring, closing, savingPng;
    private (Native.Point Point, Crop Crop, bool Resize, UIElement Handle)? gesture;
    private Hotkey hotkey = Hotkey.Default;
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

        surface = new InkSurface(ink) { Cursor = Cursors.Pen };
        overlay = new Window { Title = "FrameInk ink", AllowsTransparency = true, WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize, Background = Brushes.Transparent, ShowInTaskbar = false,
            ShowActivated = false, Topmost = true, Content = surface, Width = 1, Height = 1 };
        surface.MouseLeftButtonDown += (_, _) =>
        {
            if (!drawing || !Native.TryCursor(out var pointer)) return;
            ink.Begin(selectedTool, InkPoint(pointer));
            surface.CaptureMouse();
            surface.InvalidateVisual();
        };
        surface.MouseMove += (_, e) =>
        {
            if (!surface.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed || !Native.TryCursor(out var pointer)) return;
            ink.Move(InkPoint(pointer));
            surface.InvalidateVisual();
        };
        surface.MouseLeftButtonUp += (_, _) =>
        {
            if (!surface.IsMouseCaptured) return;
            if (Native.TryCursor(out var pointer)) ink.Move(InkPoint(pointer));
            surface.ReleaseMouseCapture();
        };
        surface.LostMouseCapture += (_, _) => { ink.End(); surface.InvalidateVisual(); };

        var grid = new Grid();
        grid.RowDefinitions.Add(new() { Height = new(8) });
        grid.RowDefinitions.Add(new());
        grid.RowDefinitions.Add(new() { Height = new(96) });
        grid.ColumnDefinitions.Add(new() { Width = new(8) });
        grid.ColumnDefinitions.Add(new());
        grid.ColumnDefinitions.Add(new() { Width = new(8) });
        move = new Border { Background = Accent, Cursor = Cursors.SizeAll };
        Add(grid, move, 0, 0, 3);
        Add(grid, new Border { Background = Accent }, 1, 0);
        Add(grid, new Border { Background = Accent }, 1, 2);
        var bar = new Grid { Background = Edge };
        for (int i = 0; i < 3; i++) bar.RowDefinitions.Add(new() { Height = new(31) });
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(5, 2, 4, 0) };
        interact = Button(top, "Interact", () => SetDrawing(!drawing));
        foreach (Tool tool in Enum.GetValues<Tool>())
        {
            var button = new RadioButton { Content = tool == Tool.Rectangle ? "Rect" : tool.ToString(), IsChecked = tool == selectedTool,
                GroupName = "InkTools", Padding = new(2), Margin = new(0, 1, 6, 1), VerticalContentAlignment = VerticalAlignment.Center };
            void SelectTool()
            {
                selectedTool = tool;
                if (!drawing) SetDrawing(true);
            }
            button.Checked += (_, _) => SelectTool();
            button.Click += (_, _) => SelectTool();
            top.Children.Add(button);
        }
        Button(top, "Undo", () => { ink.Undo(); surface.InvalidateVisual(); });
        Button(top, "Clear", () => { ink.Clear(); surface.InvalidateVisual(); });
        Button(top, "Save PNG", Snapshot);
        AddRecordButton(top);
        Add(bar, top, 0, 0);
        var info = new DockPanel { Margin = new(8, 0, 4, 0), LastChildFill = false };
        grip = new TextBlock { Text = "◢", Foreground = Brushes.White, FontSize = 22, Cursor = Cursors.SizeNWSE,
            Width = 28, Background = Edge, TextAlignment = TextAlignment.Center };
        DockPanel.SetDock(grip, Dock.Right);
        info.Children.Add(grip);
        var secondary = new StackPanel { Orientation = Orientation.Horizontal };
        settings = Button(secondary, "Hotkey", Configure);
        Button(secondary, "Close", Close);
        info.Children.Add(secondary);
        info.Children.Add(size);
        Add(bar, info, 1, 0);
        status.Margin = new(8, 4, 8, 0);
        // No toolbar popups may overlap the region. The status path is selectable instead of using a tooltip.
        status.ContextMenuOpening += (_, e) => e.Handled = true;
        Add(bar, status, 2, 0);
        Add(grid, bar, 2, 0, 3);
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

    private static Button Button(Panel panel, string text, Action action)
    {
        var button = new Button { Content = text, Padding = new(6, 2, 6, 2), Margin = new(0, 1, 4, 1) };
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
        }
        catch (Exception ex) { ShowStatus($"Frame unavailable: {ex.Message}"); }
    }

    private void Apply(Display target, Crop request)
    {
        Crop next = target.Fit(request);
        placing = true;
        try
        {
            int b = target.Border;
            Native.Place(this, new(next.X - b, next.Y - b, next.Width + 2 * b, next.Height + b + target.Bar));
            Native.Place(overlay, next);
            if (Native.Client(Native.Handle(overlay)) != next) throw new InvalidOperationException("Overlay position does not match the capture region.");
            crop = next;
            display = target;
            surface.Crop = crop;
            surface.InvalidateVisual();
            invalid = false;
            UpdateSize();
        }
        finally { placing = false; }
    }

    private void AttachDrag(UIElement handle, bool resize)
    {
        handle.MouseLeftButtonDown += (_, e) =>
        {
            if (!ready || Locked) return;
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
            surface.ReleaseMouseCapture();
            Native.SetClickThrough(overlay, !enabled);
            drawing = enabled;
            interact.Content = drawing ? "Draw" : "Interact";
        }
        catch (Exception ex) { ShowStatus($"Cannot change mode: {ex.Message}"); }
    }

    private void ValidateCrop()
    {
        if (!ready) throw new InvalidOperationException("Capture was not initialised. Close and reopen FrameInk.");
        nint frameHwnd = Native.Handle(this), inkHwnd = Native.Handle(overlay);
        if (invalid || Native.MonitorAt(crop.X, crop.Y) != display
            || Native.GetDpiForWindow(frameHwnd) != display.Dpi || Native.GetDpiForWindow(inkHwnd) != display.Dpi
            || Native.Client(inkHwnd) != crop || Native.Client(frameHwnd) !=
                new Crop(crop.X - display.Border, crop.Y - display.Border, crop.Width + 2 * display.Border, crop.Height + display.Border + display.Bar))
        {
            invalid = true;
            throw new InvalidOperationException("Capture region changed. Drag the top rail to reselect it.");
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
            string path = Capture.OutputPath(".png");
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
        configuring = true;
        try { if (Hotkey.Prompt(this, hotkey) is { } next) Register(next, persist: true); }
        catch (Exception ex) { ShowStatus($"Hotkey settings: {ex.Message}"); }
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

    private void UpdateSize() => size.Text = $"{crop.Width} × {crop.Height} px  |  PNG: {(hotkeyId == 0 ? "no hotkey" : hotkey)}{RecordingCaption}";
    private void ShowStatus(string message) { status.Text = message; status.Select(0, 0); }

    private void OnClosing(object? sender, CancelEventArgs e) => PrepareClose(e);
}
