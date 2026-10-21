function Resolve-NativeVcRuntime {
    param([Parameter(Mandatory)][string]$VisualStudioRoot, [string]$PreferredDirectory)
    $candidates = @()
    if ($PreferredDirectory) { $candidates += Join-Path $PreferredDirectory 'x64/Microsoft.VC143.CRT' }
    $redist = Join-Path $VisualStudioRoot 'VC/Redist/MSVC'
    if (Test-Path -LiteralPath $redist) {
        # VS also installs a directory named "v143"; it is not a versioned CRT root.
        $versions = Get-ChildItem -LiteralPath $redist -Directory |
            Where-Object { $_.Name -match '^\d+\.\d+\.\d+$' } |
            Sort-Object { [version]$_.Name } -Descending
        foreach ($version in $versions) { $candidates += Join-Path $version.FullName 'x64/Microsoft.VC143.CRT' }
    }
    foreach ($candidate in $candidates) {
        if ((Test-Path -LiteralPath (Join-Path $candidate 'vcruntime140.dll') -PathType Leaf) -and
            (Test-Path -LiteralPath (Join-Path $candidate 'msvcp140.dll') -PathType Leaf)) { return $candidate }
    }
    throw 'Falta el runtime redistribuible x64 de MSVC (vcruntime140.dll y msvcp140.dll). Se comprueba antes de compilar CUDA.'
}
