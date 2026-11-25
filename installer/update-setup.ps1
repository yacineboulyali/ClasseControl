<#
  Crée / supprime la tâche planifiée de mise à jour automatique de Classe_controle.
  Install   : tâche « \Classe_controle\MiseAJour », compte SYSTEM, au démarrage
              (après 2 min) puis toutes les heures ; les utilisateurs du poste ont le
              droit de la LANCER (bouton « Mettre à jour les postes » de la console),
              pas de la modifier. Elle n'installe que la version officielle GitHub.
  Uninstall : supprime la tâche.
#>
param(
    [ValidateSet('Install', 'Uninstall')] [string] $Action = 'Install',
    [string] $ScriptPath = ''
)
$ErrorActionPreference = 'Stop'
$TaskName = 'MiseAJour'
$TaskPath = '\Classe_controle\'

Unregister-ScheduledTask -TaskName $TaskName -TaskPath $TaskPath -Confirm:$false -ErrorAction SilentlyContinue
if ($Action -eq 'Uninstall') { exit 0 }
if (-not (Test-Path $ScriptPath)) { throw "Script introuvable : $ScriptPath" }

$action = New-ScheduledTaskAction -Execute "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" `
    -Argument "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$ScriptPath`""
$atStartup = New-ScheduledTaskTrigger -AtStartup
$atStartup.Delay = 'PT2M'
# Toutes les heures : déclencheur quotidien répété chaque heure pendant 24 h
# (forme acceptée par toutes les versions de Windows 10).
$hourly = New-ScheduledTaskTrigger -Daily -At '00:05'
$hourly.Repetition = (New-ScheduledTaskTrigger -Once -At '00:05' `
    -RepetitionInterval (New-TimeSpan -Hours 1) -RepetitionDuration (New-TimeSpan -Days 1)).Repetition
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -RunOnlyIfNetworkAvailable `
    -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew `
    -ExecutionTimeLimit (New-TimeSpan -Minutes 30)
Register-ScheduledTask -TaskName $TaskName -TaskPath $TaskPath -Action $action -Trigger @($atStartup, $hourly) `
    -Principal $principal -Settings $settings -Description 'Mise à jour automatique de Classe_controle depuis GitHub' | Out-Null

# Droits : SYSTEM et Administrateurs ont tout ; Utilisateurs peuvent lire et LANCER la tâche.
$service = New-Object -ComObject 'Schedule.Service'
$service.Connect()
$task = $service.GetFolder($TaskPath.TrimEnd('\')).GetTask($TaskName)
$task.SetSecurityDescriptor('D:(A;;FA;;;SY)(A;;FA;;;BA)(A;;GRGX;;;BU)', 0)
exit 0
