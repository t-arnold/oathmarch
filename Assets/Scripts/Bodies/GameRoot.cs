using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Oathmarch.Brain;

namespace Oathmarch.Bodies
{
    /// <summary>
    /// Assembles the slice at runtime (so Play works in an empty scene) and runs the loop:
    /// bodies -> snapshot -> brain (commanders) -> bodies read squad destinations.
    /// </summary>
    public class GameRoot : MonoBehaviour
    {
        public static GameRoot Instance { get; private set; }

        [Header("Country")] public int seed = 1187;
        [Header("Player commander")] public Trait commanderTrait = Trait.Cautious;
        public int soldiersPerSquad = 4;
        [Header("Enemy")] public float raidInterval = 50f, firstRaidDelay = 25f; public int raidersPerWave = 3;
        [Header("Optional: your own humanoid model (Rig = Humanoid)")] public GameObject characterModel;

        public World World { get; private set; }
        public FogGrid Fog { get; private set; }
        public Commander Commander { get; private set; }
        public Commander EnemyCommander { get; private set; }
        public ReportLog Log { get; private set; }
        public KingController King { get; private set; }
        public readonly Dictionary<int, List<GameObject>> PlaceObjects = new Dictionary<int, List<GameObject>>();

        float raidTimer; int raidCount;
        readonly List<UnitInfo> snapshot = new List<UnitInfo>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBootstrap()
        {
            if (FindAnyObjectByType<GameRoot>() == null) new GameObject("Oathmarch").AddComponent<GameRoot>();
        }

        void Awake()
        {
            Instance = this;
            Unit.All.Clear();
            if (characterModel != null) CharacterFactory.OverrideModel = characterModel;
            World = World.Generate(seed);
            Log = new ReportLog();
            Fog = new FogGrid(World.HalfSize, 128);
            BuildEnvironment();
            foreach (var p in World.Places) BuildPlace(p);

            // ---- brain
            Commander = new Commander("Sir Aldric", Faction.King, commanderTrait, World, World.KingTown, new Knowledge(Faction.King, Fog), Log);
            EnemyCommander = new Commander("The Warlord", Faction.Enemy, Trait.Aggressive, World, World.EnemyTown, null, Log)
            { Silent = true, PersistentStrike = true };
            EnemyCommander.Give(StandingOrder.Strike(World.KingTown));   // the enemy uses the same order system

            // ---- bodies
            var kt = World.KingTown.Center;
            var kingGo = CharacterFactory.Spawn("King", new Vector3(kt.x + 3, 0, kt.z - 9), Quaternion.identity, OutfitKind.King, king: true);
            King = CharacterFactory.AddBody<KingController>(kingGo);
            King.faction = Faction.King;

            string[] names = { "First squad", "Second squad" };
            for (int s = 0; s < 2; s++)
            {
                var sq = Commander.AddSquad(names[s]);
                for (int i = 0; i < soldiersPerSquad; i++) SpawnSoldier(sq, i, new Vector3(kt.x - 4 + s * 8, 0, kt.z + 6 + i), OutfitKind.Soldier, Faction.King);
            }
            foreach (var p in World.Places.Where(p => p.Owner == Faction.Enemy))
            {
                var g = EnemyCommander.AddSquad("Garrison of " + p.Name, garrison: true, home: p);
                for (int i = 0; i < (p.Kind == PlaceKind.Town ? 4 : 2); i++)
                    SpawnSoldier(g, i, new Vector3(p.Center.x + i, 0, p.Center.z + 2), OutfitKind.Enemy, Faction.Enemy);
            }
            raidTimer = firstRaidDelay;

            // ---- camera, fog, UI
            var cam = Camera.main != null ? Camera.main : new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            cam.tag = "MainCamera"; cam.nearClipPlane = 0.1f; cam.farClipPlane = 600f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.55f, 0.65f, 0.75f);
            var sc = cam.GetComponent<ShoulderCamera>() ?? cam.gameObject.AddComponent<ShoulderCamera>();
            sc.target = King.transform; King.cam = cam.transform;
            gameObject.AddComponent<FogView>();
            gameObject.AddComponent<CommandUI>();
            Log.Add(0, $"{Commander.Name} ({Commander.Trait}) stands ready. Standing order: Defend {World.KingTown.Name}. Press Tab for orders.");
        }

        Unit SpawnSoldier(Squad sq, int index, Vector3 pos, OutfitKind outfit, Faction f)
        {
            var go = CharacterFactory.Spawn($"{sq.Name} #{index + 1}", pos, Quaternion.identity, outfit);
            var u = CharacterFactory.AddBody<Unit>(go);
            u.faction = f; u.squad = sq; u.formationIndex = index;
            if (f == Faction.Enemy) { u.maxHp = u.hp = 30; u.damage = 8; }
            sq.Members.Add(u.Id);
            return u;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.R) && (King == null || !King.IsAlive))
                UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);

            // enemy host: periodic raiders from the enemy town, folded into the enemy's Strike order
            raidTimer -= Time.deltaTime;
            if (raidTimer <= 0 && World.EnemyTown.Owner == Faction.Enemy)
            {
                raidTimer = raidInterval; raidCount++;
                var sq = EnemyCommander.AddSquad("Raiders " + raidCount);
                var c = World.EnemyTown.Center; var dir = (World.KingTown.Center - c).Normalized;
                for (int i = 0; i < raidersPerWave; i++)
                    SpawnSoldier(sq, i, new Vector3(c.x + dir.x * (World.EnemyTown.Radius + 3) + i, 0, c.z + dir.z * (World.EnemyTown.Radius + 3)), OutfitKind.Enemy, Faction.Enemy);
            }

            snapshot.Clear();
            foreach (var u in Unit.All) snapshot.Add(u.Info);
            Commander.Tick(Time.deltaTime, Time.time, snapshot);
            EnemyCommander.Tick(Time.deltaTime, Time.time, snapshot);
        }

        // ------------------------------------------------------------ greybox
        static Material Mat(Color c)
        {
            var sh = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit");
            return new Material(sh) { color = c };
        }

        void BuildEnvironment()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground"; ground.transform.localScale = Vector3.one * (World.HalfSize * 2 / 10f);
            ground.GetComponent<Renderer>().sharedMaterial = Mat(new Color(0.35f, 0.45f, 0.25f));
            if (FindAnyObjectByType<Light>() == null)
            {
                var sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional; sun.intensity = 1.1f; sun.shadows = LightShadows.Soft;
                sun.transform.rotation = Quaternion.Euler(50, -30, 0);
            }
            RenderSettings.ambientLight = new Color(0.45f, 0.47f, 0.5f);
            var rng = new System.Random(World.Seed + 7);
            var trunk = Mat(new Color(0.35f, 0.25f, 0.15f)); var leaf = Mat(new Color(0.2f, 0.38f, 0.18f)); var rock = Mat(new Color(0.5f, 0.5f, 0.5f));
            var lm = new GameObject("Landmarks").transform;
            foreach (var p in World.Landmarks)
            {
                var pos = new Vector3(p.x, 0, p.z);
                if (rng.NextDouble() < 0.75)
                {
                    var t = Prim(PrimitiveType.Cylinder, lm, pos + Vector3.up * 1.5f, new Vector3(0.4f, 1.5f, 0.4f), trunk, "Tree");
                    var c = Prim(PrimitiveType.Sphere, t.transform, Vector3.zero, Vector3.one, leaf, "Canopy");
                    c.transform.position = pos + Vector3.up * 4f; c.transform.localScale = new Vector3(7f, 2.2f, 7f);
                    Destroy(c.GetComponent<Collider>());
                }
                else Prim(PrimitiveType.Sphere, lm, pos, new Vector3(2.5f, 1.6f, 2f) * (float)(0.7 + rng.NextDouble()), rock, "Rock");
            }
        }

        static GameObject Prim(PrimitiveType t, Transform parent, Vector3 pos, Vector3 scale, Material m, string n, float yaw = 0)
        {
            var go = GameObject.CreatePrimitive(t);
            go.name = n; go.transform.SetParent(parent, false);
            go.transform.position = pos; go.transform.rotation = Quaternion.Euler(0, yaw, 0); go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go;
        }

        void BuildPlace(Place p)
        {
            var root = new GameObject($"{p.Name} ({p.KindWord}, {p.Owner})").transform;
            var list = new List<GameObject>();
            var wall = Mat(p.Owner == Faction.Enemy ? new Color(0.45f, 0.4f, 0.38f) : new Color(0.75f, 0.68f, 0.55f));
            var roof = Mat(p.Owner == Faction.Enemy ? new Color(0.35f, 0.12f, 0.1f) : p.Owner == Faction.King ? new Color(0.2f, 0.25f, 0.5f) : new Color(0.5f, 0.38f, 0.2f));
            var stone = Mat(new Color(0.55f, 0.55f, 0.58f)); var wood = Mat(new Color(0.42f, 0.3f, 0.18f));
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
                list.Add(bt.gameObject);
            }
            PlaceObjects[p.Id] = list;
        }

        static GameObject Local(PrimitiveType t, Transform parent, Vector3 lp, Vector3 s, Material m)
        {
            var go = GameObject.CreatePrimitive(t);
            go.transform.SetParent(parent, false); go.transform.localPosition = lp; go.transform.localScale = s;
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go;
        }

        /// <summary>A gable roof from two tilted slabs.</summary>
        static void Roof(Transform parent, float sx, float sz, float baseY, float rise, Material m)
        {
            float half = sz / 2f; float slope = Mathf.Sqrt(half * half + rise * rise); float ang = Mathf.Atan2(rise, half) * Mathf.Rad2Deg;
            for (int s = -1; s <= 1; s += 2)
            {
                var go = Local(PrimitiveType.Cube, parent, new Vector3(0, baseY + rise / 2f, s * half / 2f), new Vector3(sx, 0.15f, slope), m);
                go.transform.localRotation = Quaternion.Euler(s * ang, 0, 0);
            }
        }
    }
}
