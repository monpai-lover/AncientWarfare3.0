$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'GrandStrategyArmyRules.Isolated.Tests\GrandStrategyArmyRules.Isolated.Tests.csproj'
dotnet run --project $project
if ($LASTEXITCODE -ne 0) { throw "Grand strategy rule tests failed" }
Write-Output 'Grand strategy source/rule guard passed.'
