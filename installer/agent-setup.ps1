<#
  Configuration de l'agent élève Classe_controle (appelé par l'installateur).

  Install   : crée une tâche planifiée cachée qui lance l'agent à l'ouverture de
              session de TOUT utilisateur du poste, le relance s'il est arrêté,
              et ouvre les ports du pare-feu (UDP 5401 découverte, TCP 5402 contrôle).
  Uninstall : supprime la tâche et les règles de pare-feu.

  Pourquoi une tâche planifiée et pas un « service Windows » classique ?
  Un service tourne dans la session 0, invisible : il ne pourrait ni afficher
  l'icône de notification, ni l'écran de verrouillage, ni capturer l'écran de
  l'élève. La tâche planifiée joue le même rôle (démarrage automatique, en
  arrière-plan, relance automatique) tout en s'exécutant dans la session de l'élève.
#>
param(
    [ValidateSet('Install', 'Uninstall')] [string] $Action = 'Install',
    [string] $AgentPath = '',
    [string] $AgentArguments = '--eleve'
)

$ErrorActionPreference = 'Stop'
$TaskName = 'Classe_controle Agent'
$TaskPath = '\Classe_controle\'

function Remove-AgentConfig {
    Unregister-ScheduledTask -TaskName $TaskName -TaskPath $TaskPath -Confirm:$false -ErrorAction SilentlyContinue
    Get-NetFirewallRule -DisplayName 'Classe_controle Agent*' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
}

if ($Action -eq 'Uninstall') {
    Remove-AgentConfig
    exit 0
}

if (-not (Test-Path $AgentPath)) { throw "Agent introuvable : $AgentPath" }
Remove-AgentConfig

# --- Tâche planifiée : à l'ouverture de session de n'importe quel utilisateur ---
$action    = New-ScheduledTaskAction -Execute $AgentPath -Argument $AgentArguments -WorkingDirectory (Split-Path $AgentPath)
$trigger   = New-ScheduledTaskTrigger -AtLogOn
# S-1-5-32-545 = groupe « Utilisateurs » : la tâche s'exécute dans la session de chaque élève.
$principal = New-ScheduledTaskPrincipal -GroupId 'S-1-5-32-545' -RunLevel Highest
$settings  = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) `
    -MultipleInstances IgnoreNew -Hidden
Register-ScheduledTask -TaskName $TaskName -TaskPath $TaskPath -Action $action -Trigger $trigger `
    -Principal $principal -Settings $settings -Description 'Agent élève Classe_controle (supervision de salle)' | Out-Null

# --- Pare-feu : réseaux privés et domaine uniquement ---
New-NetFirewallRule -DisplayName 'Classe_controle Agent (découverte UDP 5401)' -Direction Inbound -Protocol UDP `
    -LocalPort 5401 -Program $AgentPath -Action Allow -Profile Private,Domain | Out-Null
New-NetFirewallRule -DisplayName 'Classe_controle Agent (contrôle TCP 5402)' -Direction Inbound -Protocol TCP `
    -LocalPort 5402 -Program $AgentPath -Action Allow -Profile Private,Domain | Out-Null

exit 0
