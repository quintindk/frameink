[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidatePattern('^v[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version,
    [string]$InstallRoot,
    [switch]$DesktopShortcut,
    [switch]$NoShortcut,
    [switch]$RequireSignature
)

$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne 'Win32NT') { throw 'Run this installer on Windows.' }
if ($NoShortcut -and $DesktopShortcut) { throw 'Choose either -NoShortcut or -DesktopShortcut, not both.' }
if (!$InstallRoot) { $InstallRoot = Join-Path $env:LOCALAPPDATA 'Programs\FrameInk' }
$root = [IO.Path]::GetFullPath($InstallRoot)
$repo = 'quintindk/frameink'
if (!$Version) {
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/latest" -UserAgent 'FrameInk installer'
    $Version = $release.tag_name
}
if ($Version -notmatch '^v[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$') { throw "Unexpected release tag: $Version" }
$name = "FrameInk-$($Version.Substring(1))-win-x64"
$folder = Join-Path $root $Version
$exe = Join-Path $folder 'FrameInk.exe'
$marker = Join-Path $folder '.frameink-install.sha256'
if (!$PSCmdlet.ShouldProcess($folder, "Install FrameInk $Version")) { return }

function Confirm-FrameInkSignature([string]$file) {
    $signature = Get-AuthenticodeSignature -LiteralPath $file
    if ($signature.Status -eq 'NotSigned' -and !$RequireSignature) {
        Write-Warning 'This FrameInk release is unsigned. Windows may show a publisher warning.'
    } elseif ($signature.Status -ne 'Valid') {
        throw "The executable does not have a valid Windows signature: $($signature.Status)."
    }
}

$download = "https://github.com/$repo/releases/download/$Version"
[void][IO.Directory]::CreateDirectory($root)
$work = Join-Path $root ('.install-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($work)
try {
    $check = Join-Path $work "$name.zip.sha256"
    Invoke-WebRequest -Uri "$download/$name.zip.sha256" -UseBasicParsing -OutFile $check
    $expected = (Get-Content -LiteralPath $check -Raw).Trim()
    if ($expected -notmatch ('^([0-9a-fA-F]{64})\s+' + [regex]::Escape("$name.zip") + '$')) {
        throw 'Unexpected SHA-256 checksum file.'
    }
    $sha = $Matches[1]
    if (Test-Path -LiteralPath $folder) {
        if (!(Test-Path -LiteralPath $exe -PathType Leaf) -or !(Test-Path -LiteralPath $marker -PathType Leaf) -or
            (Get-Content -LiteralPath $marker -Raw).Trim() -ne $sha) {
            throw "Existing folder does not match this installation: $folder. Move it out of the way or choose a new -InstallRoot."
        }
        Confirm-FrameInkSignature $exe
    } else {
        $zip = Join-Path $work "$name.zip"
        Invoke-WebRequest -Uri "$download/$name.zip" -UseBasicParsing -OutFile $zip
        if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $sha) { throw 'ZIP failed SHA-256 verification.' }
        $expanded = Join-Path $work 'package'
        Expand-Archive -LiteralPath $zip -DestinationPath $expanded
        $bundle = Join-Path $expanded $name
        $candidate = Join-Path $bundle 'FrameInk.exe'
        if (!(Test-Path -LiteralPath $candidate -PathType Leaf)) { throw 'The release ZIP did not contain FrameInk.exe in the expected folder.' }
        Confirm-FrameInkSignature $candidate
        Set-Content -LiteralPath (Join-Path $bundle '.frameink-install.sha256') -Value $sha -Encoding ascii
        [IO.Directory]::Move($bundle, $folder)
    }
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force
}

function Set-FrameInkShortcut([string]$directory) {
    if (!$directory) { throw 'Could not find the Windows shortcut directory.' }
    $shortcutFile = Join-Path $directory 'FrameInk.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutFile)
    $prefix = $root.TrimEnd('\') + '\'
    if ((Test-Path -LiteralPath $shortcutFile) -and !$shortcut.TargetPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        Write-Warning "Kept the existing shortcut: $shortcutFile"
        return
    }
    $shortcut.TargetPath = $exe
    $shortcut.WorkingDirectory = $folder
    $shortcut.IconLocation = "$exe,0"
    $shortcut.Description = 'FrameInk screen recorder'
    $shortcut.Save()
}

if (!$NoShortcut) { Set-FrameInkShortcut ([Environment]::GetFolderPath('Programs')) }
if ($DesktopShortcut) { Set-FrameInkShortcut ([Environment]::GetFolderPath('DesktopDirectory')) }
Write-Output "FrameInk $Version is installed at $exe"
