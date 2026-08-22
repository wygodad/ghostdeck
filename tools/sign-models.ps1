#Requires -Version 7
# Signs data/models.json -> data/models.json.sig (ECDSA P-256 / SHA-256, DER, base64).
# Run with pwsh 7+ after every model-data change:
#   1. bump Devices.DataVersion, build
#   2. GhostDeck.exe --dump-models data/models.json
#   3. pwsh tools/sign-models.ps1
#   4. commit data/models.json + data/models.json.sig
# The signature is over the EXACT file bytes; data/models.json* is marked -text in
# .gitattributes so the git blob (what raw.githubusercontent serves) stays byte-identical.
param(
    [string]$File = "$PSScriptRoot\..\data\models.json",
    [string]$KeyPath = "$env:USERPROFILE\.ghostdeck\model-signing.key"
)

$sha = [System.Security.Cryptography.HashAlgorithmName]::SHA256
$der = [System.Security.Cryptography.DSASignatureFormat]::Rfc3279DerSequence

$ecdsa = [System.Security.Cryptography.ECDsa]::Create()
$n = 0
$ecdsa.ImportPkcs8PrivateKey([Convert]::FromBase64String((Get-Content $KeyPath -Raw).Trim()), [ref]$n)
$data = [IO.File]::ReadAllBytes((Resolve-Path $File))
$sig = $ecdsa.SignData($data, $sha, $der)
[IO.File]::WriteAllText("$File.sig", [Convert]::ToBase64String($sig))
Write-Output "signed: $File.sig"

# self-check against the COMMITTED public key, so a key mix-up fails here, not at users
$pub = [System.Security.Cryptography.ECDsa]::Create()
$pub.ImportSubjectPublicKeyInfo([Convert]::FromBase64String((Get-Content "$PSScriptRoot\model-signing.pub" -Raw).Trim()), [ref]$n)
if (-not $pub.VerifyData($data, $sig, $sha, $der)) { Write-Error "self-verify FAILED (wrong key?)"; exit 1 }
Write-Output "self-verify with tools/model-signing.pub: OK"
