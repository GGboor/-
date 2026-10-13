using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Drawing;
using System.Windows.Forms;
using System.Reflection;

namespace ScreenPilot
{
    static class Tests
    {
        static int count;
        static void Assert(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); count++; Console.WriteLine("PASS: " + name); }
        static void Throws(Action action, string name) { bool failed = false; try { action(); } catch (InvalidOperationException) { failed = true; } Assert(failed, name); }
        static DisplayPath PathFor(uint target, uint source, bool active)
        {
            return new DisplayPath { Source = new Source { Id = source, Mode = 0 }, Target = new Target { Id = target, Mode = 1, Available = 1 }, Flags = active ? 1u : 0u };
        }
        static Snapshot LayoutFixture(int screens=3)
        {
            var paths=new DisplayPath[screens]; var modes=new Mode[screens*2];
            for(int i=0;i<screens;i++)
            {
                paths[i]=PathFor((uint)(10+i*10),(uint)i,true); paths[i].Source.Mode=(uint)(i*2);paths[i].Target.Mode=(uint)(i*2+1);
                paths[i].Target.Rotation=1;paths[i].Target.Scaling=1;paths[i].Target.Refresh=new Rational { Numerator=60000,Denominator=1000 };
                modes[i*2]=LayoutMemory.Position(new Mode { Type=1,Id=(uint)i,D0=((ulong)1080<<32)|1920,D1=4 },i*1920,0);
                modes[i*2+1]=new Mode { Type=2,Id=(uint)(10+i*10),D0=148500000,D5=123456 };
            }
            return new Snapshot { Paths=paths,Modes=modes };
        }
        static System.Collections.Generic.List<MonitorInfo> FixtureMonitors(int count)
        {
            return Enumerable.Range(0,count).Select(i=>new MonitorInfo { Key=((char)('A'+i)).ToString(),TargetKey="0:0/"+(10+i*10),Active=true,Name="Same display model" }).ToList();
        }
        static void HotplugTests(string temp)
        {
            Func<DisplayPath,RememberedScreen> unexpected=delegate { throw new Exception("Known mode must not be guessed"); };
            var full=LayoutFixture();var monitors=FixtureMonitors(3);
            var memory=new LayoutMemory();memory.Observe(full,monitors);
            var backup=NativeDisplay.Capture(full,monitors);
            string file=System.IO.Path.Combine(temp,"identity-snapshot.xml");Recovery.WriteSnapshot(file,backup);
            backup=Recovery.ReadSnapshot(file);
            Assert(backup.Devices.Length==3 && backup.Devices[1].Key=="B","recovery snapshot serializes stable device identities");
            var withoutMiddle=memory.Plan(full,monitors,new[]{"A","C"},unexpected);
            var two=monitors.Where(m=>m.Key!="B").ToList();
            memory.Observe(withoutMiddle,two);
            Assert(memory.Screens.First(s=>s.Key=="C").SourceMode.X==3840,"physical unplug does not replace remembered full layout with Windows compacted layout");
            Assert(memory.DeviceOrder.SequenceEqual(new[]{"A","B","C"}),"unplugged monitor reserves its number; other hotkeys do not shift");
            var delayed=NativeDisplay.Capture(withoutMiddle,two);delayed.Modes=(Mode[])delayed.Modes.Clone();
            delayed.Modes[delayed.Paths[1].Source.Mode]=LayoutMemory.Position(delayed.Modes[delayed.Paths[1].Source.Mode],0,1080);
            memory.Observe(delayed,two,true);
            Assert(memory.Screens.First(s=>s.Key=="C").SourceMode.X==3840,"delayed driver rearrangement during hotplug settling does not erase canonical positions");
            var reconnect=LayoutFixture();reconnect.Modes[2]=LayoutMemory.Position(reconnect.Modes[2],3840,0);reconnect.Modes[4]=LayoutMemory.Position(reconnect.Modes[4],1920,0);
            memory.Observe(reconnect,monitors);
            Assert(LayoutMemory.Matches(memory.Plan(reconnect,monitors,new[]{"A","B","C"},unexpected),full),"reconnecting middle screen restores canonical order despite Windows choosing a different order");
            var fourth=LayoutFixture(4);var four=FixtureMonitors(4);memory.Observe(fourth,four);
            Assert(memory.DeviceOrder.SequenceEqual(new[]{"A","B","C","D"}),"new fourth monitor receives a new stable number even with same model name");
            var inactive=(Snapshot)new Snapshot { Paths=(DisplayPath[])fourth.Paths.Clone(),Modes=fourth.Modes };
            inactive.Paths[3].Flags=0;four[3].Active=false;
            Assert(NativeDisplay.InventorySignature(inactive,four)!=NativeDisplay.InventorySignature(full,monitors),"inventory detects connected inactive display absent from active desktop query");
            memory.Observe(inactive,four);
            using(var buffer=new MemoryStream())
            {
                var serializer=new System.Xml.Serialization.XmlSerializer(typeof(LayoutMemory));serializer.Serialize(buffer,memory);buffer.Position=0;
                var loaded=(LayoutMemory)serializer.Deserialize(buffer);
                Assert(loaded.DeviceOrder.SequenceEqual(memory.DeviceOrder) && loaded.ConnectedKeys.Count==4,"monitor numbering and connection inventory survive restart");
            }
            var ten=LayoutFixture(10);var tenMonitors=FixtureMonitors(10);var manyMemory=new LayoutMemory();manyMemory.Observe(ten,tenMonitors);
            Assert(manyMemory.Plan(ten,tenMonitors,tenMonitors.Select(m=>m.Key),unexpected).Paths.Length==10,"planner supports ten displays without a three-screen limit");
            var subset=manyMemory.Plan(ten,tenMonitors,new[]{"B","D","G","J"},unexpected);
            LayoutMemory.EnsureContiguous(subset);
            Assert(subset.Paths.Length==4 && subset.Paths.Select(p=>subset.Modes[p.Source.Mode].X).OrderBy(x=>x).SequenceEqual(new[]{0,1920,3840,5760}),"multiple removed middle displays compact to a contiguous four-screen desktop");
            var rebound=LayoutFixture();var newIds=FixtureMonitors(3);
            for(int i=0;i<3;i++) { rebound.Paths[i].Target.Id+=100;rebound.Paths[i].Source.Id+=10;newIds[i].TargetKey=NativeDisplay.TargetKey(rebound.Paths[i]); }
            var reboundPlan=Recovery.PlanAvailable(backup,rebound,newIds,unexpected);
            Assert(reboundPlan.Paths.All(p=>p.Target.Id>=110 && p.Source.Id>=10) && reboundPlan.Modes[0].Width==1920,"recovery rebinds saved physical identities to newly enumerated target and source IDs");
            var surviving=Recovery.PlanAvailable(backup,withoutMiddle,two,unexpected);
            Assert(surviving.Paths.Length==2 && surviving.Modes[surviving.Paths[1].Source.Mode].X==1920,"recovery excludes physically removed middle screen and closes gap");
            four[3].Active=true;
            var extra=Recovery.PlanAvailable(backup,fourth,four,unexpected);
            Assert(extra.Paths.Length==4,"recovery preserves newly connected active desktop in addition to old available screens");
            var replacement=FixtureMonitors(1);replacement[0].Key="Replacement";
            Throws(delegate { NativeDisplay.RequireDevices(LayoutFixture(1),monitors,replacement); },"confirmation rejects replacement hardware reusing the same target ID");
            Assert(Recovery.PlanAvailable(backup,LayoutFixture(1),replacement,unexpected)==null,"all original screens removed: preserve replacement desktop even if target ID is reused");
            Throws(delegate { Recovery.PlanAvailable(backup,new Snapshot { Paths=new DisplayPath[0],Modes=new Mode[0] },FixtureMonitors(0),unexpected); },"no available screen is retried instead of applying empty recovery topology");
            var one=monitors.Take(1).ToList();
            Assert(Recovery.PlanAvailable(backup,LayoutFixture(1),one,unexpected).Paths.Length==1,"three-screen snapshot can recover when only one screen remains");
        }
        [STAThread] static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--guard") return Recovery.Guard(args[1]);
            string temp = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-output"); Directory.CreateDirectory(temp);
            try
            {
                Assert(Marshal.SizeOf(typeof(Luid)) == 8, "LUID native layout");
                Assert(Marshal.SizeOf(typeof(DisplayPath)) == 72, "DISPLAYCONFIG_PATH_INFO native layout");
                Assert(Marshal.SizeOf(typeof(Mode)) == 64, "DISPLAYCONFIG_MODE_INFO native layout");
                Assert(Marshal.SizeOf(typeof(TargetName)) == 420, "target device name Unicode layout");
                Assert(Marshal.SizeOf(typeof(SourceName)) == 84, "source device name Unicode layout");
                Assert(Marshal.SizeOf(typeof(NativeDisplay.DisplayDevice))==840,"adapter description native Unicode layout");
                Assert(NativeDisplay.ScreenType(5,"NVIDIA")=="实体屏幕 · HDMI" && NativeDisplay.ScreenType(11,"")=="内置屏幕 · eDP","connector metadata identifies physical and embedded displays");
                Assert(NativeDisplay.ScreenType(17,"")=="虚拟屏幕" && NativeDisplay.ScreenType(16,"Virtual Display Driver").StartsWith("虚拟屏幕"),"virtual displays identified from explicit technology or adapter driver");
                Assert(NativeDisplay.ScreenType(5,"Zako Display Adapter")=="虚拟屏幕 · Zako","Zako driver identity overrides its simulated HDMI connector");
                Assert(NativeDisplay.ScreenType(16,"DisplayLink").Contains("待确认") && NativeDisplay.ScreenType(uint.MaxValue,"").Contains("待确认"),"indirect USB and unknown outputs are not guessed to be virtual");
                var visual=FixtureMonitors(4);
                visual[0].DesktopBounds=new Rectangle(-1920,0,1920,1080);visual[0].HasPosition=true;
                visual[1].DesktopBounds=new Rectangle(0,0,2560,1440);visual[1].HasPosition=true;visual[1].Primary=true;
                visual[2].DesktopBounds=new Rectangle(2560,-1920,1080,1920);visual[2].HasPosition=true;
                visual[3].DesktopBounds=new Rectangle(50000,50000,1920,1080);visual[3].HasPosition=true;visual[3].Active=false;
                var tiles=MonitorLayoutView.Arrange(visual,m=>visual.IndexOf(m)+1,new RectangleF(10,20,600,240));
                Assert(tiles.Count==3 && tiles.All(t=>t.Bounds.Left>=9.9 && t.Bounds.Right<=610.1 && t.Bounds.Top>=19.9 && t.Bounds.Bottom<=260.1),"layout map fits negative coordinates and portrait display; inactive screen excluded");
                Assert(Math.Abs(tiles[0].Bounds.Right-tiles[1].Bounds.Left)<.01 && Math.Abs(tiles[2].Bounds.Bottom-tiles[1].Bounds.Top)<.01,"layout map preserves horizontal and vertical screen adjacency");
                Assert(Math.Abs(tiles[2].Bounds.Width/tiles[2].Bounds.Height-1080f/1920)<.001,"portrait display keeps its actual aspect ratio");
                Assert(visual[0].PositionText.Contains("-1920") && visual[1].PositionText.Contains("主屏幕") && visual[3].PositionText.Contains("不占桌面位置"),"position readout distinguishes negative position, primary monitor and disabled output");
                Assert(MonitorLayoutView.Arrange(new MonitorInfo[0],m=>1,new RectangleF(0,0,500,200)).Count==0,"empty desktop map handles transient unplug safely");
                var sample = new Snapshot { Paths = new[] { PathFor(10, 0, true), PathFor(10, 1, false), PathFor(20, 0, false), PathFor(30, 2, true) }, Modes = new[] { new Mode { Type = 1, D0 = ((ulong)1080 << 32) | 1920, D1 = 0xfffff88000000004, D5 = ulong.MaxValue } } };
                var selected = NativeDisplay.BuildPaths(sample, new[] { "0:0/10", "0:0/20", "0:0/30" });
                Assert(selected.Length == 3 && selected.Select(NativeDisplay.SourceKey).Distinct().Count() == 3, "backtracking resolves inactive source collision");
                Assert(selected[0].Source.Id == 1 && selected[1].Source.Id == 0, "planner backtracks instead of cloning screens");
                Assert(selected.All(p => p.Source.Mode == uint.MaxValue && p.Target.Mode == uint.MaxValue && p.Flags == 1), "new topology invalidates mode indexes");
                var one = NativeDisplay.BuildPaths(sample, new[] { "0:0/10" });
                Assert(one[0].Source.Id == 0, "prefers current active path");
                Throws(delegate { NativeDisplay.BuildPaths(sample, new string[0]); }, "reject all screens off");
                Throws(delegate { NativeDisplay.BuildPaths(sample, new[] { "missing" }); }, "reject disconnected target");
                Throws(delegate { NativeDisplay.Apply(new DisplayPath[0]); }, "native apply cannot receive empty topology");
                var impossible = new Snapshot { Paths = new[] { PathFor(10, 0, true), PathFor(20, 0, false) }, Modes = new Mode[0] };
                Throws(delegate { NativeDisplay.BuildPaths(impossible, new[] { "0:0/10", "0:0/20" }); }, "reject impossible source assignment");
                var activeOne = new Snapshot { Paths = new[] { PathFor(10, 0, true) }, Modes = new Mode[0] };
                var extraScreen = new Snapshot { Paths = new[] { PathFor(10, 0, true), PathFor(20, 1, true) }, Modes = new Mode[0] };
                Assert(NativeDisplay.TargetsMatch(activeOne, new[] { "0:0/10" }), "exact active desktop accepted");
                Assert(!NativeDisplay.TargetsMatch(extraScreen, new[] { "0:0/10" }), "extra active ghost display detected");
                Throws(delegate { NativeDisplay.ApplyVerified(one, delegate { }, delegate { return extraScreen; }, delegate { }); }, "driver success with wrong topology is rejected");
                int reads = 0;
                NativeDisplay.ApplyVerified(one, delegate { }, delegate { reads++; return reads <= 2 ? extraScreen : activeOne; }, delegate { });
                Assert(reads == 5, "waits for three consecutive exact topology readings");
                Throws(delegate { NativeDisplay.RequireTargets(extraScreen, new[] { "0:0/10" }); }, "external screen reactivation cannot be confirmed or persisted silently");
                var areas = new System.Collections.Generic.List<DesktopArea> {
                    new DesktopArea { Bounds = new Rectangle(0, 0, 1920, 1080), Work = new Rectangle(0, 40, 1920, 1040), Primary = true },
                    new DesktopArea { Bounds = new Rectangle(-1920, -1080, 1920, 1080), Work = new Rectangle(-1920, -1080, 1920, 1040) }
                };
                Assert(WindowRecovery.Reachable(new Rectangle(-1800, -900, 600, 400), areas), "valid negative-coordinate monitor window is preserved");
                Assert(!WindowRecovery.Reachable(new Rectangle(-1500, 100, 600, 400), areas), "gap inside combined display bounds is still off-screen");
                Assert(!WindowRecovery.Reachable(new Rectangle(100, -100, 600, 400), areas), "visible body with inaccessible title bar is recoverable");
                var fitted = WindowRecovery.Fit(new Rectangle(3000, 100, 3000, 2000), areas[0].Work);
                Assert(areas[0].Work.Contains(fitted) && fitted.Width == 1920 && fitted.Height == 1040, "oversized off-screen window fits destination working area");
                Assert(Marshal.SizeOf(typeof(PreferredMode))==80,"preferred display mode native layout");
                HotplugTests(temp);
                var layout=LayoutFixture();
                var physical=new System.Collections.Generic.List<MonitorInfo> {
                    new MonitorInfo { Key="A",TargetKey="0:0/10",Active=true },
                    new MonitorInfo { Key="B",TargetKey="0:0/20",Active=true },
                    new MonitorInfo { Key="C",TargetKey="0:0/30",Active=true }
                };
                var memory=new LayoutMemory();memory.Observe(layout,physical);
                Func<DisplayPath,RememberedScreen> unexpected=delegate { throw new Exception("Existing screen mode must not be guessed"); };
                var removeRight=memory.Plan(layout,physical,new[]{"A","B"},unexpected);
                Assert(removeRight.Modes[removeRight.Paths[1].Source.Mode].X==1920 && removeRight.Modes[0].Width==1920,"disabling outer monitor preserves remaining coordinates and resolution");
                Assert(removeRight.Modes[removeRight.Paths[1].Target.Mode].D5==123456 && removeRight.Paths[1].Target.Refresh.Numerator==60000,"switching retains target timings and refresh rate");
                var compact=memory.Plan(layout,physical,new[]{"A","C"},unexpected);
                Assert(compact.Modes[compact.Paths[0].Source.Mode].X==0 && compact.Modes[compact.Paths[1].Source.Mode].X==1920,"removing middle monitor joins remaining outer monitors edge to edge");
                memory.Accept(compact,physical,null);physical[1].Active=false;memory.Observe(compact,physical);
                Assert(LayoutMemory.Matches(memory.Plan(layout,physical,new[]{"A","B","C"},unexpected),layout),"automatic refresh of compacted desktop preserves full layout for re-enabling");
                using(var compactBuffer=new MemoryStream())
                {
                    var serializer=new System.Xml.Serialization.XmlSerializer(typeof(LayoutMemory));serializer.Serialize(compactBuffer,memory);compactBuffer.Position=0;
                    var loaded=(LayoutMemory)serializer.Deserialize(compactBuffer);loaded.Observe(compact,physical);
                    Assert(LayoutMemory.Matches(loaded.Plan(layout,physical,new[]{"A","B","C"},unexpected),layout),"compacted layout memory survives restart without overlapping restored middle screen");
                }
                physical[1].Active=true;memory.Observe(layout,physical);
                var setPrimary=memory.Plan(layout,physical,new[]{"A","B","C"},unexpected,"B");
                Assert(setPrimary.Modes[setPrimary.Paths[1].Source.Mode].X==0 && setPrimary.Modes[setPrimary.Paths[0].Source.Mode].X==-1920 && setPrimary.Modes[setPrimary.Paths[2].Source.Mode].X==1920,"selecting middle monitor as primary translates all screens without changing relative order");
                var vertical=LayoutFixture();for(int i=0;i<3;i++) vertical.Modes[i*2]=LayoutMemory.Position(vertical.Modes[i*2],0,i*1080);
                var verticalMemory=new LayoutMemory();verticalMemory.Observe(vertical,physical);
                var compactVertical=verticalMemory.Plan(vertical,physical,new[]{"A","C"},unexpected);
                Assert(compactVertical.Modes[compactVertical.Paths[1].Source.Mode].Y==1080,"vertical monitor gap is closed without changing top-to-bottom order");
                var removePrimary=memory.Plan(layout,physical,new[]{"B","C"},unexpected);
                Assert(removePrimary.Modes[removePrimary.Paths[0].Source.Mode].X==0 && removePrimary.Modes[removePrimary.Paths[1].Source.Mode].X==1920,"primary removal only translates coordinate origin, retaining relative layout");
                physical[0].Active=false;memory.Observe(removePrimary,physical);
                var reenable=memory.Plan(layout,physical,new[]{"A","B","C"},unexpected);
                Assert(LayoutMemory.Matches(reenable,layout),"reenabling restores original primary and all original positions");
                using(var buffer=new MemoryStream())
                {
                    var serializer=new System.Xml.Serialization.XmlSerializer(typeof(LayoutMemory));serializer.Serialize(buffer,memory);buffer.Position=0;
                    var reloaded=(LayoutMemory)serializer.Deserialize(buffer);
                    Assert(LayoutMemory.Matches(reloaded.Plan(layout,physical,new[]{"A","B","C"},unexpected),layout),"position memory survives application restart serialization");
                }
                var shifted=LayoutFixture(); shifted.Modes[2]=LayoutMemory.Position(shifted.Modes[2],0,1080);
                Assert(!LayoutMemory.Matches(shifted,layout),"post-apply verification rejects changed coordinates even with same active targets");
                var slower=LayoutFixture();slower.Paths[1].Target.Refresh.Numerator=30000;
                Assert(!LayoutMemory.Matches(slower,layout),"post-apply verification rejects changed refresh rate");
                string snapshot = System.IO.Path.Combine(temp, "snapshot.xml"); Recovery.WriteSnapshot(snapshot, sample);
                var restored = Recovery.ReadSnapshot(snapshot);
                Assert(restored.Modes[0].D1 == sample.Modes[0].D1 && restored.Modes[0].D5 == ulong.MaxValue && restored.Paths[3].Target.Id == 30, "recovery file preserves native mode bytes");
                Assert(restored.Modes[0].Width == 1920 && restored.Modes[0].Height == 1080 && restored.Modes[0].X == -1920, "native mode dimensions and negative position");
                for (int i = 0; i < 30; i++)
                {
                    string race = System.IO.Path.Combine(temp, "race-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(race); File.WriteAllText(System.IO.Path.Combine(race, "pending"), "");
                    var a = Task.Factory.StartNew(() => Recovery.Claim(race, "confirmed"));
                    var b = Task.Factory.StartNew(() => Recovery.Claim(race, "restoring")); Task.WaitAll(a, b);
                    if (a.Result == b.Result) throw new Exception("confirmation/rollback race has not exactly one winner");
                }
                Assert(true, "30 concurrent confirm/rollback races have exactly one winner");
                string cancel = System.IO.Path.Combine(temp, "cancel-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(cancel);
                Recovery.WriteSnapshot(System.IO.Path.Combine(cancel, "before.xml"), sample); File.WriteAllText(System.IO.Path.Combine(cancel, "pending"), "");
                Recovery.Claim(cancel, "confirmed");
                Assert(Recovery.Guard(cancel) == 0 && !File.Exists(System.IO.Path.Combine(cancel, "restored")), "guardian exits after confirmation without touching real displays");
                string timeout = System.IO.Path.Combine(temp, "timeout-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(timeout);
                Recovery.WriteSnapshot(System.IO.Path.Combine(timeout, "before.xml"), sample);
                File.WriteAllText(System.IO.Path.Combine(timeout, "pending"), ""); File.WriteAllText(System.IO.Path.Combine(timeout, "armed"), "");
                int restoredCalls = 0;
                Assert(Recovery.RunGuard(timeout, 150, delegate(string d, Snapshot s) { restoredCalls++; if (s.Paths[3].Target.Id != 30) throw new Exception("Incorrect recovery data"); }) == 0 && restoredCalls == 1, "guardian timeout invokes restore once with original snapshot (simulated)");
                string childDir = Recovery.Start(sample);
                Assert(File.Exists(System.IO.Path.Combine(childDir, "ready")), "independent guardian executable acknowledges startup");
                Assert(Recovery.Claim(childDir, "cancelled"), "cancel unarmed guardian without changing screens");
                if (args.Contains("--inspect"))
                {
                    var live = NativeDisplay.Query(true); var monitors = NativeDisplay.Monitors(live);
                    Assert(monitors.Count > 0, "read-only live Windows display discovery");
                    foreach (var m in monitors) Console.WriteLine(m.Name + " | " + m.Active + " | " + m.Detail + " | " + m.GdiName+" | "+m.TypeLabel+" | "+m.PositionText+" | "+m.AdapterName);
                    Assert(NativeDisplay.Query(false).Paths.Length > 0, "read-only active configuration backup");
                    var liveMemory=new LayoutMemory();liveMemory.Observe(live,monitors);
                    var keepLayout=liveMemory.Plan(live,monitors,monitors.Where(m=>m.Active).Select(m=>m.Key),NativeDisplay.Preferred);
                    NativeDisplay.ValidateLayout(keepLayout);
                    Assert(LayoutMemory.Matches(NativeDisplay.Query(false),keepLayout),"current full-mode layout validates without changing any coordinates");
                    int newScreenPlans=0;
                    foreach(var inactive in monitors.Where(m=>!m.Active))
                    {
                        var add=liveMemory.Plan(live,monitors,monitors.Where(m=>m.Active || m.Key==inactive.Key).Select(m=>m.Key),NativeDisplay.Preferred);
                        NativeDisplay.ValidateLayout(add);
                        newScreenPlans++;
                    }
                    if(newScreenPlans>0) Assert(true,"unseen inactive display can be planned with explicit modes without moving existing screens");
                    if(monitors.Count<=3)
                    {
                        int accepted=0,blocked=0;
                        for(int mask=1;mask<(1<<monitors.Count);mask++)
                        {
                            Snapshot plan;
                            try { plan=liveMemory.Plan(live,monitors,monitors.Where((m,i)=>(mask&(1<<i))!=0).Select(m=>m.Key),NativeDisplay.Preferred); }
                            catch(InvalidOperationException e) { if(!e.Message.Contains("空隙")) throw; blocked++;continue; }
                            NativeDisplay.ValidateLayout(plan);accepted++;
                        }
                        Assert(accepted>0,"native full-mode validation of every supported fixed-position combination");
                        Console.WriteLine("Fixed-layout combinations: "+accepted+" accepted, "+blocked+" blocked to avoid rearrangement.");
                    }
                    var liveAreas = WindowRecovery.Areas();
                    foreach (var area in liveAreas) Console.WriteLine("Desktop work area: " + area.Work);
                    Assert(liveAreas.Count > 0, "fresh GDI desktop monitor enumeration");
                    if (monitors.Count <= 3)
                    {
                        for (int mask = 1; mask < (1 << monitors.Count); mask++)
                        {
                            var keys = monitors.Where((m, i) => (mask & (1 << i)) != 0).Select(m => m.TargetKey);
                            NativeDisplay.Validate(NativeDisplay.BuildPaths(live, keys));
                        }
                        Assert(true, "Windows validates every nonempty monitor combination (SDC_VALIDATE only)");
                    }
                    Application.EnableVisualStyles();
                    using (var lost = new Form { Text = "ScreenPilot recovery test window", StartPosition = FormStartPosition.Manual, Size = new Size(400, 260), Location = new Point(-20000, -20000) })
                    {
                        lost.Show(); lost.Location = new Point(-20000, -20000); Application.DoEvents();
                        Assert(!WindowRecovery.Reachable(lost.Bounds, liveAreas), "test-owned window starts outside desktop");
                        Assert(WindowRecovery.RecoverOne(lost.Handle, liveAreas) == 1, "native recovery moves test-owned off-screen window");
                        Application.DoEvents();
                        Assert(WindowRecovery.Reachable(lost.Bounds, liveAreas), "recovered test window is actually reachable on active screen");
                        Assert(WindowRecovery.RecoverOne(lost.Handle, liveAreas) == 0, "already reachable window is left untouched");
                        lost.Close();
                    }
                    using (var form = new MainForm())
                    {
                        form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000);
                        form.Show(); Application.DoEvents(); form.PerformLayout();
                        using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height)); bitmap.Save(System.IO.Path.Combine(temp, "ui-preview.png")); }
                        typeof(MainForm).GetMethod("SetBusy", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, new object[] { true });
                        ((Label)typeof(MainForm).GetField("countdown", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form)).Text = "确认画面是否正常？30 秒后自动恢复原配置";
                        Application.DoEvents();
                        using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height)); bitmap.Save(System.IO.Path.Combine(temp, "confirmation-preview.png")); }
                        typeof(MainForm).GetField("exit", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(form, true);
                        form.Close();
                    }
                    Assert(true, "WinForms panel renders without changing screen configuration");
                }
                Console.WriteLine("Passed " + count + " checks. No screen was switched; only a test-owned window was moved."); return 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        }
    }
}
