param([string]$NavisworksPath, [string]$ModelPath)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts\host-paths.ps1')
$NavisworksPath = Resolve-Navisworks2023Path $NavisworksPath
$dll = Join-Path $PSScriptRoot 'bin\Release\JiePinPai.TrayMeasurement.dll'
if (-not (Test-Path -LiteralPath $dll)) { throw '请先运行 build_2023.ps1。' }
$arguments = '-AddPluginAssembly "' + $dll + '"'
if ($ModelPath) {
    $model = (Resolve-Path -LiteralPath $ModelPath).Path
    $arguments += ' -OpenFile "' + $model + '"'
}
$arguments += ' -ExecuteAddInPlugin JiePinPai_TrayMeasurement.JPPM'
Start-Process -FilePath (Join-Path $NavisworksPath 'Roamer.exe') -ArgumentList $arguments -WorkingDirectory $PSScriptRoot | Out-Null
