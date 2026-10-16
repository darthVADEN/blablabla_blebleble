// Рендер предметов на земле + пул примитивов.
using System.Collections.Generic;
using AF.Core;
using UnityEngine;

public static class DropView
{
    static Transform _root;
    static readonly List<Transform> _pool = new();

    public static void Init()
    {
        var go = new GameObject("Drops"); _root = go.transform;
    }

    public static void Sync()
    {
        var drops = GameManager.Instance.World.Drops;
        while (_pool.Count < drops.Count)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(c.GetComponent<Collider>());
            c.transform.localScale = Vector3.one * 0.25f;
            c.transform.SetParent(_root, false);
            _pool.Add(c.transform);
        }
        for (int i = 0; i < _pool.Count; i++)
        {
            var t = _pool[i];
            if (i < drops.Count)
            {
                t.gameObject.SetActive(true);
                t.position = drops[i].Pos;
                t.Rotate(0, 90f * Time.deltaTime, 0);
                t.GetComponent<Renderer>().material.color = MachineView.ItemColor(drops[i].Item);
            }
            else t.gameObject.SetActive(false);
        }
    }
}
