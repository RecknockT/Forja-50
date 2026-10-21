# No requiere CUDA, Visual Studio ni GPU. Reproduce la distribución de carpetas del runner.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Native-Build-Helpers.ps1')
$root = Join-Path (Split-Path $PSScriptRoot -Parent) ('.tools/native-crt-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root -Force | Out-Null
function Fake-Crt([string]$version) {
    $directory = Join-Path $root "VC/Redist/MSVC/$version/x64/Microsoft.VC143.CRT"
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    foreach ($file in 'vcruntime140.dll','msvcp140.dll') { [IO.File]::WriteAllText((Join-Path $directory $file), 'fixture') }
    return $directory
}
$older = Fake-Crt '14.9.12345'
$newer = Fake-Crt '14.44.35211'
New-Item -ItemType Directory -Path (Join-Path $root 'VC/Redist/MSVC/v143') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $root 'VC/Redist/MSVC/14.99.12345') -Force | Out-Null
if ((Resolve-NativeVcRuntime -VisualStudioRoot $root) -ne $newer) { throw 'No seleccionó la versión numérica válida más reciente.' }
Write-Output 'PASS CRT: ignorar v143 y versiones incompletas; orden numérico'
if ((Resolve-NativeVcRuntime -VisualStudioRoot $root -PreferredDirectory (Split-Path (Split-Path $older -Parent) -Parent)) -ne $older) { throw 'No respetó VCToolsRedistDir.' }
Write-Output 'PASS CRT: preferir el runtime del entorno MSVC seleccionado'
$missingFailed = $false
try { Resolve-NativeVcRuntime -VisualStudioRoot (Join-Path $root 'missing') | Out-Null } catch { $missingFailed = $true }
if (!$missingFailed) { throw 'No rechazó el runtime ausente antes de compilar.' }
Write-Output 'PASS CRT: detectar runtime ausente antes de compilar CUDA'
