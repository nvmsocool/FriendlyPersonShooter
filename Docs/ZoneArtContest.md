# Zone art contest

Three contestants each write a procedural zone generator on their own branch. The owner judges
9 random zones from each and merges the winner.

## What a zone is

The expedition world is a wrapping grid of square zones, each 250 m on a side (see
`Assets/Scripts/Core/Expedition`). The layer generator already decides each zone's theme hint
(`TileKind`), its essence color lean, and gameplay anchors: named landmarks, shrines, the gate,
and enemy spawn points. A zone generator turns that into the world players see and walk on:
ground, rock, structures, vegetation, sky, sun and post-processing.

## Judging criteria (from the owner)

1. **Vibrant**: strong, confident color. Not muddy, not grey.
2. **Lots of verticality**: cliffs, mesas, arches, terraces, bridges, towers, layered ledges,
   overhangs. The player should look up and down a lot.
3. **Slightly abstract**: stylized, not photoreal. Shapes and palettes can be invented.
4. **Traversable**: a player can walk and jump through it (see the walkability numbers).
5. **Limited repetition**: 9 zones shouldn't feel like 1 zone 9 times. Vary landforms, palettes,
   plant species, and composition by seed and tile kind. Within a zone, avoid obvious clones.
6. **Lush vegetation with a lot of variation**: many plant species (grasses, shrubs, trees,
   flowers, vines, fungi, invented flora...), varied size, shape and color, clustered like real ecosystems.
7. **A fun skybox**: planets, rings, auroras, swirls, gradients, stars, giant clouds. Something you'd screenshot.

Hard rules:
- Deterministic: the same `ZoneRequest` gives the same zone. Randomness only from `ZoneRequest.Seed`
  (use `ZoneRandom`; never `UnityEngine.Random` or `System.Random()` without a seed).
- Seams: within `ZoneEdges.BorderWidth` of the zone edge, walkable ground eases to
  `ZoneEdges.BorderHeight` (use `ZoneEdges.Apply`), so any two zones connect.
- Anchors: every anchor must be reachable on foot. Shrines and the gate need flat, open
  ground within `ZoneRequest.ShrineClearRadius` / `GateClearRadius`. Landmarks deserve something memorable.
- Everything players can stand on or bump into needs a collider (MeshCollider is fine).
- Budget per zone: build under 15 s, under 3 million triangles.
- Targets from the walkability report: two-way walkable at least 70%, every edge at least 60%,
  all anchors reachable.

## The framework (`Assets/Scripts/ZoneArt`)

| File | What it gives you |
|---|---|
| `IZoneGenerator.cs` | The interface, `ZoneRequest` (seed, coord, origin, size, tile, lean, anchors), `ZoneResult` (root, hero views, notes) |
| `ZoneRandom.cs` | Deterministic random streams, hashing, weighted picks, HSV jitter |
| `ZoneNoise.cs` | Simplex, fbm, ridged, domain-warped, cellular noise, terraces |
| `Heightfield.cs` | Height grid from a function, chunked terrain meshes with colliders, smooth or faceted |
| `MeshBuilder.cs` | Vertex-color mesh building: tubes, blobs, blades, icospheres, appending meshes (32-bit indices) |
| `Scatter.cs` | Poisson-disk scattering, raycast ground snapping, anchor clearance |
| `ZoneEdges.cs` | The seam contract |
| `ZoneLook.cs` | Materials, sun, trilight ambient, fog, post-processing volume |
| `Shaders/VertexColorLit.shader` | Starter lit shader: vertex color, soft shadows, wrap diffuse, rim, fog, glow from vertex alpha < 1 |
| `Shaders/GradientSky.shader` | Starter skybox: gradient, sun disk, bands |
| `Walkability.cs` | Player-movement reachability analysis (1 m grid, multi-level, 1.0 m climb, 20 m drop, 50° slope) |
| `SampleZoneGenerator.cs` | The plain baseline ("sample") |
| `Editor/ZoneGallery.cs` | Renders the gallery: aerial, hero and ground shots, walk maps, contact sheets, `report.md` |

## Rendering a gallery

The project must not be open in an Editor. From the worktree root:

```
bash Tools/ZoneArt/gallery.sh <generator> <outDir> [-zoneOnly N] [-zoneSeed S] [-zoneCount 9] [-zoneWidth 960 -zoneHeight 540]
```

Each zone gets `zoneN_aerial.png` (fixed pose, the same for everyone), `zoneN_hero.png` (your
first `HeroView`), `zoneN_ground.png` (eye height at a seeded random walkable spot looking at the
zone centre: you don't choose it, so the whole zone has to look good), and `zoneN_walk.png`
(green: two-way walkable, orange: standable but stranded, dark: nothing to stand on).
A run takes a few minutes, mostly Unity startup and script compilation.
`report.md` has the numbers.

Final judging uses a different gallery seed than the default, so don't tune for seed 1's nine zones.
