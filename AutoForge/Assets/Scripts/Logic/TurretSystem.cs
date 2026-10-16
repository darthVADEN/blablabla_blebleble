// Расширения и система турелей.
using AF.Core;
using UnityEngine;

public static class GeoExt
{
    public static Vector3 ToVector3XZ(this Vector2Int v) => new(v.x, 0f, v.y);
}

// Турели: берут боеприпасы (Circuit/TurretPart как "снаряды") из InBuf,
// стреляют по ближайшему врагу в радиусе. Сигнал процессора включает/выключает.
namespace AF.Logic
{
    public static class TurretSystem
    {
        public struct Shot { public Vector3 From, To; public float T; }
        static readonly System.Collections.Generic.List<Shot> _shots = new();
        public static System.Collections.Generic.IReadOnlyList<Shot> Shots => _shots;

        public static void LogicTick()
        {
            foreach (var m in GameManager.Instance.World.MachinesAll)
            {
                if (m.Type != MachineType.Turret || !m.Alive) continue;
                TryFire(m);
            }
        }

        // визуализация снарядов — в кадрах
        public static void Tick()
        {
            for (int i = _shots.Count - 1; i >= 0; i--)
            {
                var s = _shots[i]; s.T += Time.deltaTime; _shots[i] = s;
                if (s.T > 0.15f) _shots.RemoveAt(i);
            }
        }

        static void TryFire(MachineEntity m)
        {
            m.CooldownT -= GameNet.TICK;
            if (m.CooldownT > 0f) return;
            var target = Enemies.EnemySystem.Nearest(m.Cell.ToVector3XZ(), 9f);
            if (target == null) return;
            // боеприпасы: любой предмет tier>=2 сгорает как снаряд
            if (!ConsumeAmmo(m)) return;
            m.CooldownT = 0.8f;
            target.TakeDamage(15f);
            _shots.Add(new Shot { From = m.Cell.ToVector3XZ() + Vector3.up * 0.6f, To = target.Pos + Vector3.up * 0.4f, T = 0f });
        }

        public static void TryFireNearest(Vector2Int cell)
        {
            // команда FIRE от процессора: найти соседнюю турель и заставить выстрелить сейчас
            for (int k = 0; k < 4; k++)
            {
                var nb = GameManager.Instance.World.At(cell + World.DirOff[k]);
                if (nb is { Type: MachineType.Turret, Alive: true }) { nb.CooldownT = 0f; TryFire(nb); return; }
            }
        }

        static bool ConsumeAmmo(MachineEntity m)
        {
            foreach (var kv in m.InBuf)
            {
                if (Items.Tier(kv.Key) >= 2)
                {
                    if (kv.Value <= 1) m.InBuf.Remove(kv.Key); else m.InBuf[kv.Key]--;
                    return true;
                }
            }
            return false;
        }
    }
}

