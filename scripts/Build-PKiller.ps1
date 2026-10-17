param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src\PKiller.App\PKiller.App.csproj"
$output = Join-Path $repoRoot "artifacts"
$packageOutput = Join-Path $output "AppPackages"
$publisher = "CN=GKJupiter.PKiller.Dev"
$certificatePath = Join-Path $output "PKiller.cer"

New-Item -ItemType Directory -Force -Path $output, $packageOutput | Out-Null

$certificate = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq $publisher -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if (-not $certificate) {
    $certificate = New-SelfSignedCertificate `
        -Type Custom `
        -Subject $publisher `
        -CertStoreLocation Cert:\CurrentUser\My `
        -KeyAlgorithm RSA `
        -KeyLength 2048 `
        -HashAlgorithm SHA256 `
        -KeyUsage DigitalSignature `
        -NotAfter (Get-Date).AddYears(5) `
        -TextExtension @(
            "2.5.29.37={text}1.3.6.1.5.5.7.3.3",
            "2.5.29.19={text}"
        )
}

Export-Certificate -Cert $certificate -FilePath $certificatePath -Force | Out-Null

dotnet publish $project `
    --configuration $Configuration `
    --runtime win-x64 `
    --no-self-contained `
    "-p:AppxPackageDir=$packageOutput\" `
    "-p:AppxPackageSigningEnabled=true" `
    "-p:PackageCertificateThumbprint=$($certificate.Thumbprint)"

if ($LASTEXITCODE -ne 0) {
    throw "PKiller MSIX build failed with exit code $LASTEXITCODE."
}

$package = Get-ChildItem $packageOutput -Filter "*.msix" -Recurse |
    Where-Object { $_.FullName -notmatch "\\Dependencies\\" } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $package) {
    throw "The build succeeded, but no MSIX package was produced."
}

Copy-Item $certificatePath $package.DirectoryName -Force
Write-Host "Signed per-user MSIX: $($package.FullName)"
Write-Host "Publisher certificate: $certificatePath"
Write-Host "To install or update, run Install-PKiller.ps1 with the package path."
