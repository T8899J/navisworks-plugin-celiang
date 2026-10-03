param([string]$NavisworksPath)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts\host-paths.ps1')
$NavisworksPath = Resolve-Navisworks2023Path $NavisworksPath
Write-Host "BUILD HOST: $NavisworksPath"
& dotnet build (Join-Path $PSScriptRoot 'TrayMeasurement.csproj') -c Release --ignore-failed-sources "-p:NavisworksInstallDir=$NavisworksPath" -p:NuGetAudit=false -v:minimal
if ($LASTEXITCODE -ne 0) { throw "构建失败：$LASTEXITCODE" }
