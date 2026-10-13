using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace ScreenPilot
{
    public class DesktopArea
    {
        public Rectangle Bounds, Work;
        public bool Primary;
    }
    public class WindowRecoveryResult
    {
        public int Requested, Failed;
        public string Message { get { return Requested == 0 && Failed == 0 ? "未发现离开当前桌面的窗口。" : "已向 " + Requested + " 个离屏窗口发送移回请求。" + (Failed > 0 ? "另有 " + Failed + " 个窗口未能移动（可能需要管理员权限）。" : ""); } }
    }
    public static class WindowRecovery
    {
        [StructLayout(LayoutKind.Sequential)] struct Rect
        {
            public int Left, Top, Right, Bottom;
            public Rectangle Value { get { return Rectangle.FromLTRB(Left, Top, Right, Bottom); } }
            public static Rect From(Rectangle r) { return new Rect { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom }; }
        }
        [StructLayout(LayoutKind.Sequential)] struct PointNative { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct Placement
        {
            public uint Length, Flags, Show;
            public PointNative Min, Max;
            public Rect Normal;
        }
        [StructLayout(LayoutKind.Sequential)] struct MonitorNative { public uint Size; public Rect Bounds, Work; public uint Flags; }
        delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, ref Rect rect, IntPtr data);
        delegate bool WindowCallback(IntPtr window, IntPtr data);
        [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorNative info);
        [DllImport("user32.dll")] static extern bool EnumWindows(WindowCallback callback, IntPtr data);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr window);
        [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr window, StringBuilder name, int count);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
        [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr window, ref Placement placement);
        [DllImport("user32.dll", SetLastError = true)] static extern bool SetWindowPlacement(IntPtr window, ref Placement placement);
        [DllImport("user32.dll", SetLastError = true)] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr window, uint attribute, out int value, uint size);

        // Query Win32 directly: no stale Screen.AllScreens cache after WM_DISPLAYCHANGE.
        public static List<DesktopArea> Areas()
        {
            var areas = new List<DesktopArea>();
            MonitorCallback callback = delegate(IntPtr handle, IntPtr dc, ref Rect rect, IntPtr data)
            {
                var info = new MonitorNative { Size = (uint)Marshal.SizeOf(typeof(MonitorNative)) };
                if (GetMonitorInfo(handle, ref info)) areas.Add(new DesktopArea { Bounds = info.Bounds.Value, Work = info.Work.Value, Primary = (info.Flags & 1) != 0 });
                return true;
            };
            if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero) || areas.Count == 0) throw new InvalidOperationException("无法读取当前桌面区域，未移动任何窗口。");
            return areas;
        }
        // Do not use the bounding rectangle of all screens: its gaps are not visible desktop.
        public static bool Reachable(Rectangle window, IEnumerable<DesktopArea> areas)
        {
            if (window.Width <= 0 || window.Height <= 0) return false;
            Rectangle title = new Rectangle(window.Left, window.Top, window.Width, Math.Min(40, window.Height));
            return areas.Any(a => { Rectangle part = Rectangle.Intersect(title, a.Work); return part.Width >= Math.Min(80, window.Width) && part.Height >= Math.Min(16, window.Height); });
        }
        public static Rectangle Fit(Rectangle window, Rectangle work)
        {
            int width = Math.Min(Math.Max(1, window.Width), work.Width), height = Math.Min(Math.Max(1, window.Height), work.Height);
            return new Rectangle(work.Left + (work.Width - width) / 2, work.Top + (work.Height - height) / 2, width, height);
        }
        public static WindowRecoveryResult RecoverAll(IntPtr excluded)
        {
            var areas = Areas(); var result = new WindowRecoveryResult();
            WindowCallback callback = delegate(IntPtr window, IntPtr data)
            {
                if (window == excluded) return true;
                int moved = RecoverOne(window, areas);
                if (moved > 0) result.Requested++; else if (moved < 0) result.Failed++;
                return true;
            };
            if (!EnumWindows(callback, IntPtr.Zero)) throw new InvalidOperationException("无法枚举桌面窗口。");
            return result;
        }
        // Also used with a test-owned off-screen window; tests never enumerate/move user windows.
        internal static int RecoverOne(IntPtr window, List<DesktopArea> areas)
        {
            if (!IsWindowVisible(window) || window == GetShellWindow()) return 0;
            long style = GetWindowLongPtr(window, -16).ToInt64(), extended = GetWindowLongPtr(window, -20).ToInt64();
            if ((style & 0x40000000L) != 0 || (extended & 0x80L) != 0) return 0; // Child/tool windows.
            int cloaked;
            if (DwmGetWindowAttribute(window, 14, out cloaked, 4) == 0 && cloaked != 0) return 0;
            var name = new StringBuilder(128); GetClassName(window, name, name.Capacity);
            if (new[] { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" }.Contains(name.ToString())) return 0;
            var placement = new Placement { Length = (uint)Marshal.SizeOf(typeof(Placement)) };
            bool minimized = IsIconic(window), maximized = IsZoomed(window);
            Rect rect;
            if (!GetWindowRect(window, out rect) || !GetWindowPlacement(window, ref placement)) return 0;
            Rectangle current = rect.Value;
            if (minimized)
            {
                // WINDOWPLACEMENT uses workspace coordinates; compare in the same coordinate space.
                var workspace = areas.Select(a => new DesktopArea { Work = new Rectangle(a.Bounds.Location, a.Work.Size) }).ToList();
                if (Reachable(placement.Normal.Value, workspace)) return 0;
                current = placement.Normal.Value;
            }
            else if (Reachable(current, areas)) return 0;
            var target = areas.FirstOrDefault(a => a.Primary) ?? areas[0];
            Rectangle destination = Fit(current, target.Work);
            if (minimized || maximized)
            {
                // Never pass workspace coordinates to SetWindowPos, or screen coordinates to SetWindowPlacement.
                destination.Offset(target.Bounds.Left - target.Work.Left, target.Bounds.Top - target.Work.Top);
                placement.Normal = Rect.From(destination); placement.Max = new PointNative { X = -1, Y = -1 };
                placement.Flags |= 4; // WPF_ASYNCWINDOWPLACEMENT: don't block on another application's UI thread.
                return SetWindowPlacement(window, ref placement) ? 1 : -1;
            }
            // Keep z-order and focus, and avoid blocking on an unresponsive application.
            return SetWindowPos(window, IntPtr.Zero, destination.X, destination.Y, destination.Width, destination.Height, 0x4 | 0x10 | 0x4000) ? 1 : -1;
        }
    }
}
