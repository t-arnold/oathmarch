// Oathmarch command BRAIN. Pure C#: no UnityEngine dependency.
using System;
using System.Collections.Generic;

namespace Oathmarch.Brain
{
    public struct V2
    {
        public float x, z;
        public V2(float x, float z) { this.x = x; this.z = z; }
        public static V2 operator +(V2 a, V2 b) => new V2(a.x + b.x, a.z + b.z);
        public static V2 operator -(V2 a, V2 b) => new V2(a.x - b.x, a.z - b.z);
        public static V2 operator *(V2 a, float s) => new V2(a.x * s, a.z * s);
        public float Length => (float)Math.Sqrt(x * x + z * z);
        public V2 Normalized { get { float l = Length; return l > 1e-5f ? new V2(x / l, z / l) : new V2(0, 0); } }
        public V2 Perp => new V2(-z, x);
        public static float Dist(V2 a, V2 b) => (a - b).Length;
        public override string ToString() => $"({x:0.0},{z:0.0})";
    }

    public enum Faction { Neutral = 0, King = 1, Enemy = 2 }
    public enum PlaceKind { Town, Hamlet, Fort }
    public enum Trait { Cautious, Aggressive, ScoutMinded }

    public enum BuildingKind { TownCenter, Hovel, Well, Cottage, Granary, Blacksmith, Tavern, Palisade, Tower, Barracks }

    public sealed class Building
    {
        public BuildingKind Kind;
        public V2 Pos;          // world position (x,z)
        public float SizeX, SizeZ, Height, Yaw;
        public string Label => Kind switch
        {
            BuildingKind.TownCenter => "town hall",
            BuildingKind.Hovel => "hovels",
            BuildingKind.Well => "well",
            BuildingKind.Cottage => "cottages",
            BuildingKind.Granary => "granary",
            BuildingKind.Blacksmith => "blacksmith",
            BuildingKind.Tavern => "tavern",
            BuildingKind.Palisade => "palisade",
            BuildingKind.Tower => "watchtower",
            BuildingKind.Barracks => "barracks",
            _ => Kind.ToString().ToLowerInvariant()
        };
    }

    public sealed class Place
    {
        public int Id;
        public string Name;
        public PlaceKind Kind;
        public Faction Owner;
        public V2 Center;
        public float Radius;
        public bool Raided;
        public readonly List<Building> Buildings = new List<Building>();

        public string KindWord => Kind switch { PlaceKind.Town => "town", PlaceKind.Hamlet => "hamlet", _ => "fort" };

        public Building NearestBuilding(V2 p)
        {
            Building best = null; float bd = float.MaxValue;
            foreach (var b in Buildings) { float d = V2.Dist(b.Pos, p); if (d < bd) { bd = d; best = b; } }
            return best;
        }
    }

    /// <summary>A body's state as the brain sees it. Bodies fill these in each tick.</summary>
    public struct UnitInfo
    {
        public int Id;
        public Faction Faction;
        public int SquadId;   // -1 for the king
        public V2 Pos;
        public bool Alive;
    }

    public sealed class Report
    {
        public float Time;
        public string Text;
        public Report(float t, string s) { Time = t; Text = s; }
    }

    public sealed class ReportLog
    {
        public readonly List<Report> Entries = new List<Report>();
        public event Action<Report> Added;
        public void Add(float time, string text)
        {
            var r = new Report(time, text);
            Entries.Add(r);
            if (Entries.Count > 60) Entries.RemoveAt(0);
            Added?.Invoke(r);
        }
    }
}
