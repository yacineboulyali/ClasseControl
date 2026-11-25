<#
  Supprime les jeux préinstallés de Windows 10/11 (Solitaire, Candy Crush, Minecraft,
  Mahjong, Démineur, Bubble Witch…) pour TOUS les utilisateurs du poste.
  Lancé par l'installateur (droits administrateur) si l'option est cochée.

   1. désinstalle les jeux de tous les comptes existants ;
   2. les retire de l'image Windows : les comptes créés plus tard ne les auront pas ;
   3. désactive l'installation automatique des jeux « suggérés » par Windows
      (stratégie respectée par Windows Éducation / Entreprise).

  Minecraft Éducation n'est PAS concerné. Les jeux restent réinstallables depuis le
  Microsoft Store ; l'agent Classe_controle peut alors les bloquer (Restrictions).
  Journal : %ProgramData%\Classe_controle\remove-games.log
  Liste à garder synchronisée avec ClasseControle.Common/RestrictionCatalog.cs.
#>
param([string] $Log = "$env:ProgramData\Classe_controle\remove-games.log")

$ErrorActionPreference = 'SilentlyContinue'
$patterns = @(
    '*SolitaireCollection*', 'king.com.*', '*CandyCrush*', '*BubbleWitch*', '*FarmHeroes*', '*MinecraftUWP*',
    '*MicrosoftMahjong*', '*Minesweeper*', '*MicrosoftJigsaw*', '*MicrosoftSudoku*', '*Gameloft*', '*Asphalt*',
    '*MarchofEmpires*', '*DisneyMagicKingdoms*', '*RoyalRevolt*', '*CaesarsSlots*', '*HiddenCity*', '*Zynga*'
)

New-Item -ItemType Directory -Force -Path (Split-Path $Log) | Out-Null
function Write-Log([string] $text) { "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  $text" | Out-File -FilePath $Log -Append -Encoding utf8 }
Write-Log '=== Suppression des jeux préinstallés (tous les utilisateurs) ==='

$removed = 0
foreach ($pattern in $patterns) {
    # 1) Comptes existants
    foreach ($pkg in @(Get-AppxPackage -AllUsers -Name $pattern)) {
        try {
            Remove-AppxPackage -Package $pkg.PackageFullName -AllUsers -ErrorAction Stop
            Write-Log "Supprimé pour tous les utilisateurs : $($pkg.Name)"
            $removed++
        }
        catch {
            # Anciennes versions de Windows 10 sans -AllUsers : au moins l'utilisateur courant.
            try {
                Remove-AppxPackage -Package $pkg.PackageFullName -ErrorAction Stop
                Write-Log "Supprimé (utilisateur courant seulement) : $($pkg.Name)"
                $removed++
            }
            catch { Write-Log "Échec : $($pkg.Name) — $($_.Exception.Message)" }
        }
    }
    # 2) Image Windows (nouveaux comptes)
    foreach ($prov in @(Get-AppxProvisionedPackage -Online | Where-Object { $_.DisplayName -like $pattern })) {
        try {
            Remove-AppxProvisionedPackage -Online -PackageName $prov.PackageName -ErrorAction Stop | Out-Null
            Write-Log "Retiré de l'image Windows (nouveaux comptes) : $($prov.DisplayName)"
        }
        catch { Write-Log "Échec (image) : $($prov.DisplayName) — $($_.Exception.Message)" }
    }
}

# 3) Plus d'installation automatique d'applications « suggérées »
try {
    $key = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent'
    New-Item -Path $key -Force | Out-Null
    Set-ItemProperty -Path $key -Name 'DisableWindowsConsumerFeatures' -Value 1 -Type DWord
    Write-Log 'Applications suggérées par Windows désactivées (stratégie CloudContent).'
}
catch { Write-Log "Échec de la stratégie CloudContent : $($_.Exception.Message)" }

Write-Log "Terminé : $removed paquet(s) supprimé(s)."
exit 0
