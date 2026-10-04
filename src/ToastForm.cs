using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DeskSprout
{
    // A short, non-activating notification. Sound is controlled by the widget itself.
    internal sealed class ToastForm : Form
    {
        private readonly System.Windows.Forms.Timer lifetime=new System.Windows.Forms.Timer();
        private Palette colors;
        private string heading,body;
        private readonly Action open;
        private float scale;
        public ToastForm(bool dark,string title,string message,Action onClick)
        {
            colors=Palette.Create(dark);heading=title;body=message;open=onClick;
            FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;
            AutoScaleMode=AutoScaleMode.None;DoubleBuffered=true;
            using(Graphics g=CreateGraphics())scale=g.DpiX/96f;
            ClientSize=new Size((int)(310*scale),(int)(124*scale));
            using(GraphicsPath p=Art.Rounded(new RectangleF(0,0,Width,Height),14*scale))Region=new Region(p);
            lifetime.Interval=9000;lifetime.Tick+=delegate{Close();};
            MouseUp+=delegate(object sender,MouseEventArgs e) {
                if(e.X/scale>278 && e.Y/scale<39){Close();return;}
                if(e.Button==MouseButtons.Left){if(open!=null)open();Close();}
            };
            Cursor=Cursors.Hand;
        }
        protected override bool ShowWithoutActivation {get{return true;}}
        protected override CreateParams CreateParams {get{CreateParams p=base.CreateParams;p.ExStyle|=0x08000080;return p;}}
        public void Present(Rectangle workArea)
        {
            Location=new Point(workArea.Right-Width-20,workArea.Bottom-Height-20);lifetime.Start();Show();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);Graphics g=e.Graphics;g.ScaleTransform(scale,scale);g.SmoothingMode=SmoothingMode.AntiAlias;
            Art.Fill(g,new RectangleF(0,0,310,124),14,colors.Background);
            Art.Outline(g,new RectangleF(.5f,.5f,309,123),14,colors.Line,1);
            Art.Icon(g,"leaf",new RectangleF(14,13,24,24),colors.Accent);
            Art.Text(g,heading,45,14,230,23,13,colors.Text,FontStyle.Bold,StringAlignment.Near);
            Art.Icon(g,"close",new RectangleF(280,15,20,20),colors.Muted);
            using(Font f=new Font("Microsoft YaHei UI",11,FontStyle.Regular,GraphicsUnit.Pixel))
            using(Brush b=new SolidBrush(colors.Muted))
            using(StringFormat sf=new StringFormat()) {
                sf.Trimming=StringTrimming.EllipsisWord;g.DrawString(body,f,b,new RectangleF(20,45,271,52),sf);
            }
            Art.Text(g,"点击查看",20,102,270,16,9,colors.Muted,FontStyle.Regular,StringAlignment.Far);
        }
        protected override void Dispose(bool disposing) {if(disposing)lifetime.Dispose();base.Dispose(disposing);}
    }
}
