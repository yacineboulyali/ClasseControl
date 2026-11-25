## Classe_controle 1.9.0

Supervision de salle informatique pour Windows 10/11 (64 bits). Une seule application (`ClasseControle.exe`) : mode professeur ou élève choisi à l'installation ou au premier lancement. Aucun prérequis.

### Quel fichier télécharger ?

| Fichier | Pour |
|---|---|
| **ClasseControle-Setup.exe** | **Installateur recommandé** : choisir « Poste du professeur » ou « Poste élève ». Silencieux : `/VERYSILENT /ROLE=eleve /ROOM=CLASSE` ou `/VERYSILENT /ROLE=prof` |
| ClasseControle.exe | L'application seule, sans installation (mode demandé au premier lancement) |
| ClasseControle-portable.zip | L'application + le guide d'utilisation |

### Nouveautés de la 1.9.0
- ⛔ **Restrictions par élève** : clic droit sur une vignette → restrictions de ce poste uniquement ; elles sont mémorisées et réappliquées à chaque connexion (icône ⛔ sur la vignette)
- 🗂 **Catégories prêtes** : jeux préinstallés de Windows, plateformes de jeux (Steam, Roblox, Epic…), messageries, réseaux sociaux, vidéo, jeux en ligne — en plus des listes libres
- 🎮 **Jeux préinstallés de Windows 10/11** (Solitaire, Candy Crush, Minecraft, Mahjong, Bubble Witch…) : reconnus par leur paquet du Microsoft Store et **fermés dès leur ouverture**, ou **désinstallés** d'un clic (onglet Administrer, sans droits administrateur, compte rendu de chaque poste)

Voir le [README](https://github.com/yacineboulyali/ClasseControl#aperçu-en-images) pour les captures et GIF animés.

### Remarques
- Mettez à jour la console ET les postes élèves vers la même version.
- La désinstallation des jeux vaut pour l'utilisateur Windows du poste ; ils restent réinstallables depuis le Microsoft Store.
- Les .exe ne sont pas signés : SmartScreen peut afficher un avertissement (« Informations complémentaires » → « Exécuter quand même »).
