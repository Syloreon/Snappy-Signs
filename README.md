# Snappy Signs

A small [BepInEx](https://docs.bepinex.dev/) plugin for **Valheim** that gives signs
**build snap points**, so they align cleanly to walls, beams, posts, and other build
pieces during placement — the same way most build pieces already snap.

By default vanilla signs have no snap points, which makes them fiddly to position.
Snappy Signs adds snap points at the **corners**, **edge midpoints**, and **center** of
each sign's board.

## How it works

The plugin Harmony-patches `ZNetScene.Awake`. After the game registers all prefabs, it
finds every prefab that has both a `Sign` and a `Piece` component (so it works for any
sign, including modded/future ones — no hardcoded prefab names) and injects empty child
GameObjects tagged `snappoint`. Valheim's placement system (`Piece.GetSnapPoints`) reads
those direct children automatically.

Snap point positions are derived from each sign's mesh bounds, so they fit each sign type
automatically. Signs that already have snap points (vanilla or added by another mod) are
left untouched, and the patch is idempotent.

## Configuration

After running once, `BepInEx/config/com.syloreon.snappysigns.cfg`:

| Section      | Key            | Default | Description                                      |
|--------------|----------------|---------|--------------------------------------------------|
| `General`    | `Enabled`      | `true`  | Master toggle.                                   |
| `SnapPoints` | `Corners`      | `true`  | Snap points at the four corners.                 |
| `SnapPoints` | `EdgeMidpoints`| `true`  | Snap points at the midpoint of each edge.        |
| `SnapPoints` | `Center`       | `true`  | Snap point at the board center.                  |

Changes apply on the next world/game load (prefabs are patched at scene setup).

## Building

Requires the .NET SDK and a local Valheim install (for the game assemblies).

```sh
# Uses VALHEIM_INSTALL or the default path baked into the .csproj
dotnet build -c Release

# Override the game path and auto-deploy the DLL to your plugins folder:
dotnet build -c Release \
  -p:ValheimInstall="F:\SteamLibrary\steamapps\common\Valheim" \
  -p:DeployDir="F:\SteamLibrary\steamapps\common\Valheim\BepInEx\plugins\SnappySigns"
```

The compiled `SnappySigns.dll` is written to `bin/Release/`.

## Installing

1. Install [BepInEx for Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/).
2. Copy `SnappySigns.dll` into `Valheim/BepInEx/plugins/`.
3. Launch the game.

## Notes / limitations

- Snap points are placed on the board's mid-plane. For signs that include a mounting post
  in the same mesh, the computed board face may include the post; this is fine for the
  common wood sign but may be refined later.
