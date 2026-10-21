param([Parameter(Mandatory)][string]$Aplicacion)
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('MiIALocal-storage-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $root 'models') -Force | Out-Null
$model = Join-Path $root 'models/qwen3_8_27b.ninfer'
[IO.File]::WriteAllText($model, 'fixture, no es un modelo real')
$settings = Join-Path $root 'settings.json'
[IO.File]::WriteAllText($settings, '{"EngineMode":1,"ContextTokens":65536}')
$before = (Get-FileHash -LiteralPath $model).Hash
$result = Join-Path $root 'smoke-result.json'
$preview = Join-Path $root 'storage.png'
$process = Start-Process -FilePath $Aplicacion -ArgumentList @('--data-root',('"'+$root+'"'),'--uninstall-cleanup','--storage-smoke-result',('"'+$result+'"'),'--storage-preview',('"'+$preview+'"')) -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(30000)) { Stop-Process -Id $process.Id; throw 'La ventana de almacenamiento no cerró.' }
$report = Get-Content -LiteralPath $result -Raw | ConvertFrom-Json
if ($process.ExitCode -ne 0 -or $report.error -or $report.selected -ne 0 -or $report.settingsDeleted -or !(Test-Path -LiteralPath $settings) -or (Get-FileHash -LiteralPath $model).Hash -ne $before) { throw 'Cerrar la limpieza modificó los datos o seleccionó opciones por defecto.' }
Write-Output 'PASS almacenamiento: cancelar ventana de desinstalación conserva modelo y ajustes'
$sha = [Security.Cryptography.SHA256]::Create()
try { $identity = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes([IO.Path]::GetFullPath($root)))).Replace('-','').Substring(0,20) } finally { $sha.Dispose() }
$mutex = [Threading.Mutex]::new($true, ('Local\MiIALocal-'+$identity))
try {
    $process = Start-Process -FilePath $Aplicacion -ArgumentList @('--data-root',('"'+$root+'"'),'--uninstall-cleanup') -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(15000)) { Stop-Process -Id $process.Id; throw 'La protección de instancia quedó bloqueada.' }
    if ($process.ExitCode -ne 3 -or (Get-FileHash -LiteralPath $model).Hash -ne $before) { throw 'Se permitió limpiar una instancia ocupada.' }
} finally { $mutex.Dispose() }
Write-Output 'PASS almacenamiento: instancia abierta bloquea limpieza sin borrar datos'
Write-Output "Vista de almacenamiento: $preview"
