using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oathmarch.Brain;

namespace Oathmarch.Bodies
{
    /// <summary>
    /// Assembles the slice at runtime (so Play works in any scene) and runs the loop each frame:
    /// bodies -> UnitInfo snapshot + BrainEvents -> brain (both commanders) -> BodyRequests -> bodies.
    /// </summary>
    public class GameRoot : MonoBehaviour
    {
        public static GameRoot Instance { get; private set; }

        [Header("Country")] public int seed = 1187;
        [Header("Player commander")] public Trait commanderTrait = Trait.Cautious;
        public int soldiersPerSquad = 4;
        [Header("Enemy")] public float raidInterval = 55f, firstRaidDelay = 25f; public int raidersPerWave = 4;
        [Header("Optional: your own humanoid model (Rig = Humanoid)")] public GameObject characterModel;

        public World World { get; private set; }
        public FogGrid Fog { get; private set; }
        public Commander Commander { get; private set; }
        public Commander EnemyCommander { get; private set; }
        public ReportLog Log { get; private set; }
        public KingController King { get; private set; }
        public Unit CommanderBody { get; private set; }
        public readonly Dictionary<int, List<GameObject>> PlaceObjects = new Dictionary<int, List<GameObject>>();
        public readonly Dictionary<(int place, int building), GameObject> BuildingObjects = new Dictionary<(int, int), GameObject>();
        public int SoldiersSpawned, RaidersSpawned, BuildingColliders;

        readonly List<UnitInfo> snapshot = new List<UnitInfo>();
        readonly List<BrainEvent> pending = new List<BrainEvent>(), frameEvents = new List<BrainEvent>();

        // ---------------------------------------------------------------- bootstrap
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook() { SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded; }

        static void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            if (m == LoadSceneMode.Single && FindAnyObjectByType<GameRoot>() == null) new GameObject("Oathmarch").AddComponent<GameRoot>();
        }

        public void PushEvent(BrainEvent e) => pending.Add(e);

        void Awake()
        {
            Instance = this;
            Unit.All.Clear();
            Visuals.Init();
            if (characterModel != null) CharacterFactory.OverrideModel = characterModel;
            CharacterFactory.Load();
            World = World.Generate(seed);
            Log = new ReportLog();
            Fog = new FogGrid(World.HalfSize, 128);
            BuildEnvironment();
            foreach (var p in World.Places) BuildPlace(p);

            // ---- brain
            Commander = new Commander("Sir Aldric", Faction.King, commanderTrait, World, World.KingTown, new Knowledge(Faction.King, Fog), Log);
            EnemyCommander = new Commander("The Warlord", Faction.Enemy, Trait.Aggressive, World, World.EnemyTown, null, Log)
            { Silent = true, Raider = true, RaidInterval = raidInterval, RaidTimer = firstRaidDelay, RaidSize = raidersPerWave };
            EnemyCommander.Give(StandingOrder.Strike(World.KingTown));   // the enemy uses the same order system

            // ---- bodies: king and the commander at his post
            var kt = World.KingTown.Center;
            var kingGo = CharacterFactory.Spawn("King", new Vector3(kt.x + 3, 0, kt.z - 11), Quaternion.identity, OutfitKind.King, king: true);
            King = CharacterFactory.AddBody<KingController>(kingGo);
            King.faction = Faction.King;
            var post = Commander.CommandPost;
            var cGo = CharacterFactory.Spawn("Commander " + Commander.Name, new Vector3(post.x, 0, post.z), Quaternion.Euler(0, 160, 0), OutfitKind.Commander);
            CommanderBody = CharacterFactory.AddBody<Unit>(cGo);
            CommanderBody.faction = Faction.King; CommanderBody.invulnerable = true; CommanderBody.vision = 0;
            BuildCommandPost(post);

            // ---- squads: the brain asks for men, the bodies layer makes them
            var first = Commander.AddSquad("First squad", SquadRole.Line, soldiersPerSquad);
            Commander.RequestMen(first, soldiersPerSquad, kt + new V2(-5, 7));
            var second = Commander.AddSquad("Second squad", SquadRole.Line, soldiersPerSquad);
            Commander.RequestMen(second, soldiersPerSquad, kt + new V2(5, 7));
            var riders = Commander.AddSquad("Riders", SquadRole.Riders, 2);
            Commander.RequestMen(riders, 2, post + new V2(3, 0));
            foreach (var p in World.Places.Where(p => p.Owner == Faction.Enemy))
            {
                var g = EnemyCommander.AddSquad("Garrison of " + p.Name, SquadRole.Garrison, p.Kind == PlaceKind.Town ? 4 : 2, p);
                EnemyCommander.RequestMen(g, g.Nominal, p.Center + new V2(0, 2));
            }
            ServeRequests(Commander, announce: false);
            ServeRequests(EnemyCommander, announce: false);

            // ---- camera, fog, UI
            var cam = Camera.main != null ? Camera.main : new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            cam.tag = "MainCamera"; cam.nearClipPlane = 0.05f; cam.farClipPlane = 600f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.55f, 0.65f, 0.75f);
            var sc = cam.GetComponent<ShoulderCamera>() ?? cam.gameObject.AddComponent<ShoulderCamera>();
            sc.target = King.transform; King.cam = cam.transform;
            gameObject.AddComponent<FogView>();
            gameObject.AddComponent<CommandUI>();
            Log.Add(0, $"{Commander.Name} ({Commander.Trait}) stands at the command post. Standing order: Defend {World.KingTown.Name}. Walk up to him and press E, or Tab for a messenger.");
        }

        void Start() => SelfCheck();

        void SelfCheck()
        {
            int soldiers = Unit.All.Count(u => u.faction == Faction.King && u.squad != null);
            var fogView = GetComponent<FogView>();
            string msg = "[Oathmarch] Self-check\n" +
                $"  avatar valid humanoid: {CharacterFactory.AvatarValid}\n" +
                $"  animator controller: {(CharacterFactory.HasController ? "Locomotion.controller" : "MISSING (Playables fallback)")}\n" +
                $"  clips: {CharacterFactory.ClipSummary}\n" +
                $"  fog shader/material ok: {Visuals.FogOk && fogView != null && fogView.MaterialOk}\n" +
                $"  greybox shader ok: {Visuals.LitOk}\n" +
                $"  building colliders: {BuildingColliders}\n" +
                $"  soldiers spawned: {SoldiersSpawned} (in squads now: {soldiers}), enemy bodies spawned: {RaidersSpawned}\n" +
                $"  outfit bones missing: {Outfit.BonesMissing}\n" +
                $"  places: {string.Join(", ", World.Places.Select(p => $"{p.Name} ({p.KindWord}, {p.Owner}, {p.Buildings.Count} bldg)"))}";
            bool ok = CharacterFactory.AvatarValid && Visuals.FogOk && BuildingColliders > 0 && soldiers > 0;
            if (ok) Debug.Log(msg); else Debug.LogError(msg);
        }

        // ---------------------------------------------------------------- requests from the brain
        void ServeRequests(Commander c, bool announce)
        {
            foreach (var r in c.Requests)
            {
                var sq = c.FindSquad(r.SquadId);
                if (r.Kind == RequestKind.Spawn && sq != null)
                {
                    for (int i = 0; i < r.Count; i++)
                    {
                        var outfit = c.Faction == Faction.Enemy ? OutfitKind.Enemy : sq.Role == SquadRole.Riders ? OutfitKind.Rider : OutfitKind.Soldier;
                        var off = new Vector3((i % 3) * 1.4f - 1.4f, 0, (i / 3) * 1.4f);
                        var u = SpawnSoldier(c, sq, new Vector3(r.Pos.x, 0, r.Pos.z) + off, outfit);
                        if (announce && c.Faction == Faction.King)
                            pending.Add(new BrainEvent { Kind = EventKind.Spawned, Time = Time.time, ActorId = u.Id, ActorSquad = sq.Id, ActorFaction = c.Faction, Pos = r.Pos });
                    }
                    if (c.Faction == Faction.Enemy && sq.Role == SquadRole.Raiders)
                        Debug.Log($"[Oathmarch] {sq.Name}: spawned {r.Count} raider bodies at {r.Pos}, target {sq.TargetBuilding?.Label}.");
                }
                else if (r.Kind == RequestKind.Despawn && r.UnitIds != null)
                {
                    foreach (var u in Unit.All.Where(u => r.UnitIds.Contains(u.Id)).ToList()) Destroy(u.gameObject);
                }
            }
            c.Requests.Clear();
        }

        Unit SpawnSoldier(Commander c, Squad sq, Vector3 pos, OutfitKind outfit)
        {
            var go = CharacterFactory.Spawn($"{sq.Name} #{sq.Members.Count + 1}", pos, Quaternion.identity, outfit);
            var u = CharacterFactory.AddBody<Unit>(go);
            u.faction = c.Faction; u.squad = sq; u.formationIndex = NextSlot(sq);
            if (c.Faction == Faction.Enemy) { u.maxHp = u.hp = 30; u.damage = 8; u.vision = 0; RaidersSpawned++; }
            else SoldiersSpawned++;
            if (sq.Role == SquadRole.Riders) { u.walkSpeed = 2.2f; u.runSpeed = 4.4f; u.vision = 30f; }
            c.RegisterMember(sq.Id, u.Id);
            return u;
        }

        static int NextSlot(Squad sq)
        {
            var used = new HashSet<int>(Unit.All.Where(u => u.squad == sq && u.IsAlive).Select(u => u.formationIndex));
            int i = 0; while (used.Contains(i)) i++; return i;
        }

        // ---------------------------------------------------------------- loop
        void Update()
        {
            if (Input.GetKeyDown(KeyCode.R) && (King == null || !King.IsAlive))
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);

            snapshot.Clear();
            foreach (var u in Unit.All) if (u.squad != null || u.IsKing) snapshot.Add(u.Info);

            frameEvents.Clear();
            frameEvents.AddRange(pending); pending.Clear();
            Commander.Tick(Time.deltaTime, Time.time, snapshot, frameEvents);
            EnemyCommander.Tick(Time.deltaTime, Time.time, snapshot, frameEvents);

            // brain-originated events (e.g. a building torched) reach both sides next frame and the visuals now
            foreach (var e in Commander.Emitted.Concat(EnemyCommander.Emitted))
            {
                pending.Add(e);
                if (e.Kind == EventKind.Torched && BuildingObjects.TryGetValue((e.PlaceId, e.BuildingIndex), out var go))
                    go.AddComponent<BuildingFire>();
            }
            ServeRequests(Commander, announce: true);
            ServeRequests(EnemyCommander, announce: false);
        }

        // ---------------------------------------------------------------- greybox
        void BuildEnvironment()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground"; ground.transform.localScale = Vector3.one * (World.HalfSize * 2 / 10f);
            ground.GetComponent<Renderer>().sharedMaterial = Visuals.Lit(new Color(0.35f, 0.45f, 0.25f));
            if (FindAnyObjectByType<Light>() == null)
            {
                var sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional; sun.intensity = 1.1f; sun.shadows = LightShadows.Soft;
                sun.transform.rotation = Quaternion.Euler(50, -30, 0);
            }
            RenderSettings.ambientLight = new Color(0.45f, 0.47f, 0.5f);
            var rng = new System.Random(World.Seed + 7);
            var trunk = Visuals.Lit(new Color(0.35f, 0.25f, 0.15f)); var leaf = Visuals.Lit(new Color(0.2f, 0.38f, 0.18f)); var rock = Visuals.Lit(new Color(0.5f, 0.5f, 0.5f));
            var lm = new GameObject("Landmarks").transform;
            foreach (var p in World.Landmarks)
            {
                var pos = new Vector3(p.x, 0, p.z);
                if (rng.NextDouble() < 0.75)
                {
                    var t = Prim(PrimitiveType.Cylinder, lm, pos + Vector3.up * 1.5f, new Vector3(0.4f, 1.5f, 0.4f), trunk, "Tree");
                    var c = Prim(PrimitiveType.Sphere, t.transform, Vector3.zero, Vector3.one, leaf, "Canopy");
                    c.transform.position = pos + Vector3.up * 4f; c.transform.localScale = new Vector3(7f, 2.2f, 7f);
                    c.transform.SetParent(lm, true);
                    DestroyImmediate(c.GetComponent<Collider>());
                }
                else Prim(PrimitiveType.Sphere, lm, pos, new Vector3(2.5f, 1.6f, 2f) * (float)(0.7 + rng.NextDouble()), rock, "Rock");
            }
        }

        void BuildCommandPost(V2 post)
        {
            var root = new GameObject("Command post").transform;
            var wood = Visuals.Lit(new Color(0.42f, 0.3f, 0.18f)); var cloth = Visuals.Lit(new Color(0.15f, 0.25f, 0.65f));
            var p = new Vector3(post.x, 0, post.z);
            Prim(PrimitiveType.Cylinder, root, p + new Vector3(-1.2f, 2f, 0.8f), new Vector3(0.12f, 2f, 0.12f), wood, "Standard pole");
            Prim(PrimitiveType.Cube, root, p + new Vector3(-0.75f, 3.4f, 0.8f), new Vector3(0.9f, 0.7f, 0.04f), cloth, "Standard");
            Prim(PrimitiveType.Cube, root, p + new Vector3(0.9f, 0.45f, 0.9f), new Vector3(1.4f, 0.9f, 0.8f), wood, "Map table");
        }

        GameObject Prim(PrimitiveType t, Transform parent, Vector3 pos, Vector3 scale, Material m, string n, float yaw = 0)
        {
            var go = GameObject.CreatePrimitive(t);
            go.name = n; go.transform.SetParent(parent, false);
            go.transform.position = pos; go.transform.rotation = Quaternion.Euler(0, yaw, 0); go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = m;
            go.layer = 0;
            return go;
        }

        void BuildPlace(Place p)
        {
            var root = new GameObject($"{p.Name} ({p.KindWord}, {p.Owner})").transform;
            var list = new List<GameObject>();
            var wall = Visuals.Lit(p.Owner == Faction.Enemy ? new Color(0.45f, 0.4f, 0.38f) : new Color(0.75f, 0.68f, 0.55f));
            var roof = Visuals.Lit(p.Owner == Faction.Enemy ? new Color(0.35f, 0.12f, 0.1f) : p.Owner == Faction.King ? new Color(0.2f, 0.25f, 0.5f) : new Color(0.5f, 0.38f, 0.2f));
            var stone = Visuals.Lit(new Color(0.55f, 0.55f, 0.58f)); var wood = Visuals.Lit(new Color(0.42f, 0.3f, 0.18f));
            foreach (var b in p.Buildings)
            {
                var pos = new Vector3(b.Pos.x, 0, b.Pos.z);
                var bt = new GameObject(b.Label).transform; bt.SetParent(root, false); bt.position = pos; bt.rotation = Quaternion.Euler(0, b.Yaw, 0);
                switch (b.Kind)
                {
                    case BuildingKind.Well:
                        Local(PrimitiveType.Cylinder, bt, new Vector3(0, 0.5f, 0), new Vector3(b.SizeX, 0.5f, b.SizeZ), stone);
                        Local(PrimitiveType.Cube, bt, new Vector3(0, 2.0f, 0), new Vector3(b.SizeX + 0.4f, 0.15f, b.SizeZ + 0.4f), roof);
                        Local(PrimitiveType.Cube, bt, new Vector3(b.SizeX / 2, 1.3f, 0), new Vector3(0.12f, 1.4f, 0.12f), wood);
                        Local(PrimitiveType.Cube, bt, new Vector3(-b.SizeX / 2, 1.3f, 0), new Vector3(0.12f, 1.4f, 0.12f), wood);
                        break;
                    case BuildingKind.Palisade:
                        Local(PrimitiveType.Cube, bt, new Vector3(0, b.Height / 2, 0), new Vector3(b.SizeX, b.Height, b.SizeZ), wood);
                        break;
                    case BuildingKind.Tower:
                        Local(PrimitiveType.Cylinder, bt, new Vector3(0, b.Height / 2, 0), new Vector3(b.SizeX, b.Height / 2, b.SizeZ), stone);
                        Roof(bt, b.SizeX + 0.8f, b.SizeZ + 0.8f, b.Height, 2.5f, roof);
                        break;
                    case BuildingKind.Granary:
                        Local(PrimitiveType.Cylinder, bt, new Vector3(0, b.Height / 2, 0), new Vector3(b.SizeX, b.Height / 2, b.SizeX), wood);
                        Roof(bt, b.SizeX + 0.6f, b.SizeX + 0.6f, b.Height, 2f, roof);
                        break;
                    default:
                        Local(PrimitiveType.Cube, bt, new Vector3(0, b.Height / 2, 0), new Vector3(b.SizeX, b.Height, b.SizeZ), b.Kind == BuildingKind.Blacksmith ? stone : wall);
                        Roof(bt, b.SizeX + 0.5f, b.SizeZ + 0.5f, b.Height, b.Kind == BuildingKind.TownCenter ? 3f : 1.6f, roof);
                        if (b.Kind == BuildingKind.Blacksmith) Local(PrimitiveType.Cube, bt, new Vector3(b.SizeX * 0.3f, b.Height + 1.5f, 0), new Vector3(0.6f, 2f, 0.6f), stone);
                        if (b.Kind == BuildingKind.TownCenter) Local(PrimitiveType.Cube, bt, new Vector3(0, b.Height + 4f, 0), new Vector3(0.15f, 3f, 0.15f), wood);
                        break;
                }
                foreach (var col in bt.GetComponentsInChildren<Collider>()) { col.gameObject.layer = 0; BuildingColliders++; }
                list.Add(bt.gameObject);
                BuildingObjects[(p.Id, b.Index)] = bt.gameObject;
            }
            PlaceObjects[p.Id] = list;
        }

        static GameObject Local(PrimitiveType t, Transform parent, Vector3 lp, Vector3 s, Material m)
        {
            var go = GameObject.CreatePrimitive(t);   // primitives keep their collider: camera + steering collide with buildings
            go.transform.SetParent(parent, false); go.transform.localPosition = lp; go.transform.localScale = s;
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go;
        }

        /// <summary>A gable roof from two tilted slabs (with colliders, so the camera cannot enter roofs).</summary>
        static void Roof(Transform parent, float sx, float sz, float baseY, float rise, Material m)
        {
            float half = sz / 2f; float slope = Mathf.Sqrt(half * half + rise * rise); float ang = Mathf.Atan2(rise, half) * Mathf.Rad2Deg;
            for (int s = -1; s <= 1; s += 2)
            {
                var go = Local(PrimitiveType.Cube, parent, new Vector3(0, baseY + rise / 2f, s * half / 2f), new Vector3(sx, 0.25f, slope), m);
                go.transform.localRotation = Quaternion.Euler(s * ang, 0, 0);
            }
            // gable fill so the roof is a closed volume
            var fill = Local(PrimitiveType.Cube, parent, new Vector3(0, baseY + rise * 0.3f, 0), new Vector3(sx - 0.3f, rise * 0.6f, sz * 0.55f), m);
        }
    }

    /// <summary>Simple torched-building visual: flickering unlit flames + light for a while, then charred.</summary>
    public class BuildingFire : MonoBehaviour
    {
        readonly List<Transform> flames = new List<Transform>();
        Light glow; float t;

        void Start()
        {
            var b = new Bounds(transform.position, Vector3.one);
            foreach (var r in GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            var rng = new System.Random(GetInstanceID());
            for (int i = 0; i < 9; i++)
            {
                var f = GameObject.CreatePrimitive(PrimitiveType.Cube);
                DestroyImmediate(f.GetComponent<Collider>());
                f.name = "Flame";
                f.transform.SetParent(transform, true);
                f.transform.position = new Vector3(b.center.x + ((float)rng.NextDouble() - 0.5f) * b.size.x * 0.8f, b.max.y * (0.5f + 0.5f * (float)rng.NextDouble()), b.center.z + ((float)rng.NextDouble() - 0.5f) * b.size.z * 0.8f);
                f.GetComponent<Renderer>().sharedMaterial = Visuals.Unlit(i % 2 == 0 ? new Color(1f, 0.45f, 0.05f) : new Color(1f, 0.85f, 0.2f));
                flames.Add(f.transform);
            }
            glow = new GameObject("FireLight").AddComponent<Light>();
            glow.transform.SetParent(transform, false); glow.transform.position = b.center + Vector3.up * b.extents.y;
            glow.type = LightType.Point; glow.color = new Color(1f, 0.55f, 0.2f); glow.range = 14f; glow.intensity = 2.5f;
            var mpb = new MaterialPropertyBlock();
            foreach (var r in GetComponentsInChildren<Renderer>())
                if (r.name != "Flame") { mpb.SetColor("_Color", new Color(0.12f, 0.1f, 0.09f)); r.SetPropertyBlock(mpb); }
        }

        void Update()
        {
            t += Time.deltaTime;
            bool burning = t < 40f;
            for (int i = 0; i < flames.Count; i++)
            {
                flames[i].gameObject.SetActive(burning);
                float s = 0.6f + Mathf.PerlinNoise(t * 3f, i) * 1.2f;
                flames[i].localScale = new Vector3(0.6f, s * 1.6f, 0.6f);
                flames[i].rotation = Quaternion.Euler(0, t * 90f + i * 40f, 0);
            }
            if (glow) { glow.enabled = burning; glow.intensity = 2f + Mathf.PerlinNoise(t * 5f, 0) * 1.5f; }
        }
    }
}
