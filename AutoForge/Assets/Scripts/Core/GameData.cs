// AutoForge — ядро: предметы, рецепты, дефиниции машин, сетевой тик.
// Всё живёт в плоском мире чанков (как Factorio), машины работают по "сети"
// с фиксированным тиком (как Омега Крафтер / Create — синхронная кинематика).
using System.Collections.Generic;
using UnityEngine; // Vector2Int для OreOutput

namespace AF.Core
{
    // ---- Предметы ----------------------------------------------------------
    public enum Item { None, Cobblestone, Gravel, IronNugget, Silicon, IronIngot, SteelIngot, CopperIngot, Circuit, ComputerChip, GunBarrel, TurretPart, Battery, Coal, IronOre }

    public static class Items
    {
        public static readonly Dictionary<Item, ItemDef> Defs = new()
        {
            [Item.Cobblestone]  = new("Камень",     "cobblestone", 0),
            [Item.Gravel]       = new("Гравий",     "gravel",      0),
            [Item.IronOre]      = new("Железная руда","iron_ore",   0),
            [Item.IronNugget]   = new("Жел. самородок","iron_nugget",0),
            [Item.Silicon]      = new("Кремний",    "silicon",     1),
            [Item.Coal]         = new("Уголь",      "coal",        1),
            [Item.CopperIngot]  = new("Медь",       "copper_ingot",2),
            [Item.IronIngot]    = new("Железо",     "iron_ingot",  2),
            [Item.SteelIngot]   = new("Сталь",      "steel_ingot", 3),
            [Item.Circuit]      = new("Схема",      "circuit",     3),
            [Item.ComputerChip] = new("Чип",        "chip",        4),
            [Item.Battery]      = new("Батарея",    "battery",     3),
            [Item.GunBarrel]    = new("Ствол",      "barrel",      4),
            [Item.TurretPart]   = new("Турель-модуль","turret_part",5),
        };

        public static string Name(Item i) => i == Item.None ? "-" : Defs[i].Name;
        public static int Tier(Item i) => i == Item.None ? 0 : Defs[i].Tier;
    }

    public sealed class ItemDef
    {
        public readonly string Name; public readonly string TexKey; public readonly int Tier;
        public ItemDef(string n, string t, int tier) { Name = n; TexKey = t; Tier = tier; }
    }

    // ---- Рецепты -------------------------------------------------------------
    // Цепочка железа как у пользователя: булыжник ->(дробление)-> гравий
    // ->(промывка)-> железный самородок + кремний ->(пресс)-> железный слиток.
    public sealed class Recipe
    {
        public readonly MachineType Machine;
        public readonly Item[] Inputs;   // что берём из входного буфера
        public readonly Item[] Outputs;  // что кладём в выходной буфер
        public readonly float Seconds;
        public Recipe(MachineType m, float sec, Item[] inp, Item[] outp) { Machine = m; Inputs = inp; Outputs = outp; Seconds = sec; }
    }

    public static class Recipes
    {
        static readonly List<Recipe> All = new();
        public static IReadOnlyList<Recipe> List => All;

        static void Add(MachineType m, float sec, params Item[] io)
        {
            // первые N — инпуты до null? проще: массив вида [in..., -1, out...]
            // но читаемости ради пишем явно ниже.
        }

        public static void Init()
        {
            All.Clear();
            // Дробилка (Crusher): камень -> гравий x2 ; руда -> самородок x2
            All.Add(new Recipe(MachineType.Crusher, 1.5f, I(Item.Cobblestone), O(Item.Gravel, Item.Gravel)));
            All.Add(new Recipe(MachineType.Crusher, 2.0f, I(Item.IronOre), O(Item.IronNugget, Item.IronNugget)));
            // Промывка (Washer): гравий -> самородок + кремний (как в примере пользователя)
            All.Add(new Recipe(MachineType.Washer, 2.0f, I(Item.Gravel), O(Item.IronNugget, Item.Silicon)));
            All.Add(new Recipe(MachineType.Washer, 1.2f, I(Item.IronOre), O(Item.IronNugget, Item.Gravel)));
            // Плавильня (Smelter): уголь -> кремний (очистка); самородки -> слитки (быстрее пресса, но нет стального пути)
            All.Add(new Recipe(MachineType.Smelter, 1.0f, I(Item.Coal), O(Item.Silicon)));
            All.Add(new Recipe(MachineType.Smelter, 1.5f, I(Item.IronNugget, Item.IronNugget), O(Item.IronIngot)));
            // Пресс (Compactor): самородки -> слиток; железо+уголь -> СТАЛЬ (открывается исследованием)
            All.Add(new Recipe(MachineType.Compactor, 2.0f, I(Item.IronNugget, Item.IronNugget, Item.IronNugget), O(Item.IronIngot)));
            All.Add(new Recipe(MachineType.Compactor, 2.5f, I(Item.IronIngot, Item.Coal), O(Item.SteelIngot)));
            All.Add(new Recipe(MachineType.Compactor, 1.5f, I(Item.Gravel, Item.Gravel), O(Item.Cobblestone)));
            // Сборщик (Assembler): схемы, чипы, детали турелей
            All.Add(new Recipe(MachineType.Assembler, 3.0f, I(Item.IronIngot, Item.CopperIngot, Item.CopperIngot), O(Item.Circuit)));
            All.Add(new Recipe(MachineType.Assembler, 4.0f, I(Item.Circuit, Item.Circuit, Item.Silicon), O(Item.ComputerChip)));
            All.Add(new Recipe(MachineType.Assembler, 3.0f, I(Item.SteelIngot, Item.Circuit), O(Item.GunBarrel)));
            All.Add(new Recipe(MachineType.Assembler, 5.0f, I(Item.GunBarrel, Item.ComputerChip, Item.Battery), O(Item.TurretPart)));
            All.Add(new Recipe(MachineType.Assembler, 3.0f, I(Item.CopperIngot, Item.Silicon, Item.Cobblestone), O(Item.Battery)));
            // Плавильня (Smelter): медная руда... упрощённо: медь из самородков нет — делаем: медь плавится из мусора? Нет.
            // Медь: шахтёры дают CopperIngot напрямую; плавильня ускоряет переработку камня в лава-стиле не нужна.
            // Лаборатория (Lab) — цель игры: исследовать новые рецепты/победа.
            All.Add(new Recipe(MachineType.Lab, 8.0f, I(Item.Circuit, Item.ComputerChip), O(Item.None))); // прогресс исследований
        }

        static Item[] I(params Item[] x) => x;
        static Item[] O(params Item[] x) => x;

        // Все рецепты для типа машины
        public static IEnumerable<Recipe> For(MachineType t)
        {
            foreach (var r in All) if (r.Machine == t) yield return r;
        }

        // Найти рецепт, который машина может выполнить из буфера inputs (буфер — словарь кол-в)
        public static Recipe Match(MachineType t, Dictionary<Item, int> buffer)
        {
            foreach (var r in For(t))
            {
                if (!ResearchGate.IsRecipeUnlocked(r)) continue; // гейтинг исследованиями
                bool ok = true;
                foreach (var it in r.Inputs)
                {
                    buffer.TryGetValue(it, out int have);
                    // считаем сколько нужно этого предмета в рецепте
                    int need = 0; foreach (var q in r.Inputs) if (q == it) need++;
                    if (have < need) { ok = false; break; }
                }
                if (ok) return r;
            }
            return null;
        }
    }

    // ---- Машины --------------------------------------------------------------
    public enum MachineType { Driller, Crusher, Washer, Compactor, Smelter, Assembler, Conveyor, Lab, Turret, Chest, Processor }

    public sealed class MachineDef
    {
        public readonly MachineType Type; public readonly string Name; public readonly float Cost; // стоимость build-energy
        public readonly int InSlots; public readonly int OutSlots; public readonly bool HasCrafting;
        public readonly string ColorKey;
        public MachineDef(MachineType t, string n, float cost, bool craft, string col)
        { Type = t; Name = n; Cost = cost; HasCrafting = craft; ColorKey = col; InSlots = craft ? 6 : 2; OutSlots = craft ? 3 : 2; }
    }

    public static class Machines
    {
        public static readonly Dictionary<MachineType, MachineDef> Defs = new()
        {
            [MachineType.Driller]    = new(MachineType.Driller,    "Бур", 8, false, "driller"),
            [MachineType.Crusher]    = new(MachineType.Crusher,    "Дробилка", 12, true, "crusher"),
            [MachineType.Washer]     = new(MachineType.Washer,     "Промывка", 15, true, "washer"),
            [MachineType.Compactor]  = new(MachineType.Compactor,  "Пресс", 18, true, "compactor"),
            [MachineType.Smelter]    = new(MachineType.Smelter,    "Плавильня", 20, true, "smelter"),
            [MachineType.Assembler]  = new(MachineType.Assembler,  "Сборщик", 30, true, "assembler"),
            [MachineType.Conveyor]   = new(MachineType.Conveyor,   "Конвейер", 2, false, "conveyor"),
            [MachineType.Lab]        = new(MachineType.Lab,        "Лаборатория", 40, true, "lab"),
            [MachineType.Turret]     = new(MachineType.Turret,     "Турель", 25, false, "turret"),
            [MachineType.Chest]      = new(MachineType.Chest,      "Ящик", 4, false, "chest"),
            [MachineType.Processor]  = new(MachineType.Processor,  "Процессор", 35, false, "processor"),
        };

        // Что бур добывает из земли под собой (зависит от тайла ресурса)
        public static Item OreOutput(TileResource res) => res switch
        {
            TileResource.Iron => Item.IronOre,
            TileResource.Copper => Item.CopperIngot,
            TileResource.Coal => Item.Coal,
            _ => Item.Cobblestone,
        };
    }

    // ---- Сеть ---------------------------------------------------------------
    // Единый дискретный мир. Все машины и конвейеры тикают синхронно на NetTick,
    // поэтому любые "часовые" схемы (конвейерные петли, процессоры) работают детерминированно.
    public static class GameNet
    {
        public const float TICK = 0.1f;              // 10 Гц игровая логика
        public static long TickCount = 0;
        public static double Accumulator = 0;

        // Событие нового тика — подписываются World, Logic, Enemies
        public static event System.Action OnTick;

        public static void Update(float dt)
        {
            Accumulator += dt;
            // защита от спирали смерти
            int guard = 0;
            while (Accumulator >= TICK && guard++ < 10)
            {
                Accumulator -= TICK;
                TickCount++;
                OnTick?.Invoke();
            }
        }
    }
}
