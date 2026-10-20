#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$toolsDirectory = Join-Path $projectRoot '.tools'
New-Item -ItemType Directory -Path $toolsDirectory -Force | Out-Null
$installer = Join-Path $toolsDirectory 'dotnet-install.ps1'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer -UseBasicParsing
& $installer -Version '10.0.402' -InstallDir (Join-Path $toolsDirectory 'dotnet') -NoPath
$dotnetExe = Join-Path $toolsDirectory 'dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $dotnetExe)) { throw 'No se encontro el SDK local despues de instalar.' }
$installedVersion = & $dotnetExe --version
if ($LASTEXITCODE -ne 0 -or $installedVersion.Trim() -ne '10.0.402') { throw 'No se pudo verificar el SDK local 10.0.402.' }
Write-Output 'SDK preparado dentro del proyecto. Ejecutá scripts\Compilar.ps1.'
