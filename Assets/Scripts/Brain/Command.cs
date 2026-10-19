using System;
using System.Collections.Generic;
using System.Linq;

namespace Oathmarch.Brain
{
    public enum OrderKind { Defend, Strike }

    /// <summary>A standing order. It persists until the king gives another.</summary>
    public sealed class StandingOrder
    {
        public OrderKind Kind;
        public int TargetPlaceId = -1;   // Strike names a settlement, never a map point.
        public static StandingOrder Defend() => new StandingOrder { Kind = OrderKind.Defend };
        public static StandingOrder Strike(Place p) => new StandingOrder { Kind = OrderKind.Strike, TargetPlaceId = p.Id };
    }

    public enum SquadIntent { Patrol, Intercept, Return, Stage, Reserve, Flank, Assault, Hold }

    public sealed class Squad
    {
        public int Id;
        public string Name;
        public Faction Faction;
        public bool Garrison;              // holds its place, ignores the standing order
        public int HomePlaceId = -1;
        public readonly List<int> Members = new List<int>();
        public SquadIntent Intent = SquadIntent.Patrol;
        public V2 Destination;              // the point the bodies walk to
        public bool Run;                    // bodies should run
        public float AggroRadius = 9f;      // bodies engage hostiles inside this radius on contact
        internal List<V2> Route = new List<V2>();
        internal int RouteIndex;
        internal float Timer;
        internal V2 EngagePoint;
        // filled each tick
        public int Alive;
        public V2 Centroid;
    }

    /// <summary>
    /// A brigade commander. Takes a standing order, applies his trait, assigns squad intents
    /// and destinations, and writes reports. The enemy commander uses the exact same class.
    /// </summary>
    public sealed class Commander
    {
        public readonly string Name;
        public readonly Faction Faction;
        public readonly Trait Trait;
        public readonly World World;
        public readonly Knowledge Knowledge;
        public readonly ReportLog Log;
        public readonly List<Squad> Squads = new List<Squad>();
        public StandingOrder Order { get; private set; } = StandingOrder.Defend();
        public Place Home;
        public float DefendRadius = 55f;
        public bool Silent;
        public bool PersistentStrike;   // never gives up / never stops (enemy raider host)
        float time;
        float strikeEngagedTimer, strikeClearTimer;
        static int nextSquadId = 1;

        public Commander(string name, Faction f, Trait t, World w, Place home, Knowledge k, ReportLog log)
        { Name = name; Faction = f; Trait = t; World = w; Home = home; Knowledge = k; Log = log; }

        public Squad AddSquad(string name, bool garrison = false, Place home = null)
        {
            var s = new Squad { Id = nextSquadId++, Name = name, Faction = Faction, Garrison = garrison, HomePlaceId = (home ?? Home).Id };
            s.Destination = (home ?? Home).Center;
            Squads.Add(s);
            if (!garrison) EnterDefend(s, Squads.Count(q => !q.Garrison) - 1);
            else { s.Intent = SquadIntent.Hold; s.AggroRadius = 14f; }
            if (!garrison && Order.Kind == OrderKind.Strike) { var t = World.Get(Order.TargetPlaceId); if (t != null) PlanStrike(t); }
            return s;
        }

        void Say(string text) { if (!Silent) Log?.Add(time, text); }

        /// <summary>Strike can only name a settlement someone has reported.</summary>
        public bool CanStrike(Place p) => p != null && p.Owner != Faction && (Knowledge == null || Knowledge.Knows(p)) && !p.Raided;

        public bool Give(StandingOrder order)
        {
            if (order.Kind == OrderKind.Strike)
            {
                var p = World.Get(order.TargetPlaceId);
                if (!CanStrike(p)) { Say("No one has reported such a place, my king."); return false; }
                Order = order; strikeEngagedTimer = 0; strikeClearTimer = 0;
                string plan = p.Kind == PlaceKind.Hamlet ? "a raid" : Trait == Trait.Cautious ? "an assault, holding a reserve and turning their flank" : "an assault, straight in";
                Say($"{Name}: We march on {p.Name}. It will be {plan}.");
                PlanStrike(p);
            }
            else
            {
                Order = order;
                Say($"{Name}: We hold the watch over {Home.Name}.");
                int i = 0; foreach (var s in Squads) if (!s.Garrison) EnterDefend(s, i++);
            }
            return true;
        }

        // ---------------------------------------------------------------- tick
        public void Tick(float dt, float now, IReadOnlyList<UnitInfo> units)
        {
            time = now;
            var byId = new Dictionary<int, UnitInfo>();
            foreach (var u in units) byId[u.Id] = u;
            foreach (var s in Squads)
            {
                s.Members.RemoveAll(id => !byId.TryGetValue(id, out var u) || !u.Alive);
                s.Alive = s.Members.Count;
                V2 c = new V2(0, 0);
                foreach (var id in s.Members) c = c + byId[id].Pos;
                s.Centroid = s.Alive > 0 ? c * (1f / s.Alive) : s.Destination;
            }
            Squads.RemoveAll(s => s.Alive == 0 && s.Garrison == false && s.Name.StartsWith("Raiders"));

            if (Knowledge != null)
                foreach (var p in Knowledge.Refresh(World))
                    if (p.Owner != Faction)
                        Say($"Report: {(p.Owner == Faction.Neutral ? "a quiet" : "an enemy")} {p.KindWord}, {p.Name}, {DirectionWord(Home.Center, p.Center)} of {Home.Name}.");

            var hostiles = units.Where(u => u.Alive && u.Faction != Faction && u.Faction != Faction.Neutral).ToList();

            foreach (var s in Squads.Where(q => q.Garrison)) TickGarrison(s, hostiles);

            if (Order.Kind == OrderKind.Defend) TickDefend(dt, hostiles);
            else TickStrike(dt, hostiles);
        }

        // ---------------------------------------------------------------- defend
        void EnterDefend(Squad s, int index)
        {
            s.Intent = SquadIntent.Patrol; s.Run = false; s.AggroRadius = 9f;
            s.Route.Clear(); s.RouteIndex = 0;
            if (index % 2 == 0)
            {   // the town watch: walk between the buildings
                foreach (var b in Home.Buildings) s.Route.Add(b.Pos + (b.Pos - Home.Center).Normalized * 4f);
            }
            else
            {   // the country patrol: a wide ring around home
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4 + index;
                    s.Route.Add(Home.Center + new V2((float)Math.Cos(a), (float)Math.Sin(a)) * (Home.Radius + 22f));
                }
            }
            if (s.Route.Count == 0) s.Route.Add(Home.Center);
            s.Destination = s.Route[0];
        }

        void TickDefend(float dt, List<UnitInfo> hostiles)
        {
            var threats = hostiles.Where(h => V2.Dist(h.Pos, Home.Center) < DefendRadius && h.SquadId >= 0).ToList();
            var field = Squads.Where(s => !s.Garrison && s.Alive > 0).ToList();
            int idx = 0;
            foreach (var s in field)
            {
                int myIndex = idx++;
                switch (s.Intent)
                {
                    case SquadIntent.Patrol:
                    case SquadIntent.Return:
                        if (threats.Count > 0 && ShouldIntercept(s, field, threats))
                        {
                            var t = Nearest(threats, s.Centroid);
                            s.Intent = SquadIntent.Intercept; s.Run = true; s.AggroRadius = 14f;
                            s.EngagePoint = t.Pos;
                            Say($"{s.Name} rides to head off raiders near the {Home.NearestBuilding(t.Pos)?.Label ?? Home.Name}.");
                        }
                        else if (s.Intent == SquadIntent.Return && V2.Dist(s.Centroid, s.Destination) < 6f) EnterDefend(s, myIndex);
                        else if (s.Intent == SquadIntent.Patrol && V2.Dist(s.Centroid, s.Destination) < 4f)
                        {
                            s.RouteIndex = (s.RouteIndex + 1) % s.Route.Count;
                            s.Destination = s.Route[s.RouteIndex];
                        }
                        break;
                    case SquadIntent.Intercept:
                        if (threats.Count == 0)
                        {
                            Say($"{s.Name} turned back raiders near the {Home.NearestBuilding(s.EngagePoint)?.Label ?? Home.Name}. Back to the watch.");
                            s.Intent = SquadIntent.Return; s.Run = false; s.AggroRadius = 9f;
                            s.Destination = s.Route.Count > 0 ? s.Route[s.RouteIndex] : Home.Center;
                        }
                        else
                        {
                            var t = Nearest(threats, s.Centroid);
                            s.Destination = t.Pos; s.EngagePoint = t.Pos;
                        }
                        break;
                    default:
                        EnterDefend(s, myIndex); break;
                }
            }
        }

        bool ShouldIntercept(Squad s, List<Squad> field, List<UnitInfo> threats)
        {
            // Nearest free squad goes; if the raiders outnumber it, everybody goes.
            int interceptors = field.Count(q => q.Intent == SquadIntent.Intercept);
            int strength = field.Where(q => q.Intent == SquadIntent.Intercept).Sum(q => q.Alive);
            if (interceptors > 0 && strength >= threats.Count) return false;
            var c = Centroid(threats);
            var free = field.Where(q => q.Intent != SquadIntent.Intercept).OrderBy(q => V2.Dist(q.Centroid, c)).FirstOrDefault();
            return free == s;
        }

        // ---------------------------------------------------------------- strike
        void PlanStrike(Place target)
        {
            var field = Squads.Where(s => !s.Garrison).ToList();
            V2 dir = (target.Center - Home.Center).Normalized;
            V2 staging = target.Center - dir * (target.Radius + 18f);
            for (int i = 0; i < field.Count; i++)
            {
                var s = field[i]; s.Route.Clear(); s.Run = false; s.AggroRadius = 12f;
                bool aggressive = Trait == Trait.Aggressive || Trait == Trait.ScoutMinded || target.Kind == PlaceKind.Hamlet && Trait != Trait.Cautious;
                if (aggressive)
                {   // Aggressive: everyone goes straight in.
                    s.Intent = SquadIntent.Assault; s.Destination = target.Center; s.Run = true;
                }
                else if (i == 0)
                {   // Cautious: first squad swings wide around the flank.
                    s.Intent = SquadIntent.Flank;
                    s.Destination = target.Center + dir.Perp * (target.Radius + 16f);
                }
                else
                {   // Cautious: the rest are the reserve, holding at the staging point.
                    s.Intent = SquadIntent.Reserve; s.Destination = staging;
                }
            }
        }

        void TickStrike(float dt, List<UnitInfo> hostiles)
        {
            var target = World.Get(Order.TargetPlaceId);
            var field = Squads.Where(s => !s.Garrison && s.Alive > 0).ToList();
            if (PersistentStrike && target != null && field.Count == 0) return;
            if (target == null || field.Count == 0)
            {
                Say($"{Name}: The strike has failed; what is left of us falls back to {Home.Name}.");
                Order = StandingOrder.Defend(); int k = 0; foreach (var s in Squads) if (!s.Garrison) EnterDefend(s, k++);
                return;
            }
            var defenders = hostiles.Where(h => V2.Dist(h.Pos, target.Center) < target.Radius + 10f).ToList();
            foreach (var s in field)
            {
                switch (s.Intent)
                {
                    case SquadIntent.Flank:
                        if (V2.Dist(s.Centroid, s.Destination) < 6f)
                        {
                            s.Intent = SquadIntent.Assault; s.Destination = target.Center; s.Run = true;
                            Say($"{s.Name} has turned their flank at {target.Name} and goes in.");
                        }
                        break;
                    case SquadIntent.Reserve:
                        bool flankIn = field.Any(q => q.Intent == SquadIntent.Assault);
                        if (flankIn) { strikeEngagedTimer += dt; }
                        if (flankIn && strikeEngagedTimer > 6f)
                        {
                            s.Intent = SquadIntent.Assault; s.Destination = target.Center; s.Run = true;
                            Say($"The reserve, {s.Name}, is committed at {target.Name}.");
                        }
                        break;
                    case SquadIntent.Assault:
                        s.Destination = defenders.Count > 0 ? Nearest(defenders, s.Centroid).Pos : target.Center;
                        break;
                    default:
                        s.Intent = SquadIntent.Assault; s.Destination = target.Center; break;
                }
            }
            bool inside = field.Any(s => s.Intent == SquadIntent.Assault && V2.Dist(s.Centroid, target.Center) < target.Radius);
            if (inside && defenders.Count == 0) strikeClearTimer += dt; else strikeClearTimer = 0;
            if (strikeClearTimer > 3f && !PersistentStrike)
            {
                if (target.Kind == PlaceKind.Hamlet)
                {
                    target.Raided = true;
                    Say($"{Name}: {target.Name} is raided. The hamlet is put to the torch. We return to the watch.");
                }
                else
                {
                    target.Owner = Faction;
                    Say($"{Name}: {target.Name} is taken! We return to the watch over {Home.Name}.");
                }
                Order = StandingOrder.Defend(); int k = 0; foreach (var s in Squads) if (!s.Garrison) EnterDefend(s, k++);
            }
        }

        // ---------------------------------------------------------------- garrison
        void TickGarrison(Squad s, List<UnitInfo> hostiles)
        {
            var home = World.Get(s.HomePlaceId) ?? Home;
            var near = hostiles.Where(h => V2.Dist(h.Pos, home.Center) < home.Radius + 12f).ToList();
            s.Destination = near.Count > 0 ? Nearest(near, s.Centroid).Pos : home.Center;
            s.Run = near.Count > 0;
        }

        // ---------------------------------------------------------------- helpers
        static UnitInfo Nearest(List<UnitInfo> list, V2 p) => list.OrderBy(u => V2.Dist(u.Pos, p)).First();
        static V2 Centroid(List<UnitInfo> list) { V2 c = new V2(0, 0); foreach (var u in list) c = c + u.Pos; return list.Count > 0 ? c * (1f / list.Count) : c; }

        public static string DirectionWord(V2 from, V2 to)
        {
            var d = to - from;
            double ang = Math.Atan2(d.x, d.z) * 180 / Math.PI; if (ang < 0) ang += 360;
            string[] dirs = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
            return dirs[(int)Math.Round(ang / 45) % 8];
        }
    }
}
