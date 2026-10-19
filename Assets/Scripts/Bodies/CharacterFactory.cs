using System.Linq;
using UnityEngine;

namespace Oathmarch.Bodies
{
    /// <summary>
    /// Builds every person from ONE shared humanoid model + ONE animation set.
    /// To use your own pack: put a Humanoid-rigged model at Resources/Characters/ (or assign
    /// overrideModel on GameRoot) and optionally a different humanoid clip set; the outfit is applied on top.
    /// </summary>
    public static class CharacterFactory
    {
        public const string ModelPath = "Characters/Mannequin";
        static GameObject model; static Avatar avatar;
        static AnimationClip idle, walk, run, attack, death;
        static bool loaded;

        public static GameObject OverrideModel;     // optional: a humanoid prefab/model from your pack
        public static Avatar OverrideAvatar;

        static void Load()
        {
            if (loaded) return; loaded = true;
            model = OverrideModel != null ? OverrideModel : Resources.Load<GameObject>(ModelPath);
            var all = Resources.LoadAll<Object>(ModelPath);
            avatar = OverrideAvatar != null ? OverrideAvatar : all.OfType<Avatar>().FirstOrDefault();
            var clips = all.OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToArray();
            AnimationClip Find(string n) => clips.FirstOrDefault(c => c.name == n) ?? clips.FirstOrDefault(c => c.name.EndsWith(n));
            idle = Find("Idle"); walk = Find("Walk"); run = Find("Run"); attack = Find("Attack"); death = Find("Death");
            if (model == null) Debug.LogError("[Oathmarch] Mannequin model missing at Resources/" + ModelPath);
            else if (avatar == null || !avatar.isHuman || !avatar.isValid)
                Debug.LogError("[Oathmarch] Mannequin avatar is not a valid Humanoid. Select Assets/Resources/Characters/Mannequin.fbx > Rig > Animation Type: Humanoid > Apply (or run Oathmarch > Reimport Mannequin As Humanoid).");
            if (idle == null || walk == null) Debug.LogError("[Oathmarch] Mannequin clips not found (" + string.Join(",", clips.Select(c => c.name)) + ")");
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
            foreach (var c in visual.GetComponentsInChildren<Collider>()) Object.Destroy(c);

            var animator = visual.GetComponent<Animator>() ?? visual.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var ca = visual.AddComponent<CharacterAnimator>();
            ca.Init(idle, walk, run, attack, death);
            visual.AddComponent<Outfit>().Apply(outfit);

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

        /// <summary>Unit/King components look up the CharacterAnimator on Awake, so add them after Spawn via this helper.</summary>
        public static T AddBody<T>(GameObject root) where T : Unit
        {
            return root.AddComponent<T>();
        }
    }
}
