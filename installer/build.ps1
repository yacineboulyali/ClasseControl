<#
  Construit l'application unique (ClasseControle.exe) puis l'installateur.
  Prérequis (sur Windows) : SDK .NET 8 et Inno Setup 6 (https://jrsoftware.org/isinfo.php).
  Usage : powershell -ExecutionPolicy Bypass -File installer\build.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$common = @('-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
            '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
            '-p:EnableCompressionInSingleFile=true')

# Application unique : console du professeur + agent élève dans un seul .exe.
dotnet publish ClasseControle.App/ClasseControle.App.csproj @common -o publish/app
if ($LASTEXITCODE -ne 0) { throw "Échec de la compilation de l'application" }

$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
          "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 introuvable : installez-le depuis https://jrsoftware.org/isinfo.php' }
& $iscc (Join-Path $PSScriptRoot 'ClasseControle.iss')
if ($LASTEXITCODE -ne 0) { throw "Échec d'Inno Setup (code $LASTEXITCODE)" }
Write-Host "Installateur créé dans : $root\publish\installer" -ForegroundColor Green
