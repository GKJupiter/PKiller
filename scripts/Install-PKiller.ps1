param(
    [Parameter(Mandatory = $true)]
    [string] $PackagePath
)

$ErrorActionPreference = "Stop"
$resolvedPackage = (Resolve-Path $PackagePath).Path
$packageDirectory = Split-Path -Parent $resolvedPackage
$certificatePath = Join-Path $packageDirectory "PKiller.cer"

if (-not (Test-Path $certificatePath)) {
    throw "PKiller.cer was not found next to the MSIX package."
}

$certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($certificatePath)
if ($certificate.Subject -ne "CN=GKJupiter.PKiller.Dev") {
    throw "The publisher certificate does not match the PKiller package publisher."
}

$trusted = Get-ChildItem Cert:\CurrentUser\TrustedPeople |
    Where-Object { $_.Thumbprint -eq $certificate.Thumbprint } |
    Select-Object -First 1

if (-not $trusted) {
    Import-Certificate -FilePath $certificatePath -CertStoreLocation Cert:\CurrentUser\TrustedPeople | Out-Null
}

# Add-AppxPackage installs for the current user and updates an existing installation in place.
Add-AppxPackage -Path $resolvedPackage
Write-Host "PKiller installed or updated for the current Windows user."
