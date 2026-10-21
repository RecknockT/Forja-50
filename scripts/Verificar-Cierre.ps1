#Requires -Version 5.1
param([Parameter(Mandatory=$true)][string]$Aplicacion)
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('MiIALocal-close-' + [Guid]::NewGuid().ToString('N'))
foreach ($scenario in @('primera-x','cierre-repetido','cancelar-operacion','contexto-incompatible','pausar-descarga','rechazar-cierre','rechazar-durante-operacion')) {
    $directory = Join-Path $root $scenario
    $result = Join-Path $directory 'result.json'
    $arguments = @('--data-root',('"'+$directory+'"'),'--smoke-close',('"'+$result+'"'))
    if ($scenario -eq 'cierre-repetido') { $arguments += '--repeat-close' }
    if ($scenario -eq 'cancelar-operacion') { $arguments += '--close-during-operation' }
    if ($scenario -eq 'contexto-incompatible') { $arguments += '--reject-invalid-context' }
    if ($scenario -eq 'pausar-descarga') { $arguments += '--pause-download' }
    if ($scenario -in @('rechazar-cierre','rechazar-durante-operacion')) { $arguments += '--decline-close' }
    if ($scenario -eq 'rechazar-durante-operacion') { $arguments += '--close-during-operation' }
    $process = Start-Process -FilePath $Aplicacion -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(45000)) {
        $process.Kill()
        throw "La prueba de cierre no termino: $scenario"
    }
    if (!(Test-Path -LiteralPath $result)) { throw "Falta el resultado del cierre: $scenario" }
    $report = Get-Content -LiteralPath $result -Raw | ConvertFrom-Json
    if ($process.ExitCode -ne 0 -or $report.status -ne 'PASS') { throw "Fallo el cierre $scenario : $($report | ConvertTo-Json -Compress)" }
    Write-Output "PASS cierre: $scenario"
}
