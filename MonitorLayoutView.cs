using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace ScreenPilot
{
    class LayoutTile
    {
        public MonitorInfo Monitor;
        public int Number;
        public RectangleF Bounds;
    }
    class MonitorLayoutView : RoundedPanel
    {
        List<MonitorInfo> screens=new List<MonitorInfo>();
        Func<MonitorInfo,int> number;
        readonly ToolTip tip=new ToolTip();
        string hover;
        public MonitorLayoutView() { AccessibleName="当前屏幕排列"; }
        public void UpdateScreens(List<MonitorInfo> current,Func<MonitorInfo,int> numbering)
        {
            screens=current;number=numbering;
            AccessibleDescription=String.Join("；",screens.Select(m=>"屏幕 "+number(m)+"，"+m.Name+"，"+m.TypeLabel+"，"+m.PositionText));
            hover=null;tip.SetToolTip(this,null);Invalidate();
        }
        internal static List<LayoutTile> Arrange(IEnumerable<MonitorInfo> monitors,Func<MonitorInfo,int> numbering,RectangleF viewport)
        {
            var active=monitors.Where(m=>m.Active && m.HasPosition && m.DesktopBounds.Width>0 && m.DesktopBounds.Height>0).ToList();
            var result=new List<LayoutTile>();
            if(active.Count==0 || viewport.Width<=0 || viewport.Height<=0) return result;
            double left=active.Min(m=>m.DesktopBounds.Left),top=active.Min(m=>m.DesktopBounds.Top);
            double width=active.Max(m=>(double)m.DesktopBounds.X+m.DesktopBounds.Width)-left;
            double height=active.Max(m=>(double)m.DesktopBounds.Y+m.DesktopBounds.Height)-top;
            double scale=Math.Min(viewport.Width/width,viewport.Height/height);
            double x=viewport.X+(viewport.Width-width*scale)/2,y=viewport.Y+(viewport.Height-height*scale)/2;
            foreach(var m in active) result.Add(new LayoutTile { Monitor=m,Number=numbering(m),Bounds=new RectangleF(
                (float)(x+(m.DesktopBounds.X-left)*scale),(float)(y+(m.DesktopBounds.Y-top)*scale),(float)(m.DesktopBounds.Width*scale),(float)(m.DesktopBounds.Height*scale)) });
            return result;
        }
        List<LayoutTile> Tiles() { return Arrange(screens,number,new RectangleF(20,37,Math.Max(0,Width-40),Math.Max(0,Height-67))); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            using(var title=new Font(Font.FontFamily,10,FontStyle.Bold))
                TextRenderer.DrawText(g,"当前屏幕排列",title,new Rectangle(18,10,Width-36,24),RoundedStyle.Text,TextFormatFlags.Left);
            var tiles=Tiles();
            if(tiles.Count==0) TextRenderer.DrawText(g,"暂无可用的桌面位置",Font,new Rectangle(20,42,Width-40,Height-78),RoundedStyle.Secondary,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            foreach(var tile in tiles)
            {
                var rect=tile.Bounds;float inset=Math.Min(2,Math.Min(rect.Width,rect.Height)/8);rect.Inflate(-inset,-inset);
                bool primary=tile.Monitor.Primary;
                Color ink=primary?Color.White:RoundedStyle.Blue;
                using(var shape=RoundedStyle.Round(rect,8))
                using(var brush=new SolidBrush(primary?RoundedStyle.Blue:Color.FromArgb(232,242,255)))
                using(var pen=new Pen(primary?RoundedStyle.Blue:Color.FromArgb(148,195,248),1)) { g.FillPath(brush,shape);g.DrawPath(pen,shape); }
                if(rect.Width<12 || rect.Height<12) continue;
                bool caption=rect.Height>=60 && rect.Width>=70;
                using(var font=new Font("Segoe UI",rect.Height<38?9:20,FontStyle.Bold))
                    TextRenderer.DrawText(g,tile.Number.ToString(),font,new Rectangle((int)rect.X,(int)rect.Y,(int)rect.Width,(int)rect.Height-(caption?22:0)),ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
                if(caption) TextRenderer.DrawText(g,primary?"主屏幕":"屏幕 "+tile.Number,Font,new Rectangle((int)rect.X,(int)rect.Bottom-29,(int)rect.Width,24),ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.EndEllipsis);
            }
            string off=String.Join("、",screens.Where(m=>!m.Active).Select(m=>number(m).ToString()));
            string note=off.Length==0?"按桌面像素比例显示 · 悬停查看坐标 · 蓝色为主屏幕":"未参与桌面：屏幕 "+off+" · 蓝色为主屏幕";
            using(var small=new Font(Font.FontFamily,8.5f)) TextRenderer.DrawText(g,note,small,new Rectangle(18,Height-25,Width-36,22),RoundedStyle.Secondary,TextFormatFlags.Left|TextFormatFlags.EndEllipsis);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var tile=Tiles().LastOrDefault(t=>t.Bounds.Contains(e.Location));
            string text=tile==null?null:"屏幕 "+tile.Number+" · "+tile.Monitor.Name+"\n"+tile.Monitor.TypeLabel+"\n"+tile.Monitor.PositionText+"\n"+tile.Monitor.Detail;
            if(hover!=text) { hover=text;tip.SetToolTip(this,text); }
        }
        protected override void Dispose(bool disposing) { if(disposing) tip.Dispose();base.Dispose(disposing); }
    }
}
