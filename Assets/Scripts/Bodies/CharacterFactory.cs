using System.Linq;
using UnityEngine;

namespace Oathmarch.Bodies
{
    /// <summary>
    /// Builds every person from ONE shared humanoid model + ONE animation set.
    /// To use your own pack: drop a Humanoid-rigged model in a folder named Characters (or assign
    /// GameRoot.characterModel); the shared clips retarget onto it and the outfit is applied on top.
    /// </summary>
    public static class CharacterFactory
    {
        public const string ModelPath = "Characters/Mannequin", ControllerPath = "Characters/Locomotion";
        static GameObject model; static Avatar avatar; static RuntimeAnimatorController controller;
        static AnimationClip idle, walk, run, attack, death;
        static bool loaded;

        public static GameObject OverrideModel;
        public static bool AvatarValid { get; private set; }
        public static bool HasController => controller != null;
        public static string ClipSummary { get; private set; } = "";

        public static void Load()
        {
            if (loaded) return; loaded = true;
            var all = Resources.LoadAll<Object>(ModelPath);
            model = OverrideModel != null ? OverrideModel : Resources.Load<GameObject>(ModelPath);
            avatar = (OverrideModel != null ? OverrideModel.GetComponentInChildren<Animator>()?.avatar : null) ?? all.OfType<Avatar>().FirstOrDefault();
            var clips = all.OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToArray();
            AnimationClip Find(string n) => clips.FirstOrDefault(c => c.name == n) ?? clips.FirstOrDefault(c => c.name.EndsWith(n));
            idle = Find("Idle"); walk = Find("Walk"); run = Find("Run"); attack = Find("Attack"); death = Find("Death");
            controller = Resources.Load<RuntimeAnimatorController>(ControllerPath);
            AvatarValid = avatar != null && avatar.isValid && avatar.isHuman;
            ClipSummary = string.Join(", ", clips.Select(c => $"{c.name}{(c.isLooping ? "(loop)" : "")}"));

            if (model == null) Debug.LogError("[Oathmarch] Mannequin model missing at Assets/Resources/" + ModelPath + ".fbx");
            if (!AvatarValid)
                Debug.LogError("[Oathmarch] CHARACTER AVATAR IS NOT A VALID HUMANOID -> characters will T-pose. Fix: menu Oathmarch > Reimport Mannequin As Humanoid, or select Mannequin.fbx > Rig > Animation Type: Humanoid > Apply and check Configure is all green.");
            if (idle == null || walk == null || run == null) Debug.LogError("[Oathmarch] Mannequin clips missing. Found: " + ClipSummary);
            else if (!idle.isLooping || !walk.isLooping || !run.isLooping) Debug.LogWarning("[Oathmarch] Idle/Walk/Run are not flagged Loop Time in the import settings (" + ClipSummary + "). Run Oathmarch > Reimport Mannequin As Humanoid.");
            if (controller == null) Debug.LogWarning("[Oathmarch] Locomotion.controller not found in Resources/Characters; using the Playables fallback. Run Oathmarch > Rebuild Animator Controller.");
        }

        public static GameObject Spawn(string name, Vector3 pos, Quaternion rot, OutfitKind outfit, bool king = false)
        {
            Load();
            var root = new GameObject(name);
            root.transform.SetPositionAndRotation(pos, rot);
            GameObject visual = model != null ? Object.Instantiate(model, root.transform) : GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "Body";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = model != null ? Vector3.zero : Vector3.up;
            visual.transform.localRotation = Quaternion.identity;
            foreach (var c in visual.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);

            var animator = visual.GetComponent<Animator>() ?? visual.AddComponent<Animator>();
            animator.avatar = avatar;
            // outfit parts are placed in the bind pose, so apply BEFORE any animation is evaluated
            visual.AddComponent<Outfit>().Apply(outfit);
            var ca = visual.AddComponent<CharacterAnimator>();
            ca.Init(AvatarValid ? controller : null, idle, walk, run, attack, death);

            if (king)
            {
                var cc = root.AddComponent<CharacterController>();
                cc.height = 1.8f; cc.radius = 0.35f; cc.center = new Vector3(0, 0.92f, 0);
            }
            else
            {
                var col = root.AddComponent<CapsuleCollider>();
                col.height = 1.8f; col.radius = 0.35f; col.center = new Vector3(0, 0.9f, 0);
                var rb = root.AddComponent<Rigidbody>(); rb.isKinematic = true;
            }
            return root;
        }

        public static T AddBody<T>(GameObject root) where T : Unit => root.AddComponent<T>();
    }
}
