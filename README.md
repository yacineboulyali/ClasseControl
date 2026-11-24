# Classe_controle

Outil **original** de supervision de salle informatique (type « NetSupport School »),
écrit en **C# / .NET 8** avec interface **Avalonia**. Deux exécutables autonomes
(aucune installation, aucun runtime à poser) pour Windows 10/11 64 bits.

- **Console professeur** — découvre les postes, les verrouille, envoie messages
  et fichiers, lance/ferme des applications, voit et contrôle un écran à distance.
- **Agent élève** — tourne discrètement (icône de notification), exécute les
  ordres de la console.

> Usage **pédagogique encadré** uniquement, sur des postes dont vous avez la
> responsabilité. Ce n'est pas une copie de NetSupport : c'est une
> implémentation indépendante d'un protocole simple.

## Architecture

```
ClasseControle.Common    Protocole réseau partagé (messages + encodage fil)
ClasseControle.Agent     Poste élève  (Avalonia + API Windows P/Invoke)
ClasseControle.Console   Poste prof   (Avalonia)
```

- **Découverte** : broadcast UDP (port 5401), filtré par un *code de salle*.
- **Contrôle** : TCP (port 5402), messages JSON préfixés par leur longueur.
- **Écran** : capture GDI `BitBlt` → JPEG, streaming à quelques images/seconde.
- **Contrôle à distance** : `SendInput` (souris/clavier), coordonnées normalisées.

## Fonctionnalités v1

- 🔒 Verrouiller / déverrouiller un poste ou toute la classe
- 💬 Message / consigne à un poste ou à la classe
- 👁 Voir l'écran d'un élève en direct **+ prise de contrôle** souris/clavier
- 📁 Envoyer un fichier (toute la classe ou sélection)
- 🚀 Lancer une application ou une URL à distance
- ✖ Fermer une application à distance (par nom de processus)

## Compilation

Nécessite le SDK .NET 8.

```bash
# Console professeur (exe autonome)
dotnet publish ClasseControle.Console/ClasseControle.Console.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o publish/console

# Agent élève (exe autonome)
dotnet publish ClasseControle.Agent/ClasseControle.Agent.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o publish/agent
```

> Les projets Agent/Console ciblent `net8.0` + Avalonia et se **compilent depuis
> Linux ou Windows** ; les API Windows (capture, injection) ne s'exécutent que
> sous Windows (cible `win-x64`).

## Configuration de l'agent

Fichier `agent.ini` créé à côté de l'exe au premier lancement :

```ini
RoomCode=CLASSE          # doit être identique à celui de la console
DisplayName=NOM-DU-PC    # nom affiché dans la console
```

## Pistes v2

- Miniatures live de tous les écrans en continu
- Diffusion de l'écran du **professeur** vers les élèves
- Chiffrement TLS du canal de contrôle + mot de passe fort
- Liste de présence, journal des actions
- Blocage de sites / applications par liste
- Service Windows + déploiement MSI/GPO

## Licence

À définir par l'auteur (ex. MIT). Code original.
