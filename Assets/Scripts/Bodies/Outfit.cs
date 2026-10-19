using UnityEngine;

namespace Oathmarch.Bodies
{
    public enum OutfitKind { King, Soldier, Enemy, Commander, Rider }

    /// <summary>
    /// Swappable outfit on the ONE shared humanoid rig: body tint + parts attached to real bones
    /// (Head, RightHand, LeftLowerArm, Chest via Animator.GetBoneTransform, or by bone name as a fallback).
    /// Parts are placed in world space while the model is still in its bind pose, then parented to the bone,
    /// so they follow the animation exactly regardless of bone axes or import scale.
    /// Call Apply BEFORE the first animation update.
    /// </summary>
    public class Outfit : MonoBehaviour
    {
        public OutfitKind kind;
        public Color bodyColor = Color.white, trimColor = Color.gray;
        public static int BonesMissing;

        public void Apply(OutfitKind k)
        {
            kind = k;
            switch (k)
            {
                case OutfitKind.King: bodyColor = new Color(0.45f, 0.08f, 0.12f); trimColor = new Color(1f, 0.8f, 0.2f); break;
                case OutfitKind.Soldier: bodyColor = new Color(0.15f, 0.25f, 0.6f); trimColor = new Color(0.75f, 0.75f, 0.8f); break;
                case OutfitKind.Rider: bodyColor = new Color(0.2f, 0.45f, 0.65f); trimColor = new Color(0.85f, 0.85f, 0.7f); break;
                case OutfitKind.Commander: bodyColor = new Color(0.12f, 0.35f, 0.18f); trimColor = new Color(0.9f, 0.75f, 0.3f); break;
                default: bodyColor = new Color(0.28f, 0.24f, 0.2f); trimColor = new Color(0.65f, 0.12f, 0.05f); break;
            }
            Bounds bounds = new Bounds(transform.position + Vector3.up, Vector3.one);
            bool first = true;
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mats = r.materials;
                foreach (var m in mats) m.color = bodyColor;
                r.materials = mats;
                if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds);
            }

            var anim = GetComponent<Animator>();
            Transform root = transform;
            Transform head = Bone(anim, HumanBodyBones.Head, "Head");
            Transform rHand = Bone(anim, HumanBodyBones.RightHand, "RightHand");
            Transform rLower = Bone(anim, HumanBodyBones.RightLowerArm, "RightLowerArm");
            Transform lLower = Bone(anim, HumanBodyBones.LeftLowerArm, "LeftLowerArm");
            Transform lHand = Bone(anim, HumanBodyBones.LeftHand, "LeftHand");
            Transform chest = Bone(anim, HumanBodyBones.Chest, "Chest") ?? Bone(anim, HumanBodyBones.Spine, "Spine");
            Vector3 up = root.up, fwd = root.forward;
            float top = bounds.max.y;

            if (head != null)
            {
                if (k == OutfitKind.King)
                    Part(head, PrimitiveType.Cylinder, new Vector3(head.position.x, top + 0.035f, head.position.z), Quaternion.LookRotation(fwd, up), new Vector3(0.2f, 0.035f, 0.2f), trimColor, "Crown");
                else if (k == OutfitKind.Commander)
                    Part(head, PrimitiveType.Cylinder, new Vector3(head.position.x, top + 0.01f, head.position.z), Quaternion.LookRotation(fwd, up), new Vector3(0.26f, 0.015f, 0.26f), trimColor, "Hat");
                else
                {
                    float hy = (head.position.y + top) * 0.5f + 0.03f;
                    Part(head, PrimitiveType.Sphere, new Vector3(head.position.x, hy, head.position.z), Quaternion.LookRotation(fwd, up), new Vector3(0.25f, 0.2f, 0.26f), trimColor, "Helmet");
                }
            }
            if (rHand != null && k != OutfitKind.Commander)
            {
                // blade grows out of the fist, pointing forward (and slightly up) relative to the body in bind pose
                Vector3 armDir = rLower != null ? (rHand.position - rLower.position).normalized : -root.right;
                Vector3 grip = rHand.position + armDir * 0.06f;
                Vector3 bladeDir = (fwd + up * 0.25f).normalized;
                Part(rHand, PrimitiveType.Cube, grip, Quaternion.LookRotation(bladeDir, up), new Vector3(0.05f, 0.05f, 0.12f), new Color(0.3f, 0.2f, 0.1f), "Grip");
                float len = k == OutfitKind.King ? 0.85f : 0.7f;
                Part(rHand, PrimitiveType.Cube, grip + bladeDir * (0.06f + len * 0.5f), Quaternion.LookRotation(bladeDir, up), new Vector3(0.035f, 0.012f, len), new Color(0.82f, 0.84f, 0.88f), "Blade");
                Part(rHand, PrimitiveType.Cube, grip + bladeDir * 0.06f, Quaternion.LookRotation(bladeDir, up), new Vector3(0.18f, 0.025f, 0.025f), trimColor, "Crossguard");
            }
            if (lLower != null && (k == OutfitKind.Soldier || k == OutfitKind.Rider))
            {
                Vector3 mid = lHand != null ? (lLower.position + lHand.position) * 0.5f : lLower.position;
                Part(lLower, PrimitiveType.Cylinder, mid + fwd * 0.09f, Quaternion.LookRotation(up, fwd), new Vector3(0.42f, 0.012f, 0.42f), trimColor, "Shield");
            }
            if (chest != null && k == OutfitKind.Commander)
            {
                Vector3 back = chest.position - fwd * 0.15f;
                Part(chest, PrimitiveType.Cube, back + Vector3.down * 0.35f, Quaternion.LookRotation(fwd, up), new Vector3(0.42f, 0.95f, 0.03f), trimColor * 0.8f, "Cloak");
                Part(chest, PrimitiveType.Cylinder, back - fwd * 0.05f + Vector3.up * 0.5f, Quaternion.identity, new Vector3(0.03f, 0.9f, 0.03f), new Color(0.35f, 0.25f, 0.12f), "BannerPole");
                Part(chest, PrimitiveType.Cube, back - fwd * 0.05f + Vector3.up * 1.15f + root.right * 0.22f, Quaternion.LookRotation(fwd, up), new Vector3(0.42f, 0.32f, 0.02f), new Color(0.15f, 0.25f, 0.65f), "Banner");
            }
        }

        static Transform Bone(Animator a, HumanBodyBones b, string fallbackName)
        {
            Transform t = null;
            if (a != null && a.avatar != null && a.isHuman) t = a.GetBoneTransform(b);
            if (t == null && a != null) t = FindDeep(a.transform, fallbackName);
            if (t == null) BonesMissing++;
            return t;
        }

        static Transform FindDeep(Transform t, string n)
        {
            if (t.name == n || t.name.EndsWith(":" + n)) return t;
            foreach (Transform c in t) { var r = FindDeep(c, n); if (r != null) return r; }
            return null;
        }

        /// <summary>Create in world space, then parent to the bone keeping the world pose.</summary>
        static Transform Part(Transform bone, PrimitiveType t, Vector3 worldPos, Quaternion worldRot, Vector3 worldScale, Color c, string n)
        {
            var go = GameObject.CreatePrimitive(t);
            go.name = n;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetPositionAndRotation(worldPos, worldRot);
            go.transform.localScale = worldScale;
            go.transform.SetParent(bone, true);
            go.GetComponent<Renderer>().sharedMaterial = Visuals.Lit(c);
            return go.transform;
        }
    }
}
