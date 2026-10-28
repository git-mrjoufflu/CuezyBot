# CuezyBot
CuezyBot is a modern, cross-platform visual automation tool for streamers. Build powerful Twitch and OBS workflows without coding.

## Current prototype

- Visual workflow canvas with Twitch, OBS Studio and CORE action library.
- Local SQLite drafts, published revisions and execution history in `data/` beside the application.
- Persistent OBS WebSocket and Twitch Device Code connections with automatic restoration.
- French and English interface, including settings, history and connection dialogs.
- Selecting an action from the library adds it to the canvas and persists it in the next saved or published workflow.
- The canvas event picker includes manual run and ten Twitch events: follow, subscription, resubscription, gifted subscriptions, cheer, incoming raid, custom channel points redemption, stream online/offline, and channel update.
- Each Twitch event activates its explicitly connected branch. Extra read permissions require reconnecting Twitch; missing permissions and rejected subscriptions are reported before publishing.

Run the desktop prototype from the repository root:

```bash
dotnet run --project src/Automation.Desktop
```

The Windows build uses the same local data model and can be published self-contained with the .NET SDK.

## Backups and offline restore

Settings provides **Create backup**. Database snapshots are written to `data/backups` with an integrity check. An existing database is also backed up before a schema migration. Snapshots include workflows, settings, variables and history; system credentials and media files are not included.

To restore, close CuezyBot and run the desktop executable with:

```bash
dotnet run --project src/Automation.Desktop --no-build -- --restore-backup "/absolute/path/to/backup.db" --confirm-restore
```

For a published build, pass the same arguments to its executable. The destination is the `data` folder beside that executable. The command verifies a complete schema-v4 backup, refuses an active data directory, saves a safety copy of existing data, and restores the snapshot. Restored automations are disabled and pending/running requests expire. Start CuezyBot normally, review the workflows and connections, then enable the desired automations. Restoring from the graphical interface is not yet implemented.
