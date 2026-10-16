// Рендер машин: примитивы + анимация "как в Create" — вращающиеся шестерни дробилки,
// поршень пресса (ход вниз по Progress), лента конвейера (прыгает по тикам), ствол турели.
using System.Collections.Generic;
using AF.Core;
using UnityEngine;

public static class MachineView
{
    static Transform _root;
    static readonly Dictionary<Vector2Int, MachineVisual> _vis = new();

    struct MachineVisual
    {
        public GameObject Root; public Transform Spinner; public Transform Piston;
        public Transform Barrel; public List<Transform> BeltItems;
        public MachineEntity Entity;
    }

    public static void Init()
    {
        var go = new GameObject("Machines"); _root = go.transform;
    }

    public static void SyncWorld()
    {
        var w = GameManager.Instance.World;
        // новые
        foreach (var m in w.MachinesAll)
            if (!_vis.ContainsKey(m.Cell)) Build(m);
        // удалённые
        var kill = new List<Vector2Int>();
        foreach (var kv in _vis) if (w.At(kv.Key) != kv.Value.Entity) kill.Add(kv.Key);
        foreach (var k in kill) { Object.Destroy(_vis[k].Root); _vis.Remove(k); }
    }

    static void Build(MachineEntity m)
    {
        var root = new GameObject($"m_{m.Type}_{m.Cell}");
        root.transform.SetParent(_root);
        root.transform.position = new Vector3(m.Cell.x, 0f, m.Cell.y);

        MachineVisual v = new() { Root = root, Entity = m, BeltItems = new List<Transform>() };
        Color col = m.Type switch
        {
            MachineType.Driller => new Color(0.5f, 0.55f, 0.6f),
            MachineType.Crusher => new Color(0.6f, 0.45f, 0.3f),
            MachineType.Washer => new Color(0.3f, 0.55f, 0.65f),
            MachineType.Compactor => new Color(0.55f, 0.5f, 0.25f),
            MachineType.Smelter => new Color(0.7f, 0.3f, 0.2f),
            MachineType.Assembler => new Color(0.35f, 0.5f, 0.35f),
            MachineType.Conveyor => new Color(0.3f, 0.3f, 0.38f),
            MachineType.Lab => new Color(0.4f, 0.35f, 0.7f),
            MachineType.Turret => new Color(0.7f, 0.65f, 0.2f),
            MachineType.Chest => new Color(0.5f, 0.38f, 0.25f),
            MachineType.Processor => new Color(0.2f, 0.7f, 0.55f),
            _ => Color.gray,
        };

        if (m.Type == MachineType.Conveyor)
        {
            var body = Part(root.transform, PrimitiveType.Cube, new Vector3(0.5f, 0.08f, 0.5f), new Vector3(0.5f, 0.5f), col * 0.8f);
            // стрелка направления
            var arrow = Part(root.transform, PrimitiveType.Cube, new Vector3(0.15f, 0.02f, 0.15f), new Vector3(0.5f + World.DirOff[m.Dir].x * 0.25f, 0.6f, 0.5f + World.DirOff[m.Dir].y * 0.25f), Color.yellow);
        }
        else
        {
            var body = Part(root.transform, PrimitiveType.Cube, new Vector3(0.8f, 0.5f, 0.8f), new Vector3(0.5f, 0.25f, 0.5f), col);
            if (m.Def.HasCrafting)
            {
                // шестерня-спиннер сверху
                v.Spinner = Part(root.transform, PrimitiveType.Cylinder, new Vector3(0.3f, 0.06f, 0.3f), new Vector3(0.5f, 0.55f, 0.5f), col * 1.3f).transform;
            }
            if (m.Type == MachineType.Compactor || m.Type == MachineType.Crusher)
            {
                v.Piston = Part(root.transform, PrimitiveType.Cube, new Vector3(0.5f, 0.15f, 0.5f), new Vector3(0.5f, 0.7f, 0.5f), Color.white * 0.8f).transform;
            }
            if (m.Type == MachineType.Turret)
            {
                var base_ = Part(root.transform, PrimitiveType.Cylinder, new Vector3(0.5f, 0.1f, 0.5f), new Vector3(0.5f, 0.55f, 0.5f), col);
                v.Barrel = Part(root.transform, PrimitiveType.Cube, new Vector3(0.12f, 0.12f, 0.7f), new Vector3(0.5f, 0.7f, 0.8f), Color.black).transform;
            }
            if (m.Type == MachineType.Processor)
            {
                var led = Part(root.transform, PrimitiveType.Cube, new Vector3(0.1f, 0.1f, 0.1f), new Vector3(0.8f, 0.6f, 0.8f), Color.green);
                led.name = "LED";
            }
        }
        _vis[m.Cell] = v;
    }

    static GameObject Part(Transform parent, PrimitiveType p, Vector3 scale, Vector3 pos, Color c)
    {
        var go = GameObject.CreatePrimitive(p);
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localScale = scale;
        go.transform.localPosition = pos;
        go.GetComponent<Renderer>().material.color = c;
        return go;
    }

    public static void Destroy(Vector2Int cell)
    {
        if (_vis.TryGetValue(cell, out var v)) { Object.Destroy(v.Root); _vis.Remove(cell); }
    }

    // вызывать из MonoBehaviour Update (GameManager дергает через LateUpdate-хук)
    public static void Animate(float dt)
    {
        SyncWorld();
        foreach (var kv in _vis)
        {
            var v = kv.Value; var m = v.Entity;
            if (v.Spinner != null && m.Working)
                v.Spinner.Rotate(0, 360f * dt, 0);
            if (v.Piston != null)
            {
                float t = m.Working ? Mathf.Abs(Mathf.Sin(m.Progress / CurrentSec(m) * Mathf.PI)) : 0f;
                v.Piston.localPosition = new Vector3(0.5f, 0.7f - t * 0.25f, 0.5f);
            }
            if (v.Barrel != null && v.Barrel.parent != null)
            {
                var e = AF.Enemies.EnemySystem.Nearest(m.Cell.ToVector3XZ(), 12f);
                if (e != null)
                {
                    // ствол — дочерний примитив с неравномерным масштабом, поэтому крутим его родителя-пустышку
                    var pivot = v.Barrel.parent;
                    var look = e.Pos - new Vector3(pivot.position.x, e.Pos.y, pivot.position.z);
                    if (look.sqrMagnitude > 0.01f)
                        pivot.rotation = Quaternion.Slerp(pivot.rotation, Quaternion.LookRotation(look), 10f * dt);
                }
            }
            // предметы на ленте
            while (v.BeltItems.Count > m.Belt.Count)
            { Object.Destroy(v.BeltItems[^1].gameObject); v.BeltItems.RemoveAt(v.BeltItems.Count - 1); }
            while (v.BeltItems.Count < m.Belt.Count)
            {
                var it = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(it.GetComponent<Collider>());
                it.transform.SetParent(v.Root.transform, false);
                it.transform.localScale = Vector3.one * 0.22f;
                v.BeltItems.Add(it.transform);
            }
            for (int i = 0; i < v.BeltItems.Count; i++)
            {
                var t = v.BeltItems[i];
                var d = World.DirOff[m.Dir];
                float f = (i + 0.5f) / MachineEntity.BeltCap;
                t.localPosition = new Vector3(0.5f + d.x * (f - 0.5f), 0.25f, 0.5f + d.y * (f - 0.5f));
                t.GetComponent<Renderer>().material.color = ItemColor(m.Belt[i]);
            }
        }
    }

    static float CurrentSec(MachineEntity m)
    {
        var r = Recipes.Match(m.Type, m.InBuf);
        return r != null ? r.Seconds : 1f;
    }

    public static Color ItemColor(Item it) => it switch
    {
        Item.Cobblestone => new Color(0.5f, 0.5f, 0.5f),
        Item.Gravel => new Color(0.65f, 0.62f, 0.55f),
        Item.IronOre => new Color(0.8f, 0.5f, 0.4f),
        Item.IronNugget => new Color(0.85f, 0.75f, 0.6f),
        Item.Silicon => new Color(0.6f, 0.7f, 0.8f),
        Item.Coal => new Color(0.15f, 0.15f, 0.15f),
        Item.CopperIngot => new Color(0.9f, 0.45f, 0.2f),
        Item.IronIngot => new Color(0.8f, 0.82f, 0.85f),
        Item.SteelIngot => new Color(0.6f, 0.65f, 0.75f),
        Item.Circuit => new Color(0.2f, 0.7f, 0.3f),
        Item.ComputerChip => new Color(0.9f, 0.8f, 0.2f),
        Item.Battery => new Color(0.3f, 0.8f, 0.8f),
        Item.GunBarrel => new Color(0.3f, 0.3f, 0.35f),
        Item.TurretPart => new Color(0.8f, 0.6f, 0.1f),
        _ => Color.white,
    };
}
