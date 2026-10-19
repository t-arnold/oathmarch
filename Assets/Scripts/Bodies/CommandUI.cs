using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Oathmarch.Brain;

namespace Oathmarch.Bodies
{
    /// <summary>
    /// The king's voice and ears. Walk up to the commander and press E (or Tab/O anywhere: a messenger)
    /// to open the order panel. Reports scroll in the log. F1 toggles the debug overlay.
    /// </summary>
    public class CommandUI : MonoBehaviour
    {
        public static CommandUI Instance { get; private set; }
        public bool PanelOpen { get; private set; }
        public bool DebugOpen;
        public const float TalkRange = 3.5f;
        GameRoot root;
        GUIStyle box, label, small;

        void Awake() { Instance = this; root = GetComponent<GameRoot>(); }

        bool NearCommander => root.King != null && root.CommanderBody != null &&
            Vector3.Distance(root.King.transform.position, root.CommanderBody.transform.position) < TalkRange;

        List<(string label, StandingOrder order)> Options()
        {
            var c = root.Commander;
            var list = new List<(string, StandingOrder)>
            {
                ($"Defend {root.World.KingTown.Name}", StandingOrder.Defend()),
                ("Survey the country (riders go out, the watch stays up)", StandingOrder.Survey())
            };
            foreach (var p in root.World.Places.Where(c.CanStrike))
                list.Add(($"Strike {p.Name} ({p.KindWord}{(p.Kind == PlaceKind.Hamlet ? ": raid" : ": assault")})", StandingOrder.Strike(p)));
            return list;
        }

        void Toggle(bool open)
        {
            PanelOpen = open;
            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked; Cursor.visible = open;
        }

        void Update()
        {
            if (root == null || root.Commander == null) return;
            if (Input.GetKeyDown(KeyCode.F1)) DebugOpen = !DebugOpen;
            if (Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.O)) Toggle(!PanelOpen);
            if (Input.GetKeyDown(KeyCode.E) && (NearCommander || PanelOpen)) Toggle(!PanelOpen);
            if (PanelOpen && Input.GetKeyDown(KeyCode.Escape)) Toggle(false);
            if (NearCommander && root.CommanderBody != null && root.King != null)
            {   // he turns to face his king
                var d = root.King.transform.position - root.CommanderBody.transform.position; d.y = 0;
                if (d.sqrMagnitude > 0.01f) root.CommanderBody.transform.rotation = Quaternion.Slerp(root.CommanderBody.transform.rotation, Quaternion.LookRotation(d), 4f * Time.deltaTime);
            }
            if (!PanelOpen) return;
            var opts = Options();
            for (int i = 0; i < opts.Count && i < 9; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i)) { Give(opts[i].order); break; }
        }

        void Give(StandingOrder o) { root.Commander.Give(o); Toggle(false); }

        static string Clock(float t) { int s = Mathf.Max(0, (int)t); return $"{s / 60:00}:{s % 60:00}"; }

        int BodiesIn(Squad s) => Unit.All.Count(u => u.squad == s && u.IsAlive);

        void OnGUI()
        {
            if (root == null || root.Commander == null) return;
            if (box == null)
            {
                box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, wordWrap = true, richText = false };
                label = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true, richText = false };
                small = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = false, richText = false };
            }
            var c = root.Commander;
            string order = c.Order.Kind == OrderKind.Defend ? $"Defend {root.World.KingTown.Name}" : c.Order.Kind == OrderKind.Survey ? "Survey (and Defend)" : $"Strike {root.World.Get(c.Order.TargetPlaceId)?.Name}";
            // counts come from the bodies that exist right now
            string squads = string.Join("   ", c.Squads.Where(s => s.Role != SquadRole.Garrison).Select(s => $"{s.Name}: {BodiesIn(s)}/{s.Nominal} {s.State}"));
            GUI.Box(new Rect(10, 10, 640, 66), $"Commander {c.Name} [{c.Trait}]   Standing order: {order}\n{squads}\nKing HP {Mathf.CeilToInt(root.King.hp)}   [E] at commander / [Tab] messenger: orders   [Shift] sprint   [LMB] strike   [F1] debug", box);

            var entries = root.Log.Entries;
            int n = Mathf.Min(8, entries.Count);
            float h = 22 * n + 12;
            if (n > 0)
            {
                GUI.Box(new Rect(10, Screen.height - h - 10, 760, h), GUIContent.none, box);
                for (int i = 0; i < n; i++)
                {
                    var e = entries[entries.Count - n + i];
                    GUI.Label(new Rect(18, Screen.height - h - 4 + i * 22, 750, 22), $"[{Clock(e.Time)}] {e.Text}", label);
                }
            }

            if (!PanelOpen && NearCommander)
                GUI.Label(new Rect(Screen.width / 2 - 150, Screen.height / 2 + 60, 300, 24), $"[E] Speak with {c.Name}", label);
            if (Cursor.lockState != CursorLockMode.Locked && !PanelOpen)
                GUI.Label(new Rect(Screen.width / 2 - 120, Screen.height / 2 + 40, 300, 24), "Click to take control", label);
            else if (!PanelOpen)
                GUI.Label(new Rect(Screen.width / 2 - 4, Screen.height / 2 - 12, 20, 24), "+", label);

            if (DebugOpen) DrawDebug();

            if (!PanelOpen) return;
            var opts = Options();
            float ph = 76 + opts.Count * 34;
            var r = new Rect(Screen.width / 2 - 260, Screen.height / 2 - ph / 2, 520, ph);
            GUI.Box(r, NearCommander ? $"{c.Name}: \"Your orders, my king?\"" : $"Messenger to {c.Name}", box);
            for (int i = 0; i < opts.Count; i++)
                if (GUI.Button(new Rect(r.x + 15, r.y + 30 + i * 34, 490, 28), $"{i + 1}. {opts[i].label}")) Give(opts[i].order);
            GUI.Label(new Rect(r.x + 15, r.y + ph - 36, 490, 30), "Only settlements someone has reported can be named. Esc closes.", label);
        }

        void DrawDebug()
        {
            var fv = GetComponent<FogView>();
            float size = 256, x0 = Screen.width - size - 10, y0 = 10;
            GUI.Box(new Rect(x0 - 4, y0 - 4, size + 8, size + 8), GUIContent.none);
            var w = root.World; float H = w.HalfSize;
            Rect R(Vector3 p, float s) { float u = (p.x + H) / (2 * H), v = (p.z + H) / (2 * H); return new Rect(x0 + u * size - s / 2, y0 + (1 - v) * size - s / 2, s, s); }
            var old = GUI.color;
            GUI.color = new Color(0.35f, 0.45f, 0.25f); GUI.DrawTexture(new Rect(x0, y0, size, size), Texture2D.whiteTexture);
            GUI.color = Color.white;
            if (fv != null && fv.Texture != null) GUI.DrawTextureWithTexCoords(new Rect(x0, y0, size, size), fv.Texture, new Rect(0, 0, 1, 1));
            foreach (var p in w.Places)
            {
                GUI.color = p.Owner == Faction.King ? Color.cyan : p.Owner == Faction.Enemy ? Color.red : Color.yellow;
                GUI.Label(R(new Vector3(p.Center.x, 0, p.Center.z), 8), "■", small);
                GUI.Label(new Rect(R(new Vector3(p.Center.x, 0, p.Center.z), 8).x + 8, R(new Vector3(p.Center.x, 0, p.Center.z), 8).y - 4, 120, 18), p.Name, small);
            }
            foreach (var u in Unit.All)
            {
                if (!u.IsAlive) continue;
                GUI.color = u.IsKing ? Color.white : u.faction == Faction.King ? new Color(0.4f, 0.6f, 1f) : new Color(1f, 0.3f, 0.2f);
                GUI.DrawTexture(R(u.transform.position, u.IsKing ? 6 : 4), Texture2D.whiteTexture);
            }
            GUI.color = old;
            var lines = new List<string>();
            foreach (var cmd in new[] { root.Commander, root.EnemyCommander })
                foreach (var s in cmd.Squads)
                {
                    var g = s.Engagement;
                    string eng = g == null ? "" : $" fight: contact={g.Contact} seen={g.SeenEnemyIds.Count} killed={g.KilledIds.Count} lost={g.Losses}";
                    lines.Add($"{(cmd.Faction == Faction.King ? "K" : "E")} {s.Name,-22} {s.State,-9} bodies {BodiesIn(s)}/{s.Nominal} brain {s.Alive} aggro {s.AggroRadius:0}{eng}");
                }
            lines.Add($"raids sent {root.EnemyCommander.RaidsSent}, next in {root.EnemyCommander.RaidTimer:0}s | soldiers spawned {root.SoldiersSpawned}, enemy bodies spawned {root.RaidersSpawned}");
            GUI.color = new Color(0, 0, 0, 0.75f);
            GUI.DrawTexture(new Rect(x0 - 430, y0 + size + 12, 690, 18 * lines.Count + 8), Texture2D.whiteTexture);
            GUI.color = Color.white;
            for (int i = 0; i < lines.Count; i++) GUI.Label(new Rect(x0 - 426, y0 + size + 16 + i * 18, 690, 18), lines[i], small);
        }
    }
}
