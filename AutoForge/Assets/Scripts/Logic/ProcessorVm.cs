// Программируемый процессор (как в Mindustry/Omega Crafters).
// Язык — компактный ассемблероподобный DSL, по одной инструкции на тик (10 Гц):
//   MOV rA, rB            регистр = регистру
//   SET rA, 5             константа в регистр
//   ADD rA, rB / SUB / MUL
//   CMP rA, rB -> ZF/SF  (флаги)
//   JGT n / JLT n / JEQ n / JNE n / JMP n   (переход на строку n)
//   IN rA, k              прочитать сенсор: k=0 ближайший враг X*10, k=1 дистанция до врага,
//                         k=2 кол-во предметов в InBuf машины, k=3 внешний сигнал (входной провод)
//   OUT v, ch             запись выходного сигнала в канал ch (0..3) — влияет на турели/соседей
//   FIRE                  попытаться выстрелить (если рядом есть турель с амуницией — та стреляет)
//   NOP
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace AF.Logic
{
    public sealed class ProcessorVm
    {
        public readonly int[] Regs = new int[8];      // r0..r7
        public readonly float[] OutChannels = new float[4];
        public string Source = "SET r0, 1\nOUT r0, 0";
        public string Error;
        int _ip; bool _zf, _sf;

        readonly List<Ins> _prog = new();
        readonly Dictionary<AF.Core.MachineEntity, Ins[]> _cache = new();

        struct Ins { public string Op; public string[] A; }

        public void Compile()
        {
            _prog.Clear(); Error = null;
            foreach (var raw in Source.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("//")) continue;
                var parts = line.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
                var ins = new Ins { Op = parts[0].ToUpperInvariant(), A = new string[parts.Length - 1] };
                for (int i = 1; i < parts.Length; i++) ins.A[i - 1] = parts[i];
                _prog.Add(ins);
            }
            if (_prog.Count == 0) Error = "пустая программа";
        }

        int Read(string tok) // "r3" | число
        {
            if (tok != null && tok.StartsWith("r") && tok.Length > 1 &&
                int.TryParse(tok.Substring(1), out int ri) && ri >= 0 && ri < 8) return Regs[ri];
            if (int.TryParse(tok, NumberStyles.Any, CultureInfo.InvariantCulture, out int c)) return c;
            return 0;
        }
        void Write(string tok, int v)
        {
            if (tok != null && tok.StartsWith("r") && tok.Length > 1 &&
                int.TryParse(tok.Substring(1), out int ri) && ri >= 0 && ri < 8) Regs[ri] = v;
        }

        // один такт CPU. sensorCb предоставляет внешние значения (пишет World/GameManager).
        public void Tick(AF.Core.MachineEntity host, System.Func<int, int> sensors)
        {
            if (_prog.Count == 0) return;
            for (int guard = 0; guard < 4; guard++) // до 4 инструкций за игровой тик — "частота" CPU
            {
                if (_ip < 0 || _ip >= _prog.Count) { _ip = 0; return; }
                var i = _prog[_ip++];
                switch (i.Op)
                {
                    case "NOP": break;
                    case "SET": Write(i.A[0], Read(i.A?.Length > 1 ? i.A[1] : null)); break;
                    case "MOV": Write(i.A[0], Read(i.A?.Length > 1 ? i.A[1] : null)); break;
                    case "ADD": Write(i.A[0], Read(i.A[0]) + Read(i.A?.Length > 1 ? i.A[1] : null)); break;
                    case "SUB": Write(i.A[0], Read(i.A[0]) - Read(i.A?.Length > 1 ? i.A[1] : null)); break;
                    case "MUL": Write(i.A[0], Read(i.A[0]) * Read(i.A?.Length > 1 ? i.A[1] : null)); break;
                    case "CMP":
                        {
                            int a = Read(i.A[0]), b = Read(i.A?.Length > 1 ? i.A[1] : null);
                            _zf = a == b; _sf = a < b;
                        }
                        break;
                    case "JEQ": if (_zf) _ip = Read(i.A[0]); break;
                    case "JNE": if (!_zf) _ip = Read(i.A[0]); break;
                    case "JGT": if (!_zf && !_sf) _ip = Read(i.A[0]); break;
                    case "JLT": if (_sf) _ip = Read(i.A[0]); break;
                    case "JMP": _ip = Read(i.A[0]); break;
                    case "IN":  Write(i.A[0], sensors != null && i.A?.Length > 1 ? sensors(Read(i.A[1])) : 0); break;
                    case "OUT":
                        {
                            int ch = Read(i.A?.Length > 1 ? i.A[1] : null);
                            if (ch >= 0 && ch < 4) OutChannels[ch] = Read(i.A[0]);
                        }
                        break;
                    case "FIRE": TurretSystem.TryFireNearest(host.Cell); break;
                    default: Error = "неизвестная инструкция: " + i.Op; _ip = 0; return;
                }
            }
        }
    }

    // Реестр всех процессоров мира + раздача сигналов соседям
    public static class ProcessorSystem
    {
        public static readonly List<(AF.Core.MachineEntity host, ProcessorVm vm)> All = new();

        public static void Attach(AF.Core.MachineEntity m)
        {
            if (m == null || m.Type != AF.Core.MachineType.Processor) return;
            var vm = new ProcessorVm(); vm.Compile();
            All.Add((m, vm));
        }
        public static void Detach(AF.Core.MachineEntity m) => All.RemoveAll(x => x.host == m);

        public static ProcessorVm VmFor(AF.Core.MachineEntity m)
        {
            foreach (var x in All) if (x.host == m) return x.vm;
            return null;
        }

        public static void Tick()
        {
            for (int idx = All.Count - 1; idx >= 0; idx--)
            {
                var (host, vm) = All[idx];
                if (!host.Alive) { All.RemoveAt(idx); continue; }

                vm.Tick(host, sensor =>
                {
                    switch (sensor)
                    {
                        case 0: // X ближайшего врага (умножено на 10)
                        {
                            var e = Enemies.EnemySystem.Nearest(host.Cell.ToVector3XZ());
                            return e != null ? Mathf.RoundToInt(e.Pos.x * 10f) : -999;
                        }
                        case 1: // дистанция
                        {
                            var e = Enemies.EnemySystem.Nearest(host.Cell.ToVector3XZ());
                            return e != null ? Mathf.RoundToInt(Vector2.Distance(new Vector2(host.Cell.x, host.Cell.y), new Vector2(e.Pos.x, e.Pos.z))) : 999;
                        }
                        case 2: return AF.Core.MachineEntity.Count(host.InBuf, AF.Core.Item.Cobblestone);
                        case 3: return Mathf.RoundToInt(host.SignalIn);
                        default: return 0;
                    }
                });

                // распространение сигналов на соседей (как провода)
                for (int k = 0; k < 4; k++)
                {
                    var nb = host.World.At(host.Cell + AF.Core.World.DirOff[k]);
                    if (nb != null) nb.SignalIn = vm.OutChannels[0];
                }
            }
        }
    }
}
