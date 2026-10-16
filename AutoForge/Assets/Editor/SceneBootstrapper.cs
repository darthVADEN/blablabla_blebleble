#if UNITY_EDITOR
// Генерирует Assets/Scenes/Main.unity автоматически при открытии проекта (Unity Hub -> Open Project).
// Вся игровая сцена строится кодом в рантайме (Boot.cs), тут только пустая сцена-заготовка.
using UnityEditor;
using UnityEditor.SceneManagement;

public static class SceneBootstrapper
{
    [MenuItem("AutoForge/Create Main Scene")]
    public static void CreateMain()
    {
        if (!System.IO.Directory.Exists("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Main.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Main.unity", true) };
    }

    [InitializeOnLoadMethod]
    static void EnsureScene()
    {
        if (System.IO.File.Exists("Assets/Scenes/Main.unity")) return;
        EditorApplication.delayCall += CreateMain;
    }
}
#endif
