// Мир: тайлы (ресурсная земля), сетка машин, предметы-на-земле, чанки для рендера.
using System.Collections.Generic;
using UnityEngine;

namespace AF.Core
{
    public enum TileResource { None, Iron, Copper, Coal }

    public sealed class Tile
    {
        public Vector2Int Pos;
        public TileResource Res;
        public float Richness = 1f;   // бур истощает
        public MachineEntity Machine; // занято машиной?
    }

    public sealed class World
    {
        public const int SIZE = 64;   // плоский мир 64x64 клетки, клетка = 1 юнит
        readonly Dictionary<Vector2Int, Tile> _tiles = new();
        readonly List<ItemDrop> _drops = new();

        public IReadOnlyList<ItemDrop> Drops => _drops;

        // индексы "соседей" для конвейеров: направление -> смещение
        public static readonly Vector2Int[] DirOff = { new(1,0), new(0,1), new(-1,0), new(0,-1) };

        public Tile Get(Vector2Int p)
        {
            if (!_tiles.TryGetValue(p, out var t))
            {
                t = new Tile { Pos = p, Res = RollOre(p) };
                _tiles[p] = t;
            }
            return t;
        }

        static TileResource RollOre(Vector2Int p)
        {
            // детерминированный шум: пятна руды вокруг центра карты реже, к краям чаще
            float n = Mathf.PerlinNoise(p.x * 0.12f + 7.3f, p.y * 0.12f + 2.1f);
            float m = Mathf.PerlinNoise(p.x * 0.05f, p.y * 0.05f);
            if (n > 0.78f) return TileResource.Iron;
            if (n < 0.18f && m > 0.55f) return TileResource.Coal;
            if (m > 0.82f) return TileResource.Copper;
            return TileResource.None;
        }

        public bool InBounds(Vector2Int p) => p.x >= 0 && p.y >= 0 && p.x < SIZE && p.y < SIZE;

        // ---- Предметы на земле (выпадают из машин/смертей) ----
        public void SpawnDrop(Vector2Int cell, Item item, int count = 1)
        {
            for (int i = 0; i < count; i++)
                _drops.Add(new ItemDrop
                {
                    Item = item,
                    Pos = new Vector3(cell.x + Random.Range(-0.3f, 0.3f), 0.15f, cell.y + Random.Range(-0.3f, 0.3f)),
                    Vel = new Vector3(Random.Range(-1f, 1f), 3f, Random.Range(-1f, 1f)),
                });
        }

        public void DropTick(float dt)
        {
            for (int i = _drops.Count - 1; i >= 0; i--)
            {
                var d = _drops[i];
                d.Vel.y -= 12f * dt;
                d.Pos += d.Vel * dt;
                if (d.Pos.y < 0.1f) { d.Pos.y = 0.1f; d.Vel *= 0.4f; }
                d.Age += dt;
                if (d.Age > 60f) _drops.RemoveAt(i);
            }
        }

        public List<ItemDrop> PickupsInRadius(Vector3 center, float r)
        {
            var res = new List<ItemDrop>();
            for (int i = _drops.Count - 1; i >= 0; i--)
            {
                var d = _drops[i];
                if ((d.Pos - center).sqrMagnitude < r * r) { res.Add(d); _drops.RemoveAt(i); }
            }
            return res;
        }

        // ---- Размещение/снятие машин ----
        public bool Place(MachineType type, Vector2Int cell, out string err)
        {
            err = null;
            if (!InBounds(cell)) { err = "За границей мира"; return false; }
            var t = Get(cell);
            if (t.Machine != null) { err = "Занято"; return false; }
            var m = new MachineEntity(type, cell, this);
            t.Machine = m;
            MachinesAll.Add(m);
            return true;
        }

        public void Remove(Vector2Int cell)
        {
            var t = Get(cell);
            if (t.Machine == null) return;
            MachinesAll.Remove(t.Machine);
            AF.Logic.ProcessorSystem.Detach(t.Machine); // отцепить VM если это процессор
            t.Machine.Dispose();
            t.Machine = null;
        }

        public MachineEntity At(Vector2Int cell) => InBounds(cell) ? Get(cell).Machine : null;

        // Все машины — один список, тикается на каждом NetTick
        public readonly List<MachineEntity> MachinesAll = new();

        // Логический тик мира
        public void Tick()
        {
            // 1) машины решают что крафтят / производят
            foreach (var m in MachinesAll) m.TickProduce();
            // 2) обмен предметами между соседями (конвейеры толкают, машины отдают выходы)
            foreach (var m in MachinesAll) m.TickTransfer();
        }
    }

    public sealed class ItemDrop
    {
        public Item Item; public Vector3 Pos; public Vector3 Vel; public float Age;
    }
}
