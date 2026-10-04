using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace DeskSprout
{
    // Only the outside rim is a glass window. The entire dialog rectangle is
    // removed from its native region, so input controls never inherit its alpha.
    internal sealed class ModalGlassFrame : Form
    {
        private readonly Form dialog;
        private readonly bool dark, enabled;
        private bool attached, nativeActive, failed, released;
        private float scale=1;
        private int rimPadding;
        private Bitmap ring;

        internal bool IsGlassActive { get { return nativeActive && !failed && !IsDisposed; } }
        internal int RingPadding { get { return rimPadding; } }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams value=base.CreateParams;
                value.ExStyle |= 0x08000000 | 0x00000080; // NOACTIVATE | TOOLWINDOW
                return value;
            }
        }

        internal ModalGlassFrame(Form dialog, bool dark, bool enabled)
        {
            if(dialog==null) throw new ArgumentNullException("dialog");
            this.dialog=dialog;this.dark=dark;
            this.enabled=enabled && NativeBackdrop.IsSupported;
            FormBorderStyle=FormBorderStyle.None;
            AutoScaleMode=AutoScaleMode.None;StartPosition=FormStartPosition.Manual;
            ShowInTaskbar=false;DoubleBuffered=false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque,true);
            dialog.Move+=DialogGeometryChanged;
            dialog.Resize+=DialogGeometryChanged;
            dialog.VisibleChanged+=DialogVisibilityChanged;
            dialog.FormClosing+=DialogClosing;
            dialog.FormClosed+=DialogClosed;
            dialog.Disposed+=DialogDisposed;
        }

        // The caller invokes this after the dialog's Shown event. Merely creating
        // the frame, including for an offline preview, never shows another window.
        internal void PresentAttached()
        {
            if(!enabled || failed || released || IsDisposed || dialog.IsDisposed) return;
            if(!dialog.Visible || dialog.WindowState==FormWindowState.Minimized) {Hide();return;}
            attached=true;
            Owner=dialog;if(TopMost!=dialog.TopMost)TopMost=dialog.TopMost;
            SynchronizeGeometry();
            IntPtr ownHandle=Handle;
            if(!nativeActive) {FailClosed();return;}
            if(!Visible) Show(dialog);
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            nativeActive=enabled && NativeBackdrop.Apply(this,true,dark,true);
        }
        protected override void WndProc(ref Message message)
        {
            if(message.Msg==0x0084) {message.Result=new IntPtr(-1);return;} // HTTRANSPARENT
            if(message.Msg==0x0021) {message.Result=new IntPtr(3);return;} // MA_NOACTIVATE
            base.WndProc(ref message);
            if(message.Msg==0x031E && !released && !IsDisposed)
            {
                nativeActive=NativeBackdrop.Apply(this,enabled,dark,true);
                if(!nativeActive) FailClosed();else Invalidate();
            }
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e)
        {
            if(!nativeActive || failed || ring==null) return;
            if(!NativeBackdrop.Present(e.Graphics,ring)) FailClosed();
        }

        private void SynchronizeGeometry()
        {
            if(released || dialog.IsDisposed) return;
            float nextScale;
            using(Graphics graphics=dialog.CreateGraphics()) nextScale=graphics.DpiX/96F;
            bool scaleChanged=Math.Abs(scale-nextScale)>.01F;scale=nextScale;
            rimPadding=PaddingForScale(scale);
            Rectangle bounds=dialog.Bounds;bounds.Inflate(rimPadding,rimPadding);
            bool shapeChanged=ring==null || ring.Size!=bounds.Size || scaleChanged;
            Bounds=bounds;
            if(shapeChanged)
            {
                using(GraphicsPath outer=Art.Rounded(new RectangleF(0,0,bounds.Width,bounds.Height),18*scale))
                {
                    Region next=new Region(outer);
                    next.Exclude(new Rectangle(rimPadding,rimPadding,dialog.Width,dialog.Height));
                    Region previous=Region;Region=next;if(previous!=null)previous.Dispose();
                }
                if(ring!=null)ring.Dispose();
                ring=RenderRing(dialog.Size,scale,dark);
                Invalidate();
            }
        }
        private void DialogGeometryChanged(object sender,EventArgs e)
        {
            if(!attached || failed || released) return;
            if(dialog.WindowState==FormWindowState.Minimized) {Hide();return;}
            if(!Visible) PresentAttached();else SynchronizeGeometry();
        }
        private void DialogVisibilityChanged(object sender,EventArgs e)
        {
            if(!attached || failed || released) return;
            if(dialog.Visible) PresentAttached();else Hide();
        }
        private void DialogClosing(object sender,FormClosingEventArgs e) {if(!released)Hide();}
        private void DialogClosed(object sender,FormClosedEventArgs e) {Dispose();}
        private void DialogDisposed(object sender,EventArgs e) {Dispose();}
        private void FailClosed()
        {
            failed=true;nativeActive=false;
            if(!IsDisposed) {Hide();Close();}
        }

        internal static int PaddingForScale(float scale)
        {
            if(scale<=0 || Single.IsNaN(scale) || Single.IsInfinity(scale)) throw new ArgumentOutOfRangeException("scale");
            return Math.Max(1,checked((int)Math.Ceiling(11*scale)));
        }
        // Pure bitmap render used by previews/tests. No handles or desktop reads.
        internal static Bitmap RenderRing(Size dialogSize,float scale,bool dark)
        {
            if(dialogSize.Width<=0 || dialogSize.Height<=0) throw new ArgumentOutOfRangeException("dialogSize");
            int padding=PaddingForScale(scale);
            int width=checked(dialogSize.Width+2*padding),height=checked(dialogSize.Height+2*padding);
            Bitmap result=new Bitmap(width,height,PixelFormat.Format32bppPArgb);
            try
            {
                using(Graphics graphics=Graphics.FromImage(result))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.SmoothingMode=SmoothingMode.AntiAlias;
                    graphics.ScaleTransform(scale,scale);
                    Art.GlassShell(graphics,new RectangleF(0,0,width/scale,height/scale),18,dark,dark?18:12);
                    graphics.ResetTransform();
                    graphics.CompositingMode=CompositingMode.SourceCopy;
                    using(Brush empty=new SolidBrush(Color.Transparent))
                        graphics.FillRectangle(empty,padding,padding,dialogSize.Width,dialogSize.Height);
                }
                return result;
            }
            catch {result.Dispose();throw;}
        }

        protected override void Dispose(bool disposing)
        {
            if(disposing && !released)
            {
                released=true;
                dialog.Move-=DialogGeometryChanged;
                dialog.Resize-=DialogGeometryChanged;
                dialog.VisibleChanged-=DialogVisibilityChanged;
                dialog.FormClosing-=DialogClosing;
                dialog.FormClosed-=DialogClosed;
                dialog.Disposed-=DialogDisposed;
                if(ring!=null) {ring.Dispose();ring=null;}
                Region previous=Region;Region=null;if(previous!=null)previous.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}