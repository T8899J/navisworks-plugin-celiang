param([string]$NavisworksPath, [string]$Manifest, [string]$Model, [string]$Report)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'host-paths.ps1')
$NavisworksPath = Resolve-Navisworks2023Path $NavisworksPath
if (-not $Model) { $Model = Join-Path $root 'artifacts\port-graph-fixture.ifc' }
if (-not $Manifest) { $Manifest = Join-Path $root 'artifacts\port-graph-fixture.json' }
if (-not $Report) { $Report = Join-Path $root ('artifacts\port-graph-host-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss') + '.json') }
$fixture = Get-Content -LiteralPath $Manifest -Raw -Encoding UTF8 | ConvertFrom-Json
$culture = [Globalization.CultureInfo]::InvariantCulture
function Format-Point($p) { return ((([double]$p.X).ToString('R', $culture), ([double]$p.Y).ToString('R', $culture), ([double]$p.Z).ToString('R', $culture)) -join ',') }
$assembly = Join-Path $root 'experiments\bin\CablePathV11\JiePinPai.CablePathExperimentV11.dll'
foreach ($path in @($assembly, $Model)) { if (-not (Test-Path -LiteralPath $path)) { throw "Missing input: $path" } }
$values = @($assembly, $Model, $Report, $fixture.startName, $fixture.finishName, (Format-Point $fixture.start), (Format-Point $fixture.finish))
if ($values | Where-Object { $_.Contains('"') }) { throw 'Embedded quotes are not supported in test paths or names.' }
$arguments = '-HideGui -AddPluginAssembly "' + $assembly + '" -OpenFile "' + $Model + '" -ExecuteAddInPlugin CableGraphProbeV11.JPPM "' + $Report + '" "' + $fixture.startName + '" "' + $fixture.finishName + '" "' + (Format-Point $fixture.start) + '" "' + (Format-Point $fixture.finish) + '" -Exit'
# A separate test process loads only the generated fixture. Existing user documents are untouched.
$process = Start-Process -FilePath (Join-Path $NavisworksPath 'Roamer.exe') -ArgumentList $arguments -WorkingDirectory $root -WindowStyle Hidden -PassThru
Write-Host "TEST HOST PID: $($process.Id)"
Write-Host "REPORT: $Report"
if (-not $process.WaitForExit(180000)) { throw "Test host has not exited (PID $($process.Id)). No process was terminated." }
if (-not (Test-Path -LiteralPath $Report)) { throw "Host produced no report; exit code $($process.ExitCode)." }
$result = Get-Content -LiteralPath $Report -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $result.success) { throw "Host reconstruction failed: $($result.error). Inspect rejected entries in $Report" }
if ($result.incomplete) { throw 'Fixture graph extraction was incomplete.' }
$errorMetres = [Math]::Abs($result.result.Length - $fixture.expectedRouteLength)
if ($result.result.Pieces.Count -ne $fixture.expectedContributions.Count) { throw 'Host path component count differs from the fixture.' }
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
    if (-not $step.RequiresReview -or -not $step.Join.IsVirtualConnector -or $candidate.Status -ne 'Accepted' -or $step.ReviewReason -notmatch 'deltaZ=') { throw 'Virtual connector review diagnostic is missing.' }
    if ($candidate.SourcePhysicalComponent -eq $candidate.TargetPhysicalComponent -or $candidate.CandidateCount -ne 1) { throw 'Virtual connector violates component or uniqueness rules.' }
    $xyzLength = [Math]::Sqrt($candidate.DeltaX*$candidate.DeltaX + $candidate.DeltaY*$candidate.DeltaY + $candidate.DeltaZ*$candidate.DeltaZ)
    if ([Math]::Abs($xyzLength - $step.Length) -gt 1e-10) { throw 'Virtual connector does not store its actual world XYZ distance.' }
    if ($candidate.KindName -ne $fixture.expectedVirtualKind) { throw 'Host virtual connector kind differs from the fixture.' }
    if ($candidate.KindName -eq 'PortToPort3D' -and $candidate.TargetPortCandidateCount -ne 1) { throw 'Target port is not mutually unique.' }
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
