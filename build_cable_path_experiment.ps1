param([string]$NavisworksPath)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts\host-paths.ps1')
$NavisworksPath = Resolve-Navisworks2023Path $NavisworksPath
dotnet build (Join-Path $PSScriptRoot 'experiments\CablePathExperiment.csproj') "-p:NavisworksInstallDir=$NavisworksPath" -p:NuGetAudit=false --ignore-failed-sources -v:minimal
if ($LASTEXITCODE -ne 0) { throw '电缆路径实验构建失败。' }
