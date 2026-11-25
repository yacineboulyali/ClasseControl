## Classe_controle 1.8.0

Supervision de salle informatique pour Windows 10/11 (64 bits). Une seule application (`ClasseControle.exe`) : mode professeur ou élève choisi à l'installation ou au premier lancement. Aucun prérequis.

### Quel fichier télécharger ?

| Fichier | Pour |
|---|---|
| **ClasseControle-Setup.exe** | **Installateur recommandé** : choisir « Poste du professeur » ou « Poste élève ». Silencieux : `/VERYSILENT /ROLE=eleve /ROOM=CLASSE` ou `/VERYSILENT /ROLE=prof` |
| ClasseControle.exe | L'application seule, sans installation (mode demandé au premier lancement) |
| ClasseControle-portable.zip | L'application + le guide d'utilisation |

### Nouveautés de la 1.8.0
- ⌨ **Bloquer le clavier et la souris** des élèves (ruban, barre latérale, clic droit sur une vignette) : l'écran reste visible, un bandeau prévient l'élève, et le professeur peut toujours prendre le contrôle à distance
- 🔒 Le verrouillage, l'écran noir et la diffusion plein écran bloquent désormais aussi le clavier et la souris physiques (plus d'Alt+Tab ni de touche Windows pour en sortir)
- 🛟 Sécurité : si la console se déconnecte, les postes se débloquent automatiquement au bout de 60 secondes

### Rappel (1.7.0)
Contrôle direct d'un poste, panneau des applications ouvertes, son coupé, glisser-déposer des vignettes.

Voir le [README](https://github.com/yacineboulyali/ClasseControl#aperçu-en-images) pour les captures et GIF animés.

### Remarques
- Mettez à jour la console ET les postes élèves vers la même version.
- Ctrl+Alt+Suppr reste toujours géré par Windows.
- Les .exe ne sont pas signés : SmartScreen peut afficher un avertissement (« Informations complémentaires » → « Exécuter quand même »).
