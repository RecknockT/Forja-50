#Requires -Version 5.1
param([Parameter(Mandatory=$true)][string]$Aplicacion)
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('MiIALocal-profiles-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root -Force | Out-Null
$report = Join-Path $root 'result.json'
$process = Start-Process -FilePath $Aplicacion -ArgumentList '--data-root',('"'+$root+'"'),'--smoke-gpu-profiles',('"'+$report+'"') -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(30000)) { $process.Kill(); throw 'La prueba de perfiles no terminó.' }
if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $report)) { throw 'Falta el resultado de perfiles.' }
$result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
if ($result.status -ne 'PASS' -or $result.profiles -ne 3 -or $result.gpuStarted) { throw "Fallaron los perfiles: $($result | ConvertTo-Json -Compress)" }
Write-Output 'PASS perfiles: 5080/5070 Ti nativos, 3090 sm86, cambio 32K/64K/128K y bloqueo de motor incompatible; sin inferencia'
