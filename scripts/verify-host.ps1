param([ValidateSet('mm','m')][string]$Unit = 'mm', [string]$NavisworksPath, [switch]$ShowUI)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'host-paths.ps1')
$NavisworksPath = Resolve-Navisworks2023Path $NavisworksPath
$dll = Join-Path $root 'bin\Release\JiePinPai.TrayMeasurement.dll'
$probe = Join-Path $root 'tests\bin\HostProbe\TrayHostProbe.dll'
$fixture = Join-Path $root "artifacts\tray-$Unit.ifc"
$result = Join-Path $root ("artifacts\host-$Unit-" + [DateTime]::Now.ToString('yyyyMMdd-HHmmss') + '.txt')
$arguments = '-AddPluginAssembly "' + $dll + '" -AddPluginAssembly "' + $probe + '" -OpenFile "' + $fixture + '" -ExecuteAddInPlugin TrayHostProbe.JPPM "' + $result + '"'
if ($ShowUI) { $arguments += ' ui' } else { $arguments = '-HideGui ' + $arguments + ' -Exit' }
$process = Start-Process -FilePath (Join-Path $NavisworksPath 'Roamer.exe') -ArgumentList $arguments -WorkingDirectory $root -WindowStyle Hidden -PassThru
Write-Host "PROBE PID: $($process.Id)"
Write-Host "RESULT FILE: $result"
if ($ShowUI) {
    $deadline = [DateTime]::UtcNow.AddSeconds(120)
    while (-not (Test-Path -LiteralPath $result) -and [DateTime]::UtcNow -lt $deadline -and -not $process.HasExited) { Start-Sleep -Milliseconds 500 }
} elseif (-not $process.WaitForExit(120000)) { throw "测试宿主尚未退出，PID=$($process.Id)。请检查窗口；未终止任何进程。" }
if (-not (Test-Path -LiteralPath $result)) { throw "宿主没有输出验证报告；退出码 $($process.ExitCode)。" }
Get-Content -LiteralPath $result -Encoding UTF8
if (-not (Select-String -LiteralPath $result -SimpleMatch 'RESULT: PASS')) { throw '宿主验证未通过。' }
