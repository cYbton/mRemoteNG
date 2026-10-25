# Step 1 of 2 for rebuilding mRemoteNG\Icons: exports the Citrix 2 and Network 2 icons of the
# sysprep.io network diagrams as SVG files.
#
# These icons are draw.io stencils (shapes described in stencils.min.js), not image files - so they are
# drawn by the same draw.io viewer the site uses (public/vendor/drawio) in a headless Edge, in the colours
# the site gives them (DrawioDiagram.php), and saved by render-server.py. Step 2 is Build-IconLibrary.ps1.
#
# Needs: the sysprep.io repo (default: next to this repo), Python, Microsoft Edge.
[CmdletBinding()]
param(
    [string]$SitePath  = (Join-Path $PSScriptRoot "..\..\..\sysprep.io"),
    [string]$OutFolder = (Join-Path $env:TEMP "mRemoteNG-IconBuilder\stencils"),
    [string]$EdgePath  = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
    [int]$Port = 8768,
    [int]$TimeoutSeconds = 180
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot "IconBuilder-Common.ps1")

Write-Step -Context "Icons" -Level Header -Message "Exporting the Citrix 2 and Network 2 stencils as SVG..."

$SitePath   = (Resolve-Path $SitePath).Path
$viewerPath = Join-Path $SitePath "public\vendor\drawio"
$labsCode   = Join-Path $SitePath "app\Support\Labs"
foreach ($required in (Join-Path $viewerPath "viewer-static.min.js"), (Join-Path $viewerPath "stencils.min.js"),
                      (Join-Path $labsCode "DrawioIconSets.php"), (Join-Path $labsCode "DrawioDiagram.php"), $EdgePath) {
    if (-not (Test-Path $required)) { Write-StepFailure -Message "Not found: $required" }
}
if (-not (Get-Command python -ErrorAction SilentlyContinue)) { Write-StepFailure -Message "Python not found in PATH." }

# Icon list and colours straight from the site's code, so the icons always match its diagrams:
# DrawioIconSets::CITRIX2 (name => sidebar group), DrawioDiagram::CITRIX_COLORS / NETWORK_COLORS
$iconSets = Get-Content (Join-Path $labsCode "DrawioIconSets.php") -Raw
$diagram  = Get-Content (Join-Path $labsCode "DrawioDiagram.php") -Raw
function Get-PhpColorMap([string]$Constant) {
    $body = [regex]::Match($diagram, "const $Constant = \[(.*?)\];", 'Singleline').Groups[1].Value
    $map = @{}
    foreach ($m in [regex]::Matches($body, "'([a-z0-9_ ]+)' => '(#[0-9a-fA-F]{6})'")) { $map[$m.Groups[1].Value] = $m.Groups[2].Value }
    return $map
}
$citrixColors  = Get-PhpColorMap "CITRIX_COLORS"
$networkColors = Get-PhpColorMap "NETWORK_COLORS"
$citrix = foreach ($m in [regex]::Matches($iconSets, "'([a-z0-9_]+)' => '([a-z ]+)'")) {
    [pscustomobject]@{ name = $m.Groups[1].Value; group = ($m.Groups[2].Value -replace ' ', '_'); color = $citrixColors[$m.Groups[2].Value] }
}
if (@($citrix).Count -eq 0 -or $citrixColors.Count -eq 0) { Write-StepFailure -Message "Could not read the Citrix icon list or colours from $labsCode." }
Invoke-StepSafe -Name "Read $(@($citrix).Count) Citrix 2 icons and their colours from the site's code" -Action {}

# Work folder: the page, the viewer and the list - served by render-server.py
$work = Join-Path $env:TEMP ("mRemoteNG-IconBuilder\work-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $work -Force | Out-Null
Copy-Item (Join-Path $viewerPath "viewer-static.min.js"), (Join-Path $viewerPath "stencils.min.js"), (Join-Path $PSScriptRoot "render.html") -Destination $work
@{ citrix2 = @($citrix); networks2 = @{ colors = $networkColors; default = '#e4e4e7' } } | ConvertTo-Json -Depth 5 |
    Set-Content -Path (Join-Path $work "stencil-list.json") -Encoding UTF8

if (Test-Path $OutFolder) { Remove-Item $OutFolder -Recurse -Force }
New-Item -ItemType Directory -Path $OutFolder -Force | Out-Null

$server = Start-Process -FilePath python -ArgumentList "`"$(Join-Path $PSScriptRoot 'render-server.py')`"", $Port, "`"$work`"", "`"$OutFolder`"" -WindowStyle Hidden -PassThru
Start-Sleep -Seconds 2
$profileDir = Join-Path $work "edge-profile"
Start-Process -FilePath $EdgePath -ArgumentList "--headless=new", "--disable-gpu", "--no-first-run", "--user-data-dir=`"$profileDir`"", "http://127.0.0.1:$Port/render.html"
Write-Info -Message "Drawing the stencils in a headless Edge..."

try {
    if (-not $server.WaitForExit($TimeoutSeconds * 1000)) {
        Write-StepFailure -Message "The page did not finish within $TimeoutSeconds s."
    }
} finally {
    # Edge starts several processes - all of them carry this run's own profile folder
    Get-CimInstance Win32_Process -Filter "Name = 'msedge.exe'" |
        Where-Object { $_.CommandLine -and $_.CommandLine.Contains($profileDir) } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    if (-not $server.HasExited) { Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 1
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}

$summaryPath = Join-Path $OutFolder "_summary.json"
$summary = Get-Content $summaryPath -Raw | ConvertFrom-Json
Remove-Item $summaryPath
foreach ($failure in $summary.failed) { Write-Step -Level Warn -Message "[WARN] Not exported: $failure" }
$citrixCount  = @(Get-ChildItem (Join-Path $OutFolder "citrix2") -Recurse -Filter *.svg -ErrorAction SilentlyContinue).Count
$networkCount = @(Get-ChildItem (Join-Path $OutFolder "networks2") -Filter *.svg -ErrorAction SilentlyContinue).Count
Invoke-StepSafe -Name "Exported $citrixCount Citrix 2 and $networkCount Network 2 SVGs to $OutFolder" -Action {}
