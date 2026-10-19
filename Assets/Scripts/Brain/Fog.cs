using System;
using System.Collections.Generic;

namespace Oathmarch.Brain
{
    public enum FogState : byte { Unknown = 0, Seen = 1, Visible = 2 }

    /// <summary>What one faction knows about the country.</summary>
    public sealed class FogGrid
    {
        public readonly int Size;
        public readonly float CellSize, HalfExtent;
        readonly FogState[] cells;
        public int Version { get; private set; }

        public FogGrid(float halfExtent, int size = 128)
        {
            Size = size; HalfExtent = halfExtent; CellSize = halfExtent * 2 / size;
            cells = new FogState[size * size];
        }

        public FogState this[int x, int y] => cells[y * Size + x];

        bool ToCell(V2 p, out int cx, out int cy)
        {
            cx = (int)((p.x + HalfExtent) / CellSize); cy = (int)((p.z + HalfExtent) / CellSize);
            return cx >= 0 && cy >= 0 && cx < Size && cy < Size;
        }

        public FogState At(V2 p) => ToCell(p, out int x, out int y) ? cells[y * Size + x] : FogState.Unknown;

        /// <summary>Visible cells decay to Seen; then each viewer reveals a disc.</summary>
        public void Update(IEnumerable<(V2 pos, float radius)> viewers)
        {
            for (int i = 0; i < cells.Length; i++) if (cells[i] == FogState.Visible) cells[i] = FogState.Seen;
            foreach (var (pos, r) in viewers) Reveal(pos, r);
            Version++;
        }

        public void Reveal(V2 p, float r)
        {
            int rc = (int)Math.Ceiling(r / CellSize);
            ToCell(p, out int cx, out int cy);
            for (int y = cy - rc; y <= cy + rc; y++)
                for (int x = cx - rc; x <= cx + rc; x++)
                {
                    if (x < 0 || y < 0 || x >= Size || y >= Size) continue;
                    float wx = -HalfExtent + (x + 0.5f) * CellSize, wz = -HalfExtent + (y + 0.5f) * CellSize;
                    float dx = wx - p.x, dz = wz - p.z;
                    if (dx * dx + dz * dz <= r * r) cells[y * Size + x] = FogState.Visible;
                }
        }
    }

    /// <summary>Which places a faction has heard of. Orders can only name known places.</summary>
    public sealed class Knowledge
    {
        public readonly Faction Faction;
        readonly HashSet<int> known = new HashSet<int>();
        public readonly FogGrid Fog;
        public Knowledge(Faction f, FogGrid fog) { Faction = f; Fog = fog; }
        public bool Knows(Place p) => known.Contains(p.Id);

        /// <summary>Returns places newly learned this call.</summary>
        public List<Place> Refresh(World w)
        {
            var fresh = new List<Place>();
            foreach (var p in w.Places)
            {
                if (known.Contains(p.Id)) continue;
                bool seen = p.Owner == Faction || Fog == null || Fog.At(p.Center) != FogState.Unknown;
                if (!seen) foreach (var b in p.Buildings) if (Fog.At(b.Pos) != FogState.Unknown) { seen = true; break; }
                if (seen) { known.Add(p.Id); fresh.Add(p); }
            }
            return fresh;
        }
    }
}
