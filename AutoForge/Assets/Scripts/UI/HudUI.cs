// HUD целиком кодом (IMGUI + то, что не требует ручной сборки сцены в редакторе).
// Показывает: выбранную машину, инвентарь, исследование/цель, волну врагов, HP,
// тултипы, и окно программирования процессора (текстовое — как в Mindustry).
using System.Collections.Generic;
using System.Text;
using AF.Core;
using AF.Enemies;
using AF.Logic;
using UnityEngine;

public sealed class HudUI : MonoBehaviour
{
    public static HudUI Create()
    {
        var go = new GameObject("HudUI");
        return go.AddComponent<HudUI>();
    }

    public bool InputLocked => _editorFor != null;
    MachineEntity _editorFor;
    ProcessorVm _editorVm;
    string _toast = ""; float _toastT;
    MachineEntity _infoSel;

    GUIStyle _big, _small, _btn, _title;
    Texture2D _panelTex;
    bool _stylesReady;

    void BuildStyles()
    {
        if (_stylesReady) return;
        _stylesReady = true;
        _panelTex = new Texture2D(1, 1);
        _panelTex.SetPixel(0, 0, new Color(0.05f, 0.06f, 0.08f, 0.85f));
        _panelTex.Apply();
        _big = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
        _big.normal.textColor = Color.white;
        _small = new GUIStyle(GUI.skin.label) { fontSize = 13 };
        _small.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
        _title = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
        _title.normal.textColor = new Color(1f, 0.85f, 0.3f);
        _btn = new GUIStyle(GUI.skin.button) { fontSize = 13 };
    }

    public void Toast(string s) { _toast = s; _toastT = 4f; }

    public void OpenEditor(MachineEntity m)
    {
        _editorFor = m;
        _editorVm = ProcessorSystem.VmFor(m);
        if (_editorVm == null) { _editorVm = new ProcessorVm(); ProcessorSystem.All.Add((m, _editorVm)); }
    }

    void OnGUI()
    {
        BuildStyles();
        var gm = GameManager.Instance;
        if (gm == null || gm.World == null) return;

        // === верх-лево: цель/исследование ===
        GUI.Box(new Rect(10, 10, 340, 96), GUIContent.none);
        GUI.Label(new Rect(20, 14, 320, 24), "ЦЕЛЬ: исследовать всё до Реактора", _big);
        var target = gm.Research.CurrentTarget;
        if (target != null)
        {
            float frac = Mathf.Clamp01(gm.Research.Progress / target.Cost);
            GUI.Label(new Rect(20, 40, 320, 20), $"Сейчас: {target.Name}  ({gm.Research.Progress:0}/{target.Cost:0})", _small);
            GUI.Label(new Rect(20, 60, 320 * frac, 14), "", ProgressStyle());
        }
        else GUI.Label(new Rect(20, 40, 320, 20), "ВСЁ ИССЛЕДОВАНО!", _big);

        // === верх-право: волна/HP ===
        GUI.Box(new Rect(Screen.width - 260, 10, 250, 76), GUIContent.none);
        GUI.Label(new Rect(Screen.width - 250, 14, 240, 20), $"Волна: {EnemySystem.Wave}  через {EnemySystem.NextWaveIn:0}с", _small);
        GUI.Label(new Rect(Screen.width - 250, 34, 240, 20), $"Врагов: {EnemySystem.All.Count}", _small);
        GUI.Label(new Rect(Screen.width - 250, 54, 240, 20), $"HP игрока: {gm.PlayerHp:0}", _small);

        // === низ: панель строительства ===
        var types = (MachineType[])System.Enum.GetValues(typeof(MachineType));
        float w = types.Length * 96f + 20f;
        Rect bar = new(Screen.width / 2f - w / 2f, Screen.height - 84, w, 74);
        GUI.Box(bar, GUIContent.none);
        for (int i = 0; i < types.Length; i++)
        {
            var t = types[i];
            var r = new Rect(bar.x + 10 + i * 96, bar.y + 8, 90, 58);
            bool unlocked = ResearchTree.MachineUnlocked(t);
            GUI.color = unlocked ? Color.white : new Color(0.4f, 0.4f, 0.4f);
            if (GUI.Button(r, $"{i + 1}. {Machines.Defs[t].Name}\n({Machines.Defs[t].Cost})", _btn) && unlocked)
                gm.Selected = t;
            GUI.color = gm.Selected == t ? new Color(1f, 0.9f, 0.3f) : Color.white;
            if (gm.Selected == t) GUI.DrawTexture(new Rect(r.x, r.y + r.height - 4, r.width, 3), TexWhite());
            GUI.color = Color.white;
        }
        GUI.Label(new Rect(bar.x, bar.y - 20, w, 18),
            $"Выбрано: {Machines.Defs[gm.Selected].Name}, напр.: {Arrow(gm.SelectedDir)}  (R — повернуть, ЛКМ — строить, ПКМ — снести)", _small);

        // === инвентарь слева снизу ===
        var inv = gm.Inventory;
        int n = 0;
        foreach (var kv in inv)
        {
            GUI.Label(new Rect(10, Screen.height - 20 - n * 16, 260, 16), $"{Items.Name(kv.Key)}: {kv.Value}", _small);
            n++;
        }

        // === инфо по машине ===
        if (_infoSel != null && _infoSel.Alive)
        {
            var sb = new StringBuilder();
            sb.AppendLine(_infoSel.Def.Name + $"  [{_infoSel.Cell}]  HP {_infoSel.Hp:0}");
            sb.AppendLine("Вход: " + BufStr(_infoSel.InBuf));
            sb.AppendLine("Выход: " + BufStr(_infoSel.OutBuf));
            if (_infoSel.Type == MachineType.Processor) sb.AppendLine("Ctrl+клик — открыть код");
            GUI.Box(new Rect(10, 120, 300, 20 + 16 * 4), GUIContent.none);
            GUI.Label(new Rect(16, 124, 290, 80), sb.ToString(), _small);
        }

        // === редактор процессора ===
        if (_editorFor != null && _editorVm != null) DrawEditor();

        // === тост ===
        if (_toastT > 0f)
        {
            _toastT -= Time.deltaTime;
            GUI.Label(new Rect(Screen.width / 2f - 300, 100, 600, 40), _toast, _title);
        }

        if (gm.GameOver) GUI.Label(new Rect(Screen.width / 2f - 200, Screen.height / 2f, 400, 40), "ВЫ УНИЧТОЖЕНЫ (F5 — заново)", _title);
        if (gm.Victory) GUI.Label(new Rect(Screen.width / 2f - 200, Screen.height / 2f, 400, 40), "ПОБЕДА! (F5 — песочница)", _title);
    }

    void DrawEditor()
    {
        float ex = Screen.width - 460, ey = 100;
        GUI.Box(new Rect(ex, ey, 450, 420), GUIContent.none);
        GUI.Label(new Rect(ex + 10, ey + 6, 440, 22), "ПРОГРАММИРУЕМЫЙ ПРОЦЕССОР", _big);
        GUI.Label(new Rect(ex + 10, ey + 30, 440, 60),
            "SET/MOV/ADD/SUB/MUL rN,X | CMP rA,rB | JEQ/JGT/JLT/JMP N\nIN rN,K  (0=X врага*10, 1=дист, 2=камни, 3=сигнал)\nOUT X,CH | FIRE | NOP | // комментарий", _small);

        _editorVm.Source = GUI.TextArea(new Rect(ex + 10, ey + 84, 430, 220), _editorVm.Source);

        if (GUI.Button(new Rect(ex + 10, ey + 312, 120, 30), "КОМПИЛИРОВАТЬ", _btn))
        {
            _editorVm.Compile();
            Toast(string.IsNullOrEmpty(_editorVm.Error) ? "Программа загружена" : "Ошибка: " + _editorVm.Error);
        }
        if (GUI.Button(new Rect(ex + 140, ey + 312, 120, 30), "ЗАКРЫТЬ", _btn)) { _editorFor = null; _editorVm = null; }

        var regs = new StringBuilder("Регистры: ");
        for (int i = 0; i < 8; i++) regs.Append($"r{i}={_editorVm.Regs[i]} ");
        GUI.Label(new Rect(ex + 10, ey + 350, 430, 20), regs.ToString(), _small);
        GUI.Label(new Rect(ex + 10, ey + 370, 430, 20),
            $"Выходы: [0]={_editorVm.OutChannels[0]:0} [1]={_editorVm.OutChannels[1]:0} [2]={_editorVm.OutChannels[2]:0} [3]={_editorVm.OutChannels[3]:0}", _small);
        if (!string.IsNullOrEmpty(_editorVm.Error))
            GUI.Label(new Rect(ex + 10, ey + 392, 430, 20), "! " + _editorVm.Error, _title);
    }

    public void SelectInfo(MachineEntity m) { _infoSel = m; }

    static string BufStr(Dictionary<Item, int> d)
    {
        if (d.Count == 0) return "пусто";
        var sb = new StringBuilder();
        foreach (var kv in d) sb.Append($"{Items.Name(kv.Key)}:{kv.Value} ");
        return sb.ToString();
    }

    static string Arrow(int dir) => dir switch { 0 => "→(+X)", 1 => "↑(+Z)", 2 => "←(-X)", _ => "↓(-Z)" };

    static GUIStyle _prog;
    static GUIStyle ProgressStyle()
    {
        if (_prog == null)
        {
            _prog = new GUIStyle();
            var t = new Texture2D(1, 1); t.SetPixel(0, 0, new Color(0.3f, 0.8f, 0.4f)); t.Apply();
            _prog.normal.background = t;
        }
        return _prog;
    }
    static Texture2D _white;
    static Texture2D TexWhite()
    {
        if (_white == null) { _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply(); }
        return _white;
    }
}
