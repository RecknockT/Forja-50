#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$application = Join-Path $projectRoot 'src\MiIALocal.Desktop\bin\Release\net10.0-windows\Mi IA Local.exe'
if (!(Test-Path -LiteralPath $application)) { throw 'Ejecutá scripts\Compilar.ps1 antes de abrir la aplicación.' }
$dataDirectory = Join-Path $projectRoot '.dev-data'
Start-Process -FilePath $application -ArgumentList @('--data-root', ('"' + $dataDirectory + '"')) -WindowStyle Hidden
