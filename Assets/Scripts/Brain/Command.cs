using System;
using System.Collections.Generic;
using System.Linq;

namespace Oathmarch.Brain
{
    public enum OrderKind { Defend, Strike, Survey }

    /// <summary>A standing order. It persists until the king gives another.</summary>
    public sealed class StandingOrder
    {
        public OrderKind Kind;
        public int TargetPlaceId = -1;   // Strike names a settlement, never a map point.
        public static StandingOrder Defend() => new StandingOrder { Kind = OrderKind.Defend };
        public static StandingOrder Survey() => new StandingOrder { Kind = OrderKind.Survey };
        public static StandingOrder Strike(Place p) => new StandingOrder { Kind = OrderKind.Strike, TargetPlaceId = p.Id };
    }

    public enum SquadRole { Line, Riders, Garrison, Raiders }

    public enum SquadState
    {
        Patrol, Intercept, Pursue, Engage, Regroup, Return, FallBack,   // defence
        Stage, Flank, Reserve, Assault,                                // strike
        Survey, Hold,                                                  // riders / garrison
        Raid, Torch, Flee                                              // raiders
    }

    /// <summary>A fight a squad is in. Filled only from events bodies actually produced.</summary>
    public sealed class Engagement
    {
        public readonly HashSet<int> Targets = new HashSet<int>();     // enemy squad ids
        public readonly HashSet<int> SeenEnemyIds = new HashSet<int>();
        public readonly HashSet<int> KilledIds = new HashSet<int>();
        public bool Contact;          // at least one blow was struck between us and them
        public int Losses;
        public V2 Where;              // where it started
        public float LastSeen;
        public V2 LastSeenPos;
        public bool ReinforceSent;
    }

    public sealed class Squad
    {
        public int Id;
        public string Name;
        public Faction Faction;
        public SquadRole Role;
        public int Nominal;
        public int HomePlaceId = -1;
        public readonly List<int> Members = new List<int>();
        public SquadState State = SquadState.Patrol;
        public V2 Destination;              // the anchor the bodies form up around
        public V2 Heading = new V2(0, 1);   // formation facing
        public bool Run;
        public float AggroRadius = 9f;      // bodies engage hostiles inside this radius on contact
        public Engagement Engagement;
        public Building TargetBuilding;     // raiders: what they came to burn
        public int TargetPlaceId = -1;
        internal readonly List<V2> Route = new List<V2>();
        internal int RouteIndex;
        internal float Timer;
        internal bool Spawned;
        internal int LegDir = -1, LegFound;
        internal bool ReportedWiped;
        // filled each tick from bodies
        public int Alive;
        public V2 Centroid;
        public float Strength => Nominal > 0 ? Alive / (float)Nominal : 0f;
        public bool InFight => Engagement != null;
    }

    /// <summary>
    /// A brigade commander. Takes a standing order, applies his trait, runs a state machine per squad,
    /// and writes reports built only from what the bodies actually did (events + positions).
    /// The enemy warlord is the same class with a standing Strike on the king's town.
    /// </summary>
    public sealed class Commander
    {
        readonly HashSet<int> killedAnywhere = new HashSet<int>();
        public readonly string Name;
        public readonly Faction Faction;
        public readonly Trait Trait;
        public readonly World World;
        public readonly Knowledge Knowledge;   // null = omniscient (enemy AI)
        public readonly ReportLog Log;
        public readonly List<Squad> Squads = new List<Squad>();
        public readonly List<BodyRequest> Requests = new List<BodyRequest>();
        public readonly List<BrainEvent> Emitted = new List<BrainEvent>();
        public StandingOrder Order { get; private set; } = StandingOrder.Defend();
        public Place Home;
        public V2 CommandPost;
        public float DefendRadius = 60f, Leash = 95f;
        public bool Silent;                    // enemy commander does not write to the king's log
        public bool Raider;                    // Strike on a town is carried out as repeated raids
        public float RaidInterval = 55f, RaidTimer = 20f; public int RaidSize = 4;
        public float RecruitInterval = 18f;
        public int RaidsSent, RaidersSpawnedRequested;
        float recruitTimer = 18f, time;
        int strikeKills, strikeLosses; float strikeClearTimer;
        readonly HashSet<int> surveyedDirs = new HashSet<int>();
        static int nextSquadId = 1;
        readonly Random rng;

        public Commander(string name, Faction f, Trait t, World w, Place home, Knowledge k, ReportLog log)
        {
            Name = name; Faction = f; Trait = t; World = w; Home = home; Knowledge = k; Log = log;
            CommandPost = home.Center + new V2(-6, -6);
            rng = new Random(w.Seed * 31 + (int)f);
        }

        // ================================================================ setup
        public Squad AddSquad(string name, SquadRole role, int nominal, Place home = null)
        {
            var h = home ?? Home;
            var s = new Squad { Id = nextSquadId++, Name = name, Faction = Faction, Role = role, Nominal = nominal, HomePlaceId = h.Id, Destination = h.Center, Centroid = h.Center };
            Squads.Add(s);
            switch (role)
            {
                case SquadRole.Line: EnterPatrol(s); break;
                case SquadRole.Riders: s.State = SquadState.Hold; s.Destination = CommandPost + new V2(3, 0); s.AggroRadius = 4f; break;
                case SquadRole.Garrison: s.State = SquadState.Hold; s.AggroRadius = 14f; break;
                case SquadRole.Raiders: s.State = SquadState.Raid; s.AggroRadius = 6f; break;
            }
            return s;
        }

        /// <summary>Ask the bodies for men. The squad counts them only once they exist.</summary>
        public void RequestMen(Squad s, int count, V2 at) =>
            Requests.Add(new BodyRequest { Kind = RequestKind.Spawn, SquadId = s.Id, Count = count, Pos = at });

        /// <summary>Called by the bodies layer the moment a body exists.</summary>
        public void RegisterMember(int squadId, int unitId)
        {
            var s = Squads.Find(q => q.Id == squadId);
            if (s == null) return;
            if (!s.Members.Contains(unitId)) s.Members.Add(unitId);
            s.Alive = s.Members.Count; s.Spawned = true;
        }

        public Squad FindSquad(int id) => Squads.Find(q => q.Id == id);

        void Say(string text) { if (!Silent) Log?.Add(time, $"{Name}: {text}"); }

        // ================================================================ orders
        public bool CanStrike(Place p) => p != null && p.Owner != Faction && (Knowledge == null || Knowledge.Knows(p)) && !p.Raided;

        public bool Give(StandingOrder order)
        {
            switch (order.Kind)
            {
                case OrderKind.Strike:
                    var p = World.Get(order.TargetPlaceId);
                    if (!CanStrike(p)) { Say("No one has reported such a place, my king."); return false; }
                    Order = order; strikeKills = 0; strikeLosses = 0; strikeClearTimer = 0;
                    if (Raider) { Say($"We raid {p.Name}."); return true; }
                    string plan = p.Kind == PlaceKind.Hamlet ? "a raid" : Trait == Trait.Cautious ? "an assault, holding a reserve and turning their flank" : "an assault, straight in";
                    Say($"We march on {p.Name}. It will be {plan}.");
                    PlanStrike(p);
                    return true;
                case OrderKind.Survey:
                    Order = order; surveyedDirs.Clear();
                    Say($"Riders will survey the country beyond {Home.Name}. The watch stays up.");
                    foreach (var s in Squads.Where(q => q.Role == SquadRole.Line && IsStrikeState(q.State))) EnterPatrol(s);
                    return true;
                default:
                    Order = order;
                    Say($"We hold the watch over {Home.Name}.");
                    foreach (var s in Squads.Where(q => q.Role == SquadRole.Line && q.State != SquadState.FallBack && !q.InFight)) EnterPatrol(s);
                    return true;
            }
        }

        static bool IsStrikeState(SquadState st) => st == SquadState.Stage || st == SquadState.Flank || st == SquadState.Reserve || st == SquadState.Assault;

        // ================================================================ tick
        Dictionary<int, UnitInfo> byId = new Dictionary<int, UnitInfo>();

        public void Tick(float dt, float now, IReadOnlyList<UnitInfo> units, IReadOnlyList<BrainEvent> events)
        {
            time = now;
            Emitted.Clear();
            byId.Clear();
            foreach (var u in units) byId[u.Id] = u;

            foreach (var s in Squads)
            {
                s.Members.RemoveAll(id => !byId.TryGetValue(id, out var u) || !u.Alive);
                s.Alive = s.Members.Count;
                if (s.Alive > 0)
                {
                    s.Spawned = true;
                    V2 c = new V2(0, 0);
                    foreach (var id in s.Members) c = c + byId[id].Pos;
                    s.Centroid = c * (1f / s.Alive);
                }
            }

            if (events != null) foreach (var e in events) OnEvent(e);

            if (Knowledge != null)
                foreach (var p in Knowledge.Refresh(World))
                    if (p.Owner != Faction) ReportDiscovery(p);

            var hostiles = units.Where(u => u.Alive && Hostile(u.Faction)).ToList();
            var seen = hostiles.Where(Visible).ToList();

            foreach (var s in Squads.Where(q => q.Role == SquadRole.Garrison)) TickGarrison(s, hostiles);
            foreach (var s in Squads.Where(q => q.Role == SquadRole.Raiders).ToList()) TickRaider(s, dt);
            Squads.RemoveAll(s => s.Role == SquadRole.Raiders && s.Spawned && s.Alive == 0);

            var line = Squads.Where(q => q.Role == SquadRole.Line).ToList();
            foreach (var s in line) CheckStrength(s);

            if (Order.Kind == OrderKind.Strike && !Raider) TickStrike(dt, line, hostiles);
            else TickDefend(dt, line, seen, hostiles);

            foreach (var s in Squads.Where(q => q.Role == SquadRole.Riders)) TickRiders(s, dt);

            if (Raider && Order.Kind == OrderKind.Strike) TickRaidHost(dt);
            TickRecruits(dt);

            foreach (var s in Squads)
            {
                var d = s.Destination - s.Centroid;
                if (d.Length > 1.5f) s.Heading = d.Normalized;
            }
        }

        bool Hostile(Faction f) => f != Faction && f != Faction.Neutral;
        bool Visible(UnitInfo u) => Knowledge == null || Knowledge.Fog == null || Knowledge.Fog.At(u.Pos) == FogState.Visible;

        // ================================================================ events -> engagements
        void OnEvent(BrainEvent e)
        {
            switch (e.Kind)
            {
                case EventKind.Hit:
                case EventKind.Kill:
                    if (e.Kind == EventKind.Kill) killedAnywhere.Add(e.VictimId);
                    foreach (var s in Squads)
                    {
                        bool victimMine = e.VictimSquad == s.Id, actorMine = e.ActorSquad == s.Id;
                        if (s.Engagement == null && s.Role == SquadRole.Line && (victimMine && Hostile(e.ActorFaction) || actorMine && Hostile(e.VictimFaction)))
                        {   // a fight found us before we found it
                            int enemySquad = victimMine ? e.ActorSquad : e.VictimSquad;
                            if (enemySquad >= 0 && !IsStrikeState(s.State) && s.State != SquadState.FallBack && s.Alive > 0 && V2.Dist(e.Pos, Home.Center) < Leash) StartEngagement(s, enemySquad, e.Pos, announce: false);
                        }
                        var g = s.Engagement;
                        if (g == null) continue;
                        bool victimTarget = g.Targets.Contains(e.VictimSquad), actorTarget = g.Targets.Contains(e.ActorSquad);
                        if (victimTarget && (actorMine || e.ActorFaction == Faction) || victimMine && actorTarget) g.Contact = true;
                        if (victimTarget) g.SeenEnemyIds.Add(e.VictimId);
                        if (actorTarget) g.SeenEnemyIds.Add(e.ActorId);
                        if (e.Kind == EventKind.Kill)
                        {
                            if (victimTarget && e.ActorFaction == Faction) g.KilledIds.Add(e.VictimId);
                            if (victimMine) g.Losses++;
                        }
                    }
                    if (e.Kind == EventKind.Kill && Order.Kind == OrderKind.Strike)
                    {
                        var t = World.Get(Order.TargetPlaceId);
                        if (t != null && V2.Dist(e.Pos, t.Center) < t.Radius + 25f)
                        {
                            if (e.ActorFaction == Faction && Hostile(e.VictimFaction)) strikeKills++;
                            if (e.VictimFaction == Faction) strikeLosses++;
                        }
                    }
                    break;
                case EventKind.Torched:
                    {
                        var p = World.Get(e.PlaceId);
                        if (p != null && p.Owner == Faction && e.BuildingIndex >= 0 && e.BuildingIndex < p.Buildings.Count)
                            Say($"Raiders have put the {p.Buildings[e.BuildingIndex].Label} at {p.Name} to the torch.");
                        break;
                    }
                case EventKind.Spawned:
                    {
                        var s = FindSquad(e.ActorSquad);
                        if (s != null && (s.Role == SquadRole.Line || s.Role == SquadRole.Riders) && e.ActorFaction == Faction)
                            Say($"A recruit from {Home.Name} joins {s.Name} ({s.Alive} of {s.Nominal}).");
                        break;
                    }
            }
        }

        void StartEngagement(Squad s, int enemySquad, V2 where, bool announce, int seenCount = 0, string verb = "rides out to meet")
        {
            var g = new Engagement { Where = where, LastSeen = time, LastSeenPos = where };
            g.Targets.Add(enemySquad);
            s.Engagement = g;
            s.State = SquadState.Intercept; s.Run = true; s.AggroRadius = 14f; s.Destination = where;
            if (announce) Say($"{s.Name} {verb} {EnemyNoun(where, seenCount)} sighted {WhereWord(where)}.");
        }

        // ================================================================ defend (line squads)
        void EnterPatrol(Squad s)
        {
            s.State = SquadState.Patrol; s.Run = false; s.AggroRadius = 9f; s.Engagement = null;
            s.Route.Clear(); s.RouteIndex = 0;
            int index = Squads.Where(q => q.Role == SquadRole.Line).ToList().IndexOf(s);
            if (index % 2 == 0)
            {   // the town watch: walk between the buildings
                foreach (var b in Home.Buildings) s.Route.Add(b.Pos + (b.Pos - Home.Center).Normalized * 4f);
            }
            else
            {   // the country patrol: a wide ring around home
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4 + index;
                    s.Route.Add(Home.Center + new V2((float)Math.Cos(a), (float)Math.Sin(a)) * (Home.Radius + 24f));
                }
            }
            if (s.Route.Count == 0) s.Route.Add(Home.Center);
            // start at the nearest waypoint
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < s.Route.Count; i++) { float d = V2.Dist(s.Route[i], s.Centroid); if (d < bd) { bd = d; best = i; } }
            s.RouteIndex = best; s.Destination = s.Route[best];
        }

        void CheckStrength(Squad s)
        {
            if (!s.Spawned) return;
            if (s.Alive == 0)
            {
                if (s.Engagement != null) Resolve(s);
                if (!s.ReportedWiped) { s.ReportedWiped = true; Say($"{s.Name} is cut down to the last man."); }
                s.State = SquadState.FallBack; s.Destination = Home.Center;
                return;
            }
            s.ReportedWiped = false;
            if (s.State != SquadState.FallBack && s.Alive < 0.4f * s.Nominal)
            {
                if (s.Engagement != null) Resolve(s);
                s.State = SquadState.FallBack; s.Run = true; s.AggroRadius = 3f; s.Destination = Home.Center;
                Say($"{s.Name} is down to {Count(s.Alive)} of {Count(s.Nominal)} and falls back to {Home.Name}.");
            }
        }

        void TickDefend(float dt, List<Squad> line, List<UnitInfo> seen, List<UnitInfo> allHostiles)
        {
            // threat groups: visible hostile squads inside our ground
            var groups = seen.Where(h => h.SquadId >= 0 && (V2.Dist(h.Pos, Home.Center) < DefendRadius || V2.Dist(h.Pos, Home.Center) < Leash * 0.8f && line.Any(s => s.Alive > 0 && V2.Dist(s.Centroid, h.Pos) < 22f)))
                             .GroupBy(h => h.SquadId).ToList();

            foreach (var g in groups)
            {
                bool covered = line.Any(s => s.Engagement != null && s.Engagement.Targets.Contains(g.Key));
                if (covered) continue;
                var c = Centroid(g.ToList());
                var free = line.Where(s => s.Alive > 0 && (s.State == SquadState.Patrol || s.State == SquadState.Return || s.State == SquadState.Regroup))
                               .OrderBy(s => V2.Dist(s.Centroid, c)).ToList();
                if (free.Count == 0) continue;
                var send = Trait == Trait.Aggressive ? free : free.Take(1).ToList();
                foreach (var s in send) StartEngagement(s, g.Key, c, announce: true, seenCount: g.Count());
            }

            // cautious: the reserve reinforces a squad that is losing
            if (Trait != Trait.Aggressive)
                foreach (var s in line.Where(q => q.Engagement != null && q.Engagement.Contact && !q.Engagement.ReinforceSent))
                {
                    int enemy = allHostiles.Count(h => s.Engagement.Targets.Contains(h.SquadId) && V2.Dist(h.Pos, s.Centroid) < 18f);
                    if (enemy <= s.Alive) continue;
                    var r = line.Where(q => q != s && q.Alive > 0 && (q.State == SquadState.Patrol || q.State == SquadState.Return || q.State == SquadState.Regroup))
                                .OrderBy(q => V2.Dist(q.Centroid, s.Centroid)).FirstOrDefault();
                    if (r == null) continue;
                    s.Engagement.ReinforceSent = true;
                    StartEngagement(r, s.Engagement.Targets.First(), s.Centroid, announce: false);
                    foreach (var t in s.Engagement.Targets) r.Engagement.Targets.Add(t);
                    Say($"{s.Name} is outnumbered {WhereWord(s.Centroid)}; sending {r.Name} to reinforce.");
                }

            foreach (var s in line)
            {
                if (s.Alive == 0) continue;
                switch (s.State)
                {
                    case SquadState.Patrol:
                        if (V2.Dist(s.Centroid, s.Destination) < 4f)
                        {
                            s.RouteIndex = (s.RouteIndex + 1) % s.Route.Count;
                            s.Destination = s.Route[s.RouteIndex];
                        }
                        break;
                    case SquadState.Intercept:
                    case SquadState.Engage:
                    case SquadState.Pursue:
                        TickFight(s, dt, allHostiles);
                        break;
                    case SquadState.Regroup:
                        s.Timer -= dt;
                        if (s.Timer <= 0) { s.State = SquadState.Return; s.Run = false; s.AggroRadius = 9f; s.Destination = s.Route.Count > 0 ? s.Route[s.RouteIndex] : Home.Center; }
                        break;
                    case SquadState.Return:
                        if (V2.Dist(s.Centroid, s.Destination) < 6f) EnterPatrol(s);
                        break;
                    case SquadState.FallBack:
                        s.Destination = Home.Center;
                        if (s.Alive >= Math.Ceiling(0.75f * s.Nominal))
                        {
                            Say($"{s.Name} is back to {Count(s.Alive)} men and returns to the watch.");
                            EnterPatrol(s);
                        }
                        break;
                    default:
                        if (Order.Kind != OrderKind.Strike) EnterPatrol(s);
                        break;
                }
            }
        }

        void TickFight(Squad s, float dt, List<UnitInfo> allHostiles)
        {
            var g = s.Engagement;
            if (g == null) { EnterPatrol(s); return; }
            var targets = allHostiles.Where(h => g.Targets.Contains(h.SquadId)).ToList();
            var visible = targets.Where(Visible).ToList();
            foreach (var v in visible) g.SeenEnemyIds.Add(v.Id);
            if (visible.Count > 0) { var n = Nearest(visible, s.Centroid); g.LastSeen = time; g.LastSeenPos = n.Pos; }

            if (targets.Count == 0) { Resolve(s); return; }                     // all dead
            if (time - g.LastSeen > (s.State == SquadState.Pursue ? 5f : 8f)) { Resolve(s); return; }   // lost them

            float nearest = visible.Count > 0 ? visible.Min(v => V2.Dist(v.Pos, s.Centroid)) : float.MaxValue;
            if (V2.Dist(s.Centroid, Home.Center) > Leash) { Resolve(s); return; }   // leash: never chase off home ground
            s.Destination = g.LastSeenPos; s.Run = true;
            switch (s.State)
            {
                case SquadState.Intercept:
                    s.AggroRadius = 14f;
                    if (g.Contact || nearest < 5f) s.State = SquadState.Engage;
                    break;
                case SquadState.Engage:
                    s.AggroRadius = 16f;
                    if (g.Contact && nearest > 12f) s.State = SquadState.Pursue;
                    break;
                case SquadState.Pursue:
                    s.AggroRadius = 16f;
                    if (V2.Dist(s.Centroid, Home.Center) > Leash) { Resolve(s); return; }   // leash: do not chase off home ground
                    if (nearest < 6f) s.State = SquadState.Engage;
                    break;
            }
        }

        /// <summary>The only place a fight report is written: from recorded contact, kills and live bodies.</summary>
        void Resolve(Squad s)
        {
            var g = s.Engagement; s.Engagement = null;
            if (g == null) return;
            int seen = g.SeenEnemyIds.Count;
            int deadByUs = g.KilledIds.Count;
            int deadAll = g.SeenEnemyIds.Count(id => killedAnywhere.Contains(id));
            var gone = g.SeenEnemyIds.Where(id => !killedAnywhere.Contains(id)).ToList();           // alive, or left the field
            var aliveTargets = gone.Where(id => byId.TryGetValue(id, out var u) && u.Alive).Select(id => byId[id]).ToList();
            int fled = gone.Count;
            string where = WhereWord(g.Where);
            V2 away = aliveTargets.Count > 0 ? Centroid(aliveTargets) : g.LastSeenPos;
            string fledDir = DirectionWord(g.Where, away + (away - Home.Center).Normalized * 5f);
            if (g.Contact)
            {
                string fledPart = fled > 0 ? $", {Count(fled)} fled {fledDir}" : "";
                string lost = g.Losses == 0 ? "We lost no one." : g.Losses == 1 ? "We lost one man." : $"We lost {Count(g.Losses)} men.";
                Say($"{s.Name} caught {EnemyNoun(g.Where, seen)} {where}. {Cap(Count(deadAll))} dead{fledPart}. {lost}");
            }
            else if (seen > 0)
            {
                if (fled == 0 && deadAll > 0) Say($"The {EnemyWord(g.Where)} {where} were dealt with before {s.Name} came up.");
                else Say($"{s.Name} never closed with the {EnemyWord(g.Where)} {where}; they slipped away {fledDir}.");
            }
            if (s.Alive > 0 && s.State != SquadState.FallBack)
            {
                s.State = SquadState.Regroup; s.Timer = 3f; s.Run = false; s.AggroRadius = 9f; s.Destination = s.Centroid;
            }
        }

        // ================================================================ strike
        void PlanStrike(Place target)
        {
            var field = Squads.Where(s => s.Role == SquadRole.Line && s.State != SquadState.FallBack).ToList();
            V2 dir = (target.Center - Home.Center).Normalized;
            V2 staging = target.Center - dir * (target.Radius + 18f);
            bool aggressive = Trait != Trait.Cautious;
            for (int i = 0; i < field.Count; i++)
            {
                var s = field[i]; s.Engagement = null; s.Route.Clear(); s.Run = false; s.AggroRadius = 12f;
                if (aggressive) { s.State = SquadState.Assault; s.Destination = target.Center; s.Run = true; }   // straight in
                else if (i == 0) { s.State = SquadState.Flank; s.Destination = target.Center + dir.Perp * (target.Radius + 16f); }  // swing round the flank
                else { s.State = SquadState.Reserve; s.Destination = staging; }                                 // the reserve waits
            }
        }

        void TickStrike(float dt, List<Squad> line, List<UnitInfo> hostiles)
        {
            var target = World.Get(Order.TargetPlaceId);
            var field = line.Where(s => s.Alive > 0 && IsStrikeState(s.State)).ToList();
            if (target == null || field.Count == 0)
            {
                Say($"The strike on {target?.Name} has failed. {Tally()} We fall back to {Home.Name}.");
                Order = StandingOrder.Defend();
                foreach (var s in line.Where(q => q.State != SquadState.FallBack)) EnterPatrol(s);
                return;
            }
            var defenders = hostiles.Where(h => V2.Dist(h.Pos, target.Center) < target.Radius + 10f).ToList();
            bool anyAssault = field.Any(q => q.State == SquadState.Assault);
            foreach (var s in field)
            {
                switch (s.State)
                {
                    case SquadState.Flank:
                        if (V2.Dist(s.Centroid, s.Destination) < 6f)
                        {
                            s.State = SquadState.Assault; s.Destination = target.Center; s.Run = true;
                            Say($"{s.Name} has turned their flank at {target.Name} and goes in.");
                        }
                        break;
                    case SquadState.Reserve:
                        if (anyAssault) s.Timer += dt;
                        var assaulting = field.FirstOrDefault(q => q.State == SquadState.Assault);
                        bool losing = assaulting != null && defenders.Count(d => V2.Dist(d.Pos, assaulting.Centroid) < 15f) > assaulting.Alive;
                        if (anyAssault && (s.Timer > 6f || losing))
                        {
                            s.State = SquadState.Assault; s.Destination = target.Center; s.Run = true;
                            Say($"The reserve, {s.Name}, is committed at {target.Name}.");
                        }
                        break;
                    case SquadState.Assault:
                        s.Destination = defenders.Count > 0 ? Nearest(defenders, s.Centroid).Pos : target.Center;
                        break;
                }
            }
            bool inside = field.Any(s => s.State == SquadState.Assault && V2.Dist(s.Centroid, target.Center) < target.Radius);
            if (inside && defenders.Count == 0) strikeClearTimer += dt; else strikeClearTimer = 0;
            if (strikeClearTimer > 3f)
            {
                if (target.Kind == PlaceKind.Hamlet) { target.Raided = true; Say($"{target.Name} is raided and put to the torch. {Tally()} We return to the watch."); }
                else { target.Owner = Faction; Say($"{target.Name} is taken! {Tally()} We return to the watch over {Home.Name}."); }
                Order = StandingOrder.Defend();
                foreach (var s in line.Where(q => q.State != SquadState.FallBack)) EnterPatrol(s);
            }
        }

        // ================================================================ survey (riders)
        void TickRiders(Squad s, float dt)
        {
            if (s.Alive == 0)
            {
                if (s.Spawned && !s.ReportedWiped) { s.ReportedWiped = true; Say("We have lost the riders."); }
                return;
            }
            s.ReportedWiped = false;
            if (Order.Kind != OrderKind.Survey || Knowledge == null)
            {
                s.State = SquadState.Hold; s.Run = false; s.Destination = CommandPost + new V2(3, 0); return;
            }
            s.Run = true; s.AggroRadius = 3f;
            if (s.State != SquadState.Survey || s.LegDir < 0) { if (!NextLeg(s)) FinishSurvey(s); return; }
            s.Timer += dt;
            if (V2.Dist(s.Centroid, s.Destination) < 5f || s.Timer > 80f)
            {
                if (s.LegFound == 0) Say($"Riders report the {DirName(s.LegDir)} road is quiet.");
                surveyedDirs.Add(s.LegDir);
                if (!NextLeg(s)) FinishSurvey(s);
            }
        }

        bool NextLeg(Squad s)
        {
            int bestDir = -1, bestUnknown = 2; V2 bestPoint = default;
            for (int d = 0; d < 8; d++)
            {
                if (surveyedDirs.Contains(d)) continue;
                double a = Math.PI / 2 - d * Math.PI / 4;   // 0 = north (+z), clockwise
                var dir = new V2((float)Math.Cos(a), (float)Math.Sin(a));
                int unknown = 0; V2 far = Home.Center;
                for (float r = 30; r < World.HalfSize * 1.3f; r += 12)
                {
                    var p = Home.Center + dir * r;
                    if (Math.Abs(p.x) > World.HalfSize - 8 || Math.Abs(p.z) > World.HalfSize - 8) break;
                    if (Knowledge.Fog.At(p) == FogState.Unknown) unknown++;
                    far = p;
                }
                if (unknown > bestUnknown) { bestUnknown = unknown; bestDir = d; bestPoint = far; }
                else if (unknown <= 2) surveyedDirs.Add(d);
            }
            if (bestDir < 0) return false;
            s.State = SquadState.Survey; s.LegDir = bestDir; s.LegFound = 0; s.Timer = 0; s.Destination = bestPoint;
            return true;
        }

        void FinishSurvey(Squad s)
        {
            Say($"Riders have covered the country around {Home.Name} and ride home. We keep the watch.");
            Order = StandingOrder.Defend();
            s.State = SquadState.Hold; s.LegDir = -1; s.Destination = CommandPost + new V2(3, 0);
        }

        void ReportDiscovery(Place p)
        {
            string held = p.Owner == Faction.Neutral ? "Quiet, held by no one." : Hostile(p.Owner) ? "Held against us." : "Ours.";
            var riders = Squads.FirstOrDefault(q => q.Role == SquadRole.Riders && q.Alive > 0 && q.Members.Any(id => byId.ContainsKey(id) && V2.Dist(byId[id].Pos, p.Center) < p.Radius + 35f));
            string a = p.Kind == PlaceKind.Hamlet ? "a hamlet" : p.Kind == PlaceKind.Town ? "a town" : "a fort";
            if (riders != null) { riders.LegFound++; Say($"Riders found {a}, {p.Name}, to the {DirectionWord(Home.Center, p.Center)}. {held}"); }
            else Say($"Word comes of {a}, {p.Name}, to the {DirectionWord(Home.Center, p.Center)}. {held}");
        }

        // ================================================================ garrison
        void TickGarrison(Squad s, List<UnitInfo> hostiles)
        {
            var home = World.Get(s.HomePlaceId) ?? Home;
            var near = hostiles.Where(h => V2.Dist(h.Pos, home.Center) < home.Radius + 12f).ToList();
            s.Destination = near.Count > 0 ? Nearest(near, s.Centroid).Pos : home.Center;
            s.Run = near.Count > 0;
        }

        // ================================================================ raiding (enemy uses this for Strike on a town)
        void TickRaidHost(float dt)
        {
            if (Home.Owner != Faction) return;
            RaidTimer -= dt;
            if (RaidTimer > 0) return;
            RaidTimer = RaidInterval;
            var target = World.Get(Order.TargetPlaceId);
            if (target == null) return;
            var choices = target.Buildings.Where(b => !b.Burned && b.Kind != BuildingKind.Well && b.Kind != BuildingKind.TownCenter && b.Kind != BuildingKind.Palisade).ToList();
            if (choices.Count == 0) return;
            var b = choices[rng.Next(choices.Count)];
            RaidsSent++;
            var s = AddSquad("Raiders " + RaidsSent, SquadRole.Raiders, RaidSize);
            s.TargetBuilding = b; s.TargetPlaceId = target.Id;
            var dir = (target.Center - Home.Center).Normalized;
            var start = Home.Center + dir * (Home.Radius + 4f);
            float side = rng.Next(2) == 0 ? -1f : 1f;
            var mid = (start + target.Center) * 0.5f + dir.Perp * side * 30f;     // a road through the countryside
            var outskirts = b.Pos + (b.Pos - target.Center).Normalized * 10f;
            s.Route.Add(mid); s.Route.Add(outskirts); s.Route.Add(b.Pos);
            s.RouteIndex = 0; s.Destination = mid; s.Centroid = start;
            RequestMen(s, RaidSize, start);
            RaidersSpawnedRequested += RaidSize;
        }

        void TickRaider(Squad s, float dt)
        {
            if (!s.Spawned || s.Alive == 0) return;
            var home = Home;
            if (s.State != SquadState.Flee && s.Alive < 0.5f * s.Nominal) { s.State = SquadState.Flee; }
            switch (s.State)
            {
                case SquadState.Raid:
                    s.Run = false; s.AggroRadius = 6f;
                    if (V2.Dist(s.Centroid, s.Destination) < 4f)
                    {
                        s.RouteIndex++;
                        if (s.RouteIndex >= s.Route.Count) { s.State = SquadState.Torch; s.Timer = 0; }
                        else s.Destination = s.Route[s.RouteIndex];
                    }
                    break;
                case SquadState.Torch:
                    s.Run = false; s.AggroRadius = 5f;
                    var b = s.TargetBuilding;
                    if (b == null || b.Burned) { s.State = SquadState.Flee; break; }
                    s.Destination = b.Pos;
                    bool atIt = s.Members.Any(id => byId.ContainsKey(id) && V2.Dist(byId[id].Pos, b.Pos) < Math.Max(b.SizeX, b.SizeZ) * 0.5f + 3.5f);
                    if (atIt) s.Timer += dt;
                    if (s.Timer >= 8f)
                    {
                        b.Burned = true;
                        Emitted.Add(new BrainEvent { Kind = EventKind.Torched, Time = time, ActorSquad = s.Id, ActorFaction = Faction, Pos = b.Pos, PlaceId = s.TargetPlaceId, BuildingIndex = b.Index });
                        s.State = SquadState.Flee;
                    }
                    break;
                case SquadState.Flee:
                    s.Run = true; s.AggroRadius = 0f; s.Destination = home.Center;
                    if (V2.Dist(s.Centroid, home.Center) < home.Radius)
                    {
                        Requests.Add(new BodyRequest { Kind = RequestKind.Despawn, SquadId = s.Id, UnitIds = new List<int>(s.Members) });
                        s.Members.Clear(); s.Alive = 0;
                    }
                    break;
            }
        }

        // ================================================================ replenishment
        void TickRecruits(float dt)
        {
            recruitTimer -= dt;
            if (recruitTimer > 0) return;
            recruitTimer = RecruitInterval;
            if (Home.Owner != Faction) return;
            var s = Squads.Where(q => (q.Role == SquadRole.Line || q.Role == SquadRole.Riders) && q.Alive < q.Nominal && !q.InFight && !IsStrikeState(q.State) && q.State != SquadState.Survey)
                          .OrderBy(q => q.Strength).FirstOrDefault();
            if (s != null) RequestMen(s, 1, Home.Center + new V2(0, -4));
        }

        // ================================================================ words
        static UnitInfo Nearest(List<UnitInfo> list, V2 p) => list.OrderBy(u => V2.Dist(u.Pos, p)).First();
        static V2 Centroid(List<UnitInfo> list) { V2 c = new V2(0, 0); foreach (var u in list) c = c + u.Pos; return list.Count > 0 ? c * (1f / list.Count) : c; }

        string EnemyWord(V2 p) => V2.Dist(p, Home.Center) < Leash + 10 ? "raiders" : "enemy men";
        string EnemyNoun(V2 p, int n)
        {
            bool raid = V2.Dist(p, Home.Center) < Leash + 10;
            string one = raid ? "raider" : "enemy soldier", many = raid ? "raiders" : "enemy soldiers";
            return $"{Count(n)} {(n == 1 ? one : many)}";
        }

        public string WhereWord(V2 p)
        {
            Place best = null; float bd = float.MaxValue;
            foreach (var pl in World.Places) { float d = V2.Dist(pl.Center, p) - pl.Radius; if (d < bd) { bd = d; best = pl; } }
            if (best != null && bd < 4f)
            {
                var b = best.NearestBuilding(p);
                return b != null && V2.Dist(b.Pos, p) < 14f ? $"at the {b.Label} in {best.Name}" : $"in {best.Name}";
            }
            if (best != null && bd < 18f) return $"just {DirectionWord(best.Center, p)} of {best.Name}";
            return $"in the country {DirectionWord(Home.Center, p)} of {Home.Name}";
        }

        static readonly string[] Numbers = { "no", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten" };
        public static string Count(int n) => n >= 0 && n < Numbers.Length ? Numbers[n] : n.ToString();
        string Tally() =>
            (strikeKills == 0 ? "None of theirs fell" : strikeKills == 1 ? "One of theirs fell" : $"{Cap(Count(strikeKills))} of theirs fell") + "; " +
            (strikeLosses == 0 ? "we lost no one." : strikeLosses == 1 ? "we lost one man." : $"we lost {Count(strikeLosses)} men.");
        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        static readonly string[] Dirs = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
        public static string DirName(int d) => Dirs[((d % 8) + 8) % 8];
        public static string DirectionWord(V2 from, V2 to)
        {
            var d = to - from;
            double ang = Math.Atan2(d.x, d.z) * 180 / Math.PI; if (ang < 0) ang += 360;
            return Dirs[(int)Math.Round(ang / 45) % 8];
        }
    }
}
