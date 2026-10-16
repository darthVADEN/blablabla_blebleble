// GameManager — сердце игры: мир, инвентарь игрока, исследования (цель), ввод строительства.
using System.Collections.Generic;
using AF.Core;
using AF.Enemies;
using AF.Logic;
using UnityEngine;

public sealed class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public World World { get; private set; }
    public ResearchSystemState Research => _research;
    readonly ResearchSystemState _research = new();
    public HudUI UI { get; private set; }

    // игрок
    public Vector3 PlayerPos = new(32, 0.9f, 32);
    public float PlayerHp = 100f;
    public readonly Dictionary<Item, int> Inventory = new();
    public MachineType Selected = MachineType.Conveyor;
    public int SelectedDir = 0;
    public bool GameOver, Victory;
    Transform _playerGo;

    // камера top-down
    Camera _cam;

    void Awake()
    {
        Instance = this;
        Recipes.Init();
        ResearchTree.Build();
        World = new World();
        EnemySystem.SetBase(new Vector2Int(32, 32));

        BuildScene();
        UI = HudUI.Create();
        GameNet.OnTick += OnNetTick;

        // стартовый набор
        Give(Item.Cobblestone, 30); Give(Item.IronIngot, 8); Give(Item.CopperIngot, 4);

        Debug.Log("[AutoForge] ready. Machines tick at " + (1 / GameNet.TICK) + " Hz");
    }

    void BuildScene()
    {
        // свет
        var sun = new GameObject("Sun"); var l = sun.AddComponent<Light>();
        l.type = LightType.Directional; l.intensity = 1.1f;
        sun.transform.rotation = Quaternion.Euler(50, -30, 0);

        // земля
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(World.SIZE, 0.5f, World.SIZE);
        ground.transform.position = new Vector3(World.SIZE / 2f, -0.25f, World.SIZE / 2f);
        ground.GetComponent<Renderer>().material.color = new Color(0.25f, 0.3f, 0.22f);

        // тайлы ресурсов (видимые пятна)
        TileView.Init();
        for (int x = 0; x < World.SIZE; x++)
            for (int y = 0; y < World.SIZE; y++)
                if (World.Get(new Vector2Int(x, y)).Res != TileResource.None)
                    TileView.Spawn(World.Get(new Vector2Int(x, y)));

        MachineView.Init();
        DropView.Init();
        EnemyView.Init();

        // камера сверху под углом
        var camGo = new GameObject("Cam"); _cam = camGo.AddComponent<Camera>();
        camGo.tag = "MainCamera";
        _cam.clearFlags = CameraClearFlags.SolidColor;
        _cam.backgroundColor = new Color(0.08f, 0.09f, 0.12f);
        _cam.orthographic = true; _cam.orthographicSize = 14f;
        camGo.transform.position = PlayerPos + new Vector3(0, 20, -12);
        camGo.transform.LookAt(PlayerPos);

        var listener = new GameObject("Listener"); listener.AddComponent<AudioListener>();

        SpawnPlayerAvatar();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F5)) { DestroyImmediate(gameObject); UnityEngine.SceneManagement.SceneManager.LoadScene(0); return; }
        HandleInput();
        GameNet.Update(Time.deltaTime); // все системы живут на тиках сети
        World.DropTick(Time.deltaTime);
        TurretSystem.Tick();             // трассеры снарядов — в кадрах
        FollowCam();
        MachineView.Animate(Time.deltaTime);
        DropView.Sync();
        EnemyView.Sync();
        if (_playerGo != null) _playerGo.position = PlayerPos;
    }

    void OnNetTick()
    {
        if (GameOver || Victory) return;
        World.Tick();
        TurretSystem.LogicTick();
        ProcessorSystem.Tick();
        EnemySystem.Tick();
        Research.Tick();
        CheckVictoryDefeat();
    }

    void SpawnPlayerAvatar()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = "Player";
        _playerGo = go.transform;
        go.GetComponent<Renderer>().material.color = new Color(0.9f, 0.9f, 0.3f);
    }

    void CheckVictoryDefeat()
    {
        if (Research.Done) { Victory = true; UI?.Toast("ПОБЕДА: реактор собран! Автоматизация победила."); }
        if (PlayerHp <= 0f) { GameOver = true; UI?.Toast("Вы уничтожены. F5 — начать заново."); }
    }

    // ---------- ввод: строительство/разбор/конвейеры ----------
    void HandleInput()
    {
        if (UI != null && UI.InputLocked) return; // пока открыт редактор процессора — не строим

        // выбор машины колесом / цифрами
        var types = (MachineType[])System.Enum.GetValues(typeof(MachineType));
        if (Input.mouseScrollDelta.y > 0) Selected = Next(types, Selected, 1);
        if (Input.mouseScrollDelta.y < 0) Selected = Next(types, Selected, -1);
        for (int i = 0; i < types.Length && i < 9; i++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + i)) Selected = types[i];

        if (Input.GetKeyDown(KeyCode.R)) SelectedDir = (SelectedDir + 1) % 4; // поворот конвейера

        var cell = MouseCell();
        if (cell == null) return;

        if (Input.GetMouseButtonDown(0) && !IsPointerOverUI())
        {
            var c = cell.Value;
            var existing = World.At(c);
            if (existing != null)
            {
                if (existing.Type == MachineType.Processor && Input.GetKey(KeyCode.LeftControl)) UI.OpenEditor(existing);
                else if (Input.GetKey(KeyCode.Q)) { RemoveRefund(existing); }
                else UI.SelectInfo(existing);
            }
            else TryBuild(Selected, c);
        }
        if (Input.GetMouseButtonDown(1)) // ПКМ — снести руками
        {
            var m = World.At(cell.Value);
            if (m != null) RemoveRefund(m);
        }

        // движение WASD
        Vector3 mv = Vector3.zero;
        if (Input.GetKey(KeyCode.W)) mv.z += 1; if (Input.GetKey(KeyCode.S)) mv.z -= 1;
        if (Input.GetKey(KeyCode.A)) mv.x -= 1; if (Input.GetKey(KeyCode.D)) mv.x += 1;
        if (mv != Vector3.zero)
            PlayerPos = Vector3.ClampMagnitude(PlayerPos + mv.normalized * 7f * Time.deltaTime, 600);
        PlayerPos.x = Mathf.Clamp(PlayerPos.x, 0, World.SIZE - 1);
        PlayerPos.z = Mathf.Clamp(PlayerPos.z, 0, World.SIZE - 1);

        // E — подобрать предметы рядом
        if (Input.GetKeyDown(KeyCode.E))
        {
            foreach (var d in World.PickupsInRadius(PlayerPos, 1.6f)) Give(d.Item, 1);
        }
        // Space — удар по врагам рядом (руками, плохо но можно)
        if (Input.GetKeyDown(KeyCode.Space)) EnemySystem.Splash(PlayerPos, 2.2f, 10f);
    }

    void RemoveRefund(MachineEntity m)
    {
        // возврат половины стоимости предметами в инвентарь условно: просто даём 1 ресурс tier
        World.Remove(m.Cell);
        ProcessorSystem.Detach(m);
        TileView.Refresh(m.Cell);
        MachineView.Destroy(m.Cell);
    }

    static MachineType Next(MachineType[] all, MachineType cur, int d)
    {
        int i = System.Array.IndexOf(all, cur);
        i = (i + d + all.Length) % all.Length; return all[i];
    }

    Vector2Int? MouseCell()
    {
        if (_cam == null) return null;
        var ray = _cam.ScreenPointToRay(Input.mousePosition);
        var plane = new Plane(Vector3.up, Vector3.zero);
        if (plane.Raycast(ray, out float e))
        {
            var p = ray.GetPoint(e);
            var c = new Vector2Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.z));
            if (World.InBounds(c)) return c;
        }
        return null;
    }

    bool IsPointerOverUI() => UnityEngine.EventSystems.EventSystem.current != null &&
                              UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();

    public void TryBuild(MachineType type, Vector2Int cell)
    {
        if (!ResearchTree.MachineUnlocked(type)) { UI?.Toast($"{Machines.Defs[type].Name}: сначала исследуйте технологию"); return; }
        // цена: 2 камня за конвейер и пр. — упрощённая экономика
        float costUnits = Machines.Defs[type].Cost;
        int have = Count(Item.Cobblestone) * 1 + Count(Item.IronIngot) * 4 + Count(Item.SteelIngot) * 10;
        if (have < costUnits) { UI?.Toast("Не хватает материалов (нужна 'энергия сборки': камень/железо/сталь)"); return; }
        Spend(costUnits);
        if (World.Place(type, cell, out string err))
        {
            var m = World.At(cell);
            if (type == MachineType.Conveyor && m != null) m.Dir = SelectedDir;
            ProcessorSystem.Attach(m); // no-op для не-процессоров
            UI?.Toast(err ?? $"Построен {Machines.Defs[type].Name}");
        }
        else UI?.Toast(err);
    }

    // платёж: сначала камнем, потом железом, потом сталью
    void Spend(float units)
    {
        while (units > 0.01f)
        {
            if (units >= 10 && Count(Item.SteelIngot) > 0) { Take(Item.SteelIngot, 1); units -= 10; }
            else if (units >= 4 && Count(Item.IronIngot) > 0) { Take(Item.IronIngot, 1); units -= 4; }
            else if (Count(Item.Cobblestone) > 0) { Take(Item.Cobblestone, 1); units -= 1; }
            else break;
        }
    }

    void FollowCam()
    {
        var target = PlayerPos + new Vector3(0, 20, -12);
        _cam.transform.position = Vector3.Lerp(_cam.transform.position, target, 8f * Time.deltaTime);
        _cam.transform.LookAt(PlayerPos);
    }

    public void DamagePlayer(float d) { PlayerHp -= d; }

    public void Give(Item it, int n) { Inventory.TryGetValue(it, out int c); Inventory[it] = c + n; }
    public void Take(Item it, int n) { Inventory.TryGetValue(it, out int c); if (c <= n) Inventory.Remove(it); else Inventory[it] = c - n; }
    public int Count(Item it) { Inventory.TryGetValue(it, out int c); return c; }
}
