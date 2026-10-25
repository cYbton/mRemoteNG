# Output helpers of the icon builder - the same [OK]/[SKIP]/[WARN]/[FAILED] look as the Hyper-Lab scripts,
# kept here so the icon builder works on its own in this repository.

function Write-Step {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Message,
        [ValidateSet("Info", "Success", "Warn", "Error", "Header")][string]$Level = "Info",
        [string]$Context
    )

    $prefix = if ([string]::IsNullOrWhiteSpace($Context)) { "" } else { "[$Context] " }
    $esc = [char]27
    switch ($Level) {
        "Warn"    { Write-Host "${esc}[1;38;2;255;140;0m${prefix}${Message}${esc}[0m" }
        "Success" { Write-Host "${esc}[1;32m${prefix}${Message}${esc}[0m" }
        "Error"   { Write-Host "${esc}[1;31m${prefix}${Message}${esc}[0m" }
        "Header"  { Write-Host "${esc}[1;36m${prefix}${Message}${esc}[0m" }
        default   { Write-Host "$prefix$Message" }
    }
}

function Write-Info {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Message)

    $esc = [char]27
    Write-Host "${esc}[1m[INFO] ${Message}${esc}[0m"
}

function Invoke-StepSafe {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Action
    )

    $esc = [char]27
    try {
        & $Action
        Write-Host "${esc}[1;32m[OK] ${Name}${esc}[0m"
    } catch {
        Write-Host "${esc}[1;38;2;255;140;0m[SKIP] ${Name} - $($_.Exception.Message)${esc}[0m"
    }
}

function Write-StepFailure {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Message)

    $esc = [char]27
    Write-Host "${esc}[1;31m[FAILED] ${Message}${esc}[0m"
    throw $Message
}

function Write-StepFailureNonFatal {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Message)

    $esc = [char]27
    Write-Host "${esc}[1;31m[FAILED] ${Message}${esc}[0m"
}
