// Враги. Волнами атакуют базу (как в Mindustry). Два типа:
//  Raider  — юнит-кибернетический "дрон", ломает машины, атакует игрока.
//  Harvester — большой вражеский "комбайн": ЕСТ РЕСУРСЫ с карты и может
//              выплёвывать снаряды (технологии врагов = грубая версия ваших).
using System.Collections.Generic;
using AF.Core;
using UnityEngine;

namespace AF.Enemies
{
    public enum EnemyKind { Raider, Harvester }

    public sealed class EnemyUnit
    {
        public EnemyKind Kind;
        public Vector3 Pos;
        public float Hp, MaxHp, Speed;
        public float AttackCd;
        public MachineEntity TargetMachine;
        public bool Dead => Hp <= 0f;

        public void TakeDamage(float d)
        {
            Hp -= d;
            if (Hp <= 0f)
            {
                // дроны иногда роняют трофеи — "враги используют те же ресурсы"
                var w = GameManager.Instance.World;
                var cell = new Vector2Int(Mathf.RoundToInt(Pos.x), Mathf.RoundToInt(Pos.z));
                if (Kind == EnemyKind.Harvester) w.SpawnDrop(cell, Item.CopperIngot, 4);
                else if (Random.value < 0.4f) w.SpawnDrop(cell, Item.IronNugget, 1);
            }
        }
    }

    public static class EnemySystem
    {
        public static readonly List<EnemyUnit> All = new();
        public static int Wave = 0;
        public static float NextWaveIn = 90f; // первая волна через 90 секунд
        static Vector2Int _baseCell = new(32, 32);

        public static void SetBase(Vector2Int c) => _baseCell = c;

        public static void Tick()
        {
            // спавн волн
            NextWaveIn -= GameNet.TICK;
            if (NextWaveIn <= 0f) SpawnWave();

            var playerPos = GameManager.Instance.PlayerPos;
            var world = GameManager.Instance.World;

            for (int i = All.Count - 1; i >= 0; i--)
            {
                var e = All[i];
                if (e.Dead) { All.RemoveAt(i); continue; }

                e.AttackCd -= GameNet.TICK;

                // выбор цели: ближайшая машина рядом ИЛИ игрок ИЛИ база
                Vector3 goal = playerPos;
                float goalDist = Vector3.Distance(e.Pos, playerPos);

                // ищем машину в радиусе 6 для атаки
                MachineEntity best = null; float bestD = 6f;
                foreach (var m in world.MachinesAll)
                {
                    if (!m.Alive || m.Type == MachineType.Chest) continue;
                    float d = Vector3.Distance(e.Pos, m.Cell.ToVector3XZ());
                    if (d < bestD) { bestD = d; best = m; }
                }
                if (best != null && bestD < goalDist * 0.8f)
                {
                    e.TargetMachine = best; goal = best.Cell.ToVector3XZ(); goalDist = bestD;
                }
                else e.TargetMachine = null;

                // движение к цели
                Vector3 dir = goal - e.Pos; dir.y = 0;
                if (dir.magnitude > 0.9f) e.Pos += dir.normalized * e.Speed * GameNet.TICK;
                else
                {
                    // атака
                    if (e.AttackCd <= 0f)
                    {
                        e.AttackCd = e.Kind == EnemyKind.Harvester ? 1.6f : 1.0f;
                        float dmg = e.Kind == EnemyKind.Harvester ? 12f : 5f;
                        if (e.TargetMachine != null) e.TargetMachine.Damage(dmg);
                        else GameManager.Instance.DamagePlayer(dmg);
                        // харвестер крадёт ресурс с тайла под собой
                        if (e.Kind == EnemyKind.Harvester)
                        {
                            var t = world.Get(new Vector2Int(Mathf.RoundToInt(e.Pos.x), Mathf.RoundToInt(e.Pos.z)));
                            if (t.Res != TileResource.None) t.Richness -= 0.05f;
                        }
                    }
                }
            }
        }

        static void SpawnWave()
        {
            Wave++;
            NextWaveIn = Mathf.Max(45f, 120f - Wave * 8f);
            int raiders = 2 + Wave * 2;
            int harvesters = Wave / 3;
            for (int i = 0; i < raiders; i++) Spawn(EnemyKind.Raider);
            for (int i = 0; i < harvesters; i++) Spawn(EnemyKind.Harvester);
            GameManager.Instance.UI?.Toast($"Волна {Wave}! Врагов: {raiders + harvesters}");
        }

        static void Spawn(EnemyKind kind)
        {
            // со стороны края карты, случайный угол
            float ang = Random.value * Mathf.PI * 2f;
            Vector2Int edge = _baseCell + new Vector2Int(Mathf.RoundToInt(Mathf.Cos(ang) * 30f), Mathf.RoundToInt(Mathf.Sin(ang) * 30f));
            edge.x = Mathf.Clamp(edge.x, 1, World.SIZE - 2);
            edge.y = Mathf.Clamp(edge.y, 1, World.SIZE - 2);
            var e = new EnemyUnit
            {
                Kind = kind,
                Pos = edge.ToVector3XZ() + Vector3.up * 0.3f,
                MaxHp = kind == EnemyKind.Harvester ? 140f : 35f + Wave * 4f,
                Speed = kind == EnemyKind.Harvester ? 1.1f : 1.8f + Wave * 0.05f,
            };
            e.Hp = e.MaxHp;
            All.Add(e);
        }

        public static EnemyUnit Nearest(Vector3 from, float maxR = 100f)
        {
            EnemyUnit best = null; float bd = maxR;
            foreach (var e in All)
            {
                if (e.Dead) continue;
                float d = Vector3.Distance(from, e.Pos);
                if (d < bd) { bd = d; best = e; }
            }
            return best;
        }

        // урон по площади от взрывов/игрока
        public static void Splash(Vector3 center, float r, float dmg)
        {
            foreach (var e in All)
                if (Vector3.Distance(center, e.Pos) < r) e.TakeDamage(dmg);
        }
    }
}
