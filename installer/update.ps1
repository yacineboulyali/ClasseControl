<#
  Mise à jour automatique de Classe_controle (exécuté par la tâche planifiée
  « \Classe_controle\MiseAJour », compte SYSTEM, toutes les heures et au démarrage).

  1. lit la dernière version publiée sur GitHub (HTTPS, dépôt officiel uniquement) ;
  2. si elle est plus récente que ClasseControle.exe installé : télécharge le nouvel
     exécutable dans le dossier d'installation (protégé : les élèves n'y écrivent pas),
     vérifie sa taille et son numéro de version ;
  3. renomme l'exécutable en cours (possible même s'il tourne) et met le nouveau à sa
     place. L'agent élève détecte la nouvelle version et redémarre dessus en moins d'une
     minute ; la console du professeur propose de redémarrer.

  Journal : %ProgramData%\Classe_controle\update.log
  Aucun fichier venant du réseau local n'est accepté : seule la version officielle
  GitHub est installée.
#>
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # la barre de progression ralentit énormément les téléchargements (PowerShell 5.1)
$Repo = 'yacineboulyali/ClasseControl'
$AppDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Exe = Join-Path $AppDir 'ClasseControle.exe'
$LogDir = Join-Path $env:ProgramData 'Classe_controle'
$Log = Join-Path $LogDir 'update.log'

New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
function Write-Log([string] $text) { "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  $text" | Out-File -FilePath $Log -Append -Encoding utf8 }
function Get-FileVersion([string] $path) {
    $i = (Get-Item $path).VersionInfo
    return [version]::new($i.FileMajorPart, $i.FileMinorPart, $i.FileBuildPart)
}

# Une seule exécution à la fois.
$mutex = New-Object System.Threading.Mutex($false, 'Global\ClasseControle.Update')
if (-not $mutex.WaitOne(0)) { exit 0 }
try {
    # Anciennes versions laissées par une mise à jour précédente (supprimées dès qu'elles ne tournent plus).
    Get-ChildItem -Path $AppDir -Filter 'ClasseControle.old*.exe' -ErrorAction SilentlyContinue |
        ForEach-Object { Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue }

    if (-not (Test-Path $Exe)) { Write-Log "ClasseControle.exe introuvable dans $AppDir"; exit 1 }
    $current = Get-FileVersion $Exe

    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    # PowerShell 5.1 : l'en-tête User-Agent se règle avec -UserAgent (pas avec -Headers).
    $ua = "ClasseControle-Updater/$current"
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/latest" -UserAgent $ua -TimeoutSec 30 -UseBasicParsing
    $latest = [version]($release.tag_name.TrimStart('v', 'V'))
    $latest = [version]::new($latest.Major, $latest.Minor, [Math]::Max(0, $latest.Build))

    if ($latest -le $current) { exit 0 }   # déjà à jour : rien à journaliser

    $asset = $release.assets | Where-Object { $_.name -eq 'ClasseControle.exe' } | Select-Object -First 1
    if (-not $asset) { Write-Log "Version $latest publiée sans ClasseControle.exe"; exit 1 }

    Write-Log "Mise à jour $current -> $latest : téléchargement…"
    $tmp = Join-Path $AppDir 'ClasseControle.update.tmp'
    Remove-Item $tmp -Force -ErrorAction SilentlyContinue
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $tmp -UserAgent $ua -TimeoutSec 600 -UseBasicParsing

    $size = (Get-Item $tmp).Length
    if ($size -ne [int64]$asset.size) { Remove-Item $tmp -Force; Write-Log "Taille inattendue ($size au lieu de $($asset.size)) : annulé"; exit 1 }
    $downloaded = Get-FileVersion $tmp
    if ($downloaded -ne $latest) { Remove-Item $tmp -Force; Write-Log "Version du fichier $downloaded différente de $latest : annulé"; exit 1 }

    # Remplacement : un exécutable en cours d'utilisation peut être renommé, pas écrasé.
    $old = Join-Path $AppDir ("ClasseControle.old-{0}.exe" -f (Get-Date -Format 'yyyyMMddHHmmss'))
    Rename-Item -Path $Exe -NewName (Split-Path $old -Leaf)
    try { Move-Item -Path $tmp -Destination $Exe }
    catch { Rename-Item -Path $old -NewName 'ClasseControle.exe'; throw }

    # Version affichée dans « Applications et fonctionnalités ».
    $uninstall = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{6C4B2F0E-3E7A-4C38-9B8E-5A1C2D3E4F10}_is1'
    if (Test-Path $uninstall) { Set-ItemProperty -Path $uninstall -Name 'DisplayVersion' -Value $latest.ToString() -ErrorAction SilentlyContinue }

    Write-Log "Version $latest installée (l'agent redémarre tout seul ; la console propose de redémarrer)."
}
catch { Write-Log "Échec : $($_.Exception.Message)"; exit 1 }
finally { $mutex.ReleaseMutex(); $mutex.Dispose() }
exit 0
