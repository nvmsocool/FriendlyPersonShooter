# Generated zones in the game

The expedition plays on zones built by the winning zone generator, **prismatica** (`Assets/Scripts/ZoneArt/Generators/C`).
Start a build with `-fps-flat` to play on the old flat floor instead.

## How it fits together

| Piece | Where | What it does |
|---|---|---|
| `ZoneWorld` | `ZoneArt/World/ZoneWorld.cs` | Streams zones in around players and the camera, a few ms of building per frame (`IIncrementalZoneGenerator`), and unloads zones nobody has been near for a minute. One per process: the server and client worlds of a session share it. |
| Collision | `ZoneWorld` | When a zone finishes, its colliders move to a fixed `Collision` hierarchy at the zone's true position in the wrapping world. Movement, shots and spawns query only that. |
| Visuals | `ZoneWorld` | Each zone's meshes sit at the copy of the zone nearest the camera, so the 4x4 world wraps seamlessly on screen. |
| Look | `ZoneWorld` | Each zone's sky, sun, ambient light, fog and post-processing are captured when it is built. The zone the camera is in sets the look, blending over 2.5 s when you cross into another. |
| `ExpeditionTerrain` | `Arena/ExpeditionTerrain.cs` | Player movement over the zones (capsule collisions, 0.45 m steps, 50° slopes, sliding off steeper ones, falling), spawn heights, and shots stopped by terrain. Server and predicting clients run the same code against the same colliders. |
| Bot paths | `ZoneArt/World/ZoneNav.cs`, `Arena/BotPath.cs` | A 1 m grid of standing spots per built zone (every level: ground, bridges, tower tops), weighted A* across zones and the wrap (climb at most 1 m with a jump, drop at most 6 m), and waypoint following with stuck detection. |
| Enemies | `Arena/ArenaServerSystem.cs` | Spawn on the ground; Skitters follow slopes and turn back at walls and drops; Wisps stay out of hills. |
| Seams | `ZoneEdges.BorderHeight` | Repeats at the world size, so zones also meet across the world's edge. |

The layer seed decides everything, so the server and every client build identical zones without sending any geometry.
A dedicated server (no graphics) builds with `ZoneRequest.Headless`: only colliders matter there.

## Shaders in builds

Zone materials are created from shader names at runtime. Player builds only keep shaders something references, so every
zone shader has a `ShaderRef` material in `Assets/Scripts/ZoneArt/Resources`. Add one for any new zone shader.

## Known limits

- Shots and capsule checks don't see geometry across the world's wrap edge (the border strip is gentle open ground, so this rarely shows).
- Bots path only over built zones; far goals get a partial path toward them, replanned as zones stream in.
- The look blends sky materials only when both zones use the same sky shader (prismatica's always do).
