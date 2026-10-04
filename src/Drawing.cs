using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DeskSprout
{
    internal sealed class Palette
    {
        public bool Dark;
        public bool Glass;
        public Color Background, Surface, Text, Muted, Line, Accent, AccentSoft, Orange, White;
        public static Palette Create(bool dark)
        {
            return dark ? new Palette {
                Dark = true, Background = Color.FromArgb(54, 61, 71), Surface = Color.FromArgb(65, 74, 87),
                Text = Color.FromArgb(239, 244, 250), Muted = Color.FromArgb(181, 193, 209),
                Line = Color.FromArgb(87, 99, 116), Accent = Color.FromArgb(214, 225, 241),
                AccentSoft = Color.FromArgb(85, 100, 120), Orange = Color.FromArgb(232, 196, 145), White = Color.FromArgb(44, 56, 73)
            } : new Palette {
                Dark = false, Background = Color.FromArgb(246, 249, 253), Surface = Color.FromArgb(232, 238, 247),
                Text = Color.FromArgb(41, 55, 77), Muted = Color.FromArgb(107, 123, 146),
                Line = Color.FromArgb(210, 222, 238), Accent = Color.FromArgb(63, 89, 125),
                AccentSoft = Color.FromArgb(217, 229, 246), Orange = Color.FromArgb(160, 115, 62), White = Color.FromArgb(248, 251, 255)
            };
        }
        public static Palette ClearGlass(bool dark)
        {
            Palette p=Create(dark);p.Glass=true;
            p.Surface=Color.FromArgb(dark?28:40,255,255,255);
            p.AccentSoft=Color.FromArgb(dark?48:68,255,255,255);
            p.Line=Color.FromArgb(dark?85:125,255,255,255);
            p.Background=Color.FromArgb(dark?40:248,dark?48:251,dark?62:255);
            p.Muted=dark?Color.FromArgb(208,219,235):Color.FromArgb(71,89,113);
            return p;
        }
        public static bool SystemDark()
        {
            try {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize"))
                    return key != null && Convert.ToInt32(key.GetValue("AppsUseLightTheme", 1)) == 0;
            } catch { return false; }
        }
    }

    internal static class Art
    {
        [ThreadStatic] internal static bool TransparentText=false;
        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }
        public static void Fill(Graphics g, RectangleF r, float radius, Color color)
        {
            using (GraphicsPath p = Rounded(r, radius))
            using (SolidBrush b = new SolidBrush(color)) g.FillPath(b, p);
        }
        public static void Outline(Graphics g, RectangleF r, float radius, Color color, float width)
        {
            using (GraphicsPath p = Rounded(r, radius))
            using (Pen pen = new Pen(color, width)) g.DrawPath(pen, p);
        }
        public static void GlassShell(Graphics g,RectangleF r,float radius,bool dark,int tint)
        {
            using(GraphicsPath path=Rounded(r,radius)) {
                Color baseColor=dark?Color.FromArgb(tint,30,40,58):Color.FromArgb(tint,245,250,255);
                using(Brush b=new SolidBrush(baseColor))g.FillPath(b,path);
                using(LinearGradientBrush shine=new LinearGradientBrush(r,Color.FromArgb(dark?27:40,255,255,255),Color.FromArgb(2,255,255,255),LinearGradientMode.Vertical))g.FillPath(shine,path);
            }
            RectangleF edge=new RectangleF(r.X+.6f,r.Y+.6f,r.Width-1.2f,r.Height-1.2f);
            using(GraphicsPath p=Rounded(edge,radius))
            using(LinearGradientBrush rim=new LinearGradientBrush(r,Color.FromArgb(164,255,255,255),Color.FromArgb(35,255,255,255),LinearGradientMode.Vertical))
            using(Pen pen=new Pen(rim,1.1f))g.DrawPath(pen,p);
            edge.Inflate(-1.4f,-1.4f);Outline(g,edge,Math.Max(1,radius-1.4f),Color.FromArgb(28,255,255,255),.7f);
        }
        public static void Text(Graphics g, string text, float x, float y, float w, float h,
            float size, Color color, FontStyle style, StringAlignment align)
        {
            using (Font f = new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Pixel))
            using (Brush b = new SolidBrush(color))
            using (StringFormat sf = new StringFormat()) {
                sf.Alignment = align; sf.LineAlignment = StringAlignment.Center;
                sf.Trimming = StringTrimming.EllipsisCharacter; sf.FormatFlags = StringFormatFlags.NoWrap;
                if(TransparentText) {
                    Color shadow=color.GetBrightness()>.65f?Color.FromArgb(128,12,23,39):Color.FromArgb(112,255,255,255);
                    using(Brush under=new SolidBrush(shadow))g.DrawString(text ?? "",f,under,new RectangleF(x,y+.65f,w,h),sf);
                }
                g.DrawString(text ?? "", f, b, new RectangleF(x, y, w, h), sf);
            }
        }
        public static void Icon(Graphics g, string kind, RectangleF r, Color color)
        {
            GraphicsState saved=g.Save();g.TranslateTransform(r.X,r.Y);g.ScaleTransform(r.Width/22f,r.Height/22f);
            float x = 0, y = 0, w = 22, h = 22;
            using (Pen p = new Pen(color, 1.55f)) {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                if (kind == "close") { g.DrawLine(p,x+5,y+5,x+w-5,y+h-5); g.DrawLine(p,x+w-5,y+5,x+5,y+h-5); }
                else if (kind == "fold") { g.DrawLine(p,x+4,y+h/2,x+w-4,y+h/2); }
                else if (kind == "expand") { g.DrawRectangle(p,x+4,y+5,w-8,h-10); g.DrawLine(p,x+4,y+9,x+w-4,y+9); }
                else if (kind == "pin") {
                    g.DrawLine(p,x+7,y+4,x+w-7,y+4); g.DrawLine(p,x+8,y+4,x+7,y+11);
                    g.DrawLine(p,x+w-8,y+4,x+w-7,y+11); g.DrawLine(p,x+5,y+12,x+w-5,y+12);
                    g.DrawLine(p,x+w/2,y+12,x+w/2,y+h-3);
                }
                else if (kind == "plus") { g.DrawLine(p,x+4,y+h/2,x+w-4,y+h/2); g.DrawLine(p,x+w/2,y+4,x+w/2,y+h-4); }
                else if (kind == "play") {
                    PointF[] pts = {new PointF(x+7,y+4), new PointF(x+w-5,y+h/2), new PointF(x+7,y+h-4)};
                    using (Brush b = new SolidBrush(color)) g.FillPolygon(b,pts);
                }
                else if (kind == "pause") { g.DrawLine(p,x+7,y+5,x+7,y+h-5); g.DrawLine(p,x+w-7,y+5,x+w-7,y+h-5); }
                else if (kind == "reset") { g.DrawArc(p,x+4,y+4,w-8,h-8,35,290); g.DrawLine(p,x+5,y+3,x+5,y+9); g.DrawLine(p,x+5,y+9,x+10,y+8); }
                else if (kind == "gear") {
                    g.DrawEllipse(p,x+6,y+6,w-12,h-12);
                    for(int i=0;i<8;i++) {double a=i*Math.PI/4; g.DrawLine(p,x+w/2+(float)Math.Cos(a)*8,y+h/2+(float)Math.Sin(a)*8,x+w/2+(float)Math.Cos(a)*10,y+h/2+(float)Math.Sin(a)*10);}
                }
                else if (kind == "check") { g.DrawLines(p,new PointF[]{new PointF(x+5,y+h/2),new PointF(x+9,y+h-6),new PointF(x+w-4,y+5)}); }
                else if (kind == "leaf") {
                    using (GraphicsPath path = new GraphicsPath()) {
                        path.AddBezier(x+5,y+h-4,x+2,y+5,x+w-2,y+2,x+w-4,y+3);
                        path.AddBezier(x+w-4,y+3,x+w-2,y+h-2,x+7,y+h-4,x+5,y+h-4);
                        using (Brush b = new SolidBrush(color)) g.FillPath(b,path);
                    }
                    using(Pen vein=new Pen(Color.FromArgb(140,255,255,255),1)) g.DrawLine(vein,x+6,y+h-6,x+w-8,y+8);
                }
                else if (kind == "dots") { using(Brush b=new SolidBrush(color)) for(int i=0;i<3;i++) g.FillEllipse(b,x+4+i*6,y+h/2-1.5f,3,3); }
                else if (kind == "next") { g.DrawLine(p,x+7,y+5,x+w-7,y+h/2); g.DrawLine(p,x+w-7,y+h/2,x+7,y+h-5); }
            }
            g.Restore(saved);
        }
        public static Icon AppIcon()
        {
            using (Bitmap bmp = new Bitmap(32,32))
            using (Graphics g = Graphics.FromImage(bmp)) {
                g.SmoothingMode=SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
                Fill(g,new RectangleF(1,1,30,30),9,Color.FromArgb(76,117,82));
                Icon(g,"leaf",new RectangleF(4,4,24,24),Color.FromArgb(239,247,225));
                IntPtr h=bmp.GetHicon();
                try {using(Icon icon=System.Drawing.Icon.FromHandle(h)) return (Icon)icon.Clone();}
                finally {DestroyIcon(h);}
            }
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    }
}
