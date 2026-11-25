## Classe_controle 1.7.0

Supervision de salle informatique pour Windows 10/11 (64 bits). Une seule application (`ClasseControle.exe`) : mode professeur ou élève choisi à l'installation ou au premier lancement. Aucun prérequis.

### Quel fichier télécharger ?

| Fichier | Pour |
|---|---|
| **ClasseControle-Setup.exe** | **Installateur recommandé** : choisir « Poste du professeur » ou « Poste élève ». Silencieux : `/VERYSILENT /ROLE=eleve /ROOM=CLASSE` ou `/VERYSILENT /ROLE=prof` |
| ClasseControle.exe | L'application seule, sans installation (mode demandé au premier lancement) |
| ClasseControle-portable.zip | L'application + le guide d'utilisation |

### Nouveautés de la 1.7.0
- 🖱 **Contrôler** un poste en un clic (ruban « Contrôler » ou clic droit sur la vignette)
- 🧩 **Applications ouvertes** : bouton de la barre latérale qui partage l'écran ; pour chaque poste, l'application sur laquelle l'élève travaille (icône + titre) et ses autres applications, que l'on peut fermer d'un clic droit
- 🔇 **Couper le son** des élèves (les annonces du professeur restent audibles)
- ↔ **Glisser-déposer les vignettes** pour reproduire le plan de la salle (ordre mémorisé)

Voir le [README](https://github.com/yacineboulyali/ClasseControl#aperçu-en-images) pour les captures et GIF animés.

### Remarques
- Mettez à jour la console ET les postes élèves vers la même version.
- Les .exe ne sont pas signés : SmartScreen peut afficher un avertissement (« Informations complémentaires » → « Exécuter quand même »).
- Ports : UDP 5401 (découverte), TCP 5402 (contrôle). Même code de salle partout.
