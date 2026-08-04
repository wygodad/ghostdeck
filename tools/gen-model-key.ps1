# Generates the model-database signing keypair (ECDSA P-256). Run ONCE, with pwsh 7+.
#
# The PRIVATE key is written OUTSIDE the repo and must never be committed - back it up
# (password manager or offline storage); losing it means a new key + app release, a leak
# means anyone can sign model databases. The PUBLIC key is committed as
# tools/model-signing.pub and embedded in Core/ModelDb.cs (the app's only trust anchor).
param([string]$KeyPath = "$env:USERPROFILE\.ghostdeck\model-signing.key")

if (Test-Path $KeyPath) { Write-Error "refusing to overwrite the existing key: $KeyPath"; exit 1 }
New-Item -ItemType Directory -Force (Split-Path $KeyPath) | Out-Null

$ecdsa = [System.Security.Cryptography.ECDsa]::Create([System.Security.Cryptography.ECCurve]::CreateFromFriendlyName("nistP256"))
[IO.File]::WriteAllText($KeyPath, [Convert]::ToBase64String($ecdsa.ExportPkcs8PrivateKey()))
$pub = [Convert]::ToBase64String($ecdsa.ExportSubjectPublicKeyInfo())
[IO.File]::WriteAllText("$PSScriptRoot\model-signing.pub", $pub + "`n")

Write-Output "private key: $KeyPath   (BACK IT UP - never commit)"
Write-Output "public key written to tools/model-signing.pub; embed the same value in Core/ModelDb.cs:"
Write-Output $pub
