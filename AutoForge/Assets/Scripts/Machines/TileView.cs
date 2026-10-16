// Рендер тайлов ресурсов — процедурные цвета, примитивы. Никаких ассетов не нужно.
using System.Collections.Generic;
using AF.Core;
using UnityEngine;

public static class TileView
{
    static Transform _root;
    static readonly Dictionary<Vector2Int, GameObject> _objs = new();

    public static void Init()
    {
        var go = new GameObject("Tiles"); _root = go.transform;
    }

    public static void Spawn(Tile t)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = $"tile_{t.Pos}";
        Object.Destroy(cube.GetComponent<Collider>()); // кликаем сквозь них на ground-plane
        cube.transform.SetParent(_root);
        cube.transform.position = new Vector3(t.Pos.x + 0.5f - 0.5f, 0.02f, t.Pos.y);
        cube.transform.localScale = new Vector3(0.92f, 0.04f, 0.92f);
        Paint(cube, t);
        _objs[t.Pos] = cube;
    }

    public static void Refresh(Vector2Int cell)
    {
        if (_objs.TryGetValue(cell, out var o))
        {
            var t = GameManager.Instance.World.Get(cell);
            if (t.Machine != null || t.Richness <= 0f) { Object.Destroy(o); _objs.Remove(cell); return; }
            Paint(o, t);
        }
    }

    static void Paint(GameObject o, Tile t)
    {
        var r = o.GetComponent<Renderer>();
        Color c = t.Res switch
        {
            TileResource.Iron => new Color(0.75f, 0.45f, 0.35f),
            TileResource.Copper => new Color(0.85f, 0.42f, 0.18f),
            TileResource.Coal => new Color(0.15f, 0.15f, 0.17f),
            _ => new Color(0.3f, 0.35f, 0.28f),
        };
        // истощение -> бледнее
        float k = Mathf.Clamp01(t.Richness);
        r.material.color = Color.Lerp(new Color(0.25f, 0.3f, 0.22f), c, k);
    }
}
