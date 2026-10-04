param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $projectDirectory 'artifacts' }
dotnet run --project (Join-Path $projectDirectory 'tests\PortGraphChecks.csproj') -- --fixture-only $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw 'Synthetic cable network fixture generation failed.' }
