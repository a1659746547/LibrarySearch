using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LibrarySearch {
 // Draw only the scrollbar surface; ListView keeps its native rows, selection,
 // keyboard handling and scroll position. No process-wide AD theme changes.
 internal sealed class ResultScrollBar : Control {
  [StructLayout(LayoutKind.Sequential)] struct NativeRect {public int Left,Top,Right,Bottom;}
  [StructLayout(LayoutKind.Sequential)] struct BarInfo {public int Size;public NativeRect Bounds;public int Line,ThumbTop,ThumbBottom,Reserved;[MarshalAs(UnmanagedType.ByValArray,SizeConst=6)]public uint[] States;}
  [StructLayout(LayoutKind.Sequential)] struct ScrollInfo {public int Size;public uint Mask;public int Min,Max;public uint Page;public int Position,Track;}
  [DllImport("user32.dll")] static extern bool GetScrollBarInfo(IntPtr window,int kind,ref BarInfo info);
  [DllImport("user32.dll")] static extern bool GetScrollInfo(IntPtr window,int kind,ref ScrollInfo info);
  [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
  [DllImport("user32.dll")] static extern IntPtr GetWindowDC(IntPtr window);
  [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window,IntPtr dc);
  readonly ResultList owner;
  int minimum,maximum,page,position,grabOffset;
  bool dragging,hover,syncing;
  Rectangle Thumb {get{int height=Math.Min(Height,Math.Max(24,(int)((long)Height*page/Math.Max(1,maximum-minimum+page))));int top=(int)((long)Math.Max(0,Height-height)*(position-minimum)/Math.Max(1,maximum-minimum));return new Rectangle(3,top,Math.Max(2,Width-6),height);}}
  public ResultScrollBar(ResultList list){owner=list;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);BackColor=PanelTheme.Input;Visible=false;TabStop=false;AccessibleRole=AccessibleRole.ScrollBar;AccessibleName="元件列表滚动条";}
  public void RefreshFromOwner(){
   if(syncing||IsDisposed)return;syncing=true;
   try{
    if(owner.IsDisposed||owner.Disposing||owner.Parent==null||!owner.IsHandleCreated||!owner.Visible||SystemInformation.HighContrast){Visible=false;return;}
    var bar=new BarInfo{Size=Marshal.SizeOf(typeof(BarInfo)),States=new uint[6]};var scroll=new ScrollInfo{Size=Marshal.SizeOf(typeof(ScrollInfo)),Mask=7};
    if(!GetScrollBarInfo(owner.Handle,-5,ref bar)||(bar.States[0]&0x8000)!=0||!GetScrollInfo(owner.Handle,1,ref scroll)||scroll.Page==0||scroll.Max-scroll.Min+1<=(int)scroll.Page){Visible=false;return;}
    minimum=scroll.Min;maximum=Math.Max(minimum,scroll.Max-(int)scroll.Page+1);page=(int)scroll.Page;position=Math.Max(minimum,Math.Min(maximum,scroll.Position));
    if(Parent!=owner.Parent)Parent=owner.Parent;
    var rectangle=owner.Parent.RectangleToClient(Rectangle.FromLTRB(bar.Bounds.Left,bar.Bounds.Top,bar.Bounds.Right,bar.Bounds.Bottom));
    Bounds=Rectangle.Intersect(owner.Parent.ClientRectangle,rectangle);Visible=Width>0&&Height>0;BringToFront();Invalidate();
   }finally{syncing=false;}
  }
  internal void ScrollTo(int target){
   target=Math.Max(minimum,Math.Min(maximum,target));if(!owner.IsHandleCreated||owner.Items.Count==0)return;
   int rowHeight=Math.Max(1,owner.GetItemRect(0).Height);
   SendMessage(owner.Handle,0x1014,IntPtr.Zero,new IntPtr((target-position)*rowHeight)); // LVM_SCROLL uses pixels, supports 32-bit rows.
   RefreshFromOwner();
  }
  internal void PaintOnOwner(IntPtr printDc){
   if(!Visible||owner.IsDisposed||Parent!=owner.Parent)return;
   bool release=printDc==IntPtr.Zero;IntPtr dc=release?GetWindowDC(owner.Handle):printDc;if(dc==IntPtr.Zero)return;
   try{using(var graphics=Graphics.FromHdc(dc)){var area=new Rectangle(Left-owner.Left,Top-owner.Top,Width,Height);using(var background=new SolidBrush(PanelTheme.Input))graphics.FillRectangle(background,area);var thumb=Thumb;thumb.Offset(area.Location);using(var foreground=new SolidBrush(Color.FromArgb(101,101,101)))graphics.FillRectangle(foreground,thumb);}}
   finally{if(release)ReleaseDC(owner.Handle,dc);}
  }
  protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(PanelTheme.Input);using(var brush=new SolidBrush(dragging?PanelTheme.Focus:hover?Color.FromArgb(130,130,130):Color.FromArgb(101,101,101)))e.Graphics.FillRectangle(brush,Thumb);}
  protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
  protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
  protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;owner.Focus();var thumb=Thumb;if(thumb.Contains(e.Location)){dragging=true;grabOffset=e.Y-thumb.Top;Capture=true;Invalidate();}else ScrollTo(position+(e.Y<thumb.Top?-page:page));}
  protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(dragging){int span=Math.Max(1,Height-Thumb.Height);ScrollTo(minimum+(int)((long)Math.Max(0,Math.Min(span,e.Y-grabOffset))*(maximum-minimum)/span));}}
  protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);dragging=false;Capture=false;Invalidate();}
  protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(!Capture){dragging=false;Invalidate();}}
  protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);int lines=SystemInformation.MouseWheelScrollLines;ScrollTo(position-(e.Delta/120)*(lines<0?page:lines));}
 }
}
