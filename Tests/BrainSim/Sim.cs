// Headless simulation: steps the real brain with fake bodies (no Unity) and asserts reports are truthful.
using System;
using System.Collections.Generic;
using System.Linq;
using Oathmarch.Brain;

class FakeUnit
{
    public int Id; public Faction Faction; public Squad Squad; public V2 Pos; public float Hp = 40, Dmg = 10, Cool, Vision = 22, Walk = 1.6f, Run = 3.8f;
    public int Slot; public FakeUnit LastAttacker; public bool Alive => Hp > 0;
}

class Sim
{
    public World World; public FogGrid Fog; public Commander King, Enemy; public ReportLog Log = new ReportLog();
    public List<FakeUnit> Units = new List<FakeUnit>(); public List<BrainEvent> Pending = new List<BrainEvent>(), AllEvents = new List<BrainEvent>();
    public List<(float t, string text, int hitsBefore, int raiderKillsBefore)> Reports = new List<(float, string, int, int)>();
    public float T; int nextId = 1; public int RaiderBodies, GarrisonBodies; public bool BodiesFight = true;
    FakeUnit kingBody;

    public Sim(float firstRaid = 8f)
    {
        World = World.Generate(1187); Fog = new FogGrid(World.HalfSize, 128);
        King = new Commander("Sir Aldric", Faction.King, Trait.Cautious, World, World.KingTown, new Knowledge(Faction.King, Fog), Log);
        Enemy = new Commander("Warlord", Faction.Enemy, Trait.Aggressive, World, World.EnemyTown, null, Log) { Silent = true, Raider = true, RaidTimer = firstRaid, RaidInterval = 50f, RaidSize = 4 };
        Enemy.Give(StandingOrder.Strike(World.KingTown));
        Log.Added += r => Reports.Add((r.Time, r.Text, AllEvents.Count(e => e.Kind == EventKind.Hit || e.Kind == EventKind.Kill), AllEvents.Count(e => e.Kind == EventKind.Kill && e.VictimFaction == Faction.Enemy)));
        kingBody = new FakeUnit { Id = nextId++, Faction = Faction.King, Pos = World.KingTown.Center, Hp = 1e9f, Vision = 26 };
        Units.Add(kingBody);
        var a = King.AddSquad("First squad", SquadRole.Line, 4); King.RequestMen(a, 4, World.KingTown.Center + new V2(-5, 7));
        var b = King.AddSquad("Second squad", SquadRole.Line, 4); King.RequestMen(b, 4, World.KingTown.Center + new V2(5, 7));
        var r = King.AddSquad("Riders", SquadRole.Riders, 2); King.RequestMen(r, 2, King.CommandPost);
        foreach (var p in World.Places.Where(p => p.Owner == Faction.Enemy))
        {
            var g = Enemy.AddSquad("Garrison of " + p.Name, SquadRole.Garrison, p.Kind == PlaceKind.Town ? 4 : 2, p);
            Enemy.RequestMen(g, g.Nominal, p.Center);
        }
        Serve(King); Serve(Enemy);
    }

    void Serve(Commander c)
    {
        foreach (var r in c.Requests)
        {
            var sq = c.FindSquad(r.SquadId);
            if (r.Kind == RequestKind.Spawn && sq != null)
                for (int i = 0; i < r.Count; i++)
                {
                    var u = new FakeUnit { Id = nextId++, Faction = c.Faction, Squad = sq, Pos = r.Pos + new V2(i * 1.2f, 0), Slot = sq.Members.Count };
                    if (c.Faction == Faction.Enemy) { u.Hp = 30; u.Dmg = 8; u.Vision = 0; if (sq.Role == SquadRole.Raiders) RaiderBodies++; else GarrisonBodies++; }
                    if (sq.Role == SquadRole.Riders) { u.Vision = 30; u.Run = 4.4f; }
                    Units.Add(u); c.RegisterMember(sq.Id, u.Id);
                    if (T > 0 && c.Faction == Faction.King) Pending.Add(new BrainEvent { Kind = EventKind.Spawned, Time = T, ActorId = u.Id, ActorSquad = sq.Id, ActorFaction = c.Faction });
                }
            else if (r.Kind == RequestKind.Despawn) Units.RemoveAll(u => r.UnitIds.Contains(u.Id));
        }
        c.Requests.Clear();
    }

    public void Step(float dt)
    {
        T += dt;
        // fog from our bodies + own places
        var viewers = Units.Where(u => u.Alive && u.Faction == Faction.King && u.Vision > 0).Select(u => (u.Pos, u.Vision)).ToList();
        foreach (var p in World.Places) if (p.Owner == Faction.King) viewers.Add((p.Center, p.Radius + 10));
        Fog.Update(viewers);
        // bodies
        foreach (var u in Units.Where(x => x.Alive && x.Squad != null).ToList())
        {
            u.Cool -= dt;
            float aggro = u.Squad.AggroRadius;
            if (u.LastAttacker != null && u.LastAttacker.Alive && V2.Dist(u.Pos, u.LastAttacker.Pos) < 4) aggro = Math.Max(aggro, 4);
            FakeUnit tgt = null; float bd = aggro;
            if (BodiesFight) foreach (var o in Units) if (o.Alive && o.Squad != null && Hostile(u.Faction, o.Faction)) { float d = V2.Dist(u.Pos, o.Pos); if (d < bd) { bd = d; tgt = o; } }
            V2 goal; bool run = u.Squad.Run;
            if (tgt != null)
            {
                goal = tgt.Pos; run = true;
                if (bd <= 1.7f) { if (u.Cool <= 0) { u.Cool = 1.1f; Hit(u, tgt); } continue; }
            }
            else
            {
                var h = u.Squad.Heading; var right = new V2(h.z, -h.x); int k = (u.Slot + 1) / 2; float side = u.Slot == 0 ? 0 : (u.Slot % 2 == 1 ? -1 : 1);
                goal = u.Squad.Destination + right * (side * k * 2.2f) + h * (-k * 1.8f);
            }
            var to = goal - u.Pos; float dist = to.Length; float sp = run ? u.Run : u.Walk;
            if (dist > 0.4f) u.Pos = u.Pos + to.Normalized * Math.Min(sp * dt, dist);
        }
        var snapshot = Units.Where(u => u.Squad != null || u == kingBody).Select(u => new UnitInfo { Id = u.Id, Faction = u.Faction, SquadId = u.Squad?.Id ?? -1, Pos = u.Pos, Alive = u.Alive }).ToList();
        var events = Pending.ToList(); Pending.Clear();
        King.Tick(dt, T, snapshot, events);
        Enemy.Tick(dt, T, snapshot, events);
        Pending.AddRange(King.Emitted); Pending.AddRange(Enemy.Emitted);
        AllEvents.AddRange(King.Emitted); AllEvents.AddRange(Enemy.Emitted);
        Serve(King); Serve(Enemy);
        Units.RemoveAll(u => !u.Alive && u.Squad != null && T - 0 > 0 && u.Hp < -100);
    }

    static bool Hostile(Faction a, Faction b) => a != b && a != Faction.Neutral && b != Faction.Neutral;

    public void Hit(FakeUnit a, FakeUnit v)
    {
        v.Hp -= a.Dmg; v.LastAttacker = a; bool kill = v.Hp <= 0;
        var e = new BrainEvent { Kind = kill ? EventKind.Kill : EventKind.Hit, Time = T, ActorId = a.Id, ActorSquad = a.Squad?.Id ?? -1, ActorFaction = a.Faction, VictimId = v.Id, VictimSquad = v.Squad?.Id ?? -1, VictimFaction = v.Faction, Pos = v.Pos };
        Pending.Add(e); AllEvents.Add(e);
        if (kill) v.Hp = -1000;   // dead bodies stay as corpses (Alive=false)
    }

    public void Run(float seconds) { for (float t = 0; t < seconds; t += 0.1f) Step(0.1f); }
}

static class Program
{
    static int failures;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + what); if (!ok) failures++; }

    static int Main()
    {
        Console.WriteLine("== Scenario 1: a raid comes in; reports must follow real engagement events");
        var s = new Sim(firstRaid: 5f);
        s.Run(420);
        foreach (var r in s.Reports) Console.WriteLine($"    [{r.t,6:0.0}] {r.text}");
        Check(s.RaiderBodies > 0, $"raider bodies were spawned by the bodies layer ({s.RaiderBodies})");
        var caught = s.Reports.Where(r => r.text.Contains(" caught ")).ToList();
        Check(caught.Count > 0, $"at least one engagement report ({caught.Count})");
        Check(caught.All(r => r.hitsBefore > 0), "every 'caught' report came after real blows were struck");
        foreach (var r in caught)
        {
            var words = r.text.Split(' ');
            int idx = Array.IndexOf(words, "dead,") >= 0 ? Array.IndexOf(words, "dead,") : Array.IndexOf(words, "dead.");
            int n = idx > 0 ? Num(words[idx - 1]) : -1;
            Check(n >= 0 && n <= r.raiderKillsBefore, $"dead count in report ({n}) <= raider deaths actually recorded so far ({r.raiderKillsBefore})");
        }
        int firstHit = s.AllEvents.Where(e => e.Kind == EventKind.Hit || e.Kind == EventKind.Kill).Select(e => (int)e.Time).DefaultIfEmpty(-1).First();
        Check(!s.Reports.Any(r => r.text.Contains(" one raiders") || r.text.Contains(" one men")), "grammar: no 'one raiders'");
        var fb = s.Reports.Where(r => r.text.Contains("falls back")).Select(r => r.text).ToList();
        Check(fb.Distinct().Count() == fb.Count || fb.Count < 2 || s.Reports.Count(r => r.text.Contains("falls back") && r.t - s.Reports.First(q => q.text.Contains("falls back")).t < 3) <= 1, "no repeated fall-back spam");
        Check(!s.Reports.Any(r => r.text.Contains("Harrowgate") && (r.text.Contains("rides out") || r.text.Contains("caught"))), "squads do not fight off home ground (leash)");
        Check(!s.Reports.Any(r => (r.text.Contains("turned back") || r.text.Contains(" caught ")) && r.t < firstHit), "no victory report before the first blow");

        Console.WriteLine("== Scenario 2: brain sees raiders but bodies never fight -> no fake victory");
        var s2 = new Sim(firstRaid: 5f) { BodiesFight = false };
        s2.Run(240);
        foreach (var r in s2.Reports) Console.WriteLine($"    [{r.t,6:0.0}] {r.text}");
        Check(s2.AllEvents.All(e => e.Kind != EventKind.Hit && e.Kind != EventKind.Kill), "no combat events happened");
        Check(!s2.Reports.Any(r => r.text.Contains(" caught ") || r.text.Contains("turned back") || r.text.Contains("dealt with")), "no 'caught/turned back/dealt with' report without combat");
        Check(s2.Reports.Any(r => r.text.Contains("to the torch")), "unopposed raiders torch a building and it is reported");

        Console.WriteLine("== Scenario 3: Survey reveals the country and names places");
        var s3 = new Sim(firstRaid: 9999f);
        s3.King.Give(StandingOrder.Survey());
        s3.Run(600);
        foreach (var r in s3.Reports) Console.WriteLine($"    [{r.t,6:0.0}] {r.text}");
        Check(s3.Reports.Any(r => r.text.Contains("Riders found")), "riders report a place by name and kind");
        Check(s3.Reports.Any(r => r.text.Contains("quiet")), "riders report quiet country");
        int strikable = s3.World.Places.Count(p => s3.King.CanStrike(p));
        Check(strikable >= 2, $"newly reported places become Strike targets ({strikable})");
        var hamlet = s3.World.Places.FirstOrDefault(p => p.Kind == PlaceKind.Hamlet && p.Owner == Faction.Enemy && s3.King.CanStrike(p));
        if (hamlet != null)
        {
            int before = s3.Reports.Count;
            Check(s3.King.Give(StandingOrder.Strike(hamlet)), $"Strike {hamlet.Name} accepted");
            s3.Run(300);
            foreach (var r in s3.Reports.Skip(before)) Console.WriteLine($"    [{r.t,6:0.0}] {r.text}");
            Check(s3.Reports.Skip(before).Any(r => r.text.Contains("raided") && r.text.Contains("fell")), "raid on the hamlet completes with a counted report");
        }
        var fresh = new Sim(firstRaid: 9999f);
        Check(fresh.World.Places.Count(p => fresh.King.CanStrike(p)) == 0, "before anyone has seen them, nothing can be struck");

        Console.WriteLine("== Scenario 4: a squad cut below 40% falls back; recruits refill it");
        var s4 = new Sim(firstRaid: 9999f);
        s4.Run(2);
        var sq = s4.King.Squads.First(q => q.Name == "First squad");
        var enemyDummy = new FakeUnit { Id = 9999, Faction = Faction.Enemy, Squad = null };
        foreach (var u in s4.Units.Where(u => u.Squad == sq).Take(3).ToList()) for (int i = 0; i < 4; i++) if (u.Alive) s4.Hit(enemyDummy, u);
        s4.Run(120);
        foreach (var r in s4.Reports) Console.WriteLine($"    [{r.t,6:0.0}] {r.text}");
        Check(s4.Reports.Any(r => r.text.Contains("First squad is down to one of four and falls back")), "fall-back reported with the real count");
        Check(s4.Reports.Any(r => r.text.Contains("recruit")), "recruits join from town");

        Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
        return failures == 0 ? 0 : 1;
    }

    static int Num(string w) { string[] n = { "no", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten" }; int i = Array.IndexOf(n, w.ToLowerInvariant()); return i >= 0 ? i : int.TryParse(w, out var v) ? v : -1; }
}
