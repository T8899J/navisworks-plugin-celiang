param([string]$NavisworksPath, [Parameter(Mandatory=$true)][string]$Query, [string]$Model, [string]$Report, [switch]$Spatial, [string]$PluginAssembly)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'host-paths.ps1')
$NavisworksPath = Resolve-Navisworks2023Path $NavisworksPath
$saved = Get-Content -LiteralPath $Query -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $Model) { $Model = $saved.model }
if (-not $Report) { $Report = Join-Path $root ('artifacts\connectivity-model-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss') + '.json') }
if (-not $saved.start -or -not $saved.finish) { throw 'Query must contain saved start, finish and recognized metadata.' }
$culture = [Globalization.CultureInfo]::InvariantCulture
function Format-Point($point) { return 'xyz:' + ((([double]$point.X).ToString('R',$culture),([double]$point.Y).ToString('R',$culture),([double]$point.Z).ToString('R',$culture)) -join ',') }
$startId = $saved.recognized[$saved.start.Piece].ModelItemId
$finishId = $saved.recognized[$saved.finish.Piece].ModelItemId
$assembly = Join-Path $root 'experiments\bin\CablePathV13\JiePinPai.CablePathExperimentV13.dll'
# A user's open Navisworks may lock the default DLL; -Assembly replays a build from another folder.
if ($PluginAssembly) { $assembly = (Resolve-Path -LiteralPath $PluginAssembly).Path }
Write-Host "PLUGIN DLL: $assembly"
$Model = (Resolve-Path -LiteralPath $Model).Path
$Report = [IO.Path]::GetFullPath($Report)
$startPoint = Format-Point $saved.start.Point
$finishPoint = Format-Point $saved.finish.Point
foreach ($value in @($assembly,$Model,$Report,$startId,$finishId,$startPoint,$finishPoint)) { if ($value.Contains('"')) { throw 'Embedded quotes are not supported.' } }
$arguments = '-HideGui -AddPluginAssembly "' + $assembly + '" -OpenFile "' + $Model + '" -ExecuteAddInPlugin CableGraphProbeV13.JPPM "' + $Report + '" "' + $startId + '" "' + $finishId + '" "' + $startPoint + '" "' + $finishPoint + '" "diagnose-physical"' + $(if ($Spatial) { ' "spatial"' } else { '' }) + ' -Exit'
# Open a read-only copy in a separate host; never alter the user's running document or save the model.
$process = Start-Process -FilePath (Join-Path $NavisworksPath 'Roamer.exe') -ArgumentList $arguments -WorkingDirectory $root -WindowStyle Hidden -PassThru
Write-Host "MODEL DIAGNOSTIC HOST PID: $($process.Id)"
Write-Host "REPORT: $Report"
if (-not $process.WaitForExit(300000)) { throw "Diagnostic host has not exited (PID $($process.Id)). No process was terminated." }
if (-not (Test-Path -LiteralPath $Report)) { throw "No report was written; host exit code $($process.ExitCode)." }
$result = Get-Content -LiteralPath $Report -Raw -Encoding UTF8 | ConvertFrom-Json
Write-Host ('Recognized: {0}; Rejected: {1}; Physical components: {2}; Incomplete: {3}' -f $result.recognized.Count,$result.rejected.Count,$result.physicalComponentCount,$result.incomplete)
if ($result.result) {
    if (-not $result.success) { throw "Route probe failed: $($result.error)" }
    if ($result.reverseCostConsistent -ne $true -or -not $result.reverseResult) { throw 'Forward/reverse route cost consistency was not verified.' }
    if (($result.pathCostOrder -join ',') -ne 'TotalLength,VerticalTravel,VirtualConnectorCount,VirtualConnectorTotalLength,GapBridgeCount') { throw 'Host does not use the V13 route objective.' }
    foreach ($metric in @('Length','VerticalTravel','VirtualConnectorCount','VirtualConnectorTotalLength','GapBridgeCount')) {
        if ($result.result.$metric -ne $result.reverseResult.$metric) { throw "Forward/reverse $metric differs." }
    }
    $routeVertical = 0.0
    foreach ($step in $result.result.Steps) {
        $stepVertical = 0.0
        for ($pointIndex=1; $pointIndex -lt $step.Centerline.Count; $pointIndex++) { $stepVertical += [Math]::Abs($step.Centerline[$pointIndex].Z-$step.Centerline[$pointIndex-1].Z) }
        if ([Math]::Abs($step.VerticalTravel-$stepVertical) -gt 1e-12) { throw 'Edge VerticalTravel does not include its complete polyline.' }
        $routeVertical += $stepVertical
    }
    if ([Math]::Abs($result.result.VerticalTravel-$routeVertical) -gt 1e-10) { throw 'Route VerticalTravel differs from its traversed polylines.' }
    Write-Host ('Candidate route: {0:F6} m; VerticalTravel={1:F6} m; {2} VirtualConnectors; {4} SpliceBridges; RequiresReview={3}; forward/reverse exactly consistent' -f $result.result.Length,$result.result.VerticalTravel,$result.result.VirtualConnectorCount,$result.result.RequiresReview,$result.result.SpliceBridgeCount)
}
if ($result.connectivityDiagnostics) { Write-Host ('Start component: {0}; Finish component: {1}; Breakpoint candidates: {2}' -f $result.connectivityDiagnostics.StartPhysicalComponent,$result.connectivityDiagnostics.FinishPhysicalComponent,$result.connectivityDiagnostics.Candidates.Count) }
if ($result.error) { Write-Host ('MODEL DIAGNOSTIC: ' + $result.error) }
$result.rejectionSummary | Format-Table -AutoSize
