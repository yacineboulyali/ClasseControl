<#
  Construit les deux exécutables puis l'installateur.
  Prérequis (sur Windows) : SDK .NET 8 et Inno Setup 6 (https://jrsoftware.org/isinfo.php).
  Usage : powershell -ExecutionPolicy Bypass -File installer\build.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$common = @('-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
            '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
            '-p:EnableCompressionInSingleFile=true')

dotnet publish ClasseControle.Console/ClasseControle.Console.csproj @common -o publish/console
dotnet publish ClasseControle.Agent/ClasseControle.Agent.csproj @common -o publish/agent

# Icône .ico pour l'installateur (convertie depuis le PNG de l'application).
$ico = Join-Path $root 'ClasseControle.Console\app-icon.ico'
if (-not (Test-Path $ico)) {
    Add-Type -AssemblyName System.Drawing
    $bmp = [System.Drawing.Bitmap]::FromFile((Join-Path $root 'ClasseControle.Console\app-icon.png'))
    $icon = [System.Drawing.Icon]::FromHandle(($bmp.GetThumbnailImage(64, 64, $null, [IntPtr]::Zero)).GetHicon())
    $fs = [System.IO.File]::Create($ico); $icon.Save($fs); $fs.Close()
}

$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
          "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 introuvable : installez-le depuis https://jrsoftware.org/isinfo.php' }
& $iscc (Join-Path $PSScriptRoot 'ClasseControle.iss')
Write-Host "Installateur créé dans : $root\publish\installer" -ForegroundColor Green
