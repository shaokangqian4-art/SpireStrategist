param([string]$Seed='SS-AUTO-1',[switch]$CorpusOnly,[string]$GameDir,[int]$LongMapLayers=-1,[int]$LongMapActs=0,[switch]$LongMapOnly,[string]$EventLab='',[int]$TimeoutSeconds=360,[ValidateRange(0,10)][int]$Ascension=10,[switch]$RenderUi,[ValidateSet('zhs','eng')][string]$Language='zhs')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$game=if($GameDir){(Resolve-Path -LiteralPath $GameDir).Path}else{Join-Path $root '.local\lab\game'}
$case=Join-Path $root ('artifacts\autopilot-tests\'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N').Substring(0,8))
$roaming=Join-Path $case 'roaming'
$userDir=Join-Path $roaming 'SlayTheSpire2'
$settings=Join-Path $userDir 'default\1'
New-Item -ItemType Directory -Force -Path $settings | Out-Null
if($LongMapLayers -ge 0){
    $longSettings=Join-Path $settings 'mod_data\YUILongMap'
    New-Item -ItemType Directory -Force -Path $longSettings | Out-Null
    @{ExtraLayerCount=$LongMapLayers;GuaranteedTreasureInterval=8;MonsterWeight=0;TreasureWeight=2;ShopWeight=10;EliteWeight=35;UnknownWeight=25;RestSiteWeight=20;MiniBossWeight=5;MiniBossPool=0;ExtraActs=$LongMapActs;ExtraActHp=150;ExtraActDamage=130;ExtraActGold=50} | ConvertTo-Json | Set-Content (Join-Path $longSettings 'settings.json') -Encoding utf8
}
@{seed=$Seed;ascension=$Ascension;dllSha256=(Get-FileHash -LiteralPath (Join-Path $game 'mods\SpireStrategist\SpireStrategist.dll')).Hash;longMapLayers=$LongMapLayers;longMapActs=$LongMapActs;longMapOnly=$LongMapOnly.IsPresent;eventLab=$EventLab;started=(Get-Date).ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $case 'metadata.json') -Encoding utf8
@{language=$Language;mod_settings=@{mods_enabled=$true;mod_list=@()};volume_master=0;volume_bgm=0;volume_sfx=0;volume_ambience=0} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $settings 'settings.save') -Encoding utf8
Set-Content -LiteralPath (Join-Path $userDir 'strategist-lab.marker') -Value 'isolated native integration fixtures'
$psi=[System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName=Join-Path $game 'SlayTheSpire2.exe'
$psi.WorkingDirectory=$game
$psi.UseShellExecute=$false
$psi.CreateNoWindow=$true
[string[]]$displayArgs=if($RenderUi){@('--rendering-method','gl_compatibility','--rendering-driver','opengl3','--windowed','--resolution','1600x900','--position','-32000,-32000')}else{@('--headless')}
$psi.WindowStyle=[System.Diagnostics.ProcessWindowStyle]::Hidden
foreach($arg in ($displayArgs+@('--disable-vsync','--max-fps','120','--force-steam=off','--log-file',(Join-Path $case 'game.log')))) {$psi.ArgumentList.Add($arg)}
$psi.Environment['APPDATA']=$roaming
$psi.Environment['LOCALAPPDATA']=(Join-Path $case 'local')
$psi.Environment['COMBATSOLVER_HEADLESS']='1'
$psi.Environment['COMBATSOLVER_PRECOMBAT_WORKER']='1'
$psi.Environment['SPIRE_STRATEGIST_LAB']='1'
if($RenderUi){$psi.Environment['SPIRE_STRATEGIST_RENDER_UI']='1'}
$psi.Environment['SPIRE_STRATEGIST_LAB_SEED']=$Seed
$psi.Environment['SPIRE_STRATEGIST_LAB_ASCENSION']="$Ascension"
if($EventLab){$psi.Environment['SPIRE_STRATEGIST_EVENT_LAB']=$EventLab}
if($LongMapOnly){$psi.Environment['SPIRE_STRATEGIST_LONGMAP_LAB']='1';$psi.Environment['SPIRE_STRATEGIST_LONGMAP_LAYERS']="$LongMapLayers";$psi.Environment['SPIRE_STRATEGIST_LONGMAP_ACTS']="$LongMapActs"}
if($CorpusOnly){$psi.Environment['SPIRE_STRATEGIST_CORPUS_ONLY']='1'}
$process=[System.Diagnostics.Process]::Start($psi)
Write-Output "Native autopilot suite: $case (PID $($process.Id))"
$deadline=(Get-Date).AddSeconds($TimeoutSeconds)
while(!$process.WaitForExit(1000)) {
    if((Get-Date) -gt $deadline) {$process.Kill($true);$process.WaitForExit();throw "Native suite timeout: $case"}
}
$resultPath=Join-Path $userDir 'strategist-autopilot-result.json'
if(!(Test-Path -LiteralPath $resultPath)){throw "No result; exit=$($process.ExitCode): $case"}
Copy-Item -LiteralPath $resultPath -Destination (Join-Path $case 'result.json')
$result=Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
$result | ConvertTo-Json -Depth 10
if(!$result.passed -or $process.ExitCode -ne 0){throw "Native suite failed: $case"}
if($CorpusOnly){Write-Output "Corpus: $userDir\review-corpus";return}
$errors=Get-ChildItem -LiteralPath (Join-Path $userDir 'SpireStrategist') -Filter 'session-*.jsonl' | ForEach-Object {Get-Content -LiteralPath $_.FullName | ForEach-Object {$_ | ConvertFrom-Json} | Where-Object {$_.type -in @('error','auto_error')}}
if($errors){$errors | ConvertTo-Json -Depth 10;throw 'Advisor errors during integration suite'}
$runtimeErrors=Select-String -LiteralPath (Join-Path $case 'game.log') -Pattern '^\[ERROR\]|^ERROR: System\.'
if($runtimeErrors){$runtimeErrors | ForEach-Object {$_.Line};throw 'Game runtime errors during integration suite'}
Write-Output "Native autopilot suite passed: $case"
