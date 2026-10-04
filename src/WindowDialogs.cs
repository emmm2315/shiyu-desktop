using System;
using System.Drawing;
using System.Windows.Forms;

namespace DeskSprout
{
    internal sealed partial class MainForm
    {
        private Form activeDialog;

        private bool FocusOpenDialog()
        {
            if(activeDialog==null || activeDialog.IsDisposed) return false;
            if(!preview) {
                if(activeDialog.WindowState==FormWindowState.Minimized) activeDialog.WindowState=FormWindowState.Normal;
                activeDialog.BringToFront();activeDialog.Activate();
            }
            return true;
        }

        private void RunWidgetAction(Action action)
        {
            if(FocusOpenDialog()) return;
            Reveal();action();
        }

        private void ShowInformation(string message,string title)
        {
            using(ReadNoteDialog dialog=new ReadNoteDialog(title,"",message,colors.Dark))
                ShowOwnedDialog(dialog);
        }

        private bool ConfirmRemoval(string title)
        {
            using(RemoveConfirmationDialog dialog=new RemoveConfirmationDialog(title,colors.Dark))
                return ShowOwnedDialog(dialog)==DialogResult.OK;
        }

        // Dialog constructors create their HWND for DPI measurement. Explicitly
        // inherit the owner's topmost band before showing that existing HWND.
        private DialogResult ShowOwnedDialog(Form dialog)
        {
            if(dialog==null) throw new ArgumentNullException("dialog");
            Form previous=activeDialog;
            Form owner=previous!=null && !previous.IsDisposed ? previous : this;
            bool pinned=owner.TopMost;
            WidgetDialogBase input=dialog as WidgetDialogBase;
            if(preview) {
                if(input!=null) input.RenderingPreview=true;
                dialog.StartPosition=FormStartPosition.Manual;
                System.Drawing.Rectangle desktop=SystemInformation.VirtualScreen;
                dialog.Location=new Point(desktop.Left-dialog.Width-100,desktop.Top-dialog.Height-100);
            }
            ModalGlassFrame frame=new ModalGlassFrame(dialog,colors.Dark,!preview && core.Settings.GlassEnabled);
            EventHandler shown=delegate {
                if(dialog.TopMost!=pinned) dialog.TopMost=pinned;
                if(!preview && (input==null || !input.RenderingPreview)) {
                    System.Drawing.Rectangle area=Screen.FromControl(owner).WorkingArea;
                    dialog.Location=new System.Drawing.Point(Math.Max(area.Left,Math.Min(dialog.Left,area.Right-dialog.Width)),
                        Math.Max(area.Top,Math.Min(dialog.Top,area.Bottom-dialog.Height)));
                    dialog.BringToFront();dialog.Activate();
                }
                frame.PresentAttached();
            };
            try {
                dialog.Owner=owner;
                if(dialog.TopMost!=pinned) dialog.TopMost=pinned;
                activeDialog=dialog;
                hits.Clear();tip.SetToolTip(this,"");hover=keyboard=-1;
                Invalidate();Update();
                dialog.Shown+=shown;
                return dialog.ShowDialog(owner);
            } finally {
                dialog.Shown-=shown;
                frame.Dispose();
                activeDialog=previous;
                if(!owner.IsDisposed && owner.TopMost!=pinned) owner.TopMost=pinned;
                if(!IsDisposed) Invalidate();
            }
        }
    }

    internal sealed class RemoveConfirmationDialog : WidgetDialogBase
    {
        internal RemoveConfirmationDialog(string title,bool dark)
            : base("删除计划",400,220,dark)
        {
            Label message=AddLabel("删除“"+title+"”？",18,18,364,102);
            message.TextAlign=ContentAlignment.MiddleLeft;
            Buttons("删除",delegate{DialogResult=DialogResult.OK;Close();});
            FinalizeLayout();
        }
    }
}
