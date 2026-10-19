# Oathmarch — Slice 2

Third-person 3D. You are the king in the world, over his shoulder. You give standing orders to your brigade commander; he runs the squads and reports back.

## Open it
1. Install **Unity 6.3 LTS (6000.3.26f1)** with Unity Hub, plus the **Mac Build Support** module (Apple silicon is fine). The project says 6000.3.26f1. Any later 6000.3.x works; let the Hub upgrade it if asked.
2. Unity Hub > **Add > Add project from disk** > pick this `Oathmarch` folder. The first import takes a minute or two.
3. On first open, the editor builds `Assets/Resources/Characters/Locomotion.controller` and reimports the mannequin as Humanoid. Commit those files.
4. Press **Play** in any scene, even the empty default one. A runtime bootstrap builds the whole slice. Check the Console: it prints an `[Oathmarch] Self-check` block (green when everything is fine, red with the failing item otherwise).
5. Optional: **Oathmarch > Build Slice Scene** saves `Assets/Scenes/Slice.unity` and adds it to Build Settings.

Render pipeline: Built-in (no URP setup). Input: the legacy Input Manager (no package needed). If Unity ever asks which input backend to use, pick **Both** or **Input Manager (Old)**.

## Controls
| Key | Action |
|---|---|
| WASD | walk (moves relative to the camera; the king turns to face where he walks) |
| Shift | sprint |
| Mouse | orbit the camera (right shoulder; it pulls in when something blocks it) |
| Wheel | zoom |
| Left click | sword strike |
| E | speak with the commander when standing next to him at the command post: opens the order panel |
| Tab / O | the same order panel from anywhere (a messenger) |
| 1-9 | choose an order in the panel |
| F1 | debug overlay: fog minimap with units, every squad's state, body count vs brain count, engagement ledger |
| Esc | free the cursor. Click to take control again. |
| R | restart after the king falls |

## What's in the slice
- **Country from a seed** (`GameRoot.seed`): your town (town hall, well, hovels, cottages, granary, smithy, tavern), an enemy town, an enemy hamlet, a quiet hamlet and your own fort, plus trees and rocks. Each place is made of buildings, not a single marker.
- **King**: the shared rigged humanoid. Walk/run speeds match the clips' stride (1.57 and 3.56 m/s), so the feet don't slide.
- **Commander** Sir Aldric is a body in your town, with a green cloak and a banner, standing at the command post (standard and map table). Reports come from him. Trait **Cautious** by default; change it on GameRoot > Commander Trait.
- **Squads** (First, Second, Riders). Each squad runs a state machine in the brain: Patrol → Intercept → Engage → Pursue → Regroup → Return, plus Fall back.
  - Pursuit gives up past the leash (95 m from home).
  - A squad below 40% strength falls back to town, and the commander says so.
  - Cautious keeps a reserve and sends it to reinforce a squad that is outnumbered. Aggressive commits both squads.
  - Soldiers hold a loose wedge around the squad anchor.
  - The town sends a recruit about every 18 s to the weakest squad.
- **Orders**: Defend (default), **Survey** and Strike <reported settlement>.
  - **Survey**: the Riders ride out through unexplored country, revealing the fog. They report each place by name and kind ("Riders found a hamlet, Lowmarsh, to the east. Held against us.") and report quiet roads. Meanwhile the watch keeps going. Each newly reported place becomes a Strike target.
  - **Strike** works as before: a hamlet is a raid, a town is an assault, and the trait decides flank/reserve or straight in.
- **Enemy raids you can watch**: the warlord (same `Commander` class, standing Strike on your town) sends a party of 4 about every 55 s. It takes a road through the countryside to one of your buildings. If nobody intercepts it, the raiders torch the building (flames, light, a charred shell) and run home. Raiders flee when they drop below half strength.
- **Truthful reports**: every fight report comes from events the bodies actually produced (hits and kills) plus the bodies that are still alive. Example: "Second squad caught four raiders at the granary in Brenmoor. Three dead, one fled south-east. We lost one man." If no blow was struck, the report says the raiders slipped away instead.
- **Fog**: an overlay drawn with a shader shipped in `Assets/Resources/Shaders` (no `Shader.Find`). Black means never seen, dim means seen before, clear means in view. Unseen buildings are hidden and seen-but-out-of-view ones are dimmed. Enemy bodies are drawn only while one of your men can see them.

## What changed in slice 2 (fixes)
1. **Raiders and reports**: the brain now writes fight reports only from body events. Raider bodies are logged when they spawn, and you can watch them on F1.
2. **Fog**: the overlay uses a shader from `Resources`. If it can't load, the Console shows a loud error and the self-check flags it.
3. **Animation**: the importer forces Humanoid and loops Idle/Walk/Run, with everything kept in place. The generated Animator Controller blends Idle/Walk/Run on Speed. The fallback mixer's weights always add up to 1. Movement speed matches the stride. The Console reports an invalid avatar.
4. **Outfit parts**: they are placed while the model is still in its bind pose and then parented to the real Head/RightHand/LeftLowerArm/Chest bones (with name lookup as a fallback).
5. **Camera**: it SphereCasts from inside the king's head against the environment layer, and characters sit on Ignore Raycast. Roofs are solid volumes with colliders. The near clip is 0.05.
6. **HUD**: squad counts come from the bodies that exist right now. Squads are filled before the first frame, and the log only accepts commander text.

## Headless brain test
`cd Tests/BrainSim && dotnet run -c Release` runs the real brain against fake bodies (no Unity). It covers a raid, a run where the bodies never fight, Survey and a raid on a hamlet, and fall-back with recruits. It asserts that a fight report never appears before real blows, that dead counts never exceed recorded deaths, that there is no fake victory without combat, that the leash holds, that new places become strikeable, and that fall-back reports the right count.

## Architecture: brain vs bodies
```
Assets/Scripts/Brain   pure C# with no UnityEngine: World/Place/Building (seeded), FogGrid + Knowledge,
                       StandingOrder, Commander(Trait) -> per-squad state machine + Engagement ledger, ReportLog.
                       In: UnitInfo snapshot + BrainEvents (Hit/Kill/Spawned/Torched). Out: Squad anchors + BodyRequests (Spawn/Despawn).
Assets/Scripts/Bodies  MonoBehaviours: Unit (walks to the squad's destination, engages on contact, swings, dies),
                       KingController (the only body the player drives), CharacterFactory (one shared model + clip set),
                       CharacterAnimator (generated Animator Controller, with a Playables fallback), Outfit, ShoulderCamera, FogView, CommandUI, Visuals (shaders from Resources), BuildingFire,
                       GameRoot (assembles the world; each frame: bodies -> UnitInfo snapshot -> Commander.Tick)
Assets/Scripts/Editor  MannequinImporter (Humanoid + loops + validation), AnimatorBuilder (Locomotion.controller), Oathmarch menu
Tests/BrainSim         headless console test of the brain
```
The brain never touches a GameObject. Bodies never decide strategy. They read `Squad.Destination`, `Heading`, `Run`, `AggroRadius` and `State`, and they report what they did as events.

## Swapping in your own characters (Unity packages)
1. **Assets > Import Package > Custom Package…** and import your pack.
2. Select the character model > Inspector > **Rig > Animation Type: Humanoid > Apply**. Check that Configure… shows a green mapping. If you put the FBX in any folder named `Characters`, the importer does this for you.
3. Select the `Oathmarch` GameObject (from Build Slice Scene) and drag the model into **GameRoot > Character Model**. Without a scene, replace `Assets/Resources/Characters/Mannequin.fbx` instead.
4. The shared animation set is taken from the mannequin's Idle/Walk/Run/Attack/Death clips and retargets to any humanoid. To use your pack's animations, assign them in `CharacterFactory` (clips are found by name).
5. Outfits: `Outfit.Apply(OutfitKind)` tints the body and attaches parts to humanoid bones. Swap in real outfit meshes skinned to the same rig there. Everyone (king, soldiers, enemy) uses the same rig.

The mannequin is regenerated with `blender -b -P Assets/Tools/make_mannequin.py -- Assets/Resources/Characters/Mannequin.fbx`.

## Next
NavMesh (com.unity.ai.navigation) in place of simple steering, Scout/Both/Parley orders, a messenger body to replace the Tab shortcut, fort siege (strike twice), Scout-minded trait behaviour, fog for the enemy commander, real art from your packs, places that grow over time, and a proper Animator Controller with an attack layer.
