; ============================================================================
;  Classe_controle — programme d'installation (Inno Setup 6)
;  Une seule application (ClasseControle.exe) qui contient la console du
;  professeur ET l'agent élève. Au début de l'installation, on choisit le mode
;  du poste ; il est enregistré dans classecontrole.ini à côté de l'exe.
;
;   • Poste du PROFESSEUR : mode console, raccourcis menu Démarrer + Bureau.
;   • Poste ÉLÈVE         : mode agent en arrière-plan.
;                           Aucun raccourci, rien dans le menu Démarrer :
;                           l'agent démarre seul à chaque ouverture de session
;                           (tâche planifiée cachée, relancée en cas d'arrêt)
;                           et n'apparaît que dans la zone de notification.
;
;  Installation silencieuse (déploiement sur toute la salle) :
;    ClasseControle-Setup.exe /VERYSILENT /ROLE=eleve /ROOM=CLASSE
;    ClasseControle-Setup.exe /VERYSILENT /ROLE=prof
;
;  Poste élève : l'option « supprimer les jeux préinstallés de Windows pour tous les
;  utilisateurs » est cochée par défaut (aussi en silencieux). Pour ne pas la faire :
;    ClasseControle-Setup.exe /VERYSILENT /ROLE=eleve /ROOM=CLASSE /MERGETASKS="!supprjeux"
;
;  Filtrage web renforcé (option « filtreweb », cochée par défaut) : stratégies des
;  navigateurs qui imposent le proxy de filtrage. Pour ne pas l'installer :
;    ClasseControle-Setup.exe /VERYSILENT /ROLE=eleve /ROOM=CLASSE /MERGETASKS="!filtreweb"
;
;  Compilation : voir build.ps1 (publie l'application puis appelle ISCC).
; ============================================================================

#define AppName "Classe_controle"
#define AppVersion "1.14.0"
#define Publisher "Classe_controle"

[Setup]
AppId={{6C4B2F0E-3E7A-4C38-9B8E-5A1C2D3E4F10}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#Publisher}
DefaultDirName={autopf}\Classe_controle
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\publish\installer
OutputBaseFilename=ClasseControle-Setup-{#AppVersion}
SetupIconFile=..\ClasseControle.App\app.ico
UninstallDisplayIcon={app}\ClasseControle.exe
UninstallDisplayName={code:UninstallName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=force
; Signature de code (voir sign.ps1) : build.ps1 passe /DSIGN et l'outil « ccsign » quand un
; certificat est configuré. Signe l'installateur ET le programme de désinstallation.
#ifdef SIGN
SignTool=ccsign
SignedUninstaller=yes
#endif

[Languages]
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
; Uniquement pour un poste élève (le rôle est choisi sur la page précédente).
Name: "supprjeux"; Description: "Supprimer les jeux préinstallés de Windows pour TOUS les utilisateurs (Solitaire, Candy Crush, Minecraft, Mahjong, Démineur…)"; GroupDescription: "Poste élève :"; Check: IsStudent
Name: "filtreweb"; Description: "Filtrage web renforcé : obliger Chrome, Edge, Brave et Firefox à passer par le filtrage du professeur (recommandé)"; GroupDescription: "Poste élève :"; Check: IsStudent
Name: "majauto"; Description: "Mise à jour automatique depuis GitHub (recommandé) : vérification toutes les heures, installation sans intervention"; GroupDescription: "Mises à jour :"

[InstallDelete]
; Anciennes versions : scripts PowerShell remplacés par les commandes de ClasseControle.exe,
; bibliothèque ANGLE plus utilisée, fichiers laissés par une mise à jour.
Type: files; Name: "{app}\agent-setup.ps1"
Type: files; Name: "{app}\remove-games.ps1"
Type: files; Name: "{app}\update.ps1"
Type: files; Name: "{app}\update-setup.ps1"
Type: files; Name: "{app}\av_libglesv2.dll"
Type: files; Name: "{app}\*.old-*"

[Files]
; Application unique (les deux modes) : le programme et ses bibliothèques graphiques
; (signées par Microsoft), installés dans Program Files. Rien n'est extrait dans le
; dossier temporaire au démarrage.
Source: "..\publish\app\*.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\app\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\GUIDE.txt"; DestDir: "{app}"; Flags: ignoreversion; Check: IsTeacher

[Icons]
; Uniquement pour le professeur : le poste élève n'a AUCUN raccourci.
Name: "{autoprograms}\Classe_controle — Console professeur"; Filename: "{app}\ClasseControle.exe"; Parameters: "--prof"; Check: IsTeacher
Name: "{autodesktop}\Classe_controle"; Filename: "{app}\ClasseControle.exe"; Parameters: "--prof"; Check: IsTeacher
Name: "{autoprograms}\Classe_controle — Guide"; Filename: "{app}\GUIDE.txt"; Check: IsTeacher

[Run]
; Toute la configuration passe par ClasseControle.exe lui-même (aucun script à autoriser).
Filename: "{app}\ClasseControle.exe"; Parameters: "--setup prof"; Flags: runhidden waituntilterminated; StatusMsg: "Configuration du poste du professeur…"; Check: IsTeacher
Filename: "{app}\ClasseControle.exe"; Parameters: "--setup eleve"; Flags: runhidden waituntilterminated; StatusMsg: "Configuration du poste élève…"; Check: IsStudent
Filename: "{app}\ClasseControle.exe"; Parameters: "--browser-policies on"; Flags: runhidden waituntilterminated; StatusMsg: "Filtrage web renforcé…"; Check: IsStudent; Tasks: filtreweb
Filename: "{app}\ClasseControle.exe"; Parameters: "--browser-policies off"; Flags: runhidden waituntilterminated; Check: IsStudent; Tasks: not filtreweb
Filename: "{app}\ClasseControle.exe"; Parameters: "--remove-games"; Flags: runhidden waituntilterminated; StatusMsg: "Suppression des jeux préinstallés de Windows (peut prendre une minute)…"; Tasks: supprjeux
Filename: "{app}\ClasseControle.exe"; Parameters: "--setup-update"; Flags: runhidden waituntilterminated; StatusMsg: "Configuration de la mise à jour automatique…"; Tasks: majauto
; Démarre tout de suite dans la session de l'utilisateur.
Filename: "{app}\ClasseControle.exe"; Parameters: "--eleve"; Flags: nowait runasoriginaluser skipifsilent; Check: IsStudent
Filename: "{app}\ClasseControle.exe"; Parameters: "--prof"; Description: "Lancer la console professeur"; Flags: nowait postinstall skipifsilent runasoriginaluser; Check: IsTeacher

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM ClasseControle.exe"; Flags: runhidden; RunOnceId: "KillApp"
Filename: "{app}\ClasseControle.exe"; Parameters: "--cleanup"; Flags: runhidden waituntilterminated; RunOnceId: "Cleanup"

[UninstallDelete]
Type: files; Name: "{app}\*.old-*"
Type: filesandordirs; Name: "{app}\.maj"
Type: files; Name: "{app}\ClasseControle.update.tmp"
Type: files; Name: "{app}\agent.ini"
Type: files; Name: "{app}\classecontrole.ini"
Type: files; Name: "{app}\role.txt"

[Code]
var
  RolePage: TInputOptionWizardPage;
  AgentPage: TInputQueryWizardPage;
  Role: String;   { 'prof' ou 'eleve' }

function CmdParam(const Name, Default: String): String;
begin
  Result := ExpandConstant('{param:' + Name + '|' + Default + '}');
end;

{ À la désinstallation, le rôle est relu dans role.txt. }
function StoredRole(): String;
var S: AnsiString;
begin
  Result := '';
  if LoadStringFromFile(ExpandConstant('{app}\role.txt'), S) then Result := Trim(String(S));
end;

function IsStudent(): Boolean;
begin
  if Role = '' then Role := StoredRole();
  Result := Role = 'eleve';
end;

function IsTeacher(): Boolean;
begin
  Result := not IsStudent();
end;

function UninstallName(Param: String): String;
begin
  if IsStudent() then Result := 'Classe_controle (agent élève)'
  else Result := 'Classe_controle (console professeur)';
end;

procedure InitializeWizard();
var R: String;
begin
  RolePage := CreateInputOptionPage(wpWelcome,
    'Type de poste', 'À qui est destiné cet ordinateur ?',
    'Choisissez le rôle de ce poste (ordinateur fixe ou portable), puis cliquez sur Suivant.',
    True, False);
  RolePage.Add('Poste du PROFESSEUR — console qui contrôle la salle (raccourcis menu Démarrer et Bureau)');
  RolePage.Add('Poste ÉLÈVE — agent discret en arrière-plan (aucun raccourci, visible seulement près de l''horloge)');

  R := Lowercase(CmdParam('ROLE', ''));
  if (R = 'eleve') or (R = 'élève') or (R = 'student') then RolePage.SelectedValueIndex := 1
  else RolePage.SelectedValueIndex := 0;

  AgentPage := CreateInputQueryPage(RolePage.ID,
    'Configuration du poste élève', 'Code de salle et nom du poste',
    'Le code de salle doit être IDENTIQUE sur la console du professeur et sur tous les postes élèves.');
  AgentPage.Add('Code de salle :', False);
  AgentPage.Add('Nom affiché dans la console (laisser vide = nom de l''ordinateur) :', False);
  AgentPage.Values[0] := CmdParam('ROOM', 'CLASSE');
  AgentPage.Values[1] := CmdParam('NAME', '');
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = AgentPage.ID) and (RolePage.SelectedValueIndex <> 1);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = RolePage.ID then
  begin
    if RolePage.SelectedValueIndex = 1 then Role := 'eleve' else Role := 'prof';
  end;
  if (CurPageID = AgentPage.ID) and (Trim(AgentPage.Values[0]) = '') then
  begin
    MsgBox('Le code de salle est obligatoire.', mbError, MB_OK);
    Result := False;
  end;
end;

function InitializeSetup(): Boolean;
var R: String;
begin
  { En mode silencieux les pages ne s'affichent pas : le rôle vient de /ROLE. }
  R := Lowercase(CmdParam('ROLE', 'prof'));
  if (R = 'eleve') or (R = 'élève') or (R = 'student') then Role := 'eleve' else Role := 'prof';
  Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var Ini, Name: String;
begin
  if CurStep = ssPostInstall then
  begin
    SaveStringToFile(ExpandConstant('{app}\role.txt'), Role, False);
    { Mode lu par ClasseControle.exe au démarrage (dans Program Files : non modifiable par les élèves). }
    SaveStringToFile(ExpandConstant('{app}\classecontrole.ini'),
      '# Mode de Classe_controle sur ce poste (prof ou eleve)' + #13#10 + 'Mode=' + Role + #13#10, False);
    if IsStudent() then
    begin
      Name := Trim(AgentPage.Values[1]);
      if Name = '' then Name := GetComputerNameString();
      Ini := '# Configuration de l''agent Classe_controle' + #13#10 +
             'RoomCode=' + Trim(AgentPage.Values[0]) + #13#10 +
             'DisplayName=' + Name + #13#10;
      SaveStringToFile(ExpandConstant('{app}\agent.ini'), Ini, False);
    end;
  end;
end;
