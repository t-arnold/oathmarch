using System.Collections.Generic;
using UnityEngine;
using Oathmarch.Brain;

namespace Oathmarch.Bodies
{
    /// <summary>
    /// Presents the brain's FogGrid: a dark overlay over the ground (black = never seen, dim = seen,
    /// clear = in view), place buildings hidden until discovered and tinted dim when out of view,
    /// and hostile bodies hidden unless currently visible.
    /// </summary>
    public class FogView : MonoBehaviour
    {
        GameRoot root; Texture2D tex; Color32[] px; GameObject overlay;
        float timer;
        readonly Dictionary<Renderer, Color> baseColors = new Dictionary<Renderer, Color>();
        MaterialPropertyBlock mpb;

        void Start()
        {
            root = GameRoot.Instance;
            var fog = root.Fog;
            tex = new Texture2D(fog.Size, fog.Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            px = new Color32[fog.Size * fog.Size];
            overlay = GameObject.CreatePrimitive(PrimitiveType.Quad);
            overlay.name = "FogOverlay";
            Destroy(overlay.GetComponent<Collider>());
            overlay.transform.position = new Vector3(0, 0.05f, 0);
            overlay.transform.rotation = Quaternion.Euler(90, 0, 0);
            overlay.transform.localScale = new Vector3(fog.HalfExtent * 2, fog.HalfExtent * 2, 1);
            var sh = Shader.Find("Unlit/Transparent") ?? Shader.Find("Sprites/Default");
            var m = new Material(sh) { mainTexture = tex, renderQueue = 3000 };
            overlay.GetComponent<Renderer>().sharedMaterial = m;
            overlay.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mpb = new MaterialPropertyBlock();
            foreach (var kv in root.PlaceObjects)
                foreach (var go in kv.Value)
                    foreach (var r in go.GetComponentsInChildren<Renderer>()) baseColors[r] = r.sharedMaterial.color;
            Refresh();
        }

        void Update()
        {
            timer -= Time.deltaTime;
            if (timer <= 0) { timer = 0.2f; Refresh(); }
            // hostile bodies are only drawn while someone of ours can see them
            foreach (var u in Unit.All)
            {
                if (u.faction == Faction.King) continue;
                bool vis = root.Fog.At(new V2(u.transform.position.x, u.transform.position.z)) == FogState.Visible;
                foreach (var r in u.GetComponentsInChildren<Renderer>()) r.enabled = vis;
            }
        }

        void Refresh()
        {
            var fog = root.Fog;
            var viewers = new List<(V2, float)>();
            foreach (var u in Unit.All)
                if (u.IsAlive && u.faction == Faction.King) viewers.Add((new V2(u.transform.position.x, u.transform.position.z), u.vision));
            foreach (var p in root.World.Places)
                if (p.Owner == Faction.King) viewers.Add((p.Center, p.Radius + 6f));   // own places always visible
            fog.Update(viewers);

            for (int y = 0; y < fog.Size; y++)
                for (int x = 0; x < fog.Size; x++)
                {
                    var s = fog[x, y];
                    byte a = s == FogState.Visible ? (byte)0 : s == FogState.Seen ? (byte)120 : (byte)235;
                    px[y * fog.Size + x] = new Color32(8, 10, 16, a);
                }
            tex.SetPixels32(px); tex.Apply(false);

            foreach (var p in root.World.Places)
            {
                if (!root.PlaceObjects.TryGetValue(p.Id, out var objs)) continue;
                foreach (var go in objs)
                {
                    var bs = fog.At(new V2(go.transform.position.x, go.transform.position.z));
                    bool show = bs != FogState.Unknown || p.Owner == Faction.King;
                    foreach (var r in go.GetComponentsInChildren<Renderer>())
                    {
                        r.enabled = show;
                        if (!baseColors.TryGetValue(r, out var c)) continue;
                        float k = bs == FogState.Visible ? 1f : 0.45f;
                        if (p.Raided) c = Color.Lerp(c, Color.black, 0.6f);
                        mpb.SetColor("_Color", new Color(c.r * k, c.g * k, c.b * k, 1));
                        r.SetPropertyBlock(mpb);
                    }
                }
            }
        }
    }
}
