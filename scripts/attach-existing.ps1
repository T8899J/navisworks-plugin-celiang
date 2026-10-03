param([Parameter(Mandatory=$true)][string]$Assembly, [string]$PluginId = 'TrayRouteExperiment.JPPM', [string[]]$PluginArguments = @(), [string]$NavisworksPath)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'host-paths.ps1')
$NavisworksPath = Resolve-Navisworks2023Path $NavisworksPath
Add-Type -Path (Join-Path $NavisworksPath 'Autodesk.Navisworks.Automation.dll')
$application = [Autodesk.Navisworks.Api.Automation.NavisworksApplication]::TryGetRunningInstance()
if ($null -eq $application) { throw 'No running Navisworks instance is available to Automation. No new application was started.' }
# Attaching must never close the user-owned application, including on exceptions.
$application.StayOpen()
try {
    $application.AddPluginAssembly((Resolve-Path -LiteralPath $Assembly).Path)
    $application.ExecuteAddInPlugin($PluginId, $PluginArguments)
} finally { $application.Dispose() }
