#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$toolsDirectory = Join-Path $projectRoot '.tools'
$destination = Join-Path $toolsDirectory 'inno'
if (Test-Path -LiteralPath (Join-Path $destination 'ISCC.exe')) { return }
New-Item -ItemType Directory -Path $toolsDirectory -Force | Out-Null
$download = Join-Path $toolsDirectory 'innosetup-6.7.3.exe'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Invoke-WebRequest -Uri 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe' -OutFile $download -UseBasicParsing
if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne '9C73C3BAE7ED48D44112A0F48E66742C00090BDB5BEF71D9D3C056C66E97B732') { throw 'El SHA-256 del compilador no coincide con la version verificada.' }
$signature = Get-AuthenticodeSignature -LiteralPath $download
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Pyrsys') { throw 'La firma del compilador oficial no es valida.' }
$process = Start-Process -FilePath $download -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/CURRENTUSER','/NOICONS','/TASKS=""',('/DIR="'+$destination+'"')) -WindowStyle Hidden -PassThru
$process.WaitForExit()
if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath (Join-Path $destination 'ISCC.exe'))) { throw 'No se pudo preparar Inno Setup.' }
Write-Output 'Compilador de instaladores preparado dentro de .tools.'
