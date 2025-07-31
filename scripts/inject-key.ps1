param(
    [Parameter(Mandatory=$true)]
    [string]$PrivateKeyPath,
    
    [Parameter(Mandatory=$true)]
    [string]$ConstantsFilePath
)

# Read the private key from file
if (Test-Path $PrivateKeyPath) {
    $privateKey = Get-Content $PrivateKeyPath -Raw
    
    # Escape special characters for C# string literal
    $escapedKey = $privateKey -replace '\\', '\\' -replace '"', '\"' -replace "`r`n", '\n' -replace "`n", '\n'
    
    # Read the Constants.cs file
    $content = Get-Content $ConstantsFilePath -Raw
    
    # Replace the placeholder with the actual key
    $newContent = $content -replace 'INJECTED_PRIVATE_KEY', $escapedKey
    
    # Write back to file
    Set-Content -Path $ConstantsFilePath -Value $newContent -NoNewline
    
    Write-Host "✅ Private key injected successfully into $ConstantsFilePath"
} else {
    Write-Host "❌ Private key file not found: $PrivateKeyPath"
    exit 1
}
