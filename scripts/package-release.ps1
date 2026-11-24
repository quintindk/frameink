[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$')]
    [string]$Version,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repo 'artifacts' }
$out = [IO.Path]::GetFullPath($OutputDirectory)
$name = "FrameInk-$Version-win-x64"
$staging = Join-Path ([IO.Path]::GetTempPath()) ('frameink-package-' + [Guid]::NewGuid().ToString('N'))
$bundle = Join-Path $staging $name
try {
    dotnet publish (Join-Path $repo 'src\FrameInk\FrameInk.csproj') --configuration Release --runtime win-x64 `
        --self-contained true --output $bundle "-p:Version=$Version" -p:DebugType=None -p:DebugSymbols=false -warnaserror
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }
    foreach ($required in 'FrameInk.exe', 'FrameInk.dll', 'coreclr.dll', 'PresentationFramework.dll') {
        if (!(Test-Path -LiteralPath (Join-Path $bundle $required) -PathType Leaf)) {
            throw "Published bundle is missing $required."
        }
    }

    Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $bundle

    # Redistribute the licence and notices from the runtime packs actually used by this build.
    $assets = Get-Content -LiteralPath (Join-Path $repo 'src\FrameInk\obj\project.assets.json') -Raw | ConvertFrom-Json
    $deps = Get-Content -LiteralPath (Join-Path $bundle 'FrameInk.deps.json') -Raw | ConvertFrom-Json
    foreach ($pack in 'Microsoft.NETCore.App.Runtime.win-x64', 'Microsoft.WindowsDesktop.App.Runtime.win-x64') {
        $entry = $deps.libraries.PSObject.Properties.Name | Where-Object { $_ -like "runtimepack.$pack/*" }
        if (!$entry) { throw "Could not find the published runtime version for $pack." }
        $packVersion = ($entry -split '/')[1]
        $found = $false
        foreach ($root in $assets.packageFolders.PSObject.Properties.Name) {
            $package = Join-Path $root "$($pack.ToLowerInvariant())\$packVersion"
            if (!(Test-Path -LiteralPath $package -PathType Container)) { continue }
            $files = Get-ChildItem -LiteralPath $package -File | Where-Object { $_.Name -match '^(license|third[-.]party[-.]notices)(\.txt)?$' }
            if (!($files | Where-Object { $_.Name -match '^license' })) { continue }
            $notices = Join-Path $bundle "notices\$pack"
            [void][IO.Directory]::CreateDirectory($notices)
            $files | Copy-Item -Destination $notices
            $found = $true
            break
        }
        if (!$found) { throw "Could not locate redistribution notices for $pack $packVersion." }
    }

    $stagedZip = Join-Path $staging "$name.zip"
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($bundle, $stagedZip, [IO.Compression.CompressionLevel]::Optimal, $true)
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $stagedZip).Hash.ToLowerInvariant()
    $checksum = "$stagedZip.sha256"
    "$hash  $name.zip" | Set-Content -LiteralPath $checksum -Encoding ascii
    [void][IO.Directory]::CreateDirectory($out)
    Move-Item -LiteralPath $stagedZip -Destination $out -Force
    Move-Item -LiteralPath $checksum -Destination $out -Force
    Write-Output "Packaged $name.zip and $name.zip.sha256 in $out"
}
finally {
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
}
