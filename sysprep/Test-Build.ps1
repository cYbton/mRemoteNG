<#
.SYNOPSIS
    Tests the GitHub build of a commit against Smart App Control and builds again until Windows allows one.

.DESCRIPTION
    Waits for the newest build run of the branch's last pushed commit, downloads its zip, unpacks it to -TestPath
    and lets Windows check it there: mRemoteNG.exe --warmup, mRemoteNG.dll loaded as a DLL and the Code Integrity
    log. When Windows blocks a file, the workflow runs again for the same commit (every run gives mRemoteNG.exe and
    mRemoteNG.dll a new hash) - up to -MaxAttempts runs. The build that passed stays in -TestPath for a manual
    test; the tag job later releases exactly that run, as long as no newer run of the commit follows.
    Run it on a PC with Smart App Control on.

.EXAMPLE
    .\sysprep\Test-Build.ps1 -TestPath F:\Hyper-V\mRemoteNG-test -ConfCons F:\Hyper-V\mRemoteNG\confCons.xml
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$TestPath,
    [string]$ConfCons,
    [string]$Branch = "sysprep",
    [string]$Repo = "cYbton/mRemoteNG",
    [int]$MaxAttempts = 4
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$Workflow = "sysprep-build.yml"
$TestPath = [System.IO.Path]::GetFullPath($TestPath)

function Get-BuildRunList([string]$Sha) {
    $json = gh run list --repo $Repo --workflow $Workflow --commit $Sha --limit 50 --json databaseId,status,conclusion,headBranch,createdAt
    if ($LASTEXITCODE -ne 0) { throw "gh run list failed" }
    @($json | ConvertFrom-Json | Where-Object { $_.headBranch -eq $Branch } | Sort-Object createdAt -Descending)
}

function Wait-BuildRun([long]$RunId) {
    Write-Host "Waiting for run $RunId ..."
    gh run watch $RunId --repo $Repo --exit-status --interval 20 | Out-Null
    return ($LASTEXITCODE -eq 0)
}

function Invoke-BuildRun([string]$Sha) {
    $known = @(Get-BuildRunList -Sha $Sha | ForEach-Object { $_.databaseId })
    gh workflow run $Workflow --repo $Repo --ref $Branch | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "gh workflow run failed" }
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Seconds 4
        $new = @(Get-BuildRunList -Sha $Sha | Where-Object { $known -notcontains $_.databaseId })
        if ($new.Count -gt 0) { return $new[0].databaseId }
    }
    throw "The new run of $Workflow did not show up within 2 minutes."
}

function Test-BuildRun([long]$RunId) {
    # $true when Windows allowed mRemoteNG.exe and mRemoteNG.dll of the run
    $running = Get-Process -Name "mRemoteNG" -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith("$TestPath\", [StringComparison]::OrdinalIgnoreCase) }
    if ($running) { throw "mRemoteNG is running from $TestPath - close it first." }

    $download = "$TestPath.download"
    Remove-Item -Path $download, $TestPath -Recurse -Force -ErrorAction SilentlyContinue
    gh run download $RunId --repo $Repo --dir $download | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "gh run download $RunId failed" }
    $zip = Get-ChildItem -Path $download -Recurse -File -Filter "*.zip" | Select-Object -First 1
    if (-not $zip) { throw "Run $RunId has no zip artifact." }
    Expand-Archive -LiteralPath $zip.FullName -DestinationPath $TestPath -Force
    Remove-Item -Path $download -Recurse -Force
    if ($ConfCons) { Copy-Item -Path $ConfCons -Destination (Join-Path $TestPath "confCons.xml") -Force }

    $exe = Join-Path $TestPath "mRemoteNG.exe"
    $dll = Join-Path $TestPath "mRemoteNG.dll"
    Write-Host "Testing $($zip.Name) of run $RunId ($((Get-Item $exe).VersionInfo.Comments))"
    $start = Get-Date
    $ok = $true
    try {
        $process = Start-Process -FilePath $exe -ArgumentList "--warmup" -PassThru -Wait -WindowStyle Hidden
        if ($process.ExitCode -ne 0) { $ok = $false; Write-Warning "Warm-up: $($process.ExitCode) file(s) blocked." }
    } catch {
        $ok = $false; Write-Warning "mRemoteNG.exe could not be started: $($_.Exception.Message)"
    }

    if (-not ('SysprepBuildTest.Kernel32' -as [type])) {
        Add-Type -Namespace SysprepBuildTest -Name Kernel32 -MemberDefinition @'
[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] public static extern System.IntPtr LoadLibraryEx(string file, System.IntPtr reserved, uint flags);
[DllImport("kernel32.dll")] public static extern bool FreeLibrary(System.IntPtr module);
'@
    }
    $module = [SysprepBuildTest.Kernel32]::LoadLibraryEx($dll, [IntPtr]::Zero, 0)
    if ($module -eq [IntPtr]::Zero) {
        $ok = $false; Write-Warning "mRemoteNG.dll could not be loaded (error $([Runtime.InteropServices.Marshal]::GetLastWin32Error()))."
    } else {
        [void][SysprepBuildTest.Kernel32]::FreeLibrary($module)
    }

    Start-Sleep -Seconds 2
    $folder = Split-Path -Path $TestPath -NoQualifier
    $blocked = @(Get-WinEvent -FilterHashtable @{ LogName = "Microsoft-Windows-CodeIntegrity/Operational"; Id = 3077; StartTime = $start } -ErrorAction SilentlyContinue |
        ForEach-Object { "$($_.Properties[1].Value)" } | Where-Object { $_ -like "*$folder\*" } | Sort-Object -Unique)
    if ($blocked.Count -gt 0) {
        $ok = $false; $blocked | ForEach-Object { Write-Warning "Blocked by Smart App Control: $_" }
    }
    return $ok
}

$repoRoot = Split-Path -Path $PSScriptRoot -Parent
git -C $repoRoot fetch --quiet origin $Branch
$sha = (git -C $repoRoot rev-parse "origin/$Branch").Trim()
Write-Host "Commit $($sha.Substring(0, 8)) of $Branch"

$runs = @(Get-BuildRunList -Sha $sha)
$runId = if ($runs.Count -gt 0) { $runs[0].databaseId } else { Invoke-BuildRun -Sha $sha }

for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
    if (-not (Wait-BuildRun -RunId $runId)) { throw "Run $runId failed - see https://github.com/$Repo/actions/runs/$runId" }
    if (Test-BuildRun -RunId $runId) {
        Write-Host ""
        Write-Host "PASSED: run $runId - Windows allowed the build, it is in $TestPath." -ForegroundColor Green
        Write-Host "Test it there, then push the tag of this commit. Don't start another run of the commit before: the tag job releases the newest one."
        return
    }
    if ($attempt -lt $MaxAttempts) {
        Write-Host "Windows blocked run $runId - building again ($($attempt + 1)/$MaxAttempts)."
        $runId = Invoke-BuildRun -Sha $sha
    }
}
throw "Windows blocked all $MaxAttempts runs - try again later."
