<#
  Construit l'application unique puis l'installateur :
   - publish/app    : ClasseControle.exe + ses bibliothèques (installateur, version portable,
                      mise à jour automatique) ; rien n'est extrait dans le dossier temporaire ;
   - publish/single : un seul ClasseControle.exe autonome (compatibilité : mises à jour des
                      postes installés en 1.10 à 1.12).
  Prérequis (sur Windows) : SDK .NET 8 et Inno Setup 6 (https://jrsoftware.org/isinfo.php).
  Usage : powershell -ExecutionPolicy Bypass -File installer\build.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

# Numéro de version : une seule source, « #define AppVersion » de l'installateur.
$version = (Select-String -Path (Join-Path $PSScriptRoot 'ClasseControle.iss') -Pattern '#define AppVersion "([^"]+)"').Matches[0].Groups[1].Value
Write-Host "Version $version"

$common = @('-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', "-p:Version=$version",
            '-p:PublishSingleFile=true', '-p:EnableCompressionInSingleFile=true', '-p:DebugType=none')

# Version installée : bibliothèques natives à côté de l'exe (pas d'extraction dans %TEMP%).
dotnet publish ClasseControle.App/ClasseControle.App.csproj @common '-p:IncludeNativeLibrariesForSelfExtract=false' -o publish/app
if ($LASTEXITCODE -ne 0) { throw "Échec de la compilation de l'application" }

# Version « un seul fichier » (compatibilité avec les anciens programmes de mise à jour).
dotnet publish ClasseControle.App/ClasseControle.App.csproj @common '-p:IncludeNativeLibrariesForSelfExtract=true' -o publish/single
if ($LASTEXITCODE -ne 0) { throw "Échec de la compilation de l'application (fichier unique)" }

$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
          "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 introuvable : installez-le depuis https://jrsoftware.org/isinfo.php' }
& $iscc (Join-Path $PSScriptRoot 'ClasseControle.iss')
if ($LASTEXITCODE -ne 0) { throw "Échec d'Inno Setup (code $LASTEXITCODE)" }
Write-Host "Installateur créé dans : $root\publish\installer" -ForegroundColor Green
