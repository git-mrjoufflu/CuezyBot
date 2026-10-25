# Automatisation visuelle pour le streaming

Prototype indépendant, Windows et Linux, pour construire des automatisations Twitch et OBS sous forme de parcours visuels.

## État actuel

Le socle contient le modèle versionné des workflows, le catalogue des premiers blocs, la validation avant publication et le stockage SQLite du premier flux synthétique. Aucun connecteur réel ni interface graphique n’est encore livré.

## Compiler et exécuter

```bash
dotnet build StreamerAutomation.slnx
dotnet run --project src/Automation.Runtime
```

Le Runtime crée `data/automation.db` à côté de son exécutable. Pour un essai isolé :

```bash
dotnet run --project src/Automation.Runtime -- --application-directory /tmp/streamer-automation-demo
```

Lancer la première maquette d’éditeur :

```bash
dotnet run --project src/Automation.Desktop
```

Le Runtime reste en simulation OBS par défaut. Pour un essai réel, définir localement l’adresse et le mot de passe avant de le lancer :

```bash
export AUTOMATION_OBS_URL="ws://127.0.0.1:4455"
export AUTOMATION_OBS_PASSWORD="votre-mot-de-passe-local"
dotnet run --project src/Automation.Runtime -- --application-directory /tmp/streamer-automation-obs
```

Le mot de passe n’est pas enregistré dans SQLite. Cette variable d’environnement sert uniquement au prototype ; l’interface utilisera ensuite le coffre de secrets du système.

Voir [PLAN_REFONTE.md](PLAN_REFONTE.md) pour les étapes et [ADR 0001](docs/decisions/0001-socle-du-prototype.md) pour la première décision technique.
