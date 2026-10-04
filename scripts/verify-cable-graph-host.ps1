param([string]$NavisworksPath, [string]$Manifest, [string]$Model, [string]$Report, [switch]$VerifyVisibility)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'host-paths.ps1')
$NavisworksPath = Resolve-Navisworks2023Path $NavisworksPath
if (-not $Model) { $Model = Join-Path $root 'artifacts\port-graph-fixture.ifc' }
if (-not $Manifest) { $Manifest = Join-Path $root 'artifacts\port-graph-fixture.json' }
if (-not $Report) { $Report = Join-Path $root ('artifacts\port-graph-host-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss') + '.json') }
$fixture = Get-Content -LiteralPath $Manifest -Raw -Encoding UTF8 | ConvertFrom-Json
$culture = [Globalization.CultureInfo]::InvariantCulture
function Format-Point($p) { return 'xyz:' + ((([double]$p.X).ToString('R', $culture), ([double]$p.Y).ToString('R', $culture), ([double]$p.Z).ToString('R', $culture)) -join ',') }
$assembly = Join-Path $root 'experiments\bin\CablePathV13\JiePinPai.CablePathExperimentV13.dll'
foreach ($path in @($assembly, $Model)) { if (-not (Test-Path -LiteralPath $path)) { throw "Missing input: $path" } }
$values = @($assembly, $Model, $Report, $fixture.startName, $fixture.finishName, (Format-Point $fixture.start), (Format-Point $fixture.finish))
if ($values | Where-Object { $_.Contains('"') }) { throw 'Embedded quotes are not supported in test paths or names.' }
$probeFlags = if ($VerifyVisibility) { ' "check-visibility"' } else { '' }
$arguments = '-HideGui -AddPluginAssembly "' + $assembly + '" -OpenFile "' + $Model + '" -ExecuteAddInPlugin CableGraphProbeV13.JPPM "' + $Report + '" "' + $fixture.startName + '" "' + $fixture.finishName + '" "' + (Format-Point $fixture.start) + '" "' + (Format-Point $fixture.finish) + '"' + $probeFlags + ' -Exit'
# A separate test process loads only the generated fixture. Existing user documents are untouched.
$process = Start-Process -FilePath (Join-Path $NavisworksPath 'Roamer.exe') -ArgumentList $arguments -WorkingDirectory $root -WindowStyle Hidden -PassThru
Write-Host "TEST HOST PID: $($process.Id)"
Write-Host "REPORT: $Report"
if (-not $process.WaitForExit(180000)) { throw "Test host has not exited (PID $($process.Id)). No process was terminated." }
if (-not (Test-Path -LiteralPath $Report)) { throw "Host produced no report; exit code $($process.ExitCode)." }
$result = Get-Content -LiteralPath $Report -Raw -Encoding UTF8 | ConvertFrom-Json
if ($fixture.expectedFailure) {
    $diagnostics=$result.connectivityDiagnostics
    if ($result.success -or $result.result -or -not $diagnostics) { throw 'Expected typed failed-route connectivity diagnostics.' }
    if ($result.incomplete -or $result.recognized.Count -ne $fixture.items.Count) { throw 'Failure fixture was not completely recognized.' }
    if ($diagnostics.StartPhysicalComponent -eq $diagnostics.FinishPhysicalComponent -or $diagnostics.BoundaryPorts.Count -ne 4) { throw 'Physical components or free boundary ports are incorrect.' }
    if ([Math]::Abs($diagnostics.Candidates[0].Distance3D-$fixture.expectedNearestDistance) -gt 0.00001) { throw 'Nearest physical breakpoint differs from the independent fixture.' }
    if ($diagnostics.Candidates[0].KindName -ne 'PortToSegment3D') { throw 'Failure should expose a branch-to-centreline breakpoint.' }
    foreach ($candidate in $diagnostics.Candidates) {
        if ($candidate.Status -ne 'DiagnosticOnly' -or -not $candidate.RequiresReview -or $candidate.Confirmed -or $candidate.CandidateRank -lt 1 -or $candidate.CandidateRank -gt 5) { throw 'Failed-route candidate is incorrectly confirmed or ranked.' }
        $xyz=[Math]::Sqrt($candidate.DeltaX*$candidate.DeltaX+$candidate.DeltaY*$candidate.DeltaY+$candidate.DeltaZ*$candidate.DeltaZ)
        if ([Math]::Abs($xyz-$candidate.Distance3D) -gt 1e-10) { throw 'Failed-route candidate uses an incorrect XYZ distance.' }
    }
    Write-Host ('PASS: failed route exposes {0} physical components, {1} boundary ports, {2} candidates; nearest breakpoint {3:F9} m.' -f $result.physicalComponentCount,$diagnostics.BoundaryPorts.Count,$diagnostics.Candidates.Count,$diagnostics.Candidates[0].Distance3D)
    return
}
if (-not $result.success) { throw "Host reconstruction failed: $($result.error). Inspect rejected entries in $Report" }
if ($result.reverseCostConsistent -ne $true -or -not $result.reverseResult) { throw 'Forward/reverse route cost consistency was not verified.' }
if (($result.pathCostOrder -join ',') -ne 'TotalLength,VerticalTravel,VirtualConnectorCount,VirtualConnectorTotalLength,GapBridgeCount') { throw 'Host does not use the V13 route objective.' }
foreach ($metric in @('Length','VerticalTravel','VirtualConnectorCount','VirtualConnectorTotalLength','GapBridgeCount')) {
    if ($result.result.$metric -ne $result.reverseResult.$metric) { throw "Forward/reverse $metric differs." }
}
# Independently sum every Z movement, including intermediate bends and the actual sliced path.
$routeVertical = 0.0
foreach ($step in $result.result.Steps) {
    $stepVertical = 0.0
    for ($pointIndex=1; $pointIndex -lt $step.Centerline.Count; $pointIndex++) {
        $stepVertical += [Math]::Abs($step.Centerline[$pointIndex].Z-$step.Centerline[$pointIndex-1].Z)
    }
    if ([Math]::Abs($step.VerticalTravel-$stepVertical) -gt 1e-12) { throw 'Edge VerticalTravel does not include its complete polyline.' }
    $routeVertical += $stepVertical
}
if ([Math]::Abs($result.result.VerticalTravel-$routeVertical) -gt 1e-10) { throw 'Route VerticalTravel differs from its traversed polylines.' }
if ($VerifyVisibility) {
    $visibility=$result.visibilityChecks
    if (-not $visibility.passed -or -not $visibility.exact -or $visibility.maintenanceVolumes -lt 1 -or $visibility.maintenanceGeometry -lt 1 -or -not $visibility.maintenanceExcluded) { throw 'Native visibility or Maintenance Volume verification is missing.' }
    if ($visibility.states.Count -ne 4 -or ($visibility.states | Where-Object { -not $_.recognizedIdentical -or -not $_.topologyIdentical -or -not $_.routeIdentical -or -not $_.routeLengthIdentical -or $_.hiddenGeometry -lt 1 })) { throw 'A native hidden state changed recognized items, graph topology or route length.' }
    Write-Host ('VISIBILITY: exact same recognized items, topology and route across {0} hidden states; Maintenance Volume excluded.' -f $visibility.states.Count)
}
if ($result.incomplete) { throw 'Fixture graph extraction was incomplete.' }
$errorMetres = [Math]::Abs($result.result.Length - $fixture.expectedRouteLength)
if ($result.result.Pieces.Count -ne @($fixture.expectedContributions | Where-Object { $_ -gt 0 }).Count) { throw 'Host path component count differs from the fixture.' }
$internalLength = ($result.result.InternalEdges | Measure-Object -Property Length -Sum).Sum
$connections = @($result.result.Steps | Where-Object { $_.Kind -eq 1 })
$connectionLength = ($connections | Measure-Object -Property Length -Sum).Sum
$gapSteps = @($result.result.Steps | Where-Object { $_.Kind -eq 2 })
$gapLength = ($gapSteps | Measure-Object -Property Length -Sum).Sum
$expectedGapLength = [double]$fixture.expectedGapLength
$expectedGapCount = [int]$fixture.expectedGapCount
if ($gapSteps.Count -ne $expectedGapCount -or $result.result.GapBridgeCount -ne $expectedGapCount) { throw 'Host gap bridge count differs from the independent fixture.' }
if ([Math]::Abs($gapLength - $expectedGapLength) -gt 0.00001) { throw 'Host gap bridge length differs from the independent fixture by more than 10 micrometres.' }
if ($gapSteps | Where-Object { -not $_.RequiresReview -or -not $_.Join.IsGapBridge -or $_.ReviewReason -notmatch 'gap=' }) { throw 'A gap bridge did not expose its review requirement and gap diagnostic.' }
$virtualSteps = @($result.result.Steps | Where-Object { $_.Kind -eq 3 })
$virtualLength = ($virtualSteps | Measure-Object -Property Length -Sum).Sum
$expectedVirtualCount = [int]$fixture.expectedVirtualCount
$expectedVirtualLength = [double]$fixture.expectedVirtualLength
if ($virtualSteps.Count -ne $expectedVirtualCount -or $result.result.VirtualConnectorCount -ne $expectedVirtualCount) { throw 'Host virtual connector count differs from the independent fixture.' }
if ([Math]::Abs($virtualLength - $expectedVirtualLength) -gt 0.00001 -or [Math]::Abs($result.result.VirtualConnectorTotalLength - $virtualLength) -gt 1e-10) { throw 'Host virtual length differs from the independent fixture or its route contribution.' }
foreach ($step in $virtualSteps) {
    $candidate = $step.Join.VirtualConnector
    if (-not $step.RequiresReview -or -not $step.Join.IsVirtualConnector -or -not $candidate.RequiresReview -or $candidate.Confirmed -or $step.ReviewReason -notmatch 'deltaZ=') { throw 'Virtual connector review diagnostic is missing.' }
    if ($candidate.SourcePhysicalComponent -eq $candidate.TargetPhysicalComponent) { throw 'Virtual connector violates physical component rules.' }
    if ($result.settings.VirtualConnectorExperimentalTopN) {
        if ($candidate.Status -ne 'Candidate' -or $candidate.CandidateRank -lt 1 -or $candidate.CandidateRank -gt $result.settings.VirtualConnectorTopN -or $candidate.CandidateCount -lt $candidate.CandidateRank) { throw 'Top-N candidate was confirmed, unranked or outside the configured candidate count.' }
        if ($fixture.minimumSourceCandidates -and $candidate.CandidateCount -lt $fixture.minimumSourceCandidates) { throw 'Multiple candidate fixture did not exercise an ambiguous source.' }
    } elseif ($candidate.Status -ne 'Accepted' -or $candidate.CandidateCount -ne 1) { throw 'Legacy virtual connector violates uniqueness rules.' }
    $xyzLength = [Math]::Sqrt($candidate.DeltaX*$candidate.DeltaX + $candidate.DeltaY*$candidate.DeltaY + $candidate.DeltaZ*$candidate.DeltaZ)
    if ([Math]::Abs($xyzLength - $step.Length) -gt 1e-10) { throw 'Virtual connector does not store its actual world XYZ distance.' }
    if ($candidate.KindName -ne $fixture.expectedVirtualKind) { throw 'Host virtual connector kind differs from the fixture.' }
    if (-not $result.settings.VirtualConnectorExperimentalTopN -and $candidate.KindName -eq 'PortToPort3D' -and $candidate.TargetPortCandidateCount -ne 1) { throw 'Target port is not mutually unique.' }
    if ($candidate.KindName -eq 'PortToSegment3D') {
        $dx=$candidate.TargetPoint.X-$fixture.expectedTargetPoint.X; $dy=$candidate.TargetPoint.Y-$fixture.expectedTargetPoint.Y; $dz=$candidate.TargetPoint.Z-$fixture.expectedTargetPoint.Z
        if ([Math]::Sqrt($dx*$dx+$dy*$dy+$dz*$dz) -gt 0.00001 -or [Math]::Abs($candidate.TargetStation-$fixture.expectedTargetStation) -gt 0.00001) { throw 'Host projected junction or station differs from the independent fixture.' }
    }
}
if ($expectedVirtualCount -gt 0 -and $result.settings.VirtualConnectorMaxDistance -ne 0.5) { throw 'Host did not read the independent 500mm virtual search radius.' }
if ($result.settings.PhysicalTolerance -ne 0.002) { throw 'Physical tolerance changed from 2mm.' }
# IFC's ideal fixture has coincident sockets. Navisworks can reconstruct adjacent
# sockets at slightly different coordinates. Check these tiny gaps separately from
# each internal centerline; never increase the 10 micrometre geometry threshold.
if ([Math]::Abs($internalLength - ($fixture.expectedRouteLength - $expectedGapLength - $expectedVirtualLength)) -gt 0.00001) { throw 'Internal centerline total differs from the independent fixture by more than 10 micrometres.' }
if ($connections | Where-Object { $_.Length -gt 0.00001 }) { throw 'A fixture connection gap exceeds 10 micrometres.' }
if ([Math]::Abs($internalLength + $connectionLength + $gapLength + $virtualLength - $result.result.Length) -gt 1e-10) { throw 'Route total does not equal the sum of internal, physical connection, gap bridge, and virtual connector edges.' }
for ($i=0; $i -lt $fixture.items.Count; $i++) {
    $expectedItem = $fixture.items[$i]
    $indices = @(0..($result.parts.Count-1) | Where-Object { $result.parts[$_].Name -eq $expectedItem.Name })
    if ($indices.Count -ne 1) { throw "Expected unique reconstructed item: $($expectedItem.Name)" }
    $actual = ($result.result.InternalEdges | Where-Object { $_.Piece -eq $indices[0] } | Measure-Object -Property Length -Sum).Sum
    if ([Math]::Abs($actual - $fixture.expectedContributions[$i]) -gt 0.00001) { throw "Internal contribution differs for $($expectedItem.Name)" }
}
Write-Host ('PASS: {0:F9} m; {1} parts; ideal difference {2:F9} m; physical connection gaps {3:F9} m; {4} gap bridges totaling {5:F9} m.' -f $result.result.Length, $result.result.Pieces.Count, $errorMetres, $connectionLength, $gapSteps.Count, $gapLength)
Write-Host ('VIRTUAL: {0} connectors totaling {1:F9} m.' -f $virtualSteps.Count, $virtualLength)
Write-Host ('COST: total {0:F9} m; vertical travel {1:F9} m; forward/reverse exactly consistent.' -f $result.result.Length,$result.result.VerticalTravel)
