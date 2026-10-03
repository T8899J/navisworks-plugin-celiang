param([string]$NavisworksPath, [string]$DemoBranch, [string]$DemoMain)
$ErrorActionPreference = 'Stop'
$assembly = Join-Path $PSScriptRoot 'experiments\bin\CablePathV2\JiePinPai.CablePathExperimentV2.dll'
if (-not (Test-Path -LiteralPath $assembly)) { throw '请先运行 build_cable_path_experiment.ps1。' }
$arguments = @()
if ($DemoBranch -or $DemoMain) {
    if (-not $DemoBranch -or -not $DemoMain) { throw '样本实验需要同时指定 DemoBranch 和 DemoMain 构件名称。' }
    $artifacts = Join-Path $PSScriptRoot 'artifacts'
    New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
    $arguments = @((Join-Path $artifacts ('cable-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss') + '.json')), $DemoBranch, $DemoMain)
}
& (Join-Path $PSScriptRoot 'scripts\attach-existing.ps1') -Assembly $assembly -PluginId 'CablePathExperimentV2.JPPM' -PluginArguments $arguments -NavisworksPath $NavisworksPath
