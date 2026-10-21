#Requires -Version 5.1
param([Parameter(Mandatory=$true)][string]$Aplicacion)
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('MiIALocal-modes-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root -Force | Out-Null
$report = Join-Path $root 'result.json'
$process = Start-Process -FilePath $Aplicacion -ArgumentList '--data-root',('"'+$root+'"'),'--smoke-visual',('"'+$report+'"') -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(30000)) { $process.Kill(); throw 'La prueba de modos no terminó.' }
if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $report)) { throw 'Falta el resultado de modos.' }
$result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
if ($result.status -ne 'PASS' -or !$result.independentModes -or !$result.referenceEditing -or $result.gpuStarted) { throw "Fallaron los modos: $($result | ConvertTo-Json -Compress)" }
Write-Output 'PASS modos: programación/visual independientes, referencia de edición, imagen nueva y persistencia sin iniciar modelos'
