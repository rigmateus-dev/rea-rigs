# REA de Rig's

Une appli Windows pour analyser des applications avec [REA](https://github.com/morluto/rea)
(outil libre de rétro-ingénierie), **enfermé dans une boîte** :

- **pas de réseau** (`--network none`) ;
- **ne voit que ce que tu glisses**, en lecture seule (un fichier seul est copié à part : ses voisins restent invisibles) ;
- système de la boîte en lecture seule, aucun privilège, mémoire et processus limités ;
- version de REA **figée par empreinte** (sha256) : rien ne change sans ton accord.

Les résultats vont dans `Documents\Analyse`. L'appli peut aussi **brancher REA à l'appli Claude**
(Claude ne voit alors que `Documents\Analyse`, en lecture seule, sans réseau).

## Mises à jour

Chaque lundi, le robot (`.github/workflows/mise-a-jour.yml`) :

1. regarde s'il existe une nouvelle version de `rea-agents` ;
2. vérifie qu'elle vient bien de `morluto/rea` et que sa **signature npm** est valide (`npm audit signatures`) ;
3. écrit un **rapport** : nouvelles dépendances, nouveaux accès réseau, lancements de programmes, code dynamique ;
4. construit la boîte, la teste sans réseau, la publie sur `ghcr.io/rigmateus-dev/rea-rigs` ;
5. ouvre une **demande de mise à jour** avec le rapport.

Tu acceptes (Merge) → dans l'appli, « Vérifier les mises à jour » installe la nouvelle boîte.
Tu refuses → rien ne bouge.

## Installer (PC Windows)

1. [Docker Desktop](https://www.docker.com/products/docker-desktop/) installé et démarré.
2. `app\install.cmd` (compile avec le compilateur C# fourni par Windows, installe dans
   `%LOCALAPPDATA%\Programs\REA de Rigs`, raccourcis, entrée « Applications installées »).
3. Dans l'appli : **Installer REA**.

Désinstaller : Paramètres › Applications installées › REA de Rig's. Tes analyses restent.

## Limites

- Pas d'observation d'un programme **en train de tourner** (la boîte est Linux, sans réseau).
- Pas de Ghidra / IDA dans la boîte pour l'instant : .NET, JavaScript/Electron, inventaires d'applis et de conteneurs.
- REA lit des fichiers que tu choisis : n'analyse que ce que tu as le droit d'analyser.
