$ErrorActionPreference = 'Stop'
$output = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts'
[void][IO.Directory]::CreateDirectory($output)
foreach ($unit in @('mm', 'm')) {
    $script:entities = [Collections.Generic.List[string]]::new()
    function Add-Entity([string]$body) { $script:entities.Add(('#{0}={1};' -f ($script:entities.Count + 1), $body)); return ('#{0}' -f $script:entities.Count) }
    $person = Add-Entity "IFCPERSON(`$,'MVP','Tester',`$,`$,`$,`$,`$)"
    $org = Add-Entity "IFCORGANIZATION(`$,'Local fixture',`$,`$,`$)"
    $personOrg = Add-Entity "IFCPERSONANDORGANIZATION($person,$org,`$)"
    $app = Add-Entity "IFCAPPLICATION($org,'0.1','Tray MVP tests','MVP')"
    $owner = Add-Entity "IFCOWNERHISTORY($personOrg,$app,`$,.ADDED.,`$,`$,`$,0)"
    $zero = Add-Entity 'IFCCARTESIANPOINT((0.,0.,0.))'
    $z = Add-Entity 'IFCDIRECTION((0.,0.,1.))'
    $x = Add-Entity 'IFCDIRECTION((1.,0.,0.))'
    $y = Add-Entity 'IFCDIRECTION((0.,1.,0.))'
    $axis = Add-Entity "IFCAXIS2PLACEMENT3D($zero,$z,$x)"
    $placement = Add-Entity "IFCLOCALPLACEMENT(`$,$axis)"
    $context = Add-Entity "IFCGEOMETRICREPRESENTATIONCONTEXT(`$,'Model',3,1.E-6,$axis,`$)"
    $prefix = if ($unit -eq 'mm') { '.MILLI.' } else { '$' }
    $siUnit = Add-Entity "IFCSIUNIT(*,.LENGTHUNIT.,$prefix,.METRE.)"
    $units = Add-Entity "IFCUNITASSIGNMENT(($siUnit))"
    $project = Add-Entity "IFCPROJECT('0MVP000000000000000001',$owner,'MVP fixture',`$,`$,`$,`$,($context),$units)"
    $building = Add-Entity "IFCBUILDING('0MVP000000000000000002',$owner,'Fixture',`$,`$,$placement,`$,`$,.ELEMENT.,`$,`$,`$)"
    $null = Add-Entity "IFCRELAGGREGATES('0MVP000000000000000003',$owner,`$,`$,$project,($building))"
    $length = if ($unit -eq 'mm') { '1237.' } else { '1.237' }
    $width = if ($unit -eq 'mm') { '600.' } else { '0.6' }
    $height = if ($unit -eq 'mm') { '150.' } else { '0.15' }
    $reference = if ($unit -eq 'mm') { '1000.' } else { '1.' }
    $translation = if ($unit -eq 'mm') { '7000.,3000.,2000.' } else { '7.,3.,2.' }
    $profile = Add-Entity "IFCRECTANGLEPROFILEDEF(.AREA.,'600x150',`$,$width,$height)"
    $solid = Add-Entity "IFCEXTRUDEDAREASOLID($profile,$axis,$z,$length)"
    $shape = Add-Entity "IFCSHAPEREPRESENTATION($context,'Body','SweptSolid',($solid))"
    $map = Add-Entity "IFCREPRESENTATIONMAP($axis,$shape)"
    $rotX = Add-Entity 'IFCDIRECTION((0.8,-0.6,0.))'
    $rotY = Add-Entity 'IFCDIRECTION((0.48,0.64,-0.6))'
    $rotZ = Add-Entity 'IFCDIRECTION((0.36,0.48,0.8))'
    $point = Add-Entity "IFCCARTESIANPOINT(($translation))"
    $instances = @()
    for ($i=0; $i -lt 3; $i++) {
        if ($i -eq 0) { $transform = Add-Entity "IFCCARTESIANTRANSFORMATIONOPERATOR3D($x,$y,$zero,1.,$z)" }
        else { $factor = if ($i -eq 1) {'1.'} else {'2.'}; $transform = Add-Entity "IFCCARTESIANTRANSFORMATIONOPERATOR3D($rotX,$rotY,$point,$factor,$rotZ)" }
        $mapped = Add-Entity "IFCMAPPEDITEM($map,$transform)"
        $mappedShape = Add-Entity "IFCSHAPEREPRESENTATION($context,'Body','MappedRepresentation',($mapped))"
        $definition = Add-Entity "IFCPRODUCTDEFINITIONSHAPE(`$,`$,($mappedShape))"
        $guid = '0MVP00000000000000001' + $i
        $segment = Add-Entity "IFCFLOWSEGMENT('$guid',$owner,'MVP_Straight_$i',`$,'Straight cable tray',$placement,$definition,`$)"
        $instances += $segment
        $property = Add-Entity "IFCPROPERTYSINGLEVALUE('Length',`$,IFCLENGTHMEASURE($reference),`$)"
        $setGuid = '0MVP00000000000000002' + $i
        $pset = Add-Entity "IFCPROPERTYSET('$setGuid',$owner,'MVP Reference',`$,($property))"
        $relGuid = '0MVP00000000000000003' + $i
        $null = Add-Entity "IFCRELDEFINESBYPROPERTIES('$relGuid',$owner,`$,`$,($segment),$pset)"
    }
    $null = Add-Entity "IFCRELCONTAINEDINSPATIALSTRUCTURE('0MVP000000000000000004',$owner,`$,`$,($($instances -join ',')),$building)"
    $header = "ISO-10303-21;`r`nHEADER;`r`nFILE_DESCRIPTION(('ViewDefinition [CoordinationView]'),'2;1');`r`nFILE_NAME('tray-$unit.ifc','2026-10-01T00:00:00',('MVP'),('Local'),'MVP','MVP','');`r`nFILE_SCHEMA(('IFC2X3'));`r`nENDSEC;`r`nDATA;`r`n"
    [IO.File]::WriteAllText((Join-Path $output "tray-$unit.ifc"), $header + ($entities -join "`r`n") + "`r`nENDSEC;`r`nEND-ISO-10303-21;", [Text.Encoding]::ASCII)
    Write-Host "Created tray-$unit.ifc"
}
