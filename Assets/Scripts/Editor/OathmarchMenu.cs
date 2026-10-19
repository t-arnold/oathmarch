using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Oathmarch.Bodies;

namespace Oathmarch.EditorTools
{
    public static class OathmarchMenu
    {
        const string ScenePath = "Assets/Scenes/Slice.unity";

        [MenuItem("Oathmarch/Build Slice Scene")]
        public static void BuildSliceScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)) { tag = "MainCamera" };
            cam.transform.position = new Vector3(0, 3, -6);
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.shadows = LightShadows.Soft; sun.transform.rotation = Quaternion.Euler(50, -30, 0);
            new GameObject("Oathmarch").AddComponent<GameRoot>();
            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log("[Oathmarch] Saved " + ScenePath + ". Press Play: the greybox world assembles itself at runtime.");
        }

        [MenuItem("Oathmarch/Reimport Mannequin As Humanoid")]
        public static void ReimportMannequin()
        {
            const string p = "Assets/Resources/Characters/Mannequin.fbx";
            var mi = AssetImporter.GetAtPath(p) as ModelImporter;
            if (mi == null) { Debug.LogError("Not found: " + p); return; }
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.SaveAndReimport();
            var av = AssetDatabase.LoadAllAssetsAtPath(p).OfType<Avatar>().FirstOrDefault();
            Debug.Log(av != null && av.isHuman && av.isValid ? "[Oathmarch] Mannequin avatar is a valid Humanoid." : "[Oathmarch] Avatar is NOT valid; open Rig > Configure to fix the bone mapping.");
        }
    }
}
