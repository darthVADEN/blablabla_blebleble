// Рендер врагов: дроны (красные конусы), харвестеры (большие оранжевые кубы), HP-бары.
using System.Collections.Generic;
using AF.Enemies;
using UnityEngine;

public static class EnemyView
{
    static Transform _root;
    static readonly Dictionary<EnemyUnit, Visual> _vis = new();
    struct Visual { public GameObject Root; public Transform HpFill; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => _vis.Clear();

    public static void Init()
    {
        var go = new GameObject("Enemies"); _root = go.transform;
        _vis.Clear();
    }

    public static void Sync()
    {
        foreach (var e in EnemySystem.All)
            if (!_vis.ContainsKey(e)) Build(e);

        var kill = new List<EnemyUnit>();
        foreach (var kv in _vis) if (kv.Key.Dead) { Object.Destroy(kv.Value.Root); kill.Add(kv.Key); }
        foreach (var k in kill) _vis.Remove(k);

        foreach (var kv in _vis)
        {
            var v = kv.Value; var e = kv.Key;
            v.Root.transform.position = e.Pos;
            float hpFrac = Mathf.Clamp01(e.Hp / e.MaxHp);
            if (v.HpFill != null)
            {
                v.HpFill.localScale = new Vector3(hpFrac * 0.8f, 0.06f, 0.06f);
                v.HpFill.GetComponent<Renderer>().material.color = Color.Lerp(Color.red, Color.green, hpFrac);
            }
        }
    }

    static void Build(EnemyUnit e)
    {
        var root = new GameObject($"enemy_{e.Kind}");
        root.transform.SetParent(_root);
        Color col = e.Kind == EnemyKind.Raider ? new Color(0.8f, 0.15f, 0.15f) : new Color(0.9f, 0.5f, 0.1f);
        float s = e.Kind == EnemyKind.Harvester ? 0.9f : 0.45f;

        var body = GameObject.CreatePrimitive(e.Kind == EnemyKind.Raider ? PrimitiveType.Sphere : PrimitiveType.Cube);
        Object.Destroy(body.GetComponent<Collider>());
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0, 0.4f, 0);
        body.transform.localScale = Vector3.one * s;
        body.GetComponent<Renderer>().material.color = col;

        // HP-бар
        var hp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(hp.GetComponent<Collider>());
        hp.transform.SetParent(root.transform, false);
        hp.transform.localPosition = new Vector3(0, 1.1f, 0);
        hp.transform.localScale = new Vector3(0.8f, 0.06f, 0.06f);
        hp.GetComponent<Renderer>().material.color = Color.green;

        _vis[e] = new Visual { Root = root, HpFill = hp.transform };
    }
}
