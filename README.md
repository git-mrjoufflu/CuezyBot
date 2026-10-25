# CuezyBot
CuezyBot is a modern, cross-platform visual automation tool for streamers. Build powerful Twitch and OBS workflows without coding.

## Current prototype

- Visual workflow canvas with Twitch, OBS Studio and CORE action library.
- Local SQLite drafts, published revisions and execution history in `data/` beside the application.
- Persistent OBS WebSocket and Twitch Device Code connections with automatic restoration.
- French and English interface, including settings, history and connection dialogs.
- Selecting an action from the library adds it to the canvas and persists it in the next saved or published workflow.

Run the desktop prototype from the repository root:

```bash
dotnet run --project src/Automation.Desktop
```

The Windows build uses the same local data model and can be published self-contained with the .NET SDK.
