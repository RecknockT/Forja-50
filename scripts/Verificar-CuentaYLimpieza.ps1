#Requires -Version 5.1
param([Parameter(Mandatory=$true)][string]$Aplicacion, [switch]$Navegador)
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('MiIALocal-account-' + [Guid]::NewGuid().ToString('N'))
foreach ($scenario in @('github','cleanup-all')) {
    $directory = Join-Path $root $scenario
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $report = Join-Path $directory 'result.json'
    $arguments = @('--data-root',('"'+$directory+'"'),('--smoke-'+$scenario),('"'+$report+'"'))
    if ($scenario -eq 'cleanup-all' -and $Navegador) { $arguments += '--initialize-browser' }
    $process = Start-Process -FilePath $Aplicacion -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(45000)) { $process.Kill(); throw "No terminó $scenario" }
    if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $report)) { throw "Falta el resultado $scenario" }
    $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if ($result.status -ne 'PASS' -or $result.gpuStarted) { throw "Falló $scenario : $($result | ConvertTo-Json -Compress)" }
    Write-Output "PASS ventana: $scenario, sin inferencia ni autorización OAuth"
}
