using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

static class BuildIcon
{
    static GraphicsPath Rounded(float x, float y, float w, float h, float r)
    {
        var p = new GraphicsPath(); p.AddArc(x,y,r*2,r*2,180,90); p.AddArc(x+w-r*2,y,r*2,r*2,270,90);
        p.AddArc(x+w-r*2,y+h-r*2,r*2,r*2,0,90); p.AddArc(x,y+h-r*2,r*2,r*2,90,90); p.CloseFigure(); return p;
    }
    static Bitmap Render(int size)
    {
        using(var large = new Bitmap(size*4,size*4,PixelFormat.Format32bppArgb))
        {
            using(var g=Graphics.FromImage(large))
            {
                g.Clear(Color.Transparent); g.SmoothingMode=SmoothingMode.AntiAlias; g.ScaleTransform(size*4/256f,size*4/256f);
                var points=new PointF[256];
                for(int i=0;i<points.Length;i++) { double a=i*Math.PI*2/points.Length; points[i]=new PointF(128+118*(float)(Math.Sign(Math.Cos(a))*Math.Pow(Math.Abs(Math.Cos(a)),.48)),128+118*(float)(Math.Sign(Math.Sin(a))*Math.Pow(Math.Abs(Math.Sin(a)),.48))); }
                using(var p=new GraphicsPath()) using(var brush=new LinearGradientBrush(new Point(50,10),new Point(210,245),Color.FromArgb(48,182,255),Color.FromArgb(0,91,232))) { p.AddPolygon(points); g.FillPath(brush,p); }
                using(var p=Rounded(51,65,154,108,16)) using(var pen=new Pen(Color.White,13)) g.DrawPath(pen,p);
                using(var pen=new Pen(Color.White,13) { StartCap=LineCap.Round,EndCap=LineCap.Round }) { g.DrawLine(pen,128,177,128,197); g.DrawLine(pen,96,201,160,201); }
                if(size>=24)
                {
                    using(var p=Rounded(86,105,84,28,14)) using(var b=new SolidBrush(Color.FromArgb(215,255,255,255))) g.FillPath(b,p);
                    using(var b=new SolidBrush(Color.FromArgb(0,112,245))) g.FillEllipse(b,144,109,20,20);
                }
            }
            var result=new Bitmap(size,size,PixelFormat.Format32bppArgb);
            using(var g=Graphics.FromImage(result)) { g.InterpolationMode=InterpolationMode.HighQualityBicubic; g.DrawImage(large,new Rectangle(0,0,size,size)); }
            return result;
        }
    }
    static byte[] Dib(Bitmap bitmap)
    {
        using(var stream=new MemoryStream()) using(var w=new BinaryWriter(stream))
        {
            int n=bitmap.Width,mask=((n+31)/32)*4;
            w.Write(40); w.Write(n); w.Write(n*2); w.Write((ushort)1); w.Write((ushort)32); w.Write(0); w.Write(n*n*4); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
            for(int y=n-1;y>=0;y--) for(int x=0;x<n;x++) { var c=bitmap.GetPixel(x,y); w.Write(c.B);w.Write(c.G);w.Write(c.R);w.Write(c.A); }
            for(int y=n-1;y>=0;y--) { var row=new byte[mask]; for(int x=0;x<n;x++) if(bitmap.GetPixel(x,y).A==0) row[x/8]|=(byte)(128>>(x%8)); w.Write(row); }
            return stream.ToArray();
        }
    }
    static void Main(string[] args)
    {
        string root=args[0]; int[] sizes={16,20,24,32,40,48,64,128,256}; var frames=new byte[sizes.Length][];
        for(int i=0;i<sizes.Length;i++) using(var bitmap=Render(sizes[i])) frames[i]=Dib(bitmap);
        using(var file=File.Create(Path.Combine(root,"AppIcon.ico"))) using(var w=new BinaryWriter(file))
        {
            w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Length); int offset=6+16*sizes.Length;
            for(int i=0;i<sizes.Length;i++) { w.Write((byte)(sizes[i]==256?0:sizes[i]));w.Write((byte)(sizes[i]==256?0:sizes[i]));w.Write((byte)0);w.Write((byte)0);w.Write((ushort)1);w.Write((ushort)32);w.Write(frames[i].Length);w.Write(offset);offset+=frames[i].Length; }
            foreach(var frame in frames) w.Write(frame);
        }
        using(var preview=Render(256)) preview.Save(Path.Combine(root,"图标预览.png"),ImageFormat.Png);
    }
}
