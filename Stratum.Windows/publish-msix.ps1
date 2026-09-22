# Gera o pacote .msix do Stratum para Windows (sideload).
#
#   powershell -ExecutionPolicy Bypass -File Stratum.Windows/publish-msix.ps1
#
# Usa um certificado self-signed de desenvolvimento (criado sob demanda em
# Stratum.Windows/dev-stratum.pfx, NÃO commitado). Para instalar, confie no
# certificado (o script importa para Raiz Confiável do usuário atual) e abra
# o .msix com duplo clique.
param(
    [string]$Configuration = 'Release',
    [string]$Rid = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$projDir = $PSScriptRoot
$pfxPath = Join-Path $projDir 'dev-stratum.pfx'
$pfxPassword = ConvertTo-SecureString -String 'stratum-dev' -AsPlainText -Force

function Get-OrCreateCert {
    # Se o .pfx existe, o certificado dono dele é a verdade (importa se preciso)
    if (Test-Path $pfxPath) {
        try {
            $pfxCert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
                $pfxPath, 'stratum-dev', 'EphemeralKeySet')
            $inMy = Get-ChildItem Cert:\CurrentUser\My |
                Where-Object { $_.Thumbprint -eq $pfxCert.Thumbprint } |
                Select-Object -First 1
            if (-not $inMy) {
                $storeMy = New-Object System.Security.Cryptography.X509Certificates.X509Store('My', 'CurrentUser')
                $storeMy.Open('ReadWrite')
                $storeMy.Add($pfxCert)
                $storeMy.Close()
                $inMy = $pfxCert
            }
            Trust-Cert $inMy
            return $inMy
        } catch {
            Write-Host 'PFX inválido, gerando novo certificado...'
            Remove-Item $pfxPath -Force -ErrorAction SilentlyContinue
        }
    }

    Write-Host 'Gerando certificado self-signed CN=Stratum...'
    $cert = New-SelfSignedCertificate -Type Custom `
        -Subject 'CN=Stratum' `
        -KeyUsage DigitalSignature `
        -KeyAlgorithm RSA -KeyLength 2048 `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}') `
        -NotAfter (Get-Date).AddYears(5)

    Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $pfxPassword -Force | Out-Null
    Trust-Cert $cert

    return $cert
}

function Trust-Cert($cert) {
    foreach ($storeName in @('Root', 'TrustedPeople')) {
        $trusted = Get-ChildItem "Cert:\CurrentUser\$storeName" |
            Where-Object { $_.Thumbprint -eq $cert.Thumbprint }
        if (-not $trusted) {
            Write-Host "Confiando no certificado ($storeName do usuário atual)..."
            $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($storeName, 'CurrentUser')
            $store.Open('ReadWrite')
            $store.Add($cert)
            $store.Close()
        }
    }
}

$cert = Get-OrCreateCert
Write-Host "Cert: $($cert.Thumbprint)"

$outDir = Join-Path $projDir "bin/publish-msix-$Rid"
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }

Get-Process -Name 'Stratum' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

dotnet publish (Join-Path $projDir 'Stratum.Windows.csproj') `
    -c $Configuration -r $Rid --self-contained `
    -o $outDir `
    -p:WindowsPackageType=MSIX `
    -p:EnableMsixTooling=true `
    -p:PublishAppxPackage=true `
    -p:AppxPackageSigningEnabled=true `
    -p:PackageCertificateThumbprint=$($cert.Thumbprint) `
    -p:PackageCertificateKeyFile=$pfxPath `
    -p:PackageCertificatePassword=stratum-dev

if ($LASTEXITCODE -ne 0) { throw 'publish falhou' }

$msix = Get-ChildItem (Join-Path $projDir 'AppPackages') -Filter '*.msix' -Recurse -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $msix) {
    $msix = Get-ChildItem $outDir -Filter '*.msix' -ErrorAction SilentlyContinue | Select-Object -First 1
}
if (-not $msix) { throw 'arquivo .msix não gerado' }

Write-Host ''
Write-Host "MSIX pronto: $($msix.FullName)"
Write-Host 'Instale com duplo clique (App Installer) ou: Add-AppxPackage $msix'
