using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Oathmarch.EditorTools
{
    /// <summary>
    /// Generates Assets/Resources/Characters/Locomotion.controller from the mannequin clips:
    /// Locomotion = 1D blend tree on Speed (Idle 0, Walk 1.57, Run 3.56 m/s, the clips' measured ground speeds;
    /// state speed multiplied by MoveScale), Attack (trigger, returns to Locomotion), Death (Dead bool, from Any State).
    /// Runs automatically when the editor loads or the mannequin is reimported.
    /// </summary>
    [InitializeOnLoad]
    public static class AnimatorBuilder
    {
        public const string Fbx = "Assets/Resources/Characters/Mannequin.fbx";
        public const string ControllerPath = "Assets/Resources/Characters/Locomotion.controller";

        static AnimatorBuilder() { EditorApplication.delayCall += EnsureSetup; }

        static void EnsureSetup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var mi = AssetImporter.GetAtPath(Fbx) as ModelImporter;
            if (mi == null) return;
            if (mi.animationType != ModelImporterAnimationType.Human)
            {
                Debug.Log("[Oathmarch] Mannequin was not imported as Humanoid; reimporting.");
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.SaveAndReimport();
            }
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) == null) Build();
        }

        [MenuItem("Oathmarch/Rebuild Animator Controller")]
        public static void Build()
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(Fbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToArray();
            AnimationClip Find(string n) => clips.FirstOrDefault(c => c.name == n) ?? clips.FirstOrDefault(c => c.name.EndsWith(n));
            AnimationClip idle = Find("Idle"), walk = Find("Walk"), run = Find("Run"), attack = Find("Attack"), death = Find("Death");
            if (idle == null || walk == null || run == null) { Debug.LogError("[Oathmarch] Cannot build controller: mannequin clips not found."); return; }

            AssetDatabase.DeleteAsset(ControllerPath);
            var ac = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ac.AddParameter("MoveScale", AnimatorControllerParameterType.Float);
            ac.parameters = ac.parameters.Select(p => { if (p.name == "MoveScale") p.defaultFloat = 1f; return p; }).ToArray();
            ac.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            ac.AddParameter("Dead", AnimatorControllerParameterType.Bool);
            var sm = ac.layers[0].stateMachine;

            var loco = ac.CreateBlendTreeInController("Locomotion", out var tree, 0);
            tree.blendType = BlendTreeType.Simple1D; tree.blendParameter = "Speed"; tree.useAutomaticThresholds = false;
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, 1.57f);
            tree.AddChild(run, 3.56f);
            loco.speedParameterActive = true; loco.speedParameter = "MoveScale";
            sm.defaultState = loco;

            if (attack != null)
            {
                var atk = sm.AddState("Attack"); atk.motion = attack;
                var t1 = loco.AddTransition(atk); t1.AddCondition(AnimatorConditionMode.If, 0, "Attack"); t1.hasExitTime = false; t1.duration = 0.06f;
                var t2 = atk.AddTransition(loco); t2.hasExitTime = true; t2.exitTime = 0.9f; t2.duration = 0.1f;
            }
            if (death != null)
            {
                var die = sm.AddState("Death"); die.motion = death;
                var td = sm.AddAnyStateTransition(die); td.AddCondition(AnimatorConditionMode.If, 0, "Dead"); td.hasExitTime = false; td.duration = 0.1f; td.canTransitionToSelf = false;
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Oathmarch] Built " + ControllerPath);
        }
    }

    class MannequinReimportWatcher : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported.Any(p => p == AnimatorBuilder.Fbx)) EditorApplication.delayCall += AnimatorBuilder.Build;
        }
    }
}
