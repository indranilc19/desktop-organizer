# Renders the first page of PDFs to PNG (cover images for Desktop Organizer).
# Usage: pdf-cover.ps1 -Src a.pdf -Dst a.png      (single file)
#        pdf-cover.ps1 -Serve                      (reads base64(UTF-8 src)<TAB>base64(UTF-8 dst) lines on stdin, answers OK / ERR per line)
param([string]$Src, [string]$Dst, [switch]$Serve, [int]$Width = 600)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null = [Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime]
$null = [Windows.Data.Pdf.PdfDocument, Windows.Data.Pdf, ContentType = WindowsRuntime]
$null = [Windows.Storage.Streams.InMemoryRandomAccessStream, Windows.Storage.Streams, ContentType = WindowsRuntime]
$ext = [System.WindowsRuntimeSystemExtensions].GetMethods()
$asTaskOp  = $ext | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } | Select-Object -First 1
$asTaskAct = $ext | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction' } | Select-Object -First 1
function AwaitOp($op, [Type]$t) { $task = $asTaskOp.MakeGenericMethod($t).Invoke($null, @($op)); $null = $task.Wait(); $task.Result }
function AwaitAct($op) { $task = $asTaskAct.Invoke($null, @($op)); $null = $task.Wait() }
function Render([string]$s, [string]$d) {
    $file = AwaitOp ([Windows.Storage.StorageFile]::GetFileFromPathAsync($s)) ([Windows.Storage.StorageFile])
    $doc  = AwaitOp ([Windows.Data.Pdf.PdfDocument]::LoadFromFileAsync($file)) ([Windows.Data.Pdf.PdfDocument])
    if ($doc.PageCount -lt 1) { throw 'no pages' }
    $page = $doc.GetPage(0)
    try {
        $ms  = New-Object Windows.Storage.Streams.InMemoryRandomAccessStream
        $opt = New-Object Windows.Data.Pdf.PdfPageRenderOptions
        $opt.DestinationWidth = [uint32]$Width
        AwaitAct ($page.RenderToStreamAsync($ms, $opt))
        $rs = [System.IO.WindowsRuntimeStreamExtensions]::AsStreamForRead($ms.GetInputStreamAt(0))
        $tmp = $d + '.part'
        $fs = [IO.File]::Create($tmp); try { $rs.CopyTo($fs) } finally { $fs.Close() }
        if (Test-Path -LiteralPath $d) { Remove-Item -LiteralPath $d -Force }
        Move-Item -LiteralPath $tmp -Destination $d
    } finally { $page.Dispose() }
}
if ($Serve) {
    while ($null -ne ($line = [Console]::In.ReadLine())) {
        $p = $line -split "`t" | ForEach-Object { [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($_)) }
        try { Render $p[0] $p[1]; [Console]::Out.WriteLine('OK') } catch { [Console]::Out.WriteLine('ERR ' + $_.Exception.Message) }
        [Console]::Out.Flush()
    }
} else {
    Render $Src $Dst
    Write-Host "rendered $Dst"
}
