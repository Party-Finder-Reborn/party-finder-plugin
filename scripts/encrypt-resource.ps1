param(
    [Parameter(Mandatory=$true)]
    [string]$PrivateKeyPath,
    
    [Parameter(Mandatory=$true)]
    [string]$OutputPath
)

# Read the private key
if (-not (Test-Path $PrivateKeyPath)) {
    Write-Host "Private key file not found: $PrivateKeyPath"
    exit 1
}

$privateKey = Get-Content $PrivateKeyPath -Raw

# Derive encryption key (must match ResourceDecoder.cs logic)
$assemblyName = "PartyFinderReborn"
$assemblyVersion = $env:tag -replace '^v', '' # Remove 'v' prefix if present
if (-not $assemblyVersion) { $assemblyVersion = "0.0.0.0" }
$constant = "PFR2024SecureKey"
$keyDerivationString = "$assemblyName`:$assemblyVersion`:$constant"

# Compute SHA256 of the combined string
$sha256 = [System.Security.Cryptography.SHA256]::Create()
$keyBytes = [System.Text.Encoding]::UTF8.GetBytes($keyDerivationString)
$derivedKey = $sha256.ComputeHash($keyBytes)

# Generate random IV
$aes = [System.Security.Cryptography.Aes]::Create()
$aes.GenerateIV()
$iv = $aes.IV

# Encrypt the private key with PKCS7 padding
$aes.Key = $derivedKey
$encryptor = $aes.CreateEncryptor()
$privateKeyBytes = [System.Text.Encoding]::UTF8.GetBytes($privateKey)
$padSize = 16 - ($privateKeyBytes.Length % 16)
$paddedKeyBytes = $privateKeyBytes + [byte[]]($padSize) * $padSize
$encrypted = $encryptor.TransformFinalBlock($paddedKeyBytes, 0, $paddedKeyBytes.Length)

# Combine IV + encrypted data
$encryptedResource = $iv + $encrypted

# Ensure output directory exists
$outputDir = Split-Path -Parent $OutputPath
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

# Write to output file
[System.IO.File]::WriteAllBytes($OutputPath, $encryptedResource)

Write-Host "Encrypted resource created: $OutputPath"
Write-Host "Key derivation input: $keyDerivationString"

# Cleanup
$aes.Dispose()
$sha256.Dispose()
$encryptor.Dispose()
