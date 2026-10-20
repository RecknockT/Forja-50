#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$toolsDirectory = Join-Path $projectRoot '.tools'
New-Item -ItemType Directory -Path $toolsDirectory -Force | Out-Null
$installer = Join-Path $toolsDirectory 'dotnet-install.ps1'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer -UseBasicParsing
& $installer -Version '10.0.402' -InstallDir (Join-Path $toolsDirectory 'dotnet') -NoPath
if ($LASTEXITCODE -ne 0) { throw 'No se pudo preparar el SDK local.' }
Write-Output 'SDK preparado dentro del proyecto. Ejecutá scripts\Compilar.ps1.'
