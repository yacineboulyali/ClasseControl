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

## Fonctionnalités

- 🖥️ **Miniatures de tous les écrans en direct** dans la console
- 🔒 Verrouiller / déverrouiller un poste ou toute la classe
- 💬 Message / consigne à un poste ou à la classe
- 👁 Voir l'écran d'un élève en grand **+ prise de contrôle** souris/clavier
- 📁 Envoyer un fichier (toute la classe ou sélection)
- 🚀 Lancer une application ou une URL à distance
- ✖ Fermer une application à distance (par nom de processus)
- ⏻ **Éteindre / redémarrer / déconnecter** les postes à distance (avec confirmation)
- ☑️ Sélection groupée (tout / rien / par poste)
- 📺 **Diffusion de l'écran du professeur** vers la classe (plein écran avec
  clavier/souris bloqués, ou en fenêtre)
- ⛔ **Restrictions** : applications interdites (fermées automatiquement) et
  sites interdits (onglet fermé si son titre contient un mot-clé)
- ✋ **Main levée** : l'élève demande de l'aide depuis l'icône de notification,
  la carte du poste passe en orange et remonte en tête
- 📋 **Liste de présence** de la séance (arrivée, départ, mains levées),
  export CSV pour Excel
- 📜 **Journal des actions** du professeur, enregistré automatiquement dans
  `Documents\Classe_controle\journal-AAAA-MM-JJ.csv`

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

## Pistes v1.3+

- Chiffrement TLS du canal de contrôle + mot de passe fort
- Service Windows + déploiement MSI/GPO
- Blocage des sites au niveau réseau (proxy / DNS) en complément du blocage par titre
- Sondages / quiz rapides envoyés à la classe

## Licence

À définir par l'auteur (ex. MIT). Code original.
