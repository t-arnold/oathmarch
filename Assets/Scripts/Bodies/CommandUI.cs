using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Oathmarch.Brain;

namespace Oathmarch.Bodies
{
    /// <summary>
    /// The king's voice and ears. Tab opens the order panel (Defend, Strike a reported settlement).
    /// Number keys or clicks pick an order. Reports from the commander scroll in the log.
    /// </summary>
    public class CommandUI : MonoBehaviour
    {
        public static CommandUI Instance { get; private set; }
        public bool PanelOpen { get; private set; }
        GameRoot root;
        CursorLockMode prevLock;

        void Awake() { Instance = this; }
        void Start() { root = GameRoot.Instance; }

        public void Log(string s) => root?.Log.Add(Time.time, s);

        List<(string label, StandingOrder order)> Options()
        {
            var c = root.Commander;
            var list = new List<(string, StandingOrder)> { ($"Defend {root.World.KingTown.Name}", StandingOrder.Defend()) };
            foreach (var p in root.World.Places.Where(c.CanStrike))
                list.Add(($"Strike {p.Name} ({p.KindWord}{(p.Kind == PlaceKind.Hamlet ? ": raid" : ": assault")})", StandingOrder.Strike(p)));
            return list;
        }

        void Toggle()
        {
            PanelOpen = !PanelOpen;
            if (PanelOpen) { prevLock = Cursor.lockState; Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            else { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        }

        void Update()
        {
            if (root == null) return;
            if (Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.O)) Toggle();
            if (!PanelOpen) return;
            var opts = Options();
            for (int i = 0; i < opts.Count && i < 9; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) { Give(opts[i].order); break; }
        }

        void Give(StandingOrder o) { root.Commander.Give(o); Toggle(); }

        void OnGUI()
        {
            if (root == null) return;
            var c = root.Commander;
            var box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, wordWrap = true };
            var label = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };

            string order = c.Order.Kind == OrderKind.Defend ? $"Defend {root.World.KingTown.Name}" : $"Strike {root.World.Get(c.Order.TargetPlaceId)?.Name}";
            string squads = string.Join("   ", c.Squads.Where(s => !s.Garrison).Select(s => $"{s.Name}: {s.Alive} men, {s.Intent}"));
            GUI.Box(new Rect(10, 10, 560, 64), $"Commander {c.Name} [{c.Trait}]   Standing order: {order}\n{squads}\nKing HP {Mathf.CeilToInt(root.King.hp)}   [Tab] orders  [Shift] sprint  [LMB] strike  [Esc] free cursor", box);

            var entries = root.Log.Entries;
            int n = Mathf.Min(8, entries.Count);
            float h = 22 * n + 12;
            GUI.Box(new Rect(10, Screen.height - h - 10, 640, h), "", box);
            for (int i = 0; i < n; i++)
            {
                var e = entries[entries.Count - n + i];
                int mm = (int)(e.Time / 60), ss = (int)(e.Time % 60);
                GUI.Label(new Rect(18, Screen.height - h - 4 + i * 22, 630, 22), $"[{mm:00}:{ss:00}] {e.Text}", label);
            }

            if (Cursor.lockState != CursorLockMode.Locked && !PanelOpen)
                GUI.Label(new Rect(Screen.width / 2 - 120, Screen.height / 2 + 40, 300, 24), "Click to take control", label);
            else if (!PanelOpen)
                GUI.Label(new Rect(Screen.width / 2 - 4, Screen.height / 2 - 12, 20, 24), "+", label);

            if (!PanelOpen) return;
            var opts = Options();
            float ph = 70 + opts.Count * 34;
            var r = new Rect(Screen.width / 2 - 220, Screen.height / 2 - ph / 2, 440, ph);
            GUI.Box(r, $"Orders to {c.Name}", box);
            for (int i = 0; i < opts.Count; i++)
                if (GUI.Button(new Rect(r.x + 15, r.y + 30 + i * 34, 410, 28), $"{i + 1}. {opts[i].label}")) Give(opts[i].order);
            GUI.Label(new Rect(r.x + 15, r.y + ph - 32, 410, 24), "Only settlements someone has reported can be named.", label);
        }
    }
}
