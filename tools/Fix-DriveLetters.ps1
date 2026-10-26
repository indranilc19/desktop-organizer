# Keeps external drives on fixed letters, matched by volume label (e.g. a 5 TB drive labelled "Media" always on D:).
# Edit drive-letters.json next to this script to change the rules. Windows normally remembers a letter once it is set;
# this script sets it and (optionally) re-checks at every sign-in.
param([switch]$Quiet)
$ErrorActionPreference = 'Stop'
$cfgFile = Join-Path $PSScriptRoot 'drive-letters.json'
$logFile = Join-Path $PSScriptRoot 'drive-letters.log'
function Say([string]$m) { if (-not $Quiet) { Write-Host $m }; Add-Content -Path $logFile -Value ("{0:s}  {1}" -f (Get-Date), $m) }

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) { Write-Host 'This needs administrator rights. Run "Keep drive letters.bat" and click Yes.'; if (-not $Quiet) { Read-Host 'Press Enter to close' }; exit 1 }

# ---- settings (label -> letter)
$pins = [ordered]@{}
if (Test-Path $cfgFile) {
    try { $j = Get-Content $cfgFile -Raw | ConvertFrom-Json; $pins = [ordered]@{}; foreach ($p in $j.PSObject.Properties) { $pins[$p.Name] = [string]$p.Value } } catch { }
}

function Show-Volumes {
    Get-CimInstance Win32_Volume | Where-Object { $_.DriveType -in 2, 3 -and $_.Label -ne 'System Reserved' -and $_.Capacity } |
        Sort-Object DriveLetter |
        Format-Table @{n='Letter';e={$_.DriveLetter}}, @{n='Label';e={$_.Label}}, @{n='Size (GB)';e={[math]::Round($_.Capacity/1GB)}},
                     @{n='Type';e={ if ($_.DriveType -eq 2) {'Removable'} else {'Fixed'} }} -AutoSize | Out-String | Write-Host
}

if (-not $Quiet) {
    Write-Host ''
    Write-Host 'Drives connected right now:' -ForegroundColor Cyan
    Show-Volumes
    if ($pins.Count -eq 0) {
        Write-Host 'No rules yet. Type a volume label and the letter it should always get, e.g.  Media=D  (empty line to finish).'
        while ($true) { $l = Read-Host 'label=letter'; if (-not $l) { break }; $kv = $l -split '=', 2; if ($kv.Count -eq 2) { $pins[$kv[0].Trim()] = $kv[1].Trim().TrimEnd(':').ToUpper() } }
    }
    $cur = ($pins.GetEnumerator() | ForEach-Object { "$($_.Key) -> $($_.Value):" }) -join ', '
    Write-Host "Current rules: $cur"
    ($pins | ConvertTo-Json) | Set-Content -Path $cfgFile -Encoding UTF8
}

$reserved = @('C') + @($pins.Values)
function Free-Letter {
    $used = @(Get-CimInstance Win32_Volume | Where-Object { $_.DriveLetter } | ForEach-Object { $_.DriveLetter.Substring(0,1) }) +
            @(Get-PSDrive -PSProvider FileSystem | ForEach-Object { $_.Name })
    foreach ($c in [char[]]('ZYXWVUTSRQPONMLKJHGF')) { $s = [string]$c; if ($used -notcontains $s -and $reserved -notcontains $s) { return $s } }
    throw 'No free drive letter left.'
}

foreach ($rule in $pins.GetEnumerator()) {
    $label = $rule.Key; $letter = $rule.Value.ToUpper()
    try {
        $vol = Get-CimInstance Win32_Volume | Where-Object { $_.Label -eq $label } | Select-Object -First 1
        if (-not $vol) { Say "$label is not connected - nothing to do (its letter will be applied when it is)."; continue }
        if ($vol.DriveLetter -eq "${letter}:") { Say "$label is already on ${letter}:"; continue }
        $occupant = Get-CimInstance Win32_Volume | Where-Object { $_.DriveLetter -eq "${letter}:" } | Select-Object -First 1
        if ($occupant) {
            $to = Free-Letter
            Say ("Moving '{0}' off {1}: to {2}:" -f $occupant.Label, $letter, $to)
            Set-CimInstance -InputObject $occupant -Property @{ DriveLetter = "${to}:" }
        }
        Set-CimInstance -InputObject $vol -Property @{ DriveLetter = "${letter}:" }
        Say "$label is now on ${letter}:"
    } catch { Say "Couldn't set $label to ${letter}: - $($_.Exception.Message)" }
}

if (-not $Quiet) {
    $taskName = 'Desktop Organizer - keep drive letters'
    $exists = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    $ans = Read-Host ('Re-check these letters automatically at every sign-in? (Y/N)' + ($(if ($exists) {' [currently ON]'} else {''})))
    if ($ans -match '^[Yy]') {
        $act = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument ("-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"{0}`" -Quiet" -f $PSCommandPath)
        $trg = New-ScheduledTaskTrigger -AtLogOn
        $prn = New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) -RunLevel Highest
        Register-ScheduledTask -TaskName $taskName -Action $act -Trigger $trg -Principal $prn -Force | Out-Null
        Say 'Sign-in check is ON.'
    } elseif ($ans -match '^[Nn]' -and $exists) {
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
        Say 'Sign-in check is OFF.'
    }
    Write-Host ''
    Write-Host 'Done. Drives now:' -ForegroundColor Cyan
    Show-Volumes
    Read-Host 'Press Enter to close'
}
