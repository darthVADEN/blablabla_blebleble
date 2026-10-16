// Машина — чистая логика (не MonoBehaviour). Рендер separate (MachineView).
// Типы поведения:
//  Driller   — добывает то, что под тайлом, истощает Richness.
//  Crusher/Washer/Compactor/Assembler/Lab — крафт по рецептам из входного буфера.
//  Conveyor  — хранит до 3 предметов на ленте, толкает в направлении Dir на след. тик.
//  Chest     — склад.
//  Turret    — стреляет по врагам (логика в Enemies, тут только "амуниция").
//  Processor — программируемый (см. Logic/ProcessorVm.cs), сигналы inputs/outputs.
using System.Collections.Generic;
using UnityEngine;

namespace AF.Core
{
    public class MachineEntity
    {
        public readonly MachineType Type;
        public readonly MachineDef Def;
        public readonly Vector2Int Cell;
        public World World;

        // конвейер/машина ориентация (только для конвейера — куда толкает)
        public int Dir = 0; // индекс в World.DirOff

        // буферы
        public readonly Dictionary<Item, int> InBuf = new();
        public readonly Dictionary<Item, int> OutBuf = new();

        // крафт-состояние
        protected Recipe Current;
        public float Progress;          // 0..1 доля текущего рецепта (для анимации пресса/пилы)
        public bool Working => Current != null;
        protected float Cooldown;
        public float CooldownT; // кулдаун турели (внешние системы)

        // для конвейера: лента items (глава 1 элемент за тик при 10Гц => ~10 item/s)
        public readonly List<Item> Belt = new();
        public const int BeltCap = 3;

        // бур
        public float MineTimer;

        // HP — враги могут ломать машины
        public float Hp = 60f; public float MaxHp = 60f;
        public bool Alive => Hp > 0f;

        // сигнал процессора -> турели и пр. (простая "редстоун"-сеть 1 уровня)
        public float SignalIn;

        public MachineEntity(MachineType type, Vector2Int cell, World w)
        {
            Type = type; Def = Machines.Defs[type]; Cell = cell; World = w;
            if (type == MachineType.Turret) { Hp = MaxHp = 120f; }
        }

        // ---------- фаза производства ----------
        public virtual void TickProduce()
        {
            if (!Alive) return;
            switch (Type)
            {
                case MachineType.Driller: TickDriller(); break;
                case MachineType.Conveyor: /* перенос в TickTransfer */ break;
                case MachineType.Chest: break;
                case MachineType.Turret: break; // стрельба в EnemySystem
                case MachineType.Processor: break; // в ProcessorSystem
                default: TickCraft(); break;
            }
        }

        void TickDriller()
        {
            var tile = World.Get(Cell);
            MineTimer -= GameNet.TICK;
            if (MineTimer > 0f || tile.Richness <= 0f) return;
            MineTimer = 1.2f;
            tile.Richness -= 0.02f;
            Item outp = Machines.OreOutput(tile.Res);
            AddOut(outp, 1);
        }

        void TickCraft()
        {
            if (Current != null)
            {
                Progress += GameNet.TICK;
                if (Progress >= Current.Seconds)
                {
                    // consum inputs (TakeFrom вернёт false если чего-то не хватило — тогда просто что смогли)
                    foreach (var it in Current.Inputs) TakeIn(it, 1);
                    foreach (var it in Current.Outputs) if (it != Item.None) AddOut(it, 1);
                    if (Type == MachineType.Lab) GameManager.Instance?.Research?.AddProgress(1f); // цель игры
                    Current = null; Progress = 0f; Cooldown = 0.2f;
                }
                return;
            }
            Cooldown -= GameNet.TICK;
            if (Cooldown > 0f) return;
            var r = Recipes.Match(Type, InBuf);
            if (r != null) { Current = r; Progress = 0f; }
            else Cooldown = 0.5f;
        }

        // ---------- фаза передачи ----------
        public virtual void TickTransfer()
        {
            if (!Alive) return;
            if (Type == MachineType.Conveyor) { TickConveyor(); return; }

            // машины выгружают OutBuf соседу-конвейеру/машине по всем 4 сторонам понемногу
            PushOutputsToOneNeighbor();
            // и принимают из лент соседей
            PullFromBelts();
        }

        void PushOutputsToOneNeighbor()
        {
            if (OutBuf.Count == 0) return;
            // выбор получателя: конвейер впереди (по Dir) иначе любой сосед-машина с местом
            for (int k = 0; k < 4; k++)
            {
                var nb = World.At(Cell + World.DirOff[k]);
                if (nb == null || nb == this || !nb.Alive) continue;
                if (!nb.CanAcceptAny()) continue;
                var en = FirstEntry(OutBuf);
                nb.Receive(en.Key, 1);
                TakeOut(en.Key, 1);
                return;
            }
        }

        void PullFromBelts()
        {
            for (int k = 0; k < 4; k++)
            {
                var nb = World.At(Cell + World.DirOff[k]);
                if (nb is { Alive: true } && nb.Type == MachineType.Conveyor && nb.Belt.Count > 0)
                {
                    // конвейер отдаёт последний предмет если он мне подходит (или любой — упрощение)
                    var it = nb.Belt[nb.Belt.Count - 1];
                    if (CanAcceptItem(it)) { nb.Belt.RemoveAt(nb.Belt.Count - 1); Receive(it, 1); }
                }
            }
        }

        void TickConveyor()
        {
            // лента: двигаем всё на 1 шаг вперёд если целевая клетка позволяет
            var target = World.At(Cell + World.DirOff[Dir]);
            if (Belt.Count > 0)
            {
                var head = Belt[Belt.Count - 1];
                if (target != null && target.Alive && target.Type == MachineType.Conveyor && target.Belt.Count < BeltCap)
                {
                    Belt.RemoveAt(Belt.Count - 1);
                    target.Belt.Insert(0, head);
                }
                else if (target != null && target.Alive && target.Type != MachineType.Conveyor && target.CanAcceptItem(head))
                {
                    Belt.RemoveAt(Belt.Count - 1);
                    target.Receive(head, 1);
                }
                else if (target == null || !target.Alive)
                {
                    // выпал за пределы/в пустоту — теряется
                    Belt.RemoveAt(Belt.Count - 1);
                    World.SpawnDrop(Cell, head, 1);
                }
            }
            // приём с других конвейеров, указывающих на нас
            for (int k = 0; k < 4; k++)
            {
                var src = World.At(Cell - World.DirOff[k]);
                if (src is { Alive: true } && src.Type == MachineType.Conveyor
                    && src.Dir == k && src.Belt.Count > 0 && Belt.Count < BeltCap)
                {
                    Belt.Add(src.Belt[src.Belt.Count - 1]);
                    src.Belt.RemoveAt(src.Belt.Count - 1);
                }
            }
        }

        // ---------- буферные операции ----------
        public bool CanAcceptItem(Item it)
        {
            if (Type == MachineType.Conveyor) return Belt.Count < BeltCap;
            var buf = Def.HasCrafting ? InBuf : OutBuf; // ящик принимает во внутренний склад
            if (Type == MachineType.Chest) return Count(buf, it) < 50;
            if (Def.HasCrafting) return Count(InBuf, it) < 20;
            return Count(OutBuf, it) < 20;
        }

        public bool CanAcceptAny()
        {
            if (Type == MachineType.Conveyor) return Belt.Count < BeltCap;
            return true;
        }

        public void Receive(Item it, int n)
        {
            if (Type == MachineType.Conveyor) { if (Belt.Count + n <= BeltCap) for (int i = 0; i < n; i++) Belt.Add(it); return; }
            var buf = Def.HasCrafting || Type == MachineType.Turret || Type == MachineType.Processor ? InBuf : OutBuf;
            if (Type == MachineType.Chest) buf = InBuf;
            buf.TryGetValue(it, out int c); buf[it] = c + n;
        }

        public void AddOut(Item it, int n) { OutBuf.TryGetValue(it, out int c); OutBuf[it] = c + n; }
        void TakeIn(Item it, int n) { if (TakeFrom(InBuf, it, n)) return; }
        void TakeOut(Item it, int n) => TakeFrom(OutBuf, it, n);

        static bool TakeFrom(Dictionary<Item, int> d, Item it, int n)
        {
            d.TryGetValue(it, out int c);
            if (c < n) return false;          // нечего снимать — рецепт не выполняем
            if (c == n) d.Remove(it); else d[it] = c - n;
            return true;
        }

        public static int Count(Dictionary<Item, int> d, Item it) { d.TryGetValue(it, out int c); return c; }
        static KeyValuePair<Item, int> FirstEntry(Dictionary<Item, int> d)
        {
            using var e = d.GetEnumerator(); e.MoveNext(); return e.Current;
        }

        // урон от врагов
        public void Damage(float dmg)
        {
            Hp -= dmg;
            if (Hp <= 0f)
            {
                // разброс остатков
                foreach (var kv in InBuf) World.SpawnDrop(Cell, kv.Key, Mathf.Min(kv.Value, 5));
                foreach (var kv in OutBuf) World.SpawnDrop(Cell, kv.Key, Mathf.Min(kv.Value, 5));
                World.MachinesAll.Remove(this);
                World.Get(Cell).Machine = null;
            }
        }

        public virtual void Dispose() { }
    }
}
