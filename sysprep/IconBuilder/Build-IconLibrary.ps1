# Step 2 of 2 for rebuilding mRemoteNG\Icons: converts the icons of the sysprep.io network diagrams
# into .ico files for mRemoteNG, in draw.io's own folders and names:
#   azure2\<category>\<Name>.ico   from the site's public\vendor\drawio\img\lib\azure2\**\*.svg
#   citrix2\<group>\<name>.ico     from the SVGs of Export-StencilSvgs.ps1 (step 1)
#   networks2\<name>.ico           from the SVGs of Export-StencilSvgs.ps1 (step 1)
# Icon=<that path without .ico> in a LabConfig file then names the same icon for mRemoteNG and the diagram.
#
# Every .ico holds 16, 24, 32, 48 and 256 px, each rendered by Inkscape straight from the SVG (sharper than
# scaling one big picture down): 16-48 as classic bitmaps, 256 as PNG. The thin Citrix line icons get an
# outline in their own colour at 16-32 px so they stay readable in mRemoteNG's tree; from 48 px they are
# exactly as in the diagram. Icons\mRemoteNG (mRemoteNG's own icons) is never touched; icons draw.io has
# dropped are not removed.
#
# Needs: the sysprep.io repo (default: next to this repo), Inkscape. A new set of icons ships with the next
# release of this build (mRemoteNG\Icons is copied into the program folder).
[CmdletBinding()]
param(
    [string]$SitePath      = (Join-Path $PSScriptRoot "..\..\..\sysprep.io"),
    [string]$StencilFolder = (Join-Path $env:TEMP "mRemoteNG-IconBuilder\stencils"),
    [string]$OutFolder     = (Join-Path $PSScriptRoot "..\..\mRemoteNG\Icons"),
    [string]$InkscapePath  = "$env:ProgramFiles\Inkscape\bin\inkscape.exe",
    [int]$Parallel = 6
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot "IconBuilder-Common.ps1")
Add-Type -AssemblyName System.Drawing

Write-Step -Context "Icons" -Level Header -Message "Building the mRemoteNG icon library..."

$azureSource = Join-Path (Resolve-Path $SitePath).Path "public\vendor\drawio\img\lib\azure2"
foreach ($required in $azureSource, (Join-Path $StencilFolder "citrix2"), (Join-Path $StencilFolder "networks2"), $InkscapePath) {
    if (-not (Test-Path $required)) { Write-StepFailure -Message "Not found: $required$(if ($required -like "$StencilFolder*") { ' - run Export-StencilSvgs.ps1 first' })" }
}
$OutFolder = (New-Item -ItemType Directory -Path $OutFolder -Force).FullName

$sizes = 16, 24, 32, 48, 256
# Outline for the Citrix icons per size, in SVG units of their 256 canvas
$citrixStroke = @{ 16 = 12; 24 = 8; 32 = 6 }

$work = Join-Path $env:TEMP ("mRemoteNG-IconBuilder\build-" + [guid]::NewGuid().ToString("N"))
$pngDir = Join-Path $work "png"; $varDir = Join-Path $work "variants"; $cmdDir = Join-Path $work "cmd"
foreach ($d in $pngDir, $varDir, $cmdDir) { New-Item -ItemType Directory -Path $d -Force | Out-Null }

function Get-SvgAspect([string]$Path) {
    $text = Get-Content -Path $Path -Raw
    $m = [regex]::Match($text, 'viewBox="\s*[-\d.]+[\s,]+[-\d.]+[\s,]+([\d.]+)[\s,]+([\d.]+)"')
    if ($m.Success) { return [double]$m.Groups[1].Value / [double]$m.Groups[2].Value }
    $w = [regex]::Match($text, '\swidth="([\d.]+)'); $h = [regex]::Match($text, '\sheight="([\d.]+)')
    if ($w.Success -and $h.Success) { return [double]$w.Groups[1].Value / [double]$h.Groups[1].Value }
    return 1.0
}

# --- One job per icon -------------------------------------------------------------------------
$jobs = [System.Collections.Generic.List[object]]::new()
foreach ($svg in Get-ChildItem -Path $azureSource -Filter *.svg -Recurse) {
    $jobs.Add([pscustomobject]@{ Svg = $svg.FullName; Folder = "azure2\$($svg.Directory.Name)"; Name = $svg.BaseName; Stroke = $false })
}
foreach ($svg in Get-ChildItem -Path (Join-Path $StencilFolder "citrix2") -Filter *.svg -Recurse) {
    $jobs.Add([pscustomobject]@{ Svg = $svg.FullName; Folder = "citrix2\$($svg.Directory.Name)"; Name = $svg.BaseName; Stroke = $true })
}
foreach ($svg in Get-ChildItem -Path (Join-Path $StencilFolder "networks2") -Filter *.svg) {
    $jobs.Add([pscustomobject]@{ Svg = $svg.FullName; Folder = "networks2"; Name = $svg.BaseName; Stroke = $false })
}
Write-Info -Message "Rendering $($jobs.Count) icons in $($sizes.Count) sizes with Inkscape..."

# --- Inkscape shell commands, split over parallel processes -----------------------------------
$lines = [System.Collections.Generic.List[string]]::new()
for ($i = 0; $i -lt $jobs.Count; $i++) {
    $job = $jobs[$i]
    $job | Add-Member -NotePropertyName Id -NotePropertyValue $i
    $aspect = Get-SvgAspect $job.Svg
    foreach ($size in $sizes) {
        $source = $job.Svg
        if ($job.Stroke -and $citrixStroke.ContainsKey($size)) {
            # every filled shape gets an outline in its own fill colour
            $text = [regex]::Replace((Get-Content -Path $job.Svg -Raw), '<(path|ellipse|rect|circle)\b[^>]*>', {
                param($m)
                $el = $m.Value
                $fill = [regex]::Match($el, 'fill="(#[0-9a-fA-F]{6})"')
                if ($fill.Success -and $el -match 'stroke="none"') {
                    $el = $el -replace 'stroke="none"', ('stroke="{0}" stroke-width="{1}" stroke-linejoin="round"' -f $fill.Groups[1].Value, $citrixStroke[$size])
                }
                $el
            })
            $source = Join-Path $varDir ("{0}-{1}.svg" -f $i, $size)
            [IO.File]::WriteAllText($source, $text)
        }
        $w = if ($aspect -ge 1) { $size } else { [Math]::Max(1, [int][Math]::Round($size * $aspect)) }
        $h = if ($aspect -ge 1) { [Math]::Max(1, [int][Math]::Round($size / $aspect)) } else { $size }
        $png = Join-Path $pngDir ("{0}-{1}.png" -f $i, $size)
        $lines.Add(("file-open:{0}; export-filename:{1}; export-width:{2}; export-height:{3}; export-do; file-close" -f ($source -replace '\\', '/'), ($png -replace '\\', '/'), $w, $h))
    }
}
$chunk = [int][Math]::Ceiling($lines.Count / $Parallel)
$procs = for ($p = 0; $p -lt $Parallel; $p++) {
    $part = @($lines | Select-Object -Skip ($p * $chunk) -First $chunk)
    if ($part.Count -eq 0) { continue }
    $cmdFile = Join-Path $cmdDir "shell-$p.txt"
    [IO.File]::WriteAllLines($cmdFile, [string[]]($part + "quit"))
    Start-Process -FilePath $InkscapePath -ArgumentList "--shell" -RedirectStandardInput $cmdFile `
        -RedirectStandardOutput (Join-Path $cmdDir "out-$p.txt") -RedirectStandardError (Join-Path $cmdDir "err-$p.txt") -NoNewWindow -PassThru
}
$procs | Wait-Process
Invoke-StepSafe -Name "Rendered $($lines.Count) PNGs" -Action {}

# --- ICO assembly -----------------------------------------------------------------------------
function Get-SquareBitmap([string]$Png, [int]$Size) {
    $src = [System.Drawing.Bitmap]::FromFile($Png)
    $square = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($square)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($src, [int](($Size - $src.Width) / 2), [int](($Size - $src.Height) / 2), $src.Width, $src.Height)
    $g.Dispose(); $src.Dispose()
    return $square
}
function Get-DibBytes([System.Drawing.Bitmap]$Bmp) {
    $w = $Bmp.Width; $h = $Bmp.Height; $stride = $w * 4
    $maskRow = [int]([Math]::Ceiling($w / 32.0) * 4)
    $data = $Bmp.LockBits((New-Object System.Drawing.Rectangle 0, 0, $w, $h), [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $pixels = New-Object byte[] ($stride * $h)
    [Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $Bmp.UnlockBits($data)
    $ms = New-Object IO.MemoryStream; $bw = New-Object IO.BinaryWriter $ms
    $bw.Write([int]40); $bw.Write([int]$w); $bw.Write([int]($h * 2)); $bw.Write([int16]1); $bw.Write([int16]32)
    $bw.Write([int]0); $bw.Write([int]($stride * $h + $maskRow * $h)); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
    for ($y = $h - 1; $y -ge 0; $y--) { $bw.Write($pixels, $y * $stride, $stride) }   # DIB rows bottom-up, BGRA as in memory
    $bw.Write((New-Object byte[] ($maskRow * $h)))
    $bw.Flush()
    return ,$ms.ToArray()
}

$failed = [System.Collections.Generic.List[string]]::new()
foreach ($job in $jobs) {
    try {
        $images = foreach ($size in $sizes) {
            $png = Join-Path $pngDir ("{0}-{1}.png" -f $job.Id, $size)
            if (-not (Test-Path $png)) { throw "PNG missing for size $size" }
            $bmp = Get-SquareBitmap $png $size
            if ($size -eq 256) { $ms = New-Object IO.MemoryStream; $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); [byte[]]$bytes = $ms.ToArray() }
            else { [byte[]]$bytes = Get-DibBytes $bmp }
            $bmp.Dispose()
            [pscustomobject]@{ Size = $size; Bytes = $bytes }
        }
        $dir = Join-Path $OutFolder $job.Folder
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        $ico = Join-Path $dir ($job.Name + ".ico")
        $fs = [IO.File]::Create($ico); $bw = New-Object IO.BinaryWriter $fs
        $bw.Write([int16]0); $bw.Write([int16]1); $bw.Write([int16]$images.Count)
        $offset = 6 + 16 * $images.Count
        foreach ($img in $images) {
            $dim = if ($img.Size -ge 256) { 0 } else { $img.Size }
            $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
            $bw.Write([int16]1); $bw.Write([int16]32); $bw.Write([int]$img.Bytes.Length); $bw.Write([int]$offset)
            $offset += $img.Bytes.Length
        }
        foreach ($img in $images) { $bw.Write([byte[]]$img.Bytes) }
        $bw.Close()
        # the loader mRemoteNG itself uses - fails here if the file is malformed
        (New-Object System.Drawing.Icon $ico, 16, 16).Dispose()
    } catch {
        $failed.Add("$($job.Folder)\$($job.Name): $($_.Exception.Message)")
    }
}
Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue

foreach ($failure in $failed) { Write-StepFailureNonFatal -Message $failure }
Invoke-StepSafe -Name "$($jobs.Count - $failed.Count) of $($jobs.Count) icons written to $OutFolder" -Action {}
