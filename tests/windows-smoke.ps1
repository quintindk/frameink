[CmdletBinding()]
param(
    [string]$App,
    [switch]$PngOnly,
    [int]$MonitorIndex = -1,
    [switch]$TestHotkeyRebind,
    [switch]$KeepArtifacts
)
$ErrorActionPreference = 'Stop'
if (!$App) { $App = [IO.Path]::Combine($PSScriptRoot, '..\src\FrameInk\bin\Debug\net10.0-windows\FrameInk.exe') }
Add-Type -AssemblyName System.Windows.Forms, System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
public static class Desktop {
    public delegate bool EnumProc(IntPtr hwnd, IntPtr data);
    [DllImport("user32")] static extern bool EnumWindows(EnumProc callback, IntPtr data);
    [DllImport("user32")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder text, int max);
    [DllImport("user32")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32")] public static extern IntPtr WindowFromPoint(Point p);
    [DllImport("user32")] public static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32")] public static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint key);
    [DllImport("user32")] public static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("user32")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32")] static extern bool GetWindowRect(IntPtr h, out RECT rect);
    [DllImport("user32")] static extern bool ClientToScreen(IntPtr h, ref Point p);
    [DllImport("user32")] static extern bool GetClientRect(IntPtr h, out RECT rect);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L,T,R,B; }
    public static IntPtr Find(int process, string title) {
        IntPtr match = IntPtr.Zero;
        EnumWindows(delegate(IntPtr h, IntPtr data) {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid == process) {
                var text = new StringBuilder(256);
                GetWindowText(h, text, text.Capacity);
                if (text.ToString() == title) { match = h; return false; }
            }
            return true;
        }, IntPtr.Zero);
        return match;
    }
    public static Rectangle Bounds(IntPtr h) {
        RECT r; Point p = new Point();
        if (!GetClientRect(h, out r) || !ClientToScreen(h, ref p)) throw new InvalidOperationException("Window disappeared");
        return new Rectangle(p.X, p.Y, r.R, r.B);
    }
    public static Color Pattern(int x, int y) {
        return Color.FromArgb(30 + (x / 23 % 4) * 30, 70 + (y / 17 % 5) * 25, 195);
    }
    public static int CountMismatch(Bitmap bitmap, Rectangle crop, Rectangle target, Rectangle allowed) {
        int mismatches = 0;
        for (int y = 0; y < crop.Height; y++) for (int x = 0; x < crop.Width; x++) {
            if (!allowed.Contains(x,y) && bitmap.GetPixel(x,y).ToArgb() != Pattern(x + crop.X - target.X, y + crop.Y - target.Y).ToArgb()) mismatches++;
        }
        return mismatches;
    }
}
'@
[void][Desktop]::SetProcessDpiAwarenessContext([intptr](-4))
$oldDpi = [Desktop]::SetThreadDpiAwarenessContext([intptr](-4))
function Wait-Pump([int]$ms) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 15 } while ($timer.ElapsedMilliseconds -lt $ms)
}
function Wait-For([scriptblock]$predicate, [string]$failure, [int]$seconds = 7) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do { $value = & $predicate; if ($value) { return $value }; Wait-Pump 40 } while ($timer.Elapsed.TotalSeconds -lt $seconds)
    throw $failure
}
function Assert($condition, [string]$message) { if (!$condition) { throw $message } }
function Mouse([int]$x, [int]$y, [uint32]$flags = 0) {
    Assert ([Desktop]::SetCursorPos($x,$y)) 'Cannot position test pointer.'
    Wait-Pump 80
    if ($flags) { [Desktop]::mouse_event($flags,0,0,0,[uintptr]::Zero); Wait-Pump 80 }
}
function Hit([int]$x, [int]$y, [intptr]$expected) {
    $actual = [Desktop]::WindowFromPoint([Drawing.Point]::new($x,$y))
    Assert ($actual -eq $expected) "Unexpected desktop hit target at $x,$y (expected $expected, got $actual). No input sent."
}
function Drag([int]$x, [int]$y, [int]$endX, [int]$endY, [intptr]$expected) {
    Hit $x $y $expected
    try { Mouse $x $y 2; Mouse $endX $endY } finally { Mouse $endX $endY 4 }
}
function Click([int]$x, [int]$y, [intptr]$expected) {
    Hit $x $y $expected
    try { Mouse $x $y 2 } finally { Mouse $x $y 4 }
}
function Item([string]$name) {
    $condition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, $name)
    return $script:root.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
}
function Button([string]$name) {
    $element = Wait-For { Item $name } "Missing toolbar button: $name"
    $element.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Wait-Pump 180
}
function Click-Control([string]$name, [intptr]$hwnd) {
    $element = Wait-For { Item $name } "Missing toolbar control: $name"
    $rect = $element.Current.BoundingRectangle
    Click ([int]($rect.X+$rect.Width/2)) ([int]($rect.Y+$rect.Height/2)) $hwnd
}
function Shortcut([byte]$functionKey = 121) {
    try {
        foreach ($key in 17,16,$functionKey) { [Desktop]::keybd_event($key,0,0,[uintptr]::Zero) }
    } finally {
        foreach ($key in $functionKey,16,17) { [Desktop]::keybd_event($key,0,2,[uintptr]::Zero) }
    }
    Wait-Pump 180
}
function Output([string]$extension) {
    $path = Wait-For {
        Get-ChildItem -LiteralPath $captures -Filter "*.$extension" -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -notlike '*.partial.mp4' -and $seen.Add($_.FullName) } | Select-Object -First 1 -ExpandProperty FullName
    } "No new $extension produced. FrameInk labels: $(Labels)" 10
    $outputs.Add($path)
    return $path
}
function Labels {
    ($script:root.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition) |
        ForEach-Object {
            $_.Current.Name
            $value = $null
            if ($_.TryGetCurrentPattern([Windows.Automation.ValuePattern]::Pattern, [ref]$value)) { $value.Current.Value }
        } | Where-Object { $_ }) -join ' | '
}
function Check-Pattern([string]$path, [Drawing.Rectangle]$allowed) {
    $bitmap = [Drawing.Bitmap]::new($path)
    try {
        Assert ($bitmap.Size -eq $crop.Size) "PNG dimensions $($bitmap.Size) != $($crop.Size)"
        $bad = [Desktop]::CountMismatch($bitmap, $crop, $target, $allowed)
        $expected = [Desktop]::Pattern($crop.X-$target.X,$crop.Y-$target.Y)
        Assert ($bad -eq 0) "PNG differs from the synthetic target in $bad pixels outside the expected ink. Crop origin pixel $($bitmap.GetPixel(0,0)), expected $expected."
    } finally { $bitmap.Dispose() }
}

$captures = Join-Path $env:LOCALAPPDATA 'FrameInk\Captures'
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$outputs = [Collections.Generic.List[string]]::new()
Get-ChildItem -LiteralPath $captures -ErrorAction SilentlyContinue | ForEach-Object { [void]$seen.Add($_.FullName) }
Assert (!(Get-Process -Name FrameInk -ErrorAction SilentlyContinue)) 'Close other FrameInk instances before running this test.'
$config = Join-Path $env:LOCALAPPDATA 'FrameInk\hotkey.json'
$wroteConfig = $false
if (Test-Path -LiteralPath $config) {
    Assert (!$TestHotkeyRebind) 'Rebind test only runs when there is no existing FrameInk hotkey.json.'
    $binding = Get-Content -LiteralPath $config -Raw | ConvertFrom-Json
    Assert ($binding.Ctrl -and $binding.Shift -and !$binding.Alt -and $binding.Key -eq 'F10') 'Smoke test requires the default hotkey. Existing settings were not changed.'
}
$originalCursor = [Windows.Forms.Cursor]::Position
$screens = [Windows.Forms.Screen]::AllScreens
Assert ($MonitorIndex -lt $screens.Length) "Monitor index $MonitorIndex is out of range ($($screens.Length) attached)."
$screen = if ($MonitorIndex -lt 0) { [Windows.Forms.Screen]::FromPoint($originalCursor) } else { $screens[$MonitorIndex] }
$form = [Windows.Forms.Form]::new()
$form.Text = 'FrameInk synthetic test target'
$form.FormBorderStyle = 'None'
$form.StartPosition = 'Manual'
$form.Bounds = $screen.WorkingArea
$form.TopMost = $true
$targetPattern = [Drawing.Bitmap]::new($form.Width, $form.Height)
$graphics = [Drawing.Graphics]::FromImage($targetPattern)
try {
    for ($y=0; $y -lt $form.Height; $y+=17) {
        for ($x=0; $x -lt $form.Width; $x+=23) {
            $brush = [Drawing.SolidBrush]::new([Desktop]::Pattern($x,$y))
            try { $graphics.FillRectangle($brush,$x,$y,23,17) } finally { $brush.Dispose() }
        }
    }
} finally { $graphics.Dispose() }
$form.BackgroundImage = $targetPattern
$script:targetClicks = 0
$form.add_MouseDown({ $script:targetClicks++ })
$process = $null
try {
    $form.Show()
    Wait-Pump 300
    $target = [Desktop]::Bounds($form.Handle)
    Mouse ($target.X + [int]($target.Width/2)) ($target.Y + [int]($target.Height/2))

    Assert ([Desktop]::RegisterHotKey([intptr]::Zero,98,6,121)) 'The default hotkey was already occupied (not by the smoke test).'
    try {
        $process = Start-Process -FilePath ([IO.Path]::GetFullPath($App)) -PassThru
        $chrome = Wait-For { $h = [Desktop]::Find($process.Id,'FrameInk'); if ($h -ne [intptr]::Zero) { $h } } 'Collision-test frame did not open.'
        $script:root = [Windows.Automation.AutomationElement]::FromHandle($chrome)
        [void](Wait-For { (Labels) -match 'Cannot register Ctrl\+Shift\+F10' } 'App did not report an occupied hotkey.')
        Assert ((Labels) -match 'Save PNG') 'Toolbar PNG unavailable after hotkey collision.'
        Button 'Close'
        Assert ($process.WaitForExit(5000)) 'Collision-test instance did not exit.'
        'PASS hotkey collision reported without disabling toolbar PNG'
    } finally { [void][Desktop]::UnregisterHotKey([intptr]::Zero,98) }

    $process = Start-Process -FilePath ([IO.Path]::GetFullPath($App)) -PassThru
    $chrome = Wait-For { $h = [Desktop]::Find($process.Id,'FrameInk'); if ($h -ne [intptr]::Zero) { $h } } 'Frame did not open.'
    $overlay = Wait-For { $h = [Desktop]::Find($process.Id,'FrameInk ink'); if ($h -ne [intptr]::Zero) { $h } } 'Overlay did not open.'
    $script:root = [Windows.Automation.AutomationElement]::FromHandle($chrome)
    [void](Wait-For { (Labels) -match 'PNG: Ctrl\+Shift\+F10' } ('Default shortcut is not active: ' + (Labels)))
    $dpi = [Desktop]::GetDpiForWindow($chrome)
    $scale = $dpi / 96.0
    $old = [Desktop]::Bounds($overlay)
    $frame = [Desktop]::Bounds($chrome)
    Drag ($frame.X+60) ($frame.Y+3) ($frame.X+91) ($frame.Y+26) $chrome
    $moved = [Desktop]::Bounds($overlay)
    Assert ($moved.X -eq $old.X+31 -and $moved.Y -eq $old.Y+23 -and $moved.Size -eq $old.Size) "Rail drag did not move an unchanged crop: $old -> $moved."
    $frame = [Desktop]::Bounds($chrome)
    $gx = $frame.Right - [int](16*$scale)
    $gy = $moved.Bottom + [int](46*$scale)
    Drag $gx $gy ($gx+40) ($gy+32) $chrome
    $crop = [Desktop]::Bounds($overlay)
    Assert ($crop.X -eq $moved.X -and $crop.Y -eq $moved.Y -and $crop.Width -eq $moved.Width+40 -and $crop.Height -eq $moved.Height+32) "Grip did not resize from the fixed corner: $moved -> $crop."
    Assert ($target.Contains($crop)) 'Synthetic window does not cover the crop.'
    Click ($crop.X+100) ($crop.Y+100) $form.Handle
    Assert ($script:targetClicks -eq 1) 'Interact did not click the synthetic window.'
    Button 'Save PNG'
    $plain = Output 'png'
    Check-Pattern $plain ([Drawing.Rectangle]::Empty)
    "PASS rail, grip, default click-through, exact $($crop.Width)x$($crop.Height) PNG without chrome on $($screen.DeviceName) at $dpi DPI"

    Button 'Interact'
    Drag ($crop.X+100) ($crop.Y+100) ($crop.X+170) ($crop.Y+100) $overlay
    Drag ($crop.X+220) ($crop.Y+100) ($crop.X+250) ($crop.Y+100) $overlay
    Assert ($script:targetClicks -eq 1) 'Draw mode leaked a click.'
    Button 'Undo'
    Button 'Draw'
    Click ($crop.X+130) ($crop.Y+100) $form.Handle
    Assert ($script:targetClicks -eq 2) 'Visible ink intercepted the target click.'
    Button 'Save PNG'
    $annotated = Output 'png'
    Check-Pattern $annotated ([Drawing.Rectangle]::new(96,96,79,9))
    $bitmap = [Drawing.Bitmap]::new($annotated)
    try { Assert ($bitmap.GetPixel(130,100).ToArgb() -eq [Drawing.Color]::OrangeRed.ToArgb()) 'Pen did not persist after mode switch.' }
    finally { $bitmap.Dispose() }
    $onePass = [Drawing.Bitmap]::new(1,1)
    try {
        $g = [Drawing.Graphics]::FromImage($onePass)
        try { $g.CopyFromScreen($crop.X+130,$crop.Y+100,0,0,$onePass.Size,[Drawing.CopyPixelOperation]::SourceCopy) }
        finally { $g.Dispose() }
        $expected = [Desktop]::Pattern($crop.X+130-$target.X,$crop.Y+100-$target.Y)
        Assert ($onePass.GetPixel(0,0).ToArgb() -eq $expected.ToArgb()) 'Raw desktop source baked in the live overlay.'
    } finally { $onePass.Dispose() }
    Shortcut
    $hotPng = Output 'png'
    Assert ((Get-FileHash -LiteralPath $hotPng).Hash -eq (Get-FileHash -LiteralPath $annotated).Hash) 'Toolbar and global shortcut images differ.'
    'PASS draw, undo, visible-ink click-through, single compositing pass, identical toolbar/global PNG'

    Click-Control 'Arrow' $chrome
    Drag ($crop.X+100) ($crop.Y+180) ($crop.X+156) ($crop.Y+180) $overlay
    Click-Control 'Rect' $chrome
    Drag ($crop.X+100) ($crop.Y+220) ($crop.X+158) ($crop.Y+250) $overlay
    Click-Control 'Step' $chrome
    Click ($crop.X+220) ($crop.Y+200) $overlay
    Click ($crop.X+260) ($crop.Y+200) $overlay
    Button 'Save PNG'
    $shapes = [Drawing.Bitmap]::new((Output 'png'))
    try {
        foreach ($point in @(@(120,180),@(110,220),@(220,207),@(260,207))) {
            Assert ($shapes.GetPixel($point[0],$point[1]).ToArgb() -eq [Drawing.Color]::OrangeRed.ToArgb()) "Expected arrow, rectangle or step pixel missing at $point."
        }
    } finally { $shapes.Dispose() }
    Click-Control 'Pen' $chrome
    Button 'Draw'
    'PASS arrow, rectangle and numbered step tools through the attached toolbar'

    if (!$PngOnly) {
        Button 'Clear'
        Button 'Record'
        [void](Wait-For { Item 'Stop' } ('Recorder did not start: ' + (Labels)))
        Wait-Pump 600
        $frame = [Desktop]::Bounds($chrome)
        Drag ($frame.X+60) ($frame.Y+3) ($frame.X+91) ($frame.Y+26) $chrome
        Drag $gx $gy ($gx+24) ($gy+24) $chrome
        Assert ([Desktop]::Bounds($overlay) -eq $crop) 'Recording did not lock movement and resize.'
        Button 'Interact'
        Drag ($crop.X+100) ($crop.Y+100) ($crop.X+170) ($crop.Y+100) $overlay
        Button 'Draw'
        Click ($crop.X+130) ($crop.Y+100) $form.Handle
        Assert ($script:targetClicks -eq 3) 'Interact did not let clicks through during recording.'
        Shortcut
        $mid = Output 'png'
        Assert ((Get-FileHash -LiteralPath $mid).Hash -eq (Get-FileHash -LiteralPath $annotated).Hash) 'Mid-take PNG did not match the stand-alone PNG.'
        Assert (Item 'Stop') 'Mid-take PNG stopped the take.'
        Wait-Pump 800
        Button 'Clear'
        Wait-Pump 800
        Button 'Stop'
        $video = Output 'mp4'
        [void](Wait-For { (Labels) -match 'MP4 saved:' } ('MP4 finalisation did not succeed: ' + (Labels)))
        Drag ($frame.X+60) ($frame.Y+3) ($frame.X+71) ($frame.Y+14) $chrome
        $unlocked = [Desktop]::Bounds($overlay)
        Assert ($unlocked.X -eq $crop.X+11 -and $unlocked.Y -eq $crop.Y+11) 'Frame did not unlock after stop.'
        'PASS record/stop, locked drag and resize, ink and Interact during recording, matching mid-take PNG, unlocked after stop'

        # Drive the Windows media stack to MediaEnded, not just header parsing.
        Add-Type -AssemblyName PresentationCore
        $player = [Windows.Media.MediaPlayer]::new()
        $script:ended = $false
        $script:mediaError = $null
        $player.add_MediaEnded({ $script:ended = $true })
        $player.add_MediaFailed({ $script:mediaError = $_.ErrorException.Message })
        try {
            $player.Open([uri]::new($video))
            $player.Play()
            [void](Wait-For { $script:ended -or $script:mediaError } 'Windows MediaPlayer did not play the MP4 through.' 15)
            Assert (!$script:mediaError) "Windows playback failed: $script:mediaError"
            Assert ($player.NaturalVideoWidth -eq $crop.Width -and $player.NaturalVideoHeight -eq $crop.Height) 'MP4 dimensions differ from PNG.'
            Assert ($player.NaturalDuration.TimeSpan.TotalSeconds -gt 2) 'Take was shorter than expected.'
            "PASS Windows MediaPlayer played $($player.NaturalDuration.TimeSpan.TotalSeconds)s MP4 at $($crop.Width)x$($crop.Height) through to MediaEnded"
        } finally { $player.Close() }
        if ($KeepArtifacts) { "VIDEO=$video"; "PLAIN=$plain"; "MID=$mid" }

        Button 'Record'
        Wait-Pump 600
        Assert ([Desktop]::PostMessage($chrome,0x7e,[intptr]::Zero,[intptr]::Zero)) 'Cannot inject the test display-change message.'
        [void](Output 'mp4')
        [void](Wait-For { (Labels) -match 'Display changed.*Partial take saved' } ('Display change was not reported: ' + (Labels)))
        Button 'Save PNG'
        [void](Wait-For { (Labels) -match 'PNG failed: Capture region changed' } 'Invalidated crop still allowed PNG.')
        $changed = [Desktop]::Bounds($chrome)
        Drag ($changed.X+60) ($changed.Y+3) ($changed.X+48) ($changed.Y-9) $chrome
        $crop = [Desktop]::Bounds($overlay)
        Assert ($target.Contains($crop)) 'Reselected crop is outside the synthetic target.'
        Button 'Save PNG'
        $recovered = Output 'png'
        Check-Pattern $recovered ([Drawing.Rectangle]::Empty)
        'PASS display-change message interrupted and finalised the take, blocked stale PNG and required reselection'
    }

    if ($TestHotkeyRebind) {
        Button 'Hotkey'
        $dialog = Wait-For { $h = [Desktop]::Find($process.Id,'FrameInk PNG shortcut'); if ($h -ne [intptr]::Zero) { $h } } 'Hotkey settings did not open.'
        $frameRoot = $script:root
        $script:root = [Windows.Automation.AutomationElement]::FromHandle($dialog)
        $f9 = Wait-For {
            $condition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, 'F9')
            $script:root.FindAll([Windows.Automation.TreeScope]::Descendants, $condition) | Where-Object {
                $_.GetSupportedPatterns() -contains [Windows.Automation.SelectionItemPattern]::Pattern
            } | Select-Object -First 1
        } 'F9 was not available in the settings selector.'
        $f9.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
        $wroteConfig = $true
        Button 'Apply'
        $script:root = $frameRoot
        [void](Wait-For { (Labels) -match 'PNG: Ctrl\+Shift\+F9' } 'Rebound hotkey was not active.')
        Assert ([Desktop]::RegisterHotKey([intptr]::Zero,98,6,121)) 'Old hotkey was not released.'
        [void][Desktop]::UnregisterHotKey([intptr]::Zero,98)
        Button 'Save PNG'
        $before = Output 'png'
        Shortcut 120
        $after = Output 'png'
        Assert ((Get-FileHash -LiteralPath $before).Hash -eq (Get-FileHash -LiteralPath $after).Hash) 'Rebound hotkey PNG differs.'
        if (!$PngOnly) { Check-Pattern $after ([Drawing.Rectangle]::Empty) }
        Button 'Close'
        Assert ($process.WaitForExit(5000)) 'FrameInk did not shut down after rebinding.'
        $process = Start-Process -FilePath ([IO.Path]::GetFullPath($App)) -PassThru
        $chrome = Wait-For { $h = [Desktop]::Find($process.Id,'FrameInk'); if ($h -ne [intptr]::Zero) { $h } } 'FrameInk did not restart.'
        $script:root = [Windows.Automation.AutomationElement]::FromHandle($chrome)
        [void](Wait-For { (Labels) -match 'PNG: Ctrl\+Shift\+F9' } 'The rebound hotkey did not survive restart.')
        'PASS settings UI rebind, old shortcut released, rebound shortcut PNG matches and binding survives restart'
    }
} finally {
    if ($process -and !$process.HasExited) { [void]$process.CloseMainWindow(); if (!$process.WaitForExit(5000)) { $process.Kill() } }
    $form.Dispose()
    $targetPattern.Dispose()
    if ($wroteConfig) { Remove-Item -LiteralPath $config -ErrorAction SilentlyContinue }
    if (!$KeepArtifacts) { foreach ($path in $outputs) { Remove-Item -LiteralPath $path -ErrorAction SilentlyContinue } }
    [void][Desktop]::SetCursorPos($originalCursor.X,$originalCursor.Y)
    if ($oldDpi -ne [intptr]::Zero) { [void][Desktop]::SetThreadDpiAwarenessContext($oldDpi) }
}
