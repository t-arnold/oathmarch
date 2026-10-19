using System;
using System.Collections.Generic;

namespace Oathmarch.Brain
{
    /// <summary>The country: places built from buildings, generated from a seed.</summary>
    public sealed class World
    {
        public readonly int Seed;
        public readonly float HalfSize;
        public readonly List<Place> Places = new List<Place>();
        public readonly List<V2> Landmarks = new List<V2>(); // trees / rocks
        public Place KingTown, EnemyTown;

        public World(int seed, float halfSize = 150f) { Seed = seed; HalfSize = halfSize; }

        public Place Get(int id) => Places.Find(p => p.Id == id);

        public Place PlaceAt(V2 p)
        {
            foreach (var pl in Places) if (V2.Dist(pl.Center, p) <= pl.Radius) return pl;
            return null;
        }

        static readonly string[] Names = { "Ashford", "Brenmoor", "Caldwick", "Dunhollow", "Elmstead", "Fennick", "Greywater", "Harrowgate", "Ironmere", "Kestrel Fold", "Lowmarsh", "Marrow Hill" };

        public static World Generate(int seed)
        {
            var rng = new Random(seed);
            var w = new World(seed);
            var names = new List<string>(Names);
            string TakeName() { int i = rng.Next(names.Count); var n = names[i]; names.RemoveAt(i); return n; }
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            V2 Polar(float ang, float dist) => new V2((float)Math.Cos(ang) * dist, (float)Math.Sin(ang) * dist);

            float enemyAng = R(0, (float)Math.PI * 2);
            w.KingTown = w.AddPlace(TakeName(), PlaceKind.Town, Faction.King, new V2(0, 0), 22f, rng, 8);
            w.EnemyTown = w.AddPlace(TakeName(), PlaceKind.Town, Faction.Enemy, Polar(enemyAng, R(105, 120)), 20f, rng, 6);
            w.AddPlace(TakeName(), PlaceKind.Hamlet, Faction.Enemy, Polar(enemyAng + R(0.7f, 1.0f), R(70, 85)), 12f, rng, 3);
            w.AddPlace(TakeName(), PlaceKind.Hamlet, Faction.Neutral, Polar(enemyAng - R(1.6f, 2.2f), R(60, 80)), 12f, rng, 3);
            w.AddPlace(TakeName(), PlaceKind.Fort, Faction.King, Polar(enemyAng + R(2.6f, 3.4f), R(50, 65)), 12f, rng, 0);

            for (int i = 0; i < 70; i++)
            {
                var p = new V2(R(-w.HalfSize + 8, w.HalfSize - 8), R(-w.HalfSize + 8, w.HalfSize - 8));
                bool clear = true;
                foreach (var pl in w.Places) if (V2.Dist(pl.Center, p) < pl.Radius + 6) { clear = false; break; }
                if (clear) w.Landmarks.Add(p);
            }
            return w;
        }

        Place AddPlace(string name, PlaceKind kind, Faction owner, V2 c, float radius, Random rng, int growth)
        {
            var p = new Place { Id = Places.Count, Name = name, Kind = kind, Owner = owner, Center = c, Radius = radius };
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            void B(BuildingKind k, V2 pos, float sx, float sz, float h) =>
                p.Buildings.Add(new Building { Kind = k, Pos = pos, SizeX = sx, SizeZ = sz, Height = h, Yaw = R(-25, 25) });

            if (kind == PlaceKind.Fort)
            {
                B(BuildingKind.Tower, c, 4, 4, 9);
                B(BuildingKind.Barracks, c + new V2(5, 3), 6, 4, 3.5f);
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    var pos = c + new V2((float)Math.Cos(a) * 9, (float)Math.Sin(a) * 9);
                    p.Buildings.Add(new Building { Kind = BuildingKind.Palisade, Pos = pos, SizeX = 7.5f, SizeZ = 0.6f, Height = 3, Yaw = (float)(-a * 180 / Math.PI) + 90 });
                }
            }
            else
            {
                if (kind == PlaceKind.Town) B(BuildingKind.TownCenter, c, 7, 6, 5);
                B(BuildingKind.Well, c + new V2(kind == PlaceKind.Town ? 6 : 0, kind == PlaceKind.Town ? -5 : 0), 1.6f, 1.6f, 1.2f);
                // the place grows: hovels first, then cottages, granary, smithy, tavern
                var order = new[] { BuildingKind.Hovel, BuildingKind.Hovel, BuildingKind.Cottage, BuildingKind.Granary, BuildingKind.Cottage, BuildingKind.Blacksmith, BuildingKind.Tavern, BuildingKind.Cottage, BuildingKind.Hovel };
                int count = kind == PlaceKind.Hamlet ? growth : growth;
                for (int i = 0; i < count && i < order.Length; i++)
                {
                    double a = (i / (double)count) * Math.PI * 2 + R(-0.2f, 0.2f);
                    float d = R(radius * 0.45f, radius * 0.8f);
                    var pos = c + new V2((float)Math.Cos(a) * d, (float)Math.Sin(a) * d);
                    var k = order[i];
                    float s = k == BuildingKind.Hovel ? 3 : k == BuildingKind.Granary ? 4.5f : k == BuildingKind.Tavern ? 6 : 4;
                    B(k, pos, s, s * R(0.8f, 1.3f), k == BuildingKind.Granary ? 4.5f : R(2.5f, 3.5f));
                }
            }
            Places.Add(p);
            return p;
        }
    }
}
