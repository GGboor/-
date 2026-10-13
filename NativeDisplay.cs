using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace ScreenPilot
{
    [StructLayout(LayoutKind.Sequential)] public struct Luid { public uint Low; public int High; public override string ToString() { return High + ":" + Low; } }
    [StructLayout(LayoutKind.Sequential)] public struct Rational { public uint Numerator, Denominator; }
    [StructLayout(LayoutKind.Sequential)] public struct Source { public Luid Adapter; public uint Id, Mode, Status; }
    [StructLayout(LayoutKind.Sequential)] public struct Target
    {
        public Luid Adapter; public uint Id, Mode, Technology, Rotation, Scaling;
        public Rational Refresh; public uint Scan; public int Available; public uint Status;
    }
    [StructLayout(LayoutKind.Sequential)] public struct DisplayPath { public Source Source; public Target Target; public uint Flags; }
    // Preserve all 48 bytes of the native union, including timings needed for exact rollback.
    [StructLayout(LayoutKind.Explicit, Size = 64)] public struct Mode
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(4)] public uint Id;
        [FieldOffset(8)] public Luid Adapter;
        [FieldOffset(16)] public ulong D0;
        [FieldOffset(24)] public ulong D1;
        [FieldOffset(32)] public ulong D2;
        [FieldOffset(40)] public ulong D3;
        [FieldOffset(48)] public ulong D4;
        [FieldOffset(56)] public ulong D5;
        public uint Width { get { return (uint)D0; } }
        public uint Height { get { return (uint)(D0 >> 32); } }
        public int X { get { return unchecked((int)(D1 >> 32)); } }
        public int Y { get { return unchecked((int)D2); } }
    }
    [StructLayout(LayoutKind.Sequential)] public struct Header { public uint Type, Size; public Luid Adapter; public uint Id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct TargetName
    {
        public Header Header; public uint Flags, Technology; public ushort Manufacturer, Product; public uint Connector;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Friendly;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Device;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct SourceName
    {
        public Header Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
    }
    public class DeviceBinding { public string Key,TargetKey; }
    public class Snapshot { public DisplayPath[] Paths; public Mode[] Modes; public DeviceBinding[] Devices; }
    [StructLayout(LayoutKind.Sequential)] public struct VideoData { public ulong D0,D1,D2,D3,D4,D5; }
    [StructLayout(LayoutKind.Sequential)] public struct PreferredMode { public Header Header; public uint Width,Height; public VideoData Video; }
    public class MonitorInfo
    {
        public string Key, TargetKey, Name, GdiName, Detail;
        public string TypeLabel, AdapterName;
        public Rectangle DesktopBounds;
        public bool HasPosition, Primary;
        public string PositionText { get { return !Active ? "已关闭 · 不占桌面位置" : !HasPosition ? "当前位置读取中" : "位置 ("+DesktopBounds.X+", "+DesktopBounds.Y+")"+(Primary?" · 主屏幕":""); } }
        public bool Active;
        public override string ToString() { return Name + (Active ? "（已开启）" : "（已关闭）"); }
    }
    public static class NativeDisplay
    {
        [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct DisplayDevice
        {
            public uint Size;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string Name;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Description;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Id;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string RegistryKey;
        }
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool EnumDisplayDevices(string device,uint index,ref DisplayDevice info,uint flags);
        public static string ScreenType(uint technology,string adapter)
        {
            string driver=(adapter??"").ToLowerInvariant();
            if(driver.Trim()=="zako display adapter") return "虚拟屏幕 · Zako";
            if(technology==17) return "虚拟屏幕";
            if(driver.Contains("virtual display") || driver.Contains("virtual monitor") || driver.Contains("iddsample")) return "虚拟屏幕 · 驱动识别";
            if(driver.Contains("remote display")) return "远程屏幕";
            switch(technology)
            {
                case 0: return "实体屏幕 · VGA";
                case 1: return "实体屏幕 · S-Video";
                case 2: return "实体屏幕 · 复合视频";
                case 3: return "实体屏幕 · 分量视频";
                case 4: return "实体屏幕 · DVI";
                case 5: return "实体屏幕 · HDMI";
                case 6: return "内置屏幕 · LVDS";
                case 8: return "实体屏幕 · D-Terminal";
                case 9: return "实体屏幕 · SDI";
                case 10: return "实体屏幕 · DisplayPort";
                case 11: return "内置屏幕 · eDP";
                case 12: return "实体屏幕 · UDI";
                case 13: return "内置屏幕 · UDI";
                case 14: return "实体屏幕 · SDTV";
                case 15: return "无线屏幕 · Miracast";
                case 16: return "间接屏幕 · USB / 虚拟待确认";
                case 18: return "实体屏幕 · USB 隧道 DP";
                case 0x80000000: return "内置屏幕";
                default: return "屏幕类型待确认";
            }
        }
        public const uint InvalidMode = 0xffffffff;
        [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
        [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint count, [Out] DisplayPath[] paths, ref uint modeCount, [Out] Mode[] modes, IntPtr topology);
        [DllImport("user32.dll")] static extern int SetDisplayConfig(uint count, [In] DisplayPath[] paths, uint modeCount, [In] Mode[] modes, uint flags);
        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] static extern int GetTarget(ref TargetName name);
        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] static extern int GetSource(ref SourceName name);
        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] static extern int GetPreferred(ref PreferredMode mode);
        public static string TargetKey(DisplayPath p) { return p.Target.Adapter + "/" + p.Target.Id; }
        public static string SourceKey(DisplayPath p) { return p.Source.Adapter + "/" + p.Source.Id; }
        public static Snapshot Capture(Snapshot all,List<MonitorInfo> monitors)
        {
            return new Snapshot { Paths=all.Paths.Where(p=>(p.Flags&1)!=0).ToArray(),Modes=all.Modes,
                Devices=monitors.Select(m=>new DeviceBinding { Key=m.Key,TargetKey=m.TargetKey }).ToArray() };
        }
        public static string InventorySignature(Snapshot data,List<MonitorInfo> monitors)
        {
            return String.Join("\n",monitors.OrderBy(m=>m.Key).Select(m=>
            {
                var record=LayoutMemory.ReadScreen(data,m);
                return m.Key+"|"+m.TargetKey+"|"+m.Active+"|"+m.TypeLabel+"|"+(record==null?"":LayoutMemory.Bounds(record.SourceMode).ToString()+"|"+record.Path.Target.Rotation+"|"+record.Path.Target.Refresh.Numerator+"/"+record.Path.Target.Refresh.Denominator);
            }));
        }
        public static void RequireDevices(Snapshot plan,List<MonitorInfo> expected,List<MonitorInfo> connected)
        {
            foreach(var path in plan.Paths)
            {
                var device=expected.FirstOrDefault(m=>m.TargetKey==TargetKey(path));
                if(device==null || !connected.Any(m=>m.Key==device.Key && m.TargetKey==device.TargetKey))
                    throw new InvalidOperationException("屏幕连接或设备编号已变化，请刷新后重新选择。未保存本次配置。");
            }
        }
        public static void Check(int code, string action)
        {
            if (code != 0) throw new InvalidOperationException(action + "失败（Windows " + code + "）：" + new Win32Exception(code).Message);
        }
        public static Snapshot Query(bool all)
        {
            uint flags = all ? 1u : 2u;
            for (int retry = 0; retry < 6; retry++)
            {
                uint pc, mc; Check(GetDisplayConfigBufferSizes(flags, out pc, out mc), "读取显示器数量");
                var paths = new DisplayPath[Math.Max(1, pc)]; var modes = new Mode[Math.Max(1, mc)];
                int result = QueryDisplayConfig(flags, ref pc, paths, ref mc, modes, IntPtr.Zero);
                if (result == 122) continue;
                Check(result, "读取显示器配置");
                Array.Resize(ref paths, (int)pc); Array.Resize(ref modes, (int)mc);
                return new Snapshot { Paths = paths, Modes = modes };
            }
            throw new InvalidOperationException("显示器配置正在变化，请稍后刷新。");
        }
        public static List<MonitorInfo> Monitors(Snapshot data)
        {
            var result = new List<MonitorInfo>();
            var adapters=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            for(uint i=0;i<256;i++)
            {
                var device=new DisplayDevice { Size=(uint)Marshal.SizeOf(typeof(DisplayDevice)) };
                if(!EnumDisplayDevices(null,i,ref device,0)) break;
                if(!String.IsNullOrEmpty(device.Name)) adapters[device.Name]=device.Description;
            }
            foreach (var group in data.Paths.Where(p => p.Target.Available != 0 || (p.Flags & 1) != 0).GroupBy(TargetKey))
            {
                var p = group.OrderByDescending(x => (x.Flags & 1) != 0).First();
                var name = new TargetName { Header = new Header { Type = 2, Size = (uint)Marshal.SizeOf(typeof(TargetName)), Adapter = p.Target.Adapter, Id = p.Target.Id } };
                int nameResult = GetTarget(ref name);
                var source = new SourceName { Header = new Header { Type = 1, Size = (uint)Marshal.SizeOf(typeof(SourceName)), Adapter = p.Source.Adapter, Id = p.Source.Id } };
                GetSource(ref source);
                bool active = (p.Flags & 1) != 0;
                string adapter; if(!adapters.TryGetValue(source.Name??"",out adapter)) adapter="";
                bool positioned=active && p.Source.Mode<data.Modes.Length && data.Modes[p.Source.Mode].Type==1;
                Rectangle bounds=positioned?LayoutMemory.Bounds(data.Modes[p.Source.Mode]):Rectangle.Empty;
                string detail = active ? "正在参与桌面" : "已从桌面断开 · 可重新启用";
                if (active && p.Source.Mode < data.Modes.Length)
                {
                    Mode mode = data.Modes[p.Source.Mode];
                    if (mode.Type == 1) detail = mode.Width + " × " + mode.Height + (mode.X == 0 && mode.Y == 0 ? " · 主屏幕" : "");
                    if (p.Target.Refresh.Denominator != 0) detail += " · " + ((double)p.Target.Refresh.Numerator / p.Target.Refresh.Denominator).ToString("0.#") + " Hz";
                }
                result.Add(new MonitorInfo { Key = nameResult == 0 && !String.IsNullOrEmpty(name.Device) ? name.Device : group.Key,
                    TargetKey = group.Key, Name = String.IsNullOrEmpty(name.Friendly) ? "显示器 " + p.Target.Id : name.Friendly,
                    GdiName = active ? source.Name : "", Active = active, Detail = detail,
                    TypeLabel=ScreenType(nameResult==0?name.Technology:p.Target.Technology,adapter),AdapterName=adapter,
                    HasPosition=positioned,DesktopBounds=bounds,Primary=positioned && bounds.X==0 && bounds.Y==0 });
            }
            return result;
        }
        public static DisplayPath[] BuildPaths(Snapshot all, IEnumerable<string> targets)
        {
            var wanted = targets.Distinct().ToList();
            if (wanted.Count == 0) throw new InvalidOperationException("至少保留一个开启的屏幕。");
            var choices = wanted.Select(t => all.Paths.Where(p => TargetKey(p) == t && (p.Target.Available != 0 || (p.Flags & 1) != 0))
                .OrderByDescending(p => (p.Flags & 1) != 0).ToList()).ToList();
            if (choices.Any(c => c.Count == 0)) throw new InvalidOperationException("选中的屏幕已断开，请刷新后重试。");
            var selected = new DisplayPath[wanted.Count];
            if (!Assign(choices, 0, new HashSet<string>(), selected)) throw new InvalidOperationException("显卡无法为这些屏幕分配独立显示通道。请减少开启的屏幕数量。");
            for (int i = 0; i < selected.Length; i++)
            {
                var p = selected[i]; p.Source.Mode = InvalidMode; p.Target.Mode = InvalidMode;
                p.Flags = 1; selected[i] = p;
            }
            return selected;
        }
        static bool Assign(List<List<DisplayPath>> options, int index, HashSet<string> used, DisplayPath[] selected)
        {
            if (index == options.Count) return true;
            foreach (var p in options[index])
            {
                string key = SourceKey(p); if (!used.Add(key)) continue;
                selected[index] = p;
                if (Assign(options, index + 1, used, selected)) return true;
                used.Remove(key);
            }
            return false;
        }
        public static void Apply(DisplayPath[] paths)
        {
            if (paths == null || paths.Length == 0) throw new InvalidOperationException("不能关闭所有屏幕。");
            Validate(paths);
            // Supply the complete active set explicitly; do not select a historical database topology.
            ApplyVerified(paths, delegate(DisplayPath[] requested)
            {
                Check(SetDisplayConfig((uint)requested.Length, requested, 0, null, 0x80 | 0x20 | 0x400 | 0x100), "应用屏幕组合");
            }, delegate { return Query(false); }, delegate { Thread.Sleep(150); });
        }
        public static RememberedScreen Preferred(DisplayPath path)
        {
            var preferred=new PreferredMode { Header=new Header { Type=3,Size=(uint)Marshal.SizeOf(typeof(PreferredMode)),Adapter=path.Target.Adapter,Id=path.Target.Id } };
            Check(GetPreferred(ref preferred),"读取新屏幕首选模式");
            var video=preferred.Video;
            path.Target.Rotation=1;path.Target.Scaling=1;
            path.Target.Refresh=new Rational { Numerator=(uint)video.D2,Denominator=(uint)(video.D2>>32) };
            path.Target.Scan=(uint)(video.D5>>32);
            return new RememberedScreen { Path=path,HasTarget=true,
                SourceMode=new Mode { Type=1,D0=preferred.Width|((ulong)preferred.Height<<32),D1=4 },
                TargetMode=new Mode { Type=2,D0=video.D0,D1=video.D1,D2=video.D2,D3=video.D3,D4=video.D4,D5=video.D5 } };
        }
        public static void ValidateLayout(Snapshot plan)
        {
            LayoutMemory.EnsureContiguous(plan);
            Check(SetDisplayConfig((uint)plan.Paths.Length,plan.Paths,(uint)plan.Modes.Length,plan.Modes,0x40|0x20),"验证固定布局（未更改屏幕）");
        }
        public static void ApplyLayout(Snapshot plan)
        {
            ValidateLayout(plan);
            ApplyVerified(plan.Paths,delegate(DisplayPath[] paths)
            {
                Check(SetDisplayConfig((uint)paths.Length,paths,(uint)plan.Modes.Length,plan.Modes,0x80|0x20|0x100),"按原位置切换屏幕");
            },delegate { return Query(false); },delegate { Thread.Sleep(150); });
            RequireLayout(Query(false),plan);
        }
        public static void RequireLayout(Snapshot actual,Snapshot plan)
        {
            if(!LayoutMemory.Matches(actual,plan)) throw new InvalidOperationException("Windows 改变了屏幕位置或显示模式，本次操作无法保持原布局，请恢复原配置。");
        }
        internal static void ApplyVerified(DisplayPath[] paths, Action<DisplayPath[]> apply, Func<Snapshot> read, Action wait)
        {
            var expected = paths.Select(TargetKey).ToArray();
            apply(paths);
            int stable = 0;
            for (int i = 0; i < 15; i++)
            {
                wait();
                if (TargetsMatch(read(), expected)) stable++; else stable = 0;
                if (stable >= 3) return;
            }
            throw new InvalidOperationException("Windows 的实际桌面与所选屏幕不一致，屏幕可能被驱动或串流软件重新启用。本次更改将恢复。");
        }
        public static bool TargetsMatch(Snapshot actual, IEnumerable<string> expected)
        {
            return new HashSet<string>(actual.Paths.Where(p => (p.Flags & 1) != 0).Select(TargetKey)).SetEquals(expected);
        }
        public static void RequireTargets(Snapshot actual, IEnumerable<string> expected)
        {
            if (!TargetsMatch(actual, expected)) throw new InvalidOperationException("屏幕配置已被其他程序改变，请立即恢复后重试。未保存本次配置。");
        }
        public static void Validate(DisplayPath[] paths)
        {
            if (paths == null || paths.Length == 0) throw new InvalidOperationException("不能关闭所有屏幕。");
            Check(SetDisplayConfig((uint)paths.Length, paths, 0, null, 0x40 | 0x20 | 0x400), "验证屏幕组合");
        }
        public static void Restore(Snapshot s)
        {
            if (s == null || s.Paths == null || s.Paths.Length == 0) throw new InvalidOperationException("恢复配置为空。");
            int result = SetDisplayConfig((uint)s.Paths.Length, s.Paths, (uint)s.Modes.Length, s.Modes, 0x80 | 0x20);
            if (result != 0) result = SetDisplayConfig((uint)s.Paths.Length, s.Paths, (uint)s.Modes.Length, s.Modes, 0x80 | 0x20 | 0x400);
            Check(result, "恢复屏幕配置");
        }
        public static void SaveCurrent(IEnumerable<string> expected,Snapshot layout)
        {
            Snapshot s = Query(false);
            RequireTargets(s, expected);
            RequireLayout(s,layout);
            Check(SetDisplayConfig((uint)s.Paths.Length, s.Paths, (uint)s.Modes.Length, s.Modes, 0x80 | 0x20 | 0x200), "保存屏幕配置");
            RequireTargets(Query(false), expected);
            RequireLayout(Query(false),layout);
        }
    }
}
