## Classe_controle 1.10.0 — mise à jour automatique

Supervision de salle informatique pour Windows 10/11 (64 bits). Une seule application (`ClasseControle.exe`) : mode professeur ou élève. Aucun prérequis.

### Quel fichier télécharger ?

| Fichier | Pour |
|---|---|
| **ClasseControle-Setup.exe** | **Installateur recommandé** (nécessaire pour la mise à jour automatique) |
| ClasseControle.exe | L'application seule, sans installation (pas de mise à jour automatique : la console signale les nouvelles versions) |
| ClasseControle-portable.zip | L'application + le guide d'utilisation |

### Nouveauté : mise à jour automatique
- Option de l'installateur **« Mise à jour automatique depuis GitHub »** (cochée par défaut, professeur et élève) : une tâche planifiée Windows (compte SYSTEM) vérifie **toutes les heures et au démarrage** la dernière version publiée ici et l'installe seule.
- L'**agent élève redémarre de lui-même** sur la nouvelle version (moins d'une minute) ; la **console propose de redémarrer** (pour ne pas couper un cours).
- **Sécurité** : seule la version officielle de GitHub (HTTPS) est installée ; taille et numéro de version vérifiés ; rien n'est accepté depuis le réseau local.
- Console : **version de chaque poste** (⟳ orange si plus ancienne), boutons **« Mettre à jour les postes »** (tout de suite) et **« Vérifier les mises à jour »** (onglet Administrer).
- Journal : `%ProgramData%\Classe_controle\update.log`. Internet (GitHub) doit être accessible.

### Important
Les postes en 1.9.1 ou avant n'ont pas encore la mise à jour automatique : **installez une fois cette version 1.10.0** avec l'installateur (en silencieux : `/VERYSILENT /ROLE=eleve /ROOM=CLASSE`). Les versions suivantes s'installeront ensuite toutes seules.

Déploiement silencieux sans mise à jour automatique : ajouter `/MERGETASKS="!majauto"`.
