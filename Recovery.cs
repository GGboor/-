using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Xml.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using System.Collections.Generic;

namespace ScreenPilot
{
    public static class Recovery
    {
        public const int Seconds = 30;
        public static string Root { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenPilot", "Recovery"); } }
        public static void WriteSnapshot(string path, Snapshot s)
        {
            using (var file = File.Create(path)) new XmlSerializer(typeof(Snapshot)).Serialize(file, s);
        }
        public static Snapshot ReadSnapshot(string path)
        {
            using (var file = File.OpenRead(path)) return (Snapshot)new XmlSerializer(typeof(Snapshot)).Deserialize(file);
        }
        // A cross-process mutex serializes the state transition. On Windows two concurrent
        // renames can open the same source first, so File.Move alone is not a safe claim.
        public static bool Claim(string dir, string state)
        {
            string id;
            using (var hash = SHA256.Create()) id = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(dir).ToUpperInvariant()))).Replace("-", "");
            using (var mutex = new Mutex(false, "Local\\ScreenPilot.Change." + id))
            {
                bool owned = false;
                try
                {
                    try { owned = mutex.WaitOne(5000); } catch (AbandonedMutexException) { owned = true; }
                    if (!owned) throw new IOException("等待屏幕恢复锁超时。");
                    if (!File.Exists(Path.Combine(dir, "pending"))) return false;
                    File.Move(Path.Combine(dir, "pending"), Path.Combine(dir, state)); return true;
                }
                finally { if (owned) mutex.ReleaseMutex(); }
            }
        }
        public static string Start(Snapshot s)
        {
            string dir = Path.Combine(Root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
            WriteSnapshot(Path.Combine(dir, "before.xml"), s);
            File.WriteAllText(Path.Combine(dir, "pending"), "");
            var info = new ProcessStartInfo(System.Windows.Forms.Application.ExecutablePath, "--guard \"" + dir + "\"");
            info.UseShellExecute = false; info.CreateNoWindow = true; info.WindowStyle = ProcessWindowStyle.Hidden;
            using (var child = Process.Start(info))
            {
                var wait = Stopwatch.StartNew();
                while (!File.Exists(Path.Combine(dir, "ready")))
                {
                    if (child.HasExited || wait.ElapsedMilliseconds > 5000)
                    {
                        Claim(dir, "cancelled");
                        throw new InvalidOperationException("无法启动自动恢复进程，本次没有更改屏幕。");
                    }
                    Thread.Sleep(40);
                }
            }
            return dir;
        }
        public static int Guard(string dir)
        {
            return RunGuard(dir, Seconds * 1000, RestoreWithReport);
        }
        // Injectable restore action permits timeout tests without touching physical displays.
        internal static int RunGuard(string dir, int timeoutMs, Action<string, Snapshot> restore)
        {
            try
            {
                Snapshot original = ReadSnapshot(Path.Combine(dir, "before.xml"));
                File.WriteAllText(Path.Combine(dir, "ready"), "ready");
                // Wait for arming before starting the rollback deadline. A dead parent before arming made no changes.
                var startup = Stopwatch.StartNew();
                while (!File.Exists(Path.Combine(dir, "armed")))
                {
                    if (!File.Exists(Path.Combine(dir, "pending"))) return 0;
                    if (startup.Elapsed.TotalSeconds > 10) { Claim(dir, "cancelled"); return 0; }
                    Thread.Sleep(50);
                }
                var timer = Stopwatch.StartNew();
                while (timer.ElapsedMilliseconds < timeoutMs)
                {
                    if (!File.Exists(Path.Combine(dir, "pending"))) return 0;
                    Thread.Sleep(100);
                }
                if (Claim(dir, "restoring")) restore(dir, original);
                return 0;
            }
            catch (Exception e) { try { File.WriteAllText(Path.Combine(dir, "error.txt"), e.ToString()); } catch { } return 1; }
        }
        public static void RestoreWithReport(string dir, Snapshot original)
        {
            Exception last = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try { string message=RestoreAvailable(original); File.WriteAllText(Path.Combine(dir, "restored"), message); return; }
                catch (Exception e) { last = e; Thread.Sleep(500); }
            }
            File.WriteAllText(Path.Combine(dir, "error.txt"), last.ToString());
            throw last;
        }
        public static Snapshot PlanAvailable(Snapshot original,Snapshot current,List<MonitorInfo> monitors,Func<DisplayPath,RememberedScreen> preferred)
        {
            if(original.Devices==null) throw new InvalidOperationException("此恢复记录缺少屏幕标识，请重新选择当前屏幕。");
            var saved=original.Devices.Where(d=>original.Paths.Any(p=>NativeDisplay.TargetKey(p)==d.TargetKey && (p.Flags&1)!=0))
                .Select(d=>new MonitorInfo { Key=d.Key,TargetKey=d.TargetKey,Active=true }).ToList();
            var selected=saved.Where(s=>monitors.Any(m=>m.Key==s.Key)).Select(s=>s.Key).ToList();
            if(selected.Count==0)
            {
                // All old screens are gone: never disable the replacement desktop to restore stale IDs.
                selected=monitors.Where(m=>m.Active).Select(m=>m.Key).ToList();
                if(selected.Count>0) return null;
                if(monitors.Count==0) throw new InvalidOperationException("暂无可用屏幕，等待重新连接后再恢复。");
                selected.Add(monitors[0].Key);
            }
            selected.AddRange(monitors.Where(m=>m.Active && !original.Devices.Any(d=>d.Key==m.Key)).Select(m=>m.Key));
            var memory=new LayoutMemory();memory.Observe(original,saved);
            foreach(var monitor in monitors.Where(m=>m.Active && !memory.Screens.Any(s=>s.Key==m.Key)))
            {
                var entry=LayoutMemory.ReadScreen(current,monitor);
                if(entry!=null) { memory.PlaceNew(entry);memory.Screens.Add(entry); }
            }
            return memory.Plan(current,monitors,selected.Distinct(),preferred);
        }
        public static string RestoreAvailable(Snapshot original)
        {
            if(original.Devices==null) { NativeDisplay.Restore(original);return "已恢复更改前的屏幕配置。"; }
            var current=NativeDisplay.Query(true);var monitors=NativeDisplay.Monitors(current);
            bool unchanged=original.Devices.Length==monitors.Count && original.Devices.All(d=>monitors.Any(m=>m.Key==d.Key && m.TargetKey==d.TargetKey));
            if(unchanged)
            {
                NativeDisplay.Restore(original);
                NativeDisplay.RequireTargets(NativeDisplay.Query(false),original.Paths.Select(NativeDisplay.TargetKey));
                return "已恢复更改前的屏幕配置。";
            }
            var plan=PlanAvailable(original,current,monitors,NativeDisplay.Preferred);
            if(plan==null) return "原屏幕已断开，已保留当前可用桌面。";
            NativeDisplay.ApplyLayout(plan);
            return "屏幕连接已变化，已恢复仍连接的屏幕并保留新接入的桌面。";
        }
        public static string ResultMessage(string dir)
        {
            string text=File.ReadAllText(Path.Combine(dir,"restored"));
            return text=="ok"?"已恢复更改前的屏幕配置。":text;
        }
    }
}
