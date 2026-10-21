#Requires -Version 5.1
# Icono vectorial propio. Ejecutar desde Windows; no requiere herramientas adicionales.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$destination = Join-Path (Split-Path $PSScriptRoot -Parent) 'src\MiIALocal.Desktop\Assets'
$images = @()
foreach ($size in @(16,24,32,48,64,128,256)) {
    $bitmap = New-Object Drawing.Bitmap($size,$size)
    $g = [Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.ScaleTransform($size/256.0,$size/256.0)
    $g.Clear([Drawing.ColorTranslator]::FromHtml('#101820'))
    $teal = New-Object Drawing.Pen([Drawing.ColorTranslator]::FromHtml('#42e0ba'),8)
    $g.FillRectangle((New-Object Drawing.SolidBrush([Drawing.ColorTranslator]::FromHtml('#17363c'))),46,46,164,164)
    $g.DrawRectangle($teal,46,46,164,164)
    $pins = New-Object Drawing.Pen([Drawing.ColorTranslator]::FromHtml('#42e0ba'),10)
    foreach($p in @(83,128,173)) { $g.DrawLine($pins,$p,27,$p,46); $g.DrawLine($pins,$p,210,$p,229); $g.DrawLine($pins,27,$p,46,$p); $g.DrawLine($pins,210,$p,229,$p) }
    $points = [Drawing.PointF[]]@((New-Object Drawing.PointF(88,182)),(New-Object Drawing.PointF(88,74)),(New-Object Drawing.PointF(176,74)),(New-Object Drawing.PointF(176,98)),(New-Object Drawing.PointF(116,98)),(New-Object Drawing.PointF(116,118)),(New-Object Drawing.PointF(164,118)),(New-Object Drawing.PointF(164,142)),(New-Object Drawing.PointF(116,142)),(New-Object Drawing.PointF(116,182)))
    $white = New-Object Drawing.SolidBrush([Drawing.ColorTranslator]::FromHtml('#e5fff8'))
    $g.FillPolygon($white,$points)
    $orange = New-Object Drawing.Pen([Drawing.ColorTranslator]::FromHtml('#ffb454'),12)
    $g.DrawLines($orange,[Drawing.PointF[]]@((New-Object Drawing.PointF(166,149)),(New-Object Drawing.PointF(192,167)),(New-Object Drawing.PointF(166,185))))
    $memory = New-Object IO.MemoryStream
    $bitmap.Save($memory,[Drawing.Imaging.ImageFormat]::Png)
    $images += ,$memory.ToArray()
    if($size -eq 256) { $bitmap.Save((Join-Path $destination 'logo.png'),[Drawing.Imaging.ImageFormat]::Png) }
    $memory.Dispose(); $white.Dispose(); $orange.Dispose(); $teal.Dispose(); $pins.Dispose(); $g.Dispose(); $bitmap.Dispose()
}
$stream = [IO.File]::Create((Join-Path $destination 'app.ico'))
$writer = New-Object IO.BinaryWriter($stream)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
$offset = 6 + 16*$images.Count
$sizes = @(16,24,32,48,64,128,256)
for($i=0;$i -lt $images.Count;$i++) {
    $dimension = if($sizes[$i] -eq 256){0}else{$sizes[$i]}
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach($data in $images){$writer.Write([byte[]]$data)}
$writer.Dispose(); $stream.Dispose()
Write-Output 'Logo PNG e icono multirresolución generados.'