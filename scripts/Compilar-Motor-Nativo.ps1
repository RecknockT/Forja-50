#Requires -Version 7
param([string]$WorkRoot = (Join-Path (Split-Path $PSScriptRoot -Parent) '.tools/native-build'), [int]$Jobs = 2)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$lock = Get-Content (Join-Path $projectRoot 'build/ninfer-windows.lock.json') -Raw | ConvertFrom-Json
$WorkRoot = [IO.Path]::GetFullPath($WorkRoot)
New-Item -ItemType Directory -Path $WorkRoot -Force | Out-Null
function Run([string]$exe, [string[]]$arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "Falló $exe ($LASTEXITCODE)" }
}
function Checkout([string]$url, [string]$sha, [string]$directory) {
    if (!(Test-Path (Join-Path $directory '.git'))) {
        Run git @('init', $directory)
        Run git @('-C',$directory,'remote','add','origin',$url)
    }
    Run git @('-C',$directory,'fetch','--depth','1','origin',$sha)
    Run git @('-C',$directory,'checkout','--detach','FETCH_HEAD')
    if ((& git -C $directory rev-parse HEAD).Trim() -ne $sha) { throw 'Revisión de fuente incorrecta.' }
}
$source = Join-Path $WorkRoot 'source'
Checkout $lock.SourceRepository $lock.SourceCommit $source
$vcpkg = Join-Path $WorkRoot 'vcpkg'
Checkout 'https://github.com/microsoft/vcpkg.git' $lock.VcpkgCommit $vcpkg
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs = & $vswhere -latest -version '[17.0,18.0)' -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vs) { throw 'Se requiere MSVC de Visual Studio 2022 para compilar; los usuarios finales no necesitan instalarlo.' }
$vcvars = Join-Path $vs 'VC/Auxiliary/Build/vcvars64.bat'
$environmentCommand = '"' + $vcvars + '" -vcvars_ver=14.44 >nul && set'
& cmd.exe /d /s /c $environmentCommand | ForEach-Object { if ($_ -match '^([^=]+)=(.*)$') { [Environment]::SetEnvironmentVariable($matches[1],$matches[2],'Process') } }
if ($LASTEXITCODE -ne 0) { throw 'No se pudo cargar MSVC 14.44.' }
$sdk = Join-Path $WorkRoot 'cuda'
New-Item -ItemType Directory -Path $sdk -Force | Out-Null
foreach ($asset in $lock.CudaArchives) {
    $zip = Join-Path $WorkRoot ($asset.Id + '.zip')
    if (!(Test-Path $zip) -or (Get-FileHash $zip -Algorithm SHA256).Hash -ne $asset.Sha256) {
        Invoke-WebRequest $asset.Url -OutFile $zip
    }
    if ((Get-FileHash $zip -Algorithm SHA256).Hash -ne $asset.Sha256 -or (Get-Item $zip).Length -ne $asset.Bytes) { throw "Integridad incorrecta de $($asset.Id)" }
    $unpack = Join-Path $WorkRoot ('unpack-' + $asset.Id)
    Expand-Archive -LiteralPath $zip -DestinationPath $unpack -Force
    $archiveRoot = @(Get-ChildItem $unpack -Directory)
    if ($archiveRoot.Count -ne 1) { throw 'Estructura CUDA inesperada.' }
    Copy-Item -Path (Join-Path $archiveRoot[0].FullName '*') -Destination $sdk -Recurse -Force
}
$nvcc = Get-ChildItem $sdk -Filter nvcc.exe -Recurse | Select-Object -First 1 -ExpandProperty FullName
if (!$nvcc) { throw 'Falta nvcc en el SDK extraído.' }
$env:CUDA_PATH = $sdk
$env:CUDACXX = $nvcc
$env:PATH = (Split-Path $nvcc -Parent) + ';' + $env:PATH
Run $nvcc @('--version')
Run (Join-Path $vcpkg 'bootstrap-vcpkg.bat') @('-disableMetrics')
$env:VCPKG_MAX_CONCURRENCY = "$Jobs"
$env:VCPKG_DISABLE_METRICS = '1'
Run (Join-Path $vcpkg 'vcpkg.exe') @('install','ffmpeg[core,avcodec,avformat,swscale]:x64-windows','curl[core,ssl]:x64-windows','--clean-after-build')
$prefix = Join-Path $vcpkg 'installed/x64-windows'
$build = Join-Path $WorkRoot 'build'
Run cmake @('-S',$source,'-B',$build,'-G','Ninja','-DCMAKE_BUILD_TYPE=Release','-DCMAKE_CUDA_ARCHITECTURES=120a',"-DCMAKE_CUDA_COMPILER=$nvcc", "-DCUDAToolkit_ROOT=$sdk", "-DNINFER_FFMPEG_ROOT=$prefix", "-DNINFER_CURL_ROOT=$prefix",'-DBUILD_TESTING=OFF','-DNINFER_BUILD_BENCHMARKS=OFF')
Run cmake @('--build',$build,'--parallel',"$Jobs",'--target','ninfer-serve')
$package = Join-Path $WorkRoot 'package'
New-Item -ItemType Directory -Path $package -Force | Out-Null
Copy-Item (Join-Path $build 'apps/ninfer-serve.exe') $package
Get-ChildItem (Join-Path $prefix 'bin') -Filter '*.dll' | Copy-Item -Destination $package
Get-ChildItem $sdk -Filter 'cudart64*.dll' -Recurse | Copy-Item -Destination $package
$crt = Get-ChildItem (Join-Path $vs 'VC/Redist/MSVC') -Directory | Sort-Object Name -Descending | Select-Object -First 1
Get-ChildItem (Join-Path $crt.FullName 'x64/Microsoft.VC143.CRT') -Filter '*.dll' | Copy-Item -Destination $package
$licenses = Join-Path $package 'licenses'
New-Item -ItemType Directory -Path $licenses -Force | Out-Null
Copy-Item -Path (Join-Path $projectRoot 'licenses/ninfer-native/*') -Destination $licenses -Force
foreach ($port in Get-ChildItem (Join-Path $prefix 'share') -Directory) {
    $notice = Join-Path $port.FullName 'copyright'
    if (Test-Path $notice) { Copy-Item $notice (Join-Path $licenses ($port.Name+'.txt')) }
}
Get-ChildItem $sdk -Filter '*LICENSE*' -File -Recurse | ForEach-Object { Copy-Item $_.FullName (Join-Path $licenses ('CUDA-'+$_.Name)) -Force }
Copy-Item (Join-Path $projectRoot 'build/ninfer-windows.lock.json') (Join-Path $package 'source.json')
# --help parses CLI and loads DLLs; no model argument, no GPU inference.
$help = & (Join-Path $package 'ninfer-serve.exe') --help 2>&1 | Out-String
if ($help -notmatch '--max-context' -or $help -notmatch '--api-key' -or $help -notmatch '--embedding-host') { throw "El ejecutable no pudo mostrar sus opciones: $help" }
$help | Set-Content (Join-Path $package 'help.txt') -Encoding utf8
$name = 'ninfer-native-windows-sm120a-' + $lock.SourceCommit.Substring(0,7) + '.zip'
$zip = Join-Path $WorkRoot $name
Compress-Archive -Path (Join-Path $package '*') -DestinationPath $zip -Force
$metadata = [ordered]@{File=$name;Bytes=(Get-Item $zip).Length;Sha256=(Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant();SourceCommit=$lock.SourceCommit;CudaVersion=$lock.CudaVersion;Architecture=$lock.Architecture;HelpCheckPassed=$true;GpuInferenceTested=$false}
$metadata | ConvertTo-Json | Set-Content (Join-Path $WorkRoot 'runtime.json') -Encoding utf8
Write-Output "Motor compilado y empaquetado, pendiente de prueba en GPU: $zip"
