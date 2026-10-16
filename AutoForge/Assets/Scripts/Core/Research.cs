// Цель игры: дерево исследований (линейная цепочка — достаточно для прототипа).
// Лаборатория крафтит "None"-рецепт (схема+чип) и даёт прогресс текущему узлу.
using AF.Core;

public sealed class ResearchNode
{
    public string Name;
    public float Cost;            // единиц прогресса
    public bool Unlocked;
    public ResearchNode Next;
    public ResearchNode(string name, float cost) { Name = name; Cost = cost; }
}

public static class ResearchTree
{
    public static ResearchNode Root;

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { Root = null; }

    public static void Build()
    {
        if (Root != null) return;
        Root = new ResearchNode("Сталь", 20);
        var circuits = new ResearchNode("Схемы", 40);
        var chips = new ResearchNode("Чипы", 80);
        var turretTech = new ResearchNode("Турели", 150);
        var reactor = new ResearchNode("ЯДЕРНЫЙ РЕАКТОР", 300);
        Root.Next = circuits; circuits.Next = chips; chips.Next = turretTech; turretTech.Next = reactor;
    }

    public static bool IsUnlocked(string name)
    {
        for (var n = Root; n != null; n = n.Next) if (n.Name == name) return n.Unlocked;
        return false;
    }

    public static ResearchNode FirstUnfinished
    {
        get { for (var n = Root; n != null; n = n.Next) if (!n.Unlocked) return n; return null; }
    }

    // Гейтинг рецептов (вызывается из Recipes.Match)
    public static bool RecipeUnlocked(Recipe r)
    {
        Build();
        if (r.Machine == MachineType.Compactor && Contains(r.Outputs, Item.SteelIngot)) return IsUnlocked("Сталь");
        if (r.Machine == MachineType.Assembler)
        {
            if (Contains(r.Outputs, Item.Circuit)) return IsUnlocked("Схемы");
            if (Contains(r.Outputs, Item.ComputerChip)) return IsUnlocked("Чипы");
            if (Contains(r.Outputs, Item.GunBarrel) || Contains(r.Outputs, Item.TurretPart)) return IsUnlocked("Турели");
        }
        return true;
    }

    public static bool MachineUnlocked(MachineType t)
    {
        Build();
        return t switch
        {
            MachineType.Lab => IsUnlocked("Схемы"),
            MachineType.Turret => IsUnlocked("Турели"),
            _ => true,
        };
    }

    static bool Contains(Item[] arr, Item it) { foreach (var a in arr) if (a == it) return true; return false; }
}

// Мост для кода внутри namespace AF.Core (Recipes.Match -> гейтинг)
namespace AF.Core
{
    public static class ResearchGate
    {
        public static bool IsRecipeUnlocked(Recipe r) => ResearchTree.RecipeUnlocked(r);
    }
}

// Прогресс-синглтон: лаборатории зовут GameManager.Instance.Research.AddProgress(...)
public sealed class ResearchSystemState
{
    public ResearchNode Target;
    public float Progress;

    public ResearchNode CurrentTarget
    {
        get
        {
            ResearchTree.Build();
            Target ??= ResearchTree.FirstUnfinished;
            if (Target != null && Target.Unlocked) Target = ResearchTree.FirstUnfinished;
            return Target;
        }
    }

    public bool Done
    {
        get { ResearchTree.Build(); return ResearchTree.Root != null && ResearchTree.FirstUnfinished == null; }
    }

    public void AddProgress(float p)
    {
        var t = CurrentTarget;
        if (t == null) return;
        Progress += p;
        while (Progress >= t.Cost)
        {
            Progress -= t.Cost;
            t.Unlocked = true;
            GameManager.Instance.UI?.Toast($"Исследовано: {t.Name}!");
            t = ResearchTree.FirstUnfinished;
            if (t == null) break;
        }
        Target = t;
    }

    public void Tick() { /* прогресс идёт только от лабораторий */ }
}
