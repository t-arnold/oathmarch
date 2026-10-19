using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Oathmarch.EditorTools
{
    /// <summary>
    /// Imports the shared character model(s) as Humanoid, with in-place loops for idle/walk/run.
    /// Applies to any model under a folder named "Characters" (drop your own pack's FBX there to get the same treatment).
    /// </summary>
    public class MannequinImporter : AssetPostprocessor
    {
        static bool Applies(string path) => path.Replace('\\', '/').Contains("/Characters/") && path.ToLowerInvariant().EndsWith(".fbx");

        void OnPreprocessModel()
        {
            if (!Applies(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            if (mi.animationType != ModelImporterAnimationType.Human)
            {
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            }
            mi.importAnimation = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
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
                // everything in place: root motion baked into the pose
                c.lockRootRotation = true; c.keepOriginalOrientation = true;
                c.lockRootHeightY = true; c.keepOriginalPositionY = true;
                c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true;
            }
            mi.clipAnimations = clips;
        }
    }
}
