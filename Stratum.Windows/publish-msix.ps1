# Gera o pacote .msix do Stratum para Windows (sideload) e instala.
#
#   powershell -ExecutionPolicy Bypass -File Stratum.Windows/publish-msix.ps1
#
# Usa um certificado self-signed de desenvolvimento (criado sob demanda em
# Stratum.Windows/dev-stratum.{pfx,cer}, NÃO commitados). A implantação AppX
# valida a assinatura contra a raiz confiável da MÁQUINA (0x800B0109/0x800B010A
# à toa se o cert só estiver no CurrentUser), então o script importa o .cer em
# CurrentUser\Root\TrustedPeople e, se não for admin, eleva via UAC para
# LocalMachine\Root\TrustedPeople antes do Add-AppxPackage.
param(
    [string]$Configuration = 'Release',
    [string]$Rid = 'win-x64',
    [switch]$SkipInstall = $false
)

$ErrorActionPreference = 'Stop'
$projDir = $PSScriptRoot
$pfxPath = Join-Path $projDir 'dev-stratum.pfx'
$cerPath = Join-Path $projDir 'dev-stratum.cer'
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
            Export-Certificate -Cert $inMy -FilePath $cerPath -Type CERT -Force | Out-Null
            return $inMy
        } catch {
            Write-Host 'PFX inválido, gerando novo certificado...'
            Remove-Item $pfxPath -Force -ErrorAction SilentlyContinue
        }
    }

    # Cert end-entity com EKU de assinatura de código — o mesmo perfil que o
    # Visual Studio gera para sideload: CA=false (APPX0107 se for CA, e o
    # signtool recusa repisar msix já assinado pelo tooling).
    Write-Host 'Gerando certificado self-signed CN=Stratum...'
    $cert = New-SelfSignedCertificate -Type Custom `
        -Subject 'CN=Stratum' `
        -KeyUsage DigitalSignature `
        -KeyAlgorithm RSA -KeyLength 2048 `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}ca=false') `
        -NotAfter (Get-Date).AddYears(5)

    Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $pfxPassword -Force | Out-Null
    Export-Certificate -Cert $cert -FilePath $cerPath -Type CERT -Force | Out-Null

    return $cert
}

function Add-ToStore([string]$storeName, $scope, $certificate) {
    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($storeName, $scope)
    $store.Open('ReadWrite')
    $store.Add($certificate)
    $store.Close()
}

function Trust-CurrentUser($certificate) {
    foreach ($storeName in @('Root', 'TrustedPeople')) {
        $has = Get-ChildItem "Cert:\CurrentUser\$storeName" |
            Where-Object { $_.Thumbprint -eq $certificate.Thumbprint }
        if (-not $has) {
            Write-Host "Confiando em CurrentUser\$storeName..."
            Add-ToStore $storeName 'CurrentUser' $certificate
        }
    }
}

function Trust-LocalMachine($certificate) {
    foreach ($storeName in @('Root', 'TrustedPeople')) {
        $has = Get-ChildItem "Cert:\LocalMachine\$storeName" -ErrorAction SilentlyContinue |
            Where-Object { $_.Thumbprint -eq $certificate.Thumbprint }
        if (-not $has) {
            # LocalMachine exige elevação: roda um processo admin que importa o .cer
            Write-Host "Elevando via UAC para confiar LocalMachine\$storeName..."
            $script = @"
`$cer = '$cerPath'
foreach (`$s in '$storeName') {
    Import-Certificate -FilePath `$cer -CertStoreLocation "Cert:\LocalMachine\`$s" -ErrorAction Stop | Out-Null
}
"@
            $tmp = Join-Path $env:TEMP 'stratum-trust.ps1'
            Set-Content -Path $tmp -Value $script -Encoding UTF8
            $p = Start-Process -FilePath 'powershell.exe' -ArgumentList @(
                '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$tmp`"") `
                -Verb RunAs -PassThru -Wait
            Remove-Item $tmp -Force -ErrorAction SilentlyContinue
            if ($p.ExitCode -ne 0) { throw 'Falha ao confiar o certificado na máquina (UAC cancelado?).' }
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

if ($SkipInstall) {
    Write-Host 'Instalação ignorada (-SkipInstall). Dê duplo clique no .msix (App Installer) após confiar o certificado.'
    exit 0
}

Trust-CurrentUser $cert
Trust-LocalMachine $cert

Write-Host 'Instalando pacote...'
Add-AppxPackage -Path $msix.FullName
if ($LASTEXITCODE -ne 0) { throw 'Falha na instalação do pacote.' }

$pkg = Get-AppxPackage -Name 'StratumWindows'
Write-Host "Instalado: $($pkg.PackageFullName) — status $($pkg.Status). Veja no Menu Iniciar."