# Builds DesktopOrganizer.exe from src\*.cs with the C# compiler that ships with Windows (only when the sources change).
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$data = Join-Path $here 'data'
$null = New-Item -ItemType Directory -Force -Path $data
$log  = Join-Path $data 'build.log'
$exe  = Join-Path $here 'DesktopOrganizer.exe'
$stampFile = Join-Path $data 'build.hash'
$files = @('Core.cs', 'Catalog.cs', 'Media.cs', 'Inbox.cs', 'Home.cs') | ForEach-Object { Join-Path (Join-Path $here 'src') $_ }
$code = ($files | ForEach-Object { Get-Content -Raw -Encoding UTF8 $_ }) -join "`r`n"
$md5 = [System.Security.Cryptography.MD5]::Create()
$hash = [BitConverter]::ToString($md5.ComputeHash([Text.Encoding]::UTF8.GetBytes($code))).Replace('-', '')
if ((Test-Path $exe) -and (Test-Path $stampFile) -and ((Get-Content $stampFile -Raw).Trim() -eq $hash)) { exit 0 }
try {
    # stop a running copy so the exe can be replaced
    Get-Process DesktopOrganizer -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 400
    Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Xaml, System.Windows.Forms, System.Drawing, System.IO.Compression, System.IO.Compression.FileSystem, System.Web.Extensions
    $refs = @(
        [System.Windows.Window].Assembly.Location,
        [System.Windows.Media.ImageSource].Assembly.Location,
        [System.Windows.Threading.Dispatcher].Assembly.Location,
        [System.Xaml.XamlReader].Assembly.Location,
        [System.Windows.Forms.NotifyIcon].Assembly.Location,
        [System.Drawing.Icon].Assembly.Location,
        [System.IO.Compression.ZipArchive].Assembly.Location,
        [System.IO.Compression.ZipFile].Assembly.Location,
        [System.Web.Script.Serialization.JavaScriptSerializer].Assembly.Location,
        [System.Linq.Enumerable].Assembly.Location,
        [System.Xml.XmlReader].Assembly.Location,
        'System.dll'
    )
    $ico = Join-Path $here 'app.ico'
    $tmp = Join-Path $data 'DesktopOrganizer.new.exe'
    if (Test-Path $tmp) { Remove-Item $tmp -Force }
    $provider = New-Object Microsoft.CSharp.CSharpCodeProvider
    $cp = New-Object System.CodeDom.Compiler.CompilerParameters
    $cp.GenerateExecutable = $true
    $cp.GenerateInMemory = $false
    $cp.OutputAssembly = $tmp
    $cp.CompilerOptions = "/target:winexe /optimize+ /win32icon:`"$ico`""
    foreach ($r in $refs) { [void]$cp.ReferencedAssemblies.Add($r) }
    $res = $provider.CompileAssemblyFromSource($cp, $code)
    $errs = @($res.Errors | Where-Object { -not $_.IsWarning })
    if ($errs.Count -gt 0) {
        $lines = $code -split "`n"
        $txt = ($errs | ForEach-Object { "line $($_.Line),$($_.Column) $($_.ErrorNumber): $($_.ErrorText)`r`n    > " + $lines[[Math]::Max(0, $_.Line - 1)].Trim() }) -join "`r`n"
        throw "Compile errors:`r`n$txt"
    }
    Move-Item -Force $tmp $exe
    Set-Content -Path $stampFile -Value $hash
    "$(Get-Date -Format s)  build OK" | Set-Content $log
    exit 0
} catch {
    $msg = "$(Get-Date -Format s)  BUILD FAILED`r`n" + ($_ | Out-String) + "`r`n" + ($_.Exception.Message)
    if ($_.Exception.InnerException) { $msg += "`r`n" + $_.Exception.InnerException.Message }
    $msg | Set-Content $log
    exit 1
}
