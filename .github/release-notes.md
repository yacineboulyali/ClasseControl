## Classe_controle 1.13.0 — fiabilité (Windows Defender / contrôle des applications)

Supervision de salle informatique pour Windows 10/11 (64 bits). Une seule application (`ClasseControle.exe`) : mode professeur ou élève. Aucun prérequis.

### Quel fichier télécharger ?

| Fichier | Pour |
|---|---|
| **ClasseControle-Setup.exe** | **Installateur recommandé** (mise à jour automatique incluse) |
| ClasseControle-portable.zip | L'application (exe + 2 DLL signées Microsoft) + le guide, sans installation |
| ClasseControle.exe | L'application en un seul fichier (compatibilité avec les anciennes mises à jour) |

### Corrections
- 🛡 **Erreur 4551 « Une stratégie de contrôle d'application a bloqué ce fichier »** : l'application ne s'extrait plus dans le dossier temporaire au lancement, et la DLL graphique ANGLE (non signée) n'est plus livrée. Il ne reste **qu'un seul fichier non signé** : `ClasseControle.exe`.
- 🧰 **Plus aucun script PowerShell** : la création des tâches, le pare-feu, la mise à jour automatique et la suppression des jeux sont faits par `ClasseControle.exe` lui-même (les scripts étaient bloqués en « mode de langage restreint »).
- 💬 **Message clair** si Windows bloque l'application ou si le démarrage échoue, avec un journal (`%LocalAppData%\Classe_controle\erreurs.log`).
- 🖥 Rendu graphique de secours (logiciel) sur les PC sans pilote OpenGL.

### À savoir
- **Smart App Control** (Windows 11) bloque tout programme non signé : la solution définitive est la **signature numérique** du code (en préparation). D'ici là : le désactiver sur un PC personnel, ou sur un PC de l'établissement faire autoriser `C:\Program Files\Classe_controle\ClasseControle.exe` par le service informatique.
- Postes installés en 1.10 – 1.12 : ils se mettent à jour seuls, mais **réinstaller une fois** avec ce nouvel installateur applique la nouvelle organisation (sans scripts).
