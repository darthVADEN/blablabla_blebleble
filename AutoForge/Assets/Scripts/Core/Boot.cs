// Bootstrapper: Unity требует, чтобы сцена содержала хотя бы один объект.
// Этот скрипт сам создаёт GameManager при загрузке первой сцены — вам не нужно
// ничего перетаскивать в редакторе. Просто Open Scene "Main" и Play.
using UnityEngine;

public static class Boot
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Start()
    {
        if (GameManager.Instance != null) return;
        var go = new GameObject("AutoForge");
        go.AddComponent<GameManager>();
        Object.DontDestroyOnLoad(go);
        Application.targetFrameRate = 60;
    }
}
