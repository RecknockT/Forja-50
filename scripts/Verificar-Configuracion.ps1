#Requires -Version 5.1
param([Parameter(Mandatory=$true)][string]$Aplicacion)
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('MiIALocal-config-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root -Force | Out-Null
$settingsFile = Join-Path $root 'settings.json'
$previous = '{"EngineMode":0,"ContextTokens":32768,"ModelDirectory":"models","ImportedModel":"legacy-model.ninfer","LastProject":"proyecto-preservado","LicenseAccepted":true,"UseRecommendedProfile":false}'
[IO.File]::WriteAllText($settingsFile,$previous)
$report = Join-Path $root 'report.json'
$process = Start-Process -FilePath $Aplicacion -ArgumentList '--data-root',('"'+$root+'"'),'--configure-native','--configuration-result',('"'+$report+'"') -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(20000)) { $process.Kill(); throw 'La migración no terminó.' }
if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $report)) { throw 'La migración no generó resultado.' }
$result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
$settings = Get-Content -LiteralPath $settingsFile -Raw | ConvertFrom-Json
$backups = @(Get-ChildItem -LiteralPath $root -Filter 'settings.json.before-native-*.json')
if (!$result.configured -or $result.gpuStarted -or $settings.EngineMode -ne 1 -or $settings.ContextTokens -ne 65536 -or $settings.NinferVision -or $settings.LicenseAccepted) { throw 'La migración no dejó el perfil nativo inicial esperado.' }
if ($settings.ImportedModel -ne 'legacy-model.ninfer' -or $settings.LastProject -ne 'proyecto-preservado' -or $backups.Count -ne 1 -or [IO.File]::ReadAllText($backups[0].FullName) -ne $previous) { throw 'La migración no conservó los datos anteriores.' }
Write-Output 'PASS configuración: migración nativa con copia y datos conservados, sin iniciar GPU'
