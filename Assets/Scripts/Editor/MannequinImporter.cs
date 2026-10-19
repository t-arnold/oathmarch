using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Oathmarch.EditorTools
{
    /// <summary>
    /// Imports any FBX under a folder named "Characters" as Humanoid (avatar created from the model,
    /// auto-mapped; the mannequin's bones use Unity's humanoid names), with Idle/Walk/Run looping and
    /// every clip in place (root motion baked into the pose). After import it validates the avatar and
    /// shouts in the Console if it is not a valid humanoid.
    /// </summary>
    public class MannequinImporter : AssetPostprocessor
    {
        public override uint GetVersion() => 3;   // bump to force a reimport with new settings

        public static bool Applies(string path) => path.Replace('\\', '/').Contains("/Characters/") && path.ToLowerInvariant().EndsWith(".fbx");

        void OnPreprocessModel()
        {
            if (!Applies(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.autoGenerateAvatarMappingIfUnspecified = true;
            mi.importAnimation = true;
            mi.optimizeGameObjects = false;          // keep bone transforms so outfit parts can attach to them
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            mi.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
        }

        void OnPreprocessAnimation()
        {
            if (!Applies(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            var clips = mi.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return;
            foreach (var c in clips)
            {
                string shortName = c.takeName.Split('|').Last();
                c.name = shortName;
                bool loop = shortName == "Idle" || shortName == "Walk" || shortName == "Run";
                c.loopTime = loop; c.loopPose = loop;
                c.lockRootRotation = true; c.keepOriginalOrientation = true;
                c.lockRootHeightY = true; c.keepOriginalPositionY = true; c.heightFromFeet = false;
                c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true;
            }
            mi.clipAnimations = clips;
        }

        void OnPostprocessModel(GameObject g)
        {
            if (!Applies(assetPath)) return;
            EditorApplication.delayCall += () => Validate(assetPath);
        }

        public static bool Validate(string path)
        {
            var av = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            bool ok = av != null && av.isValid && av.isHuman;
            if (!ok) Debug.LogError($"[Oathmarch] {path}: avatar is NOT a valid Humanoid. Select it > Rig > Configure and fix the mapping (Hips, Spine, Chest, Neck, Head, Left/Right UpperArm/LowerArm/Hand, UpperLeg/LowerLeg/Foot).");
            return ok;
        }
    }
}
