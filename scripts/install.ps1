param([string]$GameDir = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (!$GameDir) {
    $props = Join-Path $projectRoot 'local.props'
    if (Test-Path -LiteralPath $props) { $GameDir = ([xml](Get-Content -LiteralPath $props -Raw)).Project.PropertyGroup.GameDir | Select-Object -Last 1 }
    if (!$GameDir) { $GameDir = Join-Path ${env:ProgramFiles(x86)} 'Steam/steamapps/common/Slay the Spire 2' }
}

$gameRoot = (Resolve-Path -LiteralPath $GameDir).Path
if (!(Test-Path -LiteralPath (Join-Path $gameRoot 'SlayTheSpire2.exe'))) { throw 'Not a Slay the Spire 2 installation' }
$sourceDir = Join-Path $projectRoot 'artifacts\SpireStrategist'
$targetDir = Join-Path $gameRoot 'mods\SpireStrategist'
$filesToInstall = @('SpireStrategist.dll', 'SpireStrategist.json')
foreach ($name in $filesToInstall) {
    if (!(Test-Path -LiteralPath (Join-Path $sourceDir $name))) { throw "Build first; missing $name" }
}
$backupDir = Join-Path $projectRoot ('.local\install-backups\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
foreach ($name in $filesToInstall) {
    $oldFile = Join-Path $targetDir $name
    if (Test-Path -LiteralPath $oldFile) {
        New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
        Copy-Item -LiteralPath $oldFile -Destination $backupDir
    }
}
New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
foreach ($name in $filesToInstall) {
    $installTarget = Join-Path $targetDir $name
    try {
        Copy-Item -LiteralPath (Join-Path $sourceDir $name) -Destination $targetDir -Force
    } catch [System.IO.IOException] {
        # A running .NET game can lock its mapped DLL against writes while allowing an
        # atomic file replacement. The current process keeps its existing loaded image.
        $stagedFile = Join-Path $targetDir ($name + '.new-' + [guid]::NewGuid().ToString('N'))
        Copy-Item -LiteralPath (Join-Path $sourceDir $name) -Destination $stagedFile
        $mappedBackup = Join-Path $targetDir ($name + '.previous-' + [guid]::NewGuid().ToString('N'))
        [System.IO.File]::Replace($stagedFile, $installTarget, $mappedBackup)
    }
    if ((Get-FileHash -LiteralPath (Join-Path $sourceDir $name)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $targetDir $name)).Hash) {
        throw "Installed file verification failed: $name"
    }
}
Write-Output "Installed and verified: $targetDir"
Write-Output 'Restart the game through Steam and enable 全局战略助手.'
