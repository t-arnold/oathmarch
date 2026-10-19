using UnityEngine;

namespace Oathmarch.Bodies
{
    public enum OutfitKind { King, Soldier, Enemy, Villager }

    /// <summary>
    /// Swappable outfit on the ONE shared humanoid rig. For now: body tint + simple parts
    /// attached to humanoid bones (crown, helmet, shield, sword). Replace with real outfit meshes later
    /// (skinned to the same rig) without touching any brain or body logic.
    /// </summary>
    public class Outfit : MonoBehaviour
    {
        public OutfitKind kind;
        public Color bodyColor = Color.white, trimColor = Color.gray;

        public void Apply(OutfitKind k)
        {
            kind = k;
            switch (k)
            {
                case OutfitKind.King: bodyColor = new Color(0.45f, 0.08f, 0.12f); trimColor = new Color(1f, 0.8f, 0.2f); break;
                case OutfitKind.Soldier: bodyColor = new Color(0.15f, 0.25f, 0.6f); trimColor = new Color(0.75f, 0.75f, 0.8f); break;
                case OutfitKind.Enemy: bodyColor = new Color(0.25f, 0.22f, 0.18f); trimColor = new Color(0.6f, 0.1f, 0.05f); break;
                default: bodyColor = new Color(0.6f, 0.55f, 0.45f); trimColor = bodyColor; break;
            }
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mats = r.materials;
                foreach (var m in mats) m.color = bodyColor;
                r.materials = mats;
            }
            var anim = GetComponent<Animator>();
            Transform head = Bone(anim, HumanBodyBones.Head), rHand = Bone(anim, HumanBodyBones.RightHand), lArm = Bone(anim, HumanBodyBones.LeftLowerArm);
            if (head != null)
            {
                if (k == OutfitKind.King) Part(head, PrimitiveType.Cylinder, new Vector3(0, 0.27f, 0), new Vector3(0.2f, 0.04f, 0.2f), trimColor, "Crown");
                else if (k != OutfitKind.Villager) Part(head, PrimitiveType.Sphere, new Vector3(0, 0.17f, 0), new Vector3(0.24f, 0.16f, 0.25f), trimColor, "Helmet");
            }
            if (rHand != null && k != OutfitKind.Villager)
                Part(rHand, PrimitiveType.Cube, new Vector3(0, 0.05f, 0), new Vector3(0.05f, 0.05f, 0.05f), trimColor, "Grip")
                    .gameObject.AddComponent<SwordMarker>();
            if (lArm != null && k == OutfitKind.Soldier)
                Part(lArm, PrimitiveType.Cylinder, new Vector3(0, 0.12f, 0.07f), new Vector3(0.38f, 0.015f, 0.38f), trimColor, "Shield", new Vector3(90, 0, 0));
        }

        public static Vector3 Div(Vector3 a, Vector3 b) => new Vector3(a.x / Mathf.Max(1e-4f, Mathf.Abs(b.x)), a.y / Mathf.Max(1e-4f, Mathf.Abs(b.y)), a.z / Mathf.Max(1e-4f, Mathf.Abs(b.z)));

        static Transform Bone(Animator a, HumanBodyBones b) => a != null && a.isHuman ? a.GetBoneTransform(b) : null;

        static Transform Part(Transform bone, PrimitiveType t, Vector3 localPos, Vector3 scale, Color c, string n, Vector3 euler = default)
        {
            var go = GameObject.CreatePrimitive(t);
            go.name = n;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(bone, false);
            go.transform.localPosition = localPos; go.transform.localEulerAngles = euler;
            go.transform.localScale = Div(scale, bone.lossyScale);
            go.GetComponent<Renderer>().material.color = c;
            return go.transform;
        }
    }

    /// <summary>Adds a blade along the hand's pointing axis once the bone is known.</summary>
    public class SwordMarker : MonoBehaviour
    {
        void Start()
        {
            var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blade.name = "Blade";
            Destroy(blade.GetComponent<Collider>());
            blade.transform.SetParent(transform.parent, false);
            // the blade points forward out of the fist (bone +Z after humanoid retarget is not guaranteed, so use the body forward)
            blade.transform.position = transform.position;
            blade.transform.rotation = transform.root.rotation;
            blade.transform.localScale = Vector3.one;
            blade.transform.localScale = Outfit.Div(new Vector3(0.03f, 0.03f, 0.75f), blade.transform.lossyScale);
            blade.transform.position += blade.transform.forward * 0.4f;
            blade.GetComponent<Renderer>().material.color = new Color(0.8f, 0.82f, 0.85f);
        }
    }
}
