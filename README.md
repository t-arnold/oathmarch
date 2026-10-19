# Oathmarch — Slice 1

Third-person 3D. You are the king in the world, over his shoulder. You give standing orders to your brigade commander; he runs the squads and reports back.

## Open it
1. Install **Unity 6 LTS (6000.0.x)** with Unity Hub, plus the **Mac Build Support** module (Apple silicon is fine). The project says 6000.0.23f1. Any 6000.0.x works; let the Hub upgrade it if asked.
2. Unity Hub > **Add > Add project from disk** > pick this `Oathmarch` folder. The first import takes a minute or two.
3. Press **Play** in any scene, even the empty default one. A runtime bootstrap (`GameRoot.AutoBootstrap`) builds the whole slice.
4. Optional: **Oathmarch > Build Slice Scene** saves `Assets/Scenes/Slice.unity` and adds it to Build Settings.

Render pipeline: Built-in (no URP setup). Input: the legacy Input Manager (no package needed). If Unity ever asks which input backend to use, pick **Both** or **Input Manager (Old)**.

## Controls
| Key | Action |
|---|---|
| WASD | walk (moves relative to the camera; the king turns to face where he walks) |
| Shift | sprint |
| Mouse | orbit the camera (right shoulder; it pulls in when something blocks it) |
| Wheel | zoom |
| Left click | sword strike |
| Tab / O | order panel. Press 1-9 or click to choose. |
| Esc | free the cursor. Click to take control again. |
| R | restart after the king falls |

## What's in the slice
- **Country from a seed** (`GameRoot.seed`): your town (town hall, well, hovels, cottages, granary, smithy, tavern), an enemy town, an enemy hamlet, a quiet hamlet and your own fort, plus trees and rocks. Each place is made of buildings, not a single marker.
- **King**: a rigged humanoid with an Animator that uses one shared idle/walk/run/attack/death set.
- **Commander** Sir Aldric, trait **Cautious** by default (change it on the `Oathmarch` GameObject > GameRoot > Commander Trait). Two squads of 4.
  - **Defend** (default standing order): First squad walks the town and Second squad patrols the country. When raiders come within range, the nearest squad heads them off (or both squads, if the raiders outnumber one). Then they report and go back to the watch.
  - **Strike <settlement>**: you can only name a settlement that has been reported. A hamlet means a raid. A town means an assault. **Aggressive** sends everyone straight in. **Cautious** sends one squad around the flank and holds the other in reserve, committing it once the flank is in. Afterwards the order goes back to Defend.
  - If you say nothing, the standing order continues.
- **Enemy**: a commander built from the same `Commander` class, with the standing order *Strike <your town>*. Raider waves leave the enemy town every 50 seconds. Enemy places have garrisons.
- **Fog**: your own places are always visible. Everything else is black until you or your men walk near it, and stays dim once seen. Enemy bodies show only while someone of yours can see them. When a place is discovered, a report comes in (place, kind, direction).
- **Combat**: units have HP, swing on contact, and play a death clip when they fall.

## Architecture: brain vs bodies
```
Assets/Scripts/Brain   pure C# with no UnityEngine: World/Place/Building (seeded), FogGrid + Knowledge,
                       StandingOrder, Commander(Trait) -> Squad intents + destinations, ReportLog
Assets/Scripts/Bodies  MonoBehaviours: Unit (walks to the squad's destination, engages on contact, swings, dies),
                       KingController (the only body the player drives), CharacterFactory (one shared model + clip set),
                       CharacterAnimator (Playables blend, no controller asset), Outfit, ShoulderCamera, FogView, CommandUI,
                       GameRoot (assembles the world; each frame: bodies -> UnitInfo snapshot -> Commander.Tick)
Assets/Scripts/Editor  MannequinImporter (forces Humanoid + in-place loops for FBX under */Characters/*), Oathmarch menu
```
The brain never touches a GameObject. Bodies never decide strategy. They only read `Squad.Destination`, `Run` and `AggroRadius`.

## Swapping in your own characters (Unity packages)
1. **Assets > Import Package > Custom Package…** and import your pack.
2. Select the character model > Inspector > **Rig > Animation Type: Humanoid > Apply**. Check that Configure… shows a green mapping. If you put the FBX in any folder named `Characters`, the importer does this for you.
3. Select the `Oathmarch` GameObject (from Build Slice Scene) and drag the model into **GameRoot > Character Model**. Without a scene, replace `Assets/Resources/Characters/Mannequin.fbx` instead.
4. The shared animation set is taken from the mannequin's Idle/Walk/Run/Attack/Death clips and retargets to any humanoid. To use your pack's animations, assign them in `CharacterFactory` (clips are found by name).
5. Outfits: `Outfit.Apply(OutfitKind)` tints the body and attaches parts to humanoid bones. Swap in real outfit meshes skinned to the same rig there. Everyone (king, soldiers, enemy) uses the same rig.

The mannequin is regenerated with `blender -b -P Assets/Tools/make_mannequin.py -- Assets/Resources/Characters/Mannequin.fbx`.

## Next
NavMesh (com.unity.ai.navigation) in place of simple steering, Survey/Scout/Both/Parley orders, fort siege (strike twice), Scout-minded trait behaviour, fog for the enemy commander, real art from your packs, places that grow over time, and a proper Animator Controller with an attack layer.
