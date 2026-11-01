param([string]$GameDir = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (!$GameDir) {
    $props = Join-Path $projectRoot 'local.props'
    if (Test-Path -LiteralPath $props) { $GameDir = ([xml](Get-Content -LiteralPath $props -Raw)).Project.PropertyGroup.GameDir | Select-Object -Last 1 }
    if (!$GameDir) { $GameDir = Join-Path ${env:ProgramFiles(x86)} 'Steam/steamapps/common/Slay the Spire 2' }
}

$dotnetExe = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $dotnetExe)) { $dotnetExe = 'dotnet' }
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\dotnet-home'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
Push-Location $projectRoot
try {
    & $dotnetExe build 'src/SpireStrategist.Mod/SpireStrategist.Mod.csproj' -c Release "-p:GameDir=$GameDir" --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Mod compilation failed' }
    & $dotnetExe run --project 'tests/SpireStrategist.Tests/SpireStrategist.Tests.csproj' -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Policy tests failed' }
    $sharedBuild = [xml](Get-Content -LiteralPath 'Directory.Build.props' -Raw)
    $priorPath = $sharedBuild.Project.PropertyGroup.DefectPriorPath.Replace('$(MSBuildThisFileDirectory)', $projectRoot + '/')
    & $dotnetExe run --project 'tools/IntegrationCheck/IntegrationCheck.csproj' -c Release "-p:GameDir=$GameDir" -- (Join-Path $GameDir 'data_sts2_windows_x86_64') $priorPath 'tests/SpireStrategist.Tests/Fixtures/defect-prior-v14.json'
    if ($LASTEXITCODE -ne 0) { throw 'Game hook compatibility checks failed' }
    & $dotnetExe build 'tools/Replay/Replay.csproj' -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Replay tool compilation failed' }
    & $dotnetExe run --project 'tools/Evaluate/Evaluate.csproj' -c Release -- 'artifacts/evaluation'
    if ($LASTEXITCODE -ne 0) { throw 'Batch rule checks failed' }
    $outputDir = Join-Path $projectRoot 'artifacts\SpireStrategist'
    New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
    Copy-Item -LiteralPath 'src\SpireStrategist.Mod\bin\Release\net9.0\SpireStrategist.dll' -Destination $outputDir
    Copy-Item -LiteralPath 'mod\SpireStrategist.json' -Destination $outputDir
    Get-FileHash -LiteralPath (Join-Path $outputDir 'SpireStrategist.dll') -Algorithm SHA256 | Format-List
    Write-Output "Ready: $outputDir"
} finally { Pop-Location }
