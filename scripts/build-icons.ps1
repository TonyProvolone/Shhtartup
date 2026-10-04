# Rebuilds the .ico files in src\Assets from the SVGs next to them:
#   AppIcon.svg -> AppIcon.ico            (exe, shortcuts, Installed apps, window title bars)
#   Glyph.svg   -> TrayDark.ico (white)   (tray icon on a dark taskbar)
#                  TrayLight.ico (black)  (tray icon on a light taskbar)
#
# Headless Edge draws every size of an icon side by side on one page (so small sizes are rasterised
# at their real pixel size, not scaled down from a big one); each size is then cropped out and the
# PNGs are packed into the .ico.
#
# Run it after editing either SVG: powershell -ExecutionPolicy Bypass -File scripts\build-icons.ps1
# Needs Microsoft Edge.

#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$Root = Split-Path -Parent $PSScriptRoot
$Assets = Join-Path $Root 'src\Assets'

$AppSizes = 16, 20, 24, 32, 40, 48, 64, 256
# Small-icon sizes for 100% to 300% display scaling.
$TraySizes = 16, 20, 24, 28, 32, 36, 40, 48
$Gap = 8
# At this size and below, SVG elements with class="detail" are left out (they only blur when tiny).
$SmallSize = 20

$Edge = @(
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $Edge) { throw 'Microsoft Edge not found.' }

$Work = Join-Path ([IO.Path]::GetTempPath()) "shhtartup-icons-$PID"
New-Item -ItemType Directory -Force $Work | Out-Null

function Stop-RenderEdge {
    Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" |
        Where-Object { $_.CommandLine -like "*$Work*" } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
}

# Screenshots an HTML page with a transparent background. Headless Edge now and then never exits
# (or never writes the file), so each attempt gets a time limit and a retry. A fresh profile each
# time, because a killed instance can leave its profile locked and stall the next launch.
function Save-Screenshot([string]$html, [string]$png, [int]$width, [int]$height) {
    for ($attempt = 1; $attempt -le 6; $attempt++) {
        if (Test-Path $png) { Remove-Item $png }
        $psi = New-Object Diagnostics.ProcessStartInfo $Edge
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true
        $psi.Arguments = "--headless=new --disable-gpu --hide-scrollbars --no-first-run " +
            "--user-data-dir=`"$Work\profile-$([guid]::NewGuid())`" --force-device-scale-factor=1 " +
            "--default-background-color=00000000 --window-size=$width,$height " +
            "--screenshot=`"$png`" `"$(([Uri]$html).AbsoluteUri)`""
        $proc = [Diagnostics.Process]::Start($psi)
        $proc.WaitForExit(8000) | Out-Null
        Start-Sleep -Milliseconds 300
        Stop-RenderEdge
        if (Test-Path $png) { return }
        Start-Sleep -Seconds 1
        Write-Host "Edge didn't produce a screenshot (attempt $attempt), retrying..."
    }
    throw 'Edge did not render the icons.'
}

# Renders the SVG at each size and returns one PNG (byte array) per size.
function ConvertTo-Pngs([string]$svg, [string]$color, [int[]]$sizes) {
    $html = Join-Path $Work 'render.html'
    $sheet = Join-Path $Work 'sheet.png'
    $x = 0
    $tiles = foreach ($size in $sizes) {
        # Each copy needs its own mask id.
        $copy = $svg -replace 'ear-guard', "ear-guard-$size"
        $class = if ($size -le $SmallSize) { 'small' } else { '' }
        "<div class=`"$class`" style=`"position:absolute;left:${x}px;top:0;width:${size}px;height:${size}px`">$copy</div>"
        $x += $size + $Gap
    }
    $height = ($sizes | Measure-Object -Maximum).Maximum
    $page = "<!doctype html><html><head><style>html,body{margin:0;background:transparent;overflow:hidden}" +
        "svg{display:block;width:100%;height:100%;color:$color}.small .detail{display:none}</style></head><body>$($tiles -join '')</body></html>"
    [IO.File]::WriteAllText($html, $page, [Text.Encoding]::UTF8)
    Save-Screenshot $html $sheet $x $height

    $bitmap = [Drawing.Bitmap]::FromFile($sheet)
    try {
        $x = 0
        foreach ($size in $sizes) {
            $crop = $bitmap.Clone((New-Object Drawing.Rectangle $x, 0, $size, $size), [Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $stream = New-Object IO.MemoryStream
            $crop.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
            $crop.Dispose()
            , $stream.ToArray()
            $x += $size + $Gap
        }
    }
    finally {
        $bitmap.Dispose()
    }
}

# Packs PNG images into an .ico file (PNG-compressed entries, supported since Windows Vista).
function Write-Ico([string]$path, [int[]]$sizes, [byte[][]]$pngs) {
    $stream = New-Object IO.MemoryStream
    $w = New-Object IO.BinaryWriter $stream
    $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
        $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([uint16]1); $w.Write([uint16]32)
        $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
        $offset += $pngs[$i].Length
    }
    foreach ($png in $pngs) { $w.Write($png) }
    $w.Flush()
    [IO.File]::WriteAllBytes($path, $stream.ToArray())
    Write-Host "Wrote $path"
}

function New-Ico([string]$svgName, [string]$color, [int[]]$sizes, [string]$icoName) {
    $svg = [IO.File]::ReadAllText((Join-Path $Assets $svgName)) -replace '(?s)<!--.*?-->', ''
    $pngs = ConvertTo-Pngs $svg $color $sizes
    Write-Ico (Join-Path $Assets $icoName) $sizes $pngs
}

try {
    New-Ico 'AppIcon.svg' '#ffffff' $AppSizes 'AppIcon.ico'
    New-Ico 'Glyph.svg' '#ffffff' $TraySizes 'TrayDark.ico'
    New-Ico 'Glyph.svg' '#1a1a1a' $TraySizes 'TrayLight.ico'
}
finally {
    Stop-RenderEdge
    Remove-Item -Recurse -Force $Work -ErrorAction SilentlyContinue
}
