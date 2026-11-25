## Classe_controle 1.6.0 — une seule application

Supervision de salle informatique pour Windows 10/11 (64 bits). Aucun prérequis : .NET est inclus.

**Nouveau : la console du professeur et l'agent élève sont réunis dans une seule application, `ClasseControle.exe`.**
Le mode est choisi à l'installation (« Poste du professeur » ou « Poste élève »), ou au premier lancement de la version portable.

### Quel fichier télécharger ?

| Fichier | Pour |
|---|---|
| **ClasseControle-Setup.exe** | **Installateur recommandé** : au premier écran, choisir le mode du poste. Mode élève : démarrage automatique en arrière-plan, sans raccourci. Déploiement silencieux : `/VERYSILENT /ROLE=eleve /ROOM=CLASSE` ou `/VERYSILENT /ROLE=prof` |
| ClasseControle.exe | L'application seule, sans installation : au premier lancement, choisir « Poste du professeur » ou « Poste de l'élève » |
| ClasseControle-portable.zip | L'application + le guide d'utilisation |

Options de lancement : `ClasseControle.exe --prof` (console), `--eleve` (agent), `--choisir` (redemander le mode).

### Nouveautés de la 1.6.0
- Une seule application pour les deux modes, avec écran de choix au premier démarrage
- Installateur simplifié (un seul fichier installé), icône Windows de l'application

### Rappel (1.5.0)
Couper Internet, document d'équipe fiable à plusieurs, modèle de document, interface à ruban, zoom des vignettes, séance avec identification, sondages, étoiles, discussion, annonce audio…
Voir le [README](https://github.com/yacineboulyali/ClasseControl#aperçu-en-images) pour les captures et GIF animés.

### Remarques
- Les .exe ne sont pas signés : SmartScreen peut afficher un avertissement (« Informations complémentaires » → « Exécuter quand même »).
- Ports utilisés : UDP 5401 (découverte), TCP 5402 (contrôle). Même code de salle sur la console et les postes.
- Usage pédagogique encadré, sur des postes dont vous avez la responsabilité.
