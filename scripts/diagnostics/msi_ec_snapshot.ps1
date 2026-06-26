# =====================================================================
#  msi_ec_snapshot.ps1   --   READ-ONLY EC snapshot for model support
#
#  Reads a few EC registers in EACH MSI Center scenario so we can map
#  your laptop. Uses the official MSI WMI interface (root\wmi MSI_ACPI /
#  Get_Data) -- NO writes, nothing is changed.
#
#  HOW TO RUN (admin PowerShell):
#     pwsh -ExecutionPolicy Bypass -File msi_ec_snapshot.ps1
#  Then follow the prompts (switch MSI Center scenario, press Enter).
#  Results are printed and saved to msi_ec_snapshot_results.txt next to
#  this script. Paste them into your GitHub "Model support request".
# =====================================================================

$ErrorActionPreference = 'Stop'

# --- self-elevate (EC access needs admin) ---
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
           ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "Administrator rights required - relaunching (UAC)..." -ForegroundColor Yellow
    Start-Process (Get-Process -Id $PID).Path -Verb RunAs `
        -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    return
}

$outFile = Join-Path $PSScriptRoot 'msi_ec_snapshot_results.txt'
"=== MSI EC snapshot  $(Get-Date)  (READ-ONLY) ===" | Out-File $outFile -Encoding utf8

$inst = Get-CimInstance -Namespace root\wmi -ClassName MSI_ACPI

# read one EC byte: input Bytes[0]=address, output Bytes[1]=value
function ReadEC([byte]$addr) {
    $b = New-Object byte[] 32
    $b[0] = $addr
    $pkg = New-CimInstance -Namespace root\wmi -ClassName Package_32 -ClientOnly -Property @{ Bytes = $b }
    $res = Invoke-CimMethod -InputObject $inst -MethodName Get_Data -Arguments @{ Data = $pkg }
    return $res.Data.Bytes[1]
}

# read EC firmware string (Get_EC returns it)
function ReadFirmware {
    try {
        $res = Invoke-CimMethod -InputObject $inst -MethodName Get_EC
        $b = $res.Data.Bytes
        $s = ''
        for ($i = 2; $i -lt $b.Length -and $b[$i] -ne 0; $i++) { if ($b[$i] -ge 32 -and $b[$i] -lt 127) { $s += [char]$b[$i] } }
        return $s
    } catch { return '(unknown)' }
}

# common power/fan registers across MSI generations
$addrs = [byte[]](0xD2, 0xD4, 0xEB, 0xF2, 0xF4, 0xD7, 0xEF)

function Snapshot($label) {
    $line = ("[{0,-20}] " -f $label) + (($addrs | ForEach-Object {
        '{0:X2}={1:X2}' -f $_, (ReadEC $_)
    }) -join '  ')
    Write-Host $line -ForegroundColor Cyan
    $line | Out-File $outFile -Append -Encoding utf8
}

$fw = "EC firmware: " + (ReadFirmware)
Write-Host $fw -ForegroundColor Green
$fw | Out-File $outFile -Append -Encoding utf8
Write-Host ""
Write-Host "  READ-ONLY. Switch the MSI Center scenario, then press Enter." -ForegroundColor Green
Write-Host ""

foreach ($s in 'SILENT', 'BALANCED', 'EXTREME PERFORMANCE', 'SUPER BATTERY') {
    [void](Read-Host ">> In MSI Center set scenario: $s   -- then press Enter")
    Snapshot $s
}

Write-Host ""
Write-Host "Done. Results saved to: $outFile" -ForegroundColor Green
[void](Read-Host "Press Enter to close")
