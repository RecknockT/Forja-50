#Requires -Version 5.1
param([Parameter(Mandatory=$true)][string]$Compilador)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$root = Join-Path $projectRoot ('artifacts\installer-check-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root -Force | Out-Null
function Run-Installer([string]$file, [string[]]$Extra = @()) {
    $parameters = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/NOICONS') + $Extra
    $process = Start-Process -FilePath $file -ArgumentList $parameters -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(60000)) { $process.CloseMainWindow() | Out-Null; if (!$process.WaitForExit(5000)) { $process.Kill(); $process.WaitForExit() }; throw 'No terminó el instalador de prueba.' }
    if ($process.ExitCode -ne 0) { throw "Falló el instalador de prueba: $($process.ExitCode)" }
}
foreach ($scenario in @('default','custom')) {
    $case = Join-Path $root $scenario
    $source = Join-Path $case 'source'
    New-Item -ItemType Directory -Path $source -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $source 'Mi IA Local.exe'), 'fixture; nunca ejecutar')
    [IO.File]::WriteAllText((Join-Path $source 'Forja 50.exe'), 'fixture; nunca ejecutar')
    $legacy = Join-Path $case 'Mi IA Local'
    $modern = Join-Path $case 'Forja 50'
    $previous = if ($scenario -eq 'custom') { Join-Path $case 'Carpeta elegida' } else { $legacy }
    $id = [guid]::NewGuid().ToString().ToUpperInvariant()
    $cleaned = $false
    try {
    $old = @"
[Setup]
AppId={{$id}
AppName=Forja Installer Test
AppVersion=0.1
DefaultDirName=$previous
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
OutputDir=$case
OutputBaseFilename=old
[Files]
Source: "$source\Mi IA Local.exe"; DestDir: "{app}"
"@
    [IO.File]::WriteAllText((Join-Path $case 'old.iss'), $old)
    & $Compilador /Q (Join-Path $case 'old.iss')
    if ($LASTEXITCODE -ne 0) { throw 'No compiló la instalación anterior de prueba.' }
    Run-Installer (Join-Path $case 'old.exe')
    Remove-Item -LiteralPath (Join-Path $source 'Mi IA Local.exe')
    $keep = Join-Path $previous 'archivo-del-usuario.txt'
    [IO.File]::WriteAllText($keep, 'conservar')
    # A private AppId and sandboxed shortcuts prevent touching the real app or registry.
    $script = [IO.File]::ReadAllText((Join-Path $projectRoot 'installer\MiIALocal.iss'))
    $script = $script.Replace('9AD610A8-C95C-460B-B5BD-2F45CC736DC0', $id)
    $script = $script.Replace('{localappdata}\Programs\Mi IA Local', $legacy.Replace("'", "''"))
    $script = $script.Replace('{localappdata}\Programs\Forja 50', $modern.Replace("'", "''"))
    $script = $script.Replace('{autodesktop}', (Join-Path $case 'desktop')).Replace('{userprograms}', (Join-Path $case 'menu'))
    $script = $script.Replace('DefaultGroupName=Forja 50', ('DefaultGroupName=Forja Installer Test ' + $id))
    $script = $script.Replace('SetupIconFile=..\src\MiIALocal.Desktop\Assets\app.ico', ('SetupIconFile=' + (Join-Path $projectRoot 'src\MiIALocal.Desktop\Assets\app.ico')))
    [IO.File]::WriteAllText((Join-Path $case 'new.iss'), $script)
    & $Compilador /Q "/DAppSource=$source" "/DSetupOutput=$case" /DAppVersionText=0.0.0-test (Join-Path $case 'new.iss')
    if ($LASTEXITCODE -ne 0) { throw 'No compiló la actualización de prueba.' }
    Run-Installer (Join-Path $case 'Forja-50-0.0.0-test-Setup.exe') @(('/DIR="' + $previous + '"'))
    $expected = if ($scenario -eq 'custom') { $previous } else { $modern }
    if (!(Test-Path -LiteralPath (Join-Path $expected 'Forja 50.exe')) -or !(Test-Path -LiteralPath $keep) -or (Test-Path -LiteralPath (Join-Path $previous 'Mi IA Local.exe')) -or (Test-Path -LiteralPath (Join-Path $expected 'Mi IA Local.exe'))) { throw "Falló la actualización $scenario o tocó archivos del usuario." }
    Run-Installer (Join-Path $expected 'unins000.exe')
    $cleaned = $true
    if (!(Test-Path -LiteralPath $keep)) { throw 'Desinstalar borró un archivo ajeno.' }
    Write-Output "PASS instalador: $scenario, nombre nuevo, ubicación correcta y archivo ajeno conservado"
    }
    finally {
        foreach ($directory in @($previous, $modern) | Select-Object -Unique) {
            $uninstaller = Join-Path $directory 'unins000.exe'
            if (!$cleaned -and (Test-Path -LiteralPath $uninstaller)) { Run-Installer $uninstaller }
        }
    }
}
