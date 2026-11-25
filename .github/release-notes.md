## Classe_controle 1.9.1

Supervision de salle informatique pour Windows 10/11 (64 bits). Une seule application (`ClasseControle.exe`) : mode professeur ou élève choisi à l'installation ou au premier lancement. Aucun prérequis.

### Quel fichier télécharger ?

| Fichier | Pour |
|---|---|
| **ClasseControle-Setup.exe** | **Installateur recommandé** : choisir « Poste du professeur » ou « Poste élève » |
| ClasseControle.exe | L'application seule, sans installation (mode demandé au premier lancement) |
| ClasseControle-portable.zip | L'application + le guide d'utilisation |

### Nouveauté de la 1.9.1 : jeux Windows supprimés pour tous les utilisateurs
Sur un **poste élève**, l'installateur propose (coché par défaut) : **« Supprimer les jeux préinstallés de Windows pour TOUS les utilisateurs »**.
- Solitaire, Candy Crush, Bubble Witch, Minecraft, Mahjong, Démineur, Sudoku, Asphalt… sont désinstallés de **tous les comptes** du poste ;
- ils sont **retirés de l'image Windows** : les comptes créés plus tard ne les auront pas ;
- l'installation automatique des jeux « suggérés » par Windows est désactivée (stratégie respectée par Windows Éducation / Entreprise) ;
- Minecraft Éducation n'est pas concerné ; journal : `%ProgramData%\Classe_controle\remove-games.log`.

Déploiement silencieux :
- `ClasseControle-Setup.exe /VERYSILENT /ROLE=eleve /ROOM=CLASSE` (jeux supprimés)
- `ClasseControle-Setup.exe /VERYSILENT /ROLE=eleve /ROOM=CLASSE /MERGETASKS="!supprjeux"` (jeux conservés)
- `ClasseControle-Setup.exe /VERYSILENT /ROLE=prof`

Un jeu réinstallé plus tard depuis le Microsoft Store reste bloqué si la catégorie « Jeux préinstallés de Windows » est cochée dans les Restrictions de la console.

### Rappel (1.9.0)
Restrictions par élève, catégories prêtes (jeux, plateformes, messageries, réseaux sociaux, vidéo, jeux en ligne), désinstallation des jeux depuis la console.
