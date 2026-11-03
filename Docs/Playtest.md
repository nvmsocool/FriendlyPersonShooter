# Playtesting with friends

The playtest build starts at a main menu:

- **Host game**: starts a game on your computer and shows a join code (top right, also copied to the clipboard).
- **Join game**: type a friend's join code.
- **Play solo**: you and your bot allies, no internet needed.

In the game, **Esc** frees the mouse and shows **Copy join code** and **Leave game** / **End game**.
When the host ends the game, everyone else goes back to the menu.

Players connect through **Unity Relay**, so nobody needs to know an IP address or open a port.

## One-time setup: link the project to Unity Cloud

Hosting and joining need the project linked to a Unity Cloud project with Relay turned on.
Until then, Host and Join show "Couldn't sign in to Unity's online services".

1. In the Unity Editor: **Edit > Project Settings > Services**. Sign in, then link the project to a new or existing
   Unity Cloud project.
2. On [cloud.unity.com](https://cloud.unity.com), open that project, find **Relay** under Multiplayer, and turn it on.
   The free tier covers small playtests.
3. Make a new build (below). The link is baked into the build.

## Making a build

**FriendlyPersonShooter > Build Playtest (Windows)** in the Editor, or:

```
unity command script eval --code 'return FriendlyPersonShooter.EditorTools.PlaytestBuild.BuildWindows();' --timeout 1500
```

The game lands in `Builds/Playtest`. Zip that folder, leaving out `FriendlyPersonShooter_BackUpThisFolder_ButDontShipItWithYourGame`,
or upload it to Steam. On a first run, Windows may ask to let the game through the firewall: allow it.

## Testing host and join on one computer

Run two copies with different profiles, so they sign in as different players:

```
FriendlyPersonShooter.exe -fps-profile host
FriendlyPersonShooter.exe -fps-profile guest
```

## In the Editor

Play the `Menu` scene to use the menu. Playing `Expedition` or `Arena` directly still starts those games right away,
as before. The net test's command-line flags also still work in the playtest build: `-fps-game expedition` skips the menu.

## How it works

| Piece | Where |
|---|---|
| Menu | `Arena/Session/MainMenu.cs` (Menu scene) |
| Hosting, joining, leaving | `Arena/Session/PlaySession.cs` |
| In-game join code and Leave | `Arena/Session/SessionHud.cs` |
| Relay network drivers | `Net/RelayDriverConstructor.cs` |
| Menu start (no worlds until you host or join) | `Net/NetTestBootstrap.cs` (`StartsAtMenu`, `CreateArenaWorlds`) |
| Build | `Assets/Editor/PlaytestBuild.cs` (defines `FPS_PLAYTEST`) |

The host runs the server, its own client and its bots in one process. Its client and bots connect in-process; friends
connect through the relay. Everyone uses the same expedition world seed.
