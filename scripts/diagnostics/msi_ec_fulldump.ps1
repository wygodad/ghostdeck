# =====================================================================
#  msi_ec_fulldump.ps1   --   READ-ONLY full EC dump for model support
#
#  Dumps the whole EC (256 bytes) in EACH MSI Center scenario so we can
#  diff them byte-by-byte and find the registers your model uses.
#  This is the MOST USEFUL file for a model-support request.
#
#  Uses the official MSI WMI interface (root\wmi MSI_ACPI / Get_Data) --
#  NO writes, nothing is changed.
#
#  HOW TO RUN (admin PowerShell):
#     pwsh -ExecutionPolicy Bypass -File msi_ec_fulldump.ps1
#  Tips: do it at idle (less sensor noise). Switch only the scenario.
#  Results are saved to msi_ec_fulldump_results.txt next to this script.
#  Attach/paste that file into your GitHub "Model support request".
# =====================================================================

$ErrorActionPreference = 'Stop'

# --- self-elevate ---
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
           ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "Administrator rights required - relaunching (UAC)..." -ForegroundColor Yellow
    Start-Process (Get-Process -Id $PID).Path -Verb RunAs `
        -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    return
}

$outFile = Join-Path $PSScriptRoot 'msi_ec_fulldump_results.txt'
"=== MSI EC FULL DUMP  $(Get-Date)  (READ-ONLY) ===" | Out-File $outFile -Encoding utf8

$inst = Get-CimInstance -Namespace root\wmi -ClassName MSI_ACPI

function ReadEC([byte]$addr) {
    $b = New-Object byte[] 32
    $b[0] = $addr
    $pkg = New-CimInstance -Namespace root\wmi -ClassName Package_32 -ClientOnly -Property @{ Bytes = $b }
    $res = Invoke-CimMethod -InputObject $inst -MethodName Get_Data -Arguments @{ Data = $pkg }
    return $res.Data.Bytes[1]
}

# firmware string (Get_EC)
try {
    $r = Invoke-CimMethod -InputObject $inst -MethodName Get_EC
    $bb = $r.Data.Bytes; $fw = ''
    for ($i = 2; $i -lt $bb.Length -and $bb[$i] -ne 0; $i++) { if ($bb[$i] -ge 32 -and $bb[$i] -lt 127) { $fw += [char]$bb[$i] } }
    ("EC firmware: " + $fw) | Tee-Object -FilePath $outFile -Append | Out-Null
    Write-Host ("EC firmware: " + $fw) -ForegroundColor Green
} catch { }

# full 0x00..0xFF dump in a 16-column grid
function DumpAll($label) {
    "" | Out-File $outFile -Append -Encoding utf8
    "[$label]" | Out-File $outFile -Append -Encoding utf8
    for ($row = 0; $row -lt 256; $row += 16) {
        $vals = for ($c = 0; $c -lt 16; $c++) { '{0:X2}' -f (ReadEC ([byte]($row + $c))) }
        ('{0:X2}: {1}' -f $row, ($vals -join ' ')) | Out-File $outFile -Append -Encoding utf8
    }
    Write-Host "  $label - dumped (256 bytes)" -ForegroundColor Cyan
}

Write-Host ""
Write-Host "  READ-ONLY. Switch the MSI Center scenario, then press Enter." -ForegroundColor Green
Write-Host ""

foreach ($s in 'SILENT', 'BALANCED', 'EXTREME PERFORMANCE', 'SUPER BATTERY') {
    [void](Read-Host ">> In MSI Center set scenario: $s   -- then press Enter")
    DumpAll $s
}

Write-Host ""
Write-Host "Done. Results saved to: $outFile" -ForegroundColor Green
[void](Read-Host "Press Enter to close")
