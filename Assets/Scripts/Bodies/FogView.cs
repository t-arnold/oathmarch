using System.Collections.Generic;
using UnityEngine;
using Oathmarch.Brain;

namespace Oathmarch.Bodies
{
    /// <summary>
    /// Presents the brain's FogGrid: a dark overlay over the ground (black = never seen, dim = seen,
    /// clear = in view) using a shader shipped in Resources; place buildings hidden until discovered and
    /// dimmed when out of view; hostile bodies drawn only while one of ours can actually see them.
    /// </summary>
    public class FogView : MonoBehaviour
    {
        GameRoot root; Texture2D tex; Color32[] px; GameObject overlay;
        float timer;
        readonly Dictionary<Renderer, Color> baseColors = new Dictionary<Renderer, Color>();
        readonly Dictionary<Renderer, (int place, int building)> owners = new Dictionary<Renderer, (int, int)>();
        MaterialPropertyBlock mpb;
        public bool MaterialOk { get; private set; }
        public Texture2D Texture => tex;

        void Awake()
        {
            root = GetComponent<GameRoot>();
            var fog = root.Fog;
            tex = new Texture2D(fog.Size, fog.Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "FogTex" };
            px = new Color32[fog.Size * fog.Size];
            overlay = GameObject.CreatePrimitive(PrimitiveType.Quad);
            overlay.name = "FogOverlay";
            DestroyImmediate(overlay.GetComponent<Collider>());
            overlay.layer = 2;
            // a Quad faces -Z; rotate so it lies flat facing up. UV (0,0) lands at world (-H,-H) like the fog grid.
            overlay.transform.position = new Vector3(0, 0.08f, 0);
            overlay.transform.rotation = Quaternion.Euler(90, 0, 0);
            overlay.transform.localScale = new Vector3(fog.HalfExtent * 2, fog.HalfExtent * 2, 1);
            var mat = Visuals.Fog(tex);
            var r = overlay.GetComponent<Renderer>();
            if (mat != null) { r.sharedMaterial = mat; MaterialOk = true; }
            else { r.enabled = false; Debug.LogError("[Oathmarch] Fog overlay disabled: no usable shader. Buildings/enemies are still hidden by fog."); }
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            mpb = new MaterialPropertyBlock();
        }

        void Start()
        {
            foreach (var p in root.World.Places)
                foreach (var b in p.Buildings)
                    if (root.BuildingObjects.TryGetValue((p.Id, b.Index), out var go))
                        foreach (var rr in go.GetComponentsInChildren<Renderer>()) { baseColors[rr] = rr.sharedMaterial.color; owners[rr] = (p.Id, b.Index); }
            Refresh();
        }

        void Update()
        {
            timer -= Time.deltaTime;
            if (timer <= 0) { timer = 0.2f; Refresh(); }
            // hostile bodies are drawn only while someone of ours can see them (dead ones stay visible where they fell if seen)
            foreach (var u in Unit.All)
            {
                if (u.faction == Faction.King) continue;
                bool vis = root.Fog.At(new V2(u.transform.position.x, u.transform.position.z)) == FogState.Visible;
                foreach (var rr in u.GetComponentsInChildren<Renderer>()) rr.enabled = vis;
            }
        }

        void Refresh()
        {
            var fog = root.Fog;
            var viewers = new List<(V2, float)>();
            foreach (var u in Unit.All)
                if (u.IsAlive && u.faction == Faction.King && u.vision > 0) viewers.Add((new V2(u.transform.position.x, u.transform.position.z), u.vision));
            foreach (var p in root.World.Places)
                if (p.Owner == Faction.King) viewers.Add((p.Center, p.Radius + 10f));   // own places are always watched
            fog.Update(viewers);

            for (int y = 0; y < fog.Size; y++)
                for (int x = 0; x < fog.Size; x++)
                {
                    var s = fog[x, y];
                    byte a = s == FogState.Visible ? (byte)0 : s == FogState.Seen ? (byte)140 : (byte)245;
                    px[y * fog.Size + x] = new Color32(0, 0, 0, a);
                }
            tex.SetPixels32(px); tex.Apply(false);

            foreach (var kv in baseColors)
            {
                var rr = kv.Key; if (rr == null) continue;
                var (pid, bi) = owners[rr];
                var p = root.World.Get(pid); var b = p.Buildings[bi];
                var bs = fog.At(b.Pos);
                rr.enabled = bs != FogState.Unknown || p.Owner == Faction.King;
                Color c = b.Burned ? new Color(0.12f, 0.1f, 0.09f) : kv.Value;
                float k = bs == FogState.Visible ? 1f : 0.4f;
                mpb.SetColor("_Color", new Color(c.r * k, c.g * k, c.b * k, 1));
                rr.SetPropertyBlock(mpb);
            }
        }
    }
}
