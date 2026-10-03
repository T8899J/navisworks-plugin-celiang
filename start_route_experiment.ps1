param([string]$NavisworksPath, [switch]$Demo)
$ErrorActionPreference = 'Stop'
$assembly = Join-Path $PSScriptRoot 'experiments\bin\RouteExperiment\JiePinPai.TrayRouteExperiment.dll'
if (-not (Test-Path -LiteralPath $assembly)) { throw '请先运行 build_route_experiment.ps1 构建路径实验。' }
$arguments = @()
if ($Demo) {
    $artifacts = Join-Path $PSScriptRoot 'artifacts'
    New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
    $arguments = @(Join-Path $artifacts ('route-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss') + '.json'))
}
& (Join-Path $PSScriptRoot 'scripts\attach-existing.ps1') -Assembly $assembly -PluginId 'TrayRouteExperiment.JPPM' -PluginArguments $arguments -NavisworksPath $NavisworksPath
