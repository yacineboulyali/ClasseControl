## Classe_controle 1.5.0

Supervision de salle informatique pour Windows 10/11 (64 bits). Aucun prérequis : .NET est inclus dans les fichiers.

### Quel fichier télécharger ?

| Fichier | Pour |
|---|---|
| **ClasseControle-Setup.exe** | **Installateur recommandé** : au premier écran, choisir « Poste du professeur » (console + raccourcis) ou « Poste élève » (agent en arrière-plan, sans raccourci). Déploiement silencieux : `/VERYSILENT /ROLE=eleve /ROOM=CLASSE` |
| ClasseControle-portable.zip | Sans installation : console + agent + guide + `agent.ini` |
| ClasseControle.Console.exe | Console du professeur seule |
| ClasseControle.Agent.exe | Agent élève seul |


### Nouveautés
- 📵 **Couper Internet** sur la classe ou la sélection (le réseau de la salle reste joignable)
- 👥 **Document d'équipe fiabilisé** : plusieurs élèves peuvent écrire en même temps sans mélanger leurs phrases ; **modèle de document** à compléter
- 🎨 Interface à ruban, icônes flat monochromes, **zoom des vignettes** (−, +, Ajuster, Ctrl + molette)
- 🎓 Séance avec identification des élèves, 🌐 ouverture de site, 🖼 écran de verrouillage personnalisé, 📡 présentation de l'écran d'un élève
- 🌙 Écran noir, 💬 discussion, 📢 annonce audio, ⭐ étoiles, 📊 sondages, ✋ demandes d'aide, 🚀 lancement rapide, 🖥 bureau élève
- 📋 Liste de présence, 📜 journal des actions

Voir le [README](https://github.com/yacineboulyali/ClasseControl#aperçu-en-images) pour les captures et GIF animés.

### Remarques
- Les .exe ne sont pas signés : SmartScreen peut afficher un avertissement (« Informations complémentaires » → « Exécuter quand même »).
- Ports utilisés : UDP 5401 (découverte), TCP 5402 (contrôle). Même code de salle sur la console et les postes.
- Usage pédagogique encadré, sur des postes dont vous avez la responsabilité.
