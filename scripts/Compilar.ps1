#Requires -Version 5.1
param([switch]$Publicar, [string]$CompiladorInstalador)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$version = [regex]::Match([IO.File]::ReadAllText((Join-Path $projectRoot 'src\MiIALocal.Core\Configuration.cs')), 'public const string Version = "([^"]+)"').Groups[1].Value
if ($version -notmatch '^\d+\.\d+\.\d+(-[a-zA-Z0-9.-]+)?$') { throw 'La version del producto no es valida.' }
$dotnetExe = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $dotnetExe)) {
    throw 'Falta el SDK local .NET 10.0.402. Ejecutá primero scripts\Preparar-Desarrollo.ps1.'
}
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\dotnet-home'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& $dotnetExe build (Join-Path $projectRoot 'MiIALocal.slnx') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'La compilación falló.' }
& $dotnetExe run --project (Join-Path $projectRoot 'tests\MiIALocal.Tests') -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'Las pruebas fallaron.' }
& (Join-Path $PSScriptRoot 'Verificar-Cierre.ps1') -Aplicacion (Join-Path $projectRoot 'src\MiIALocal.Desktop\bin\Release\net10.0-windows\Mi IA Local.exe')
if (!$Publicar) { return }
$output = Join-Path $projectRoot ('artifacts\paquete-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$appDirectory = Join-Path $output 'app'
& $dotnetExe publish (Join-Path $projectRoot 'src\MiIALocal.Desktop') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o $appDirectory --nologo
if ($LASTEXITCODE -ne 0) { throw 'La publicación falló.' }
if (Get-ChildItem -LiteralPath $appDirectory -Recurse -File | Where-Object { $_.Extension -in '.ninfer','.part' }) { throw 'El paquete contiene archivos de modelo inesperados.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\PRIMEROS-PASOS.md') -Destination $appDirectory
[IO.File]::WriteAllText((Join-Path $appDirectory 'portable.flag'), 'Los datos se guardan en la carpeta datos junto al ejecutable.')
$zip = Join-Path $output "Mi-IA-Local-$version-win-x64.zip"
Compress-Archive -LiteralPath $appDirectory -DestinationPath $zip
$hash = Get-FileHash -LiteralPath $zip -Algorithm SHA256
$hash | Format-List
$manifest = [pscustomobject]@{Version=$version;File=[IO.Path]::GetFileName($zip);Bytes=(Get-Item -LiteralPath $zip).Length;Sha256=$hash.Hash.ToLowerInvariant();ModelIncluded=$false;GpuTestsExecutedInThisBuild=$false;SourceCommit=$(if ($env:GITHUB_SHA) {$env:GITHUB_SHA} else {'local-development'})}
if ($CompiladorInstalador) {
    if (!(Test-Path -LiteralPath $CompiladorInstalador)) { throw 'No existe el compilador de Inno Setup indicado.' }
    & $CompiladorInstalador /Q "/DAppSource=$appDirectory" "/DSetupOutput=$output" "/DAppVersionText=$version" (Join-Path $projectRoot 'installer\MiIALocal.iss')
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo compilar el instalador.' }
    $setup = Get-Item -LiteralPath (Join-Path $output "Mi-IA-Local-$version-Setup.exe")
    $manifest | Add-Member -NotePropertyName Setup -NotePropertyValue ([pscustomobject]@{File=$setup.Name;Bytes=$setup.Length;Sha256=(Get-FileHash -LiteralPath $setup.FullName -Algorithm SHA256).Hash.ToLowerInvariant();Signed=$false})
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'paquete.json') -Encoding UTF8
Write-Output "Paquete de desarrollo: $output"
