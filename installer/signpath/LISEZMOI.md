# Signature gratuite avec SignPath Foundation

Windows bloque `ClasseControle.exe` (« Le Contrôle intelligent des applications a bloqué
une application potentiellement dangereuse », erreur 4551, SmartScreen) parce que le
programme **n'est pas signé numériquement**. Aucune modification du code ne peut l'éviter :
il faut une **signature de code** avec un certificat reconnu par Microsoft.

[SignPath Foundation](https://signpath.org/) offre gratuitement ce certificat aux projets
**open source**. La construction GitHub de Classe_controle est déjà prête : il ne reste
que les étapes ci-dessous, à faire une seule fois.

## Ce qui est déjà fait dans le dépôt

- Licence open source **MIT** (`LICENSE`), exigée par SignPath Foundation.
- **Politique de signature** et **confidentialité** publiées dans le `README.md`.
- Workflow `.github/workflows/installateur.yml` :
  1. construit `ClasseControle.exe` ;
  2. l'envoie à SignPath (configuration `programme`), attend la signature, la vérifie ;
  3. construit l'installateur avec l'exe signé ;
  4. envoie l'installateur à SignPath (configuration `installateur`) ;
  5. publie la version (installateur, exe, portable, `SHA256SUMS.txt`).
- Les étapes SignPath ne s'exécutent que si la variable `SIGNPATH_ORGANIZATION_ID`
  existe : sans elle, la construction continue sans signature, comme avant.

## Étapes à faire (une seule fois)

### 1. Demander l'adhésion à SignPath Foundation

1. Aller sur <https://signpath.org/> › **Apply** (formulaire pour projets open source).
2. Indiquer le dépôt `https://github.com/yacineboulyali/ClasseControl`, la licence MIT,
   et décrire le projet honnêtement : *outil pédagogique de supervision de salle
   informatique (type NetSupport School), installé par l'établissement sur ses propres
   postes, avec un indicateur visible pour l'élève (icône de notification)*.
3. Attendre la réponse de SignPath (quelques jours à quelques semaines). Ils vérifient
   que le projet respecte leurs conditions.

> SignPath peut refuser certains types de logiciels. Un outil de supervision est accepté
> plus facilement s'il est clairement réservé à un usage encadré, visible et documenté —
> c'est le cas ici (README, avertissement d'usage, icône visible chez l'élève).

### 2. Configurer le projet sur signpath.io (après acceptation)

Dans l'organisation créée par SignPath pour vous :

1. **Projet** : slug `ClasseControl` (sinon, créer la variable `SIGNPATH_PROJECT_SLUG`).
2. **Trusted build system** : relier le projet à **GitHub.com** (l'organisation SignPath
   doit avoir le système de build de confiance GitHub activé).
3. **Artifact configurations** : en créer deux, en copiant le contenu de :
   - `installer/signpath/programme.xml` → slug **`programme`** ;
   - `installer/signpath/installateur.xml` → slug **`installateur`**.
4. **Signing policy** : `release-signing` (fournie par SignPath Foundation ; sinon, créer
   la variable `SIGNPATH_POLICY_SLUG`). Chaque signature demande une **approbation
   manuelle** sur signpath.io (vous recevez un e-mail) ; le workflow attend jusqu'à 1 heure.
5. Créer un **utilisateur CI** (ou un jeton d'API) avec le rôle *Submitter* sur ce projet
   et copier son **API token**.

### 3. Ajouter les réglages dans GitHub

Dépôt › **Settings › Secrets and variables › Actions** :

| Type | Nom | Valeur |
|---|---|---|
| **Secret** | `SIGNPATH_API_TOKEN` | le jeton d'API SignPath |
| **Variable** | `SIGNPATH_ORGANIZATION_ID` | l'identifiant de l'organisation SignPath (un GUID) |
| Variable (facultatif) | `SIGNPATH_PROJECT_SLUG` | si le projet ne s'appelle pas `ClasseControl` |
| Variable (facultatif) | `SIGNPATH_POLICY_SLUG` | si la politique n'est pas `release-signing` |

### 4. Publier une version signée

Changer `#define AppVersion` dans `installer/ClasseControle.iss` (ex. `1.14.1`) et
envoyer sur `main`. Pendant la construction, approuver les deux demandes de signature
sur signpath.io. Dans le journal de l'étape « Préparer les fichiers », les `.exe` doivent
apparaître en **Valid**.

## Après la signature

- **Smart App Control** et les stratégies d'entreprise ne bloquent plus un programme signé
  par un certificat reconnu.
- **SmartScreen** peut encore avertir pendant les premières semaines : sa « réputation »
  se construit au fil des téléchargements d'un même certificat.
- **Antivirus** : si un antivirus signale encore le programme (faux positif, fréquent pour
  les outils qui bloquent clavier/souris ou ferment des applications), envoyer le fichier
  pour analyse : Microsoft Defender → <https://www.microsoft.com/wdsi/filesubmission>
  (« Software developer »), et le formulaire équivalent de l'éditeur concerné.
- La **mise à jour automatique** n'acceptera plus que des versions signées par le même
  éditeur.

## Limite connue

L'installateur Inno Setup lance une copie temporaire de lui-même (`is-XXXX.tmp\*.tmp`).
Signé après coup par SignPath, l'installateur est bien signé, mais rien ne garantit que
cette copie temporaire le soit. Si un poste très verrouillé la bloque encore, installer
avec **ClasseControle-portable.zip** (programme signé) ou faire autoriser l'installateur
par le service informatique.
