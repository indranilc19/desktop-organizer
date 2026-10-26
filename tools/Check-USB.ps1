# Reports USB controllers, problem devices and which USB drives are connected at what speed.
# Writes usb-report.txt next to this script.
$out = Join-Path $PSScriptRoot 'usb-report.txt'
$r = New-Object System.Collections.Generic.List[string]
$r.Add("USB report  $(Get-Date -Format 'yyyy-MM-dd HH:mm')")
$r.Add('')
$r.Add('== USB host controllers and root hubs')
Get-PnpDevice -PresentOnly | Where-Object { $_.Class -eq 'USB' -and $_.FriendlyName -match 'Host Controller|Root Hub|USB4|Hub' } |
    Sort-Object FriendlyName | ForEach-Object { $r.Add(("  [{0}] {1}" -f $_.Status, $_.FriendlyName)) }
$r.Add('')
$r.Add('== USB devices with problems (present or recently seen)')
$bad = Get-PnpDevice | Where-Object { ($_.Class -in 'USB','DiskDrive','USBDevice') -and $_.Status -in 'Error','Degraded','Unknown' -and $_.Problem -ne 'CM_PROB_PHANTOM' }
if ($bad) { $bad | ForEach-Object { $r.Add(("  [{0}] {1}  (problem: {2})" -f $_.Status, $_.FriendlyName, $_.Problem)) } } else { $r.Add('  none') }
$r.Add('')
$r.Add('== USB drives connected now')
foreach ($d in Get-CimInstance Win32_DiskDrive | Where-Object { $_.InterfaceType -eq 'USB' -or $_.PNPDeviceID -like 'USBSTOR*' -or $_.PNPDeviceID -like 'SCSI*' }) {
    $letters = ($d | Get-CimAssociatedInstance -ResultClassName Win32_DiskPartition | Get-CimAssociatedInstance -ResultClassName Win32_LogicalDisk | ForEach-Object { "$($_.DeviceID) $($_.VolumeName)" }) -join ', '
    $loc = ''; $speed = ''
    try {
        $dev = Get-PnpDevice -InstanceId $d.PNPDeviceID -ErrorAction Stop
        $parent = ($dev | Get-PnpDeviceProperty -KeyName 'DEVPKEY_Device_Parent' -ErrorAction Stop).Data
        for ($i = 0; $i -lt 4 -and $parent; $i++) {
            $p = Get-PnpDevice -InstanceId $parent -ErrorAction Stop
            $li = ($p | Get-PnpDeviceProperty -KeyName 'DEVPKEY_Device_LocationInfo' -ErrorAction SilentlyContinue).Data
            if ($li -and -not $loc) { $loc = $li }
            if ($p.FriendlyName -match 'UAS|Mass Storage') { $speed = if ($p.FriendlyName -match 'UAS') { 'UASP (USB 3 fast mode)' } else { 'USB Mass Storage (BOT)' } }
            $parent = ($p | Get-PnpDeviceProperty -KeyName 'DEVPKEY_Device_Parent' -ErrorAction SilentlyContinue).Data
        }
    } catch { }
    $r.Add(("  {0}  {1:N0} GB  [{2}]  port: {3}  driver: {4}" -f $d.Model, ($d.Size / 1GB), $letters, $loc, $speed))
}
$r.Add('')
$r.Add('== Quick read-speed test (first 1 GB of the biggest file on each USB drive)')
foreach ($ld in Get-CimInstance Win32_LogicalDisk | Where-Object { $_.DriveType -in 2,3 -and $_.DeviceID -notin 'C:','I:' }) {
    try {
        $f = Get-ChildItem -Path ($ld.DeviceID + '\') -Recurse -File -ErrorAction SilentlyContinue | Where-Object { $_.Length -gt 1GB } | Select-Object -First 1
        if (-not $f) { $r.Add("  $($ld.DeviceID) no large file to test"); continue }
        $buf = New-Object byte[] (8MB); $fs = [IO.File]::OpenRead($f.FullName); $sw = [Diagnostics.Stopwatch]::StartNew(); $tot = 0
        while ($tot -lt 1GB) { $n = $fs.Read($buf, 0, $buf.Length); if ($n -le 0) { break }; $tot += $n }
        $sw.Stop(); $fs.Close()
        $mbs = [math]::Round(($tot / 1MB) / $sw.Elapsed.TotalSeconds)
        $verdict = if ($mbs -ge 80) { 'USB 3 speed' } elseif ($mbs -ge 25) { 'USB 2 speed (slow port or cable)' } else { 'very slow - check cable/port' }
        $r.Add("  $($ld.DeviceID) $($ld.VolumeName): $mbs MB/s  -> $verdict")
    } catch { $r.Add("  $($ld.DeviceID) test failed: $($_.Exception.Message)") }
}
$r | Set-Content -Path $out -Encoding UTF8
$r | ForEach-Object { Write-Host $_ }
Write-Host ''
Write-Host "Saved to $out"
Read-Host 'Press Enter to close'
