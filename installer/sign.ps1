<#
  Signature de code (Authenticode) des exécutables Classe_controle.

  POURQUOI : Windows bloque les programmes NON SIGNÉS :
    • Smart App Control / stratégie WDAC → « Erreur 4551 : une stratégie de contrôle
      d'application a bloqué ce fichier » (blocage total) ;
    • SmartScreen → « Windows a protégé votre ordinateur » (bouton « Exécuter quand même »).
  Un fichier signé avec un certificat reconnu par Microsoft n'est plus bloqué par Smart App
  Control, et SmartScreen cesse d'avertir.

  Ce script est appelé par build.ps1 (exécutable) et par Inno Setup (installateur et
  programme de désinstallation). Il ne fait RIEN si aucun certificat n'est configuré :
  la construction continue, sans signature.

  Deux façons de fournir le certificat (variables d'environnement, ou secrets GitHub) :

  1) Certificat .pfx (Certum, Sectigo, SSL.com… avec clé exportable) :
       CC_SIGN_PFX_BASE64    contenu du fichier .pfx encodé en Base64
       CC_SIGN_PFX_PASSWORD  mot de passe du .pfx

  2) Azure Trusted Signing (« Artifact Signing », service de signature de Microsoft) :
       CC_TS_ENDPOINT        ex. https://weu.codesigning.azure.net
       CC_TS_ACCOUNT         nom du compte de signature
       CC_TS_PROFILE         nom du profil de certificat
       AZURE_TENANT_ID, AZURE_CLIENT_ID, AZURE_CLIENT_SECRET   application Entra ID
                             qui a le rôle « Trusted Signing Certificate Profile Signer »
       CC_TS_DLIB            (facultatif) chemin de Azure.CodeSigning.Dlib.dll

  Usage :
    sign.ps1 -Check            → code 0 si un certificat est configuré, 1 sinon
    sign.ps1 fichier1.exe …    → signe puis vérifie chaque fichier
#>
param(
    [switch] $Check,
    [Parameter(ValueFromRemainingArguments = $true)] [string[]] $Path
)

$ErrorActionPreference = 'Stop'

function Get-Method {
    if ($env:CC_SIGN_PFX_BASE64) { return 'pfx' }
    if ($env:CC_TS_ENDPOINT -and $env:CC_TS_ACCOUNT -and $env:CC_TS_PROFILE) { return 'azure' }
    return ''
}

$method = Get-Method
if ($Check) { if ($method) { Write-Host "Signature : $method"; exit 0 } else { exit 1 } }
if (-not $Path -or $Path.Count -eq 0) { throw 'Aucun fichier à signer.' }
if (-not $method) { Write-Host 'Aucun certificat configuré : fichiers NON signés.'; exit 0 }

function Find-SignTool {
    $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $found = Get-ChildItem -Path $kits -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $found) { throw 'signtool.exe introuvable : installez le SDK Windows 10/11.' }
    return $found.FullName
}

function Get-TrustedSigningDlib {
    if ($env:CC_TS_DLIB -and (Test-Path $env:CC_TS_DLIB)) { return $env:CC_TS_DLIB }
    $dir = Join-Path $env:TEMP 'cc-trusted-signing'
    $dll = Get-ChildItem -Path $dir -Filter Azure.CodeSigning.Dlib.dll -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\' } | Select-Object -First 1
    if ($dll) { return $dll.FullName }
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $zip = Join-Path $dir 'client.zip'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri 'https://www.nuget.org/api/v2/package/Microsoft.Trusted.Signing.Client' -OutFile $zip -UseBasicParsing
    Expand-Archive -Path $zip -DestinationPath $dir -Force
    $dll = Get-ChildItem -Path $dir -Filter Azure.CodeSigning.Dlib.dll -Recurse | Where-Object { $_.FullName -match '\\x64\\' } | Select-Object -First 1
    if (-not $dll) { throw 'Azure.CodeSigning.Dlib.dll introuvable dans le paquet Microsoft.Trusted.Signing.Client.' }
    return $dll.FullName
}

$signtool = Find-SignTool
$temp = @()
try {
    if ($method -eq 'pfx') {
        $pfx = Join-Path $env:TEMP ("cc-sign-{0}.pfx" -f [guid]::NewGuid())
        [IO.File]::WriteAllBytes($pfx, [Convert]::FromBase64String($env:CC_SIGN_PFX_BASE64))
        $temp += $pfx
        $signArgs = @('sign', '/fd', 'SHA256', '/f', $pfx, '/tr', 'http://timestamp.digicert.com', '/td', 'SHA256',
                      '/d', 'Classe_controle')
        if ($env:CC_SIGN_PFX_PASSWORD) { $signArgs += @('/p', $env:CC_SIGN_PFX_PASSWORD) }
    }
    else {
        $meta = Join-Path $env:TEMP ("cc-sign-{0}.json" -f [guid]::NewGuid())
        @{ Endpoint = $env:CC_TS_ENDPOINT; CodeSigningAccountName = $env:CC_TS_ACCOUNT; CertificateProfileName = $env:CC_TS_PROFILE } |
            ConvertTo-Json | Set-Content -Path $meta -Encoding UTF8
        $temp += $meta
        $signArgs = @('sign', '/fd', 'SHA256', '/tr', 'http://timestamp.acs.microsoft.com', '/td', 'SHA256',
                      '/d', 'Classe_controle', '/dlib', (Get-TrustedSigningDlib), '/dmdf', $meta)
    }

    foreach ($file in $Path) {
        $file = $file.Trim('"')
        if (-not (Test-Path $file)) { throw "Fichier introuvable : $file" }
        # Le serveur d'horodatage est parfois indisponible : quelques essais.
        $ok = $false
        for ($i = 1; $i -le 4 -and -not $ok; $i++) {
            & $signtool @signArgs $file | Out-Host
            if ($LASTEXITCODE -eq 0) { $ok = $true } else { Start-Sleep -Seconds (5 * $i) }
        }
        if (-not $ok) { throw "Échec de la signature : $file" }
        & $signtool verify /pa $file | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Signature invalide : $file" }
        Write-Host "Signé : $file" -ForegroundColor Green
    }
}
finally {
    foreach ($t in $temp) { Remove-Item $t -Force -ErrorAction SilentlyContinue }
}
exit 0
