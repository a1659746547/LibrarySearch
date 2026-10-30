using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Drawing;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace LibrarySearch {
 internal sealed class AppearancePreview : Control {
  public SymbolAppearance Style=new SymbolAppearance();
  public AppearancePreview(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);AccessibleName="符号外观示意预览";}
  protected override void OnPaint(PaintEventArgs e){
   var style=Style;var graphics=e.Graphics;graphics.Clear(ColorTranslator.FromHtml(style.Background));graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
   int middle=Width/2,top=Math.Max(65,Height/5),bodyWidth=Math.Min(170,Width-100),bodyHeight=170;var body=new Rectangle(middle-bodyWidth/2,top,bodyWidth,bodyHeight);
   using(var grid=new Pen(Color.FromArgb(235,238,241))){for(int x=0;x<Width;x+=24)graphics.DrawLine(grid,x,40,x,Math.Max(40,Height-65));for(int y=40;y<Height-65;y+=24)graphics.DrawLine(grid,0,y,Width,y);}
   using(var pen=new Pen(ColorTranslator.FromHtml(style.BorderColor),style.LineWidth==2?2.5F:style.LineWidth==1?1.7F:1F))using(var brush=new SolidBrush(ColorTranslator.FromHtml(style.BodyFill)))using(var path=new System.Drawing.Drawing2D.GraphicsPath()){
    int r=style.RoundedCorners?Math.Max(4,Math.Min(12,style.CornerRadius/4)):0;if(r==0)path.AddRectangle(body);else{path.AddArc(body.Left,body.Top,r*2,r*2,180,90);path.AddArc(body.Right-r*2,body.Top,r*2,r*2,270,90);path.AddArc(body.Right-r*2,body.Bottom-r*2,r*2,r*2,0,90);path.AddArc(body.Left,body.Bottom-r*2,r*2,r*2,90,90);path.CloseFigure();}if(style.FillBody)graphics.FillPath(brush,path);graphics.DrawPath(pen,path);
    string[] left={"VBUS","D−","GND"},right={"CC1","SHIELD","6"};
    using(var names=new Font(style.FontFamily,style.PinNameSize))using(var numbers=new Font(style.FontFamily,style.PinNumberSize))using(var title=new Font(style.FontFamily,style.LabelSize,FontStyle.Bold)){
     TextRenderer.DrawText(graphics,"CN?",title,new Point(body.Left,body.Top-32),ColorTranslator.FromHtml(style.TextColor));
     for(int i=0;i<3;i++){int y=body.Top+40+i*46;graphics.DrawLine(pen,body.Left-32,y,body.Left,y);graphics.DrawLine(pen,body.Right,y,body.Right+32,y);TextRenderer.DrawText(graphics,(i+1).ToString(),numbers,new Point(body.Left-25,y-20),ColorTranslator.FromHtml(style.PinNumberColor));TextRenderer.DrawText(graphics,(i+4).ToString(),numbers,new Point(body.Right+5,y-20),ColorTranslator.FromHtml(style.PinNumberColor));TextRenderer.DrawText(graphics,left[i],names,new Rectangle(body.Left+8,y-10,body.Width/2-6,30),ColorTranslator.FromHtml(style.TextColor),TextFormatFlags.Left|TextFormatFlags.NoPadding);if(i!=2||!style.HideDuplicatePinNames)TextRenderer.DrawText(graphics,right[i],names,new Rectangle(middle,y-10,body.Width/2-8,30),ColorTranslator.FromHtml(style.TextColor),TextFormatFlags.Right|TextFormatFlags.NoPadding);}
     if(style.ShowComment)TextRenderer.DrawText(graphics,"USB-C / MODEL",names,new Rectangle(10,body.Bottom+20,Width-20,30),ColorTranslator.FromHtml(style.TextColor),TextFormatFlags.HorizontalCenter);
    }
   }
   TextRenderer.DrawText(graphics,"符号预览",Font,new Rectangle(5,Height-46,Width-10,38),Color.FromArgb(102,102,102),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak);
  }
 }

 internal static class PanelTheme {
  public static readonly Color Surface=Color.FromArgb(59,59,59),Input=Color.FromArgb(45,45,45),Header=Color.FromArgb(73,73,73),Border=Color.FromArgb(37,37,37);
  public static readonly Color Ink=Color.FromArgb(227,227,227),Muted=Color.FromArgb(185,185,185),Selection=Color.FromArgb(76,110,135),Hover=Color.FromArgb(82,82,82),Focus=Color.FromArgb(145,186,215);
  public static readonly Color Error=Color.FromArgb(239,153,139),Warning=Color.FromArgb(223,184,124);
 }
 internal sealed class PanelMenuColors : ProfessionalColorTable {
  public override Color ToolStripDropDownBackground {get{return PanelTheme.Surface;}}
  public override Color ImageMarginGradientBegin {get{return PanelTheme.Surface;}}
  public override Color ImageMarginGradientMiddle {get{return PanelTheme.Surface;}}
  public override Color ImageMarginGradientEnd {get{return PanelTheme.Surface;}}
  public override Color MenuItemSelected {get{return PanelTheme.Selection;}}
  public override Color MenuItemSelectedGradientBegin {get{return PanelTheme.Selection;}}
  public override Color MenuItemSelectedGradientEnd {get{return PanelTheme.Selection;}}
  public override Color MenuItemBorder {get{return PanelTheme.Focus;}}
  public override Color MenuBorder {get{return PanelTheme.Border;}}
  public override Color SeparatorDark {get{return PanelTheme.Border;}}
  public override Color SeparatorLight {get{return PanelTheme.Surface;}}
 }
 public sealed class ResultList : ListView {
  readonly ImageList rowImages=new ImageList();
  readonly ResultScrollBar scrollBar;
  public ResultList(){
   DoubleBuffered=true;OwnerDraw=true;BackColor=PanelTheme.Input;ForeColor=PanelTheme.Ink;
   rowImages.ImageSize=new Size(1,24);SmallImageList=rowImages;
   scrollBar=new ResultScrollBar(this);
  }
  protected override void OnParentChanged(EventArgs e){base.OnParentChanged(e);if(scrollBar!=null&&!scrollBar.IsDisposed){scrollBar.Parent=Parent;scrollBar.RefreshFromOwner();}}
  protected override void OnVisibleChanged(EventArgs e){base.OnVisibleChanged(e);if(scrollBar!=null)scrollBar.RefreshFromOwner();}
  protected override void WndProc(ref Message message){base.WndProc(ref message);if(scrollBar!=null){if(message.Msg==0xF||message.Msg==0x85||message.Msg==5||message.Msg==0x115||message.Msg==0x20A||message.Msg==0x101||message.Msg==0x102F||message.Msg==0x1014||message.Msg==0x317)scrollBar.RefreshFromOwner();if(message.Msg==0xF||message.Msg==0x85||message.Msg==0x317)scrollBar.PaintOnOwner(message.Msg==0x317?message.WParam:IntPtr.Zero);}}
  protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);if(rowImages!=null)rowImages.ImageSize=new Size(1,Math.Max(24,Font.Height+12));}
  protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e){
   using(var brush=new SolidBrush(PanelTheme.Header))e.Graphics.FillRectangle(brush,e.Bounds);
   using(var pen=new Pen(PanelTheme.Border)){e.Graphics.DrawLine(pen,e.Bounds.Right-1,e.Bounds.Top,e.Bounds.Right-1,e.Bounds.Bottom);e.Graphics.DrawLine(pen,e.Bounds.Left,e.Bounds.Bottom-1,e.Bounds.Right,e.Bounds.Bottom-1);}
   TextRenderer.DrawText(e.Graphics,e.Header.Text,Font,new Rectangle(e.Bounds.X+7,e.Bounds.Y,e.Bounds.Width-10,e.Bounds.Height),PanelTheme.Muted,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
  }
  protected override void OnDrawItem(DrawListViewItemEventArgs e){if(View!=View.Details)e.DrawDefault=true;}
  internal int AvailableColumnWidth {get{
   int border=BorderStyle==BorderStyle.Fixed3D?SystemInformation.Border3DSize.Width*2:BorderStyle==BorderStyle.FixedSingle?2:0;
   // Reserve the native vertical bar before items arrive: its late nonclient
   // layout does not reliably raise Resize in a docked AD panel.
   return Math.Max(0,Math.Min(ClientSize.Width,Width-border-SystemInformation.VerticalScrollBarWidth)-1);
  }}
  internal void ResetResultOrigin(){
   if(!IsHandleCreated||Items.Count==0)return;
   EnsureVisible(0);
   // Native virtual lists can retain a negative origin when a scrolled result
   // set shrinks. EnsureVisible does nothing when row zero is already inside
   // that offset viewport; rebuilding only that inconsistent handle clears it.
   if(TopItem==null||TopItem.Index!=0)RecreateHandle();
   Invalidate();
  }
  protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);if(IsHandleCreated)Invalidate();}
  protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e){base.OnDrawSubItem(e); }
  protected override void Dispose(bool disposing){if(disposing&&scrollBar!=null)scrollBar.Dispose();base.Dispose(disposing);if(disposing)rowImages.Dispose();}
 }

 internal enum PanelButtonKind { Command, Primary, Filter, Navigation, Tool }
 internal sealed class PanelButton : Control {
  PanelButtonKind kind;
  bool active,hover,pressed;
  public PanelButtonKind Kind {get{return kind;}set{kind=value;Invalidate();}}
  public bool Active {get{return active;}set{active=value;Invalidate();}}
  public PanelButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable|ControlStyles.StandardClick,true);SetStyle(ControlStyles.StandardDoubleClick,false);TabStop=true;AccessibleRole=AccessibleRole.PushButton;Cursor=Cursors.Default;}
  protected override void OnPaint(PaintEventArgs e){
   bool nav=kind==PanelButtonKind.Navigation;
   Color background=PanelTheme.Surface;
   if(kind==PanelButtonKind.Primary)background=Enabled?(pressed?Color.FromArgb(55,91,117):hover?Color.FromArgb(86,132,164):PanelTheme.Selection):Color.FromArgb(63,70,75);
   else if(active&&!nav)background=Color.FromArgb(67,84,97);
   else if(pressed)background=PanelTheme.Input;
   else if(hover&&Enabled)background=PanelTheme.Hover;
   else if(kind==PanelButtonKind.Command)background=PanelTheme.Header;
   using(var brush=new SolidBrush(background))e.Graphics.FillRectangle(brush,ClientRectangle);
   if(Width<12||Height<6)return;
   if(kind==PanelButtonKind.Command||kind==PanelButtonKind.Primary){using(var pen=new Pen(PanelTheme.Border))e.Graphics.DrawRectangle(pen,0,0,Math.Max(0,Width-1),Math.Max(0,Height-1));}
   if(active&&nav){using(var brush=new SolidBrush(PanelTheme.Focus))e.Graphics.FillRectangle(brush,7,Math.Max(0,Height-3),Math.Max(0,Width-14),2);}
   Color foreground=!Enabled?Color.FromArgb(153,158,162):active||kind==PanelButtonKind.Primary?Color.White:kind==PanelButtonKind.Filter||kind==PanelButtonKind.Tool?PanelTheme.Muted:PanelTheme.Ink;
   var bounds=Rectangle.Inflate(ClientRectangle,-6,-2);
   TextRenderer.DrawText(e.Graphics,Text,Font,bounds,foreground,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
   if(Focused&&ShowFocusCues){using(var pen=new Pen(PanelTheme.Focus)){pen.DashStyle=System.Drawing.Drawing2D.DashStyle.Dot;e.Graphics.DrawRectangle(pen,3,3,Math.Max(0,Width-7),Math.Max(0,Height-7));}}
  }
  protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
  protected override void OnMouseLeave(EventArgs e){hover=false;pressed=false;Invalidate();base.OnMouseLeave(e);}
  protected override void OnMouseDown(MouseEventArgs e){if(e.Button==MouseButtons.Left&&Enabled){Focus();pressed=true;Invalidate();}base.OnMouseDown(e);}
  protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
  protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
  protected override void OnLostFocus(EventArgs e){pressed=false;base.OnLostFocus(e);Invalidate();}
  protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);Invalidate();}
  protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);Invalidate();}
  protected override bool IsInputKey(Keys keyData){Keys key=keyData&Keys.KeyCode;return key==Keys.Space||key==Keys.Enter||key==Keys.Left||key==Keys.Right||base.IsInputKey(keyData);}
  protected override void OnKeyDown(KeyEventArgs e){
   if(Enabled&&(e.KeyCode==Keys.Space||e.KeyCode==Keys.Enter)){pressed=true;Invalidate();e.Handled=true;if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;pressed=false;OnClick(EventArgs.Empty);}}
   base.OnKeyDown(e);
  }
  protected override void OnKeyUp(KeyEventArgs e){if(e.KeyCode==Keys.Space&&pressed){pressed=false;Invalidate();if(Enabled)OnClick(EventArgs.Empty);e.Handled=true;}base.OnKeyUp(e);}
  protected override void OnClick(EventArgs e){if(Enabled)base.OnClick(e);}
 }
 internal sealed class SourceTabs : UserControl {
  readonly PanelButton[] tabs=new PanelButton[3];
  readonly int[] displayOrder={0,2,1};
  readonly string[] titles={"在线库","已下载","本地库"},compactTitles={"在线库","已下载","本地库"};
  bool arranging;
  int selected;
  public event EventHandler SelectedIndexChanged;
  public int SelectedIndex {get{return selected;}set{if(value<0||value>=tabs.Length)throw new ArgumentOutOfRangeException("value");if(selected==value)return;selected=value;UpdateSelection();if(SelectedIndexChanged!=null)SelectedIndexChanged(this,EventArgs.Empty);}}
  public int PreferredTabWidth {get{int total=0;for(int i=0;i<tabs.Length;i++)if(tabs[i]!=null)total+=TextRenderer.MeasureText(titles[i],tabs[i].Font).Width+24;return total;}}
  public int PreferredTabHeight {get{return tabs.Where(tab=>tab!=null).Select(tab=>tab.Font.Height+16).DefaultIfEmpty(32).Max();}}
  public SourceTabs(){
   AutoScaleMode=AutoScaleMode.None;TabStop=false;BackColor=PanelTheme.Surface;
   for(int index=0;index<tabs.Length;index++){
    int choice=index;var tab=new PanelButton{Text=titles[index],AccessibleName=titles[index],Kind=PanelButtonKind.Navigation,Margin=Padding.Empty,TabIndex=Array.IndexOf(displayOrder,index)};
    tab.Click+=(sender,args)=>SelectedIndex=choice;
    tab.KeyDown+=(sender,args)=>{if(args.KeyCode==Keys.Left||args.KeyCode==Keys.Right){SelectedIndex=displayOrder[(Array.IndexOf(displayOrder,choice)+(args.KeyCode==Keys.Right?1:2))%3];tabs[SelectedIndex].Focus();args.Handled=true;}};
    tab.FontChanged+=(sender,args)=>{PerformLayout();if(Parent!=null)Parent.PerformLayout();};tabs[index]=tab;Controls.Add(tab);
   }
   UpdateSelection();
  }
  void UpdateSelection(){for(int i=0;i<tabs.Length;i++)if(tabs[i]!=null)tabs[i].Active=i==selected;}
  protected override void OnLayout(LayoutEventArgs e){
   base.OnLayout(e);if(arranging||tabs==null||tabs.Any(tab=>tab==null))return;
   arranging=true;try{
    bool compact=PreferredTabWidth>ClientSize.Width;
    for(int i=0;i<tabs.Length;i++)tabs[i].Text=compact?compactTitles[i]:titles[i];
    int ideal=tabs.Sum(tab=>TextRenderer.MeasureText(tab.Text,tab.Font).Width+24),x=0;
    for(int position=0;position<displayOrder.Length;position++){
     int i=displayOrder[position];
     int width=ideal<=ClientSize.Width?TextRenderer.MeasureText(tabs[i].Text,tabs[i].Font).Width+24:ClientSize.Width/3;
     if(position==tabs.Length-1&&ideal>ClientSize.Width)width=ClientSize.Width-x;
     tabs[i].Bounds=new Rectangle(x,0,Math.Max(0,width),Math.Max(0,ClientSize.Height));x+=width;
    }
   }finally{arranging=false;}
  }
 }
 internal sealed class EmptyResultState : Control {
  public EmptyResultState(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);BackColor=PanelTheme.Input;TabStop=false;}
  protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);Invalidate();}
  protected override void OnPaint(PaintEventArgs e){
   e.Graphics.Clear(PanelTheme.Input);
   string[] lines=Text.Split(new[]{"\r\n","\n"},StringSplitOptions.RemoveEmptyEntries);
   string heading=lines.Length>0?lines[0]:"";
   string description=string.Join("\r\n",lines.Skip(1).ToArray());
   int line=Font.Height,icon=Math.Max(22,line+7),top=Math.Max(16,(Height-(icon+line*4+20))/2);
   e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
   using(var pen=new Pen(Color.FromArgb(108,122,131),1.5F)){int x=(Width-icon)/2;e.Graphics.DrawEllipse(pen,x,top,icon-7,icon-7);e.Graphics.DrawLine(pen,x+icon-9,top+icon-9,x+icon,top+icon);}
   var bounds=new Rectangle(18,top+icon+15,Math.Max(0,Width-36),line*2+4);
   using(var bold=new Font(Font,FontStyle.Bold))TextRenderer.DrawText(e.Graphics,heading,bold,bounds,PanelTheme.Ink,TextFormatFlags.Top|TextFormatFlags.HorizontalCenter|TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
   bounds.Y+=line+10;bounds.Height=line*3+8;
   TextRenderer.DrawText(e.Graphics,description,Font,bounds,PanelTheme.Muted,TextFormatFlags.Top|TextFormatFlags.HorizontalCenter|TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
  }
 }

 public sealed class LibraryPanel : UserControl {
  static readonly Color Ink=PanelTheme.Ink,Muted=PanelTheme.Muted,Accent=PanelTheme.Selection,Surface=PanelTheme.Surface;
  readonly string distribution;
  UserSettings settings;
  LibraryStore store;
  readonly TextBox query=new TextBox();
  readonly SourceTabs source=new SourceTabs();
  readonly ResultList list=new ResultList();
  readonly InlineModelResults modelResults=new InlineModelResults();
  readonly PanelPageHost pageHost=new PanelPageHost();
  readonly Panel rootHost=new Panel();
  bool candidateMode,usageNoticeShown;
  internal Func<string,CancellationToken,FetchResult> FetchOverride=null;
  readonly EmptyResultState empty=new EmptyResultState();
  readonly Label placeholder=new Label(),resultCount=new Label(),name=new Label(),description=new Label(),meta=new Label(),related=new Label(),location=new Label(),notice=new Label(),status=new Label(),pathHint=new Label();
  readonly PanelButton search=new PanelButton(),cancel=new PanelButton(),place=new PanelButton(),open=new PanelButton(),model=new PanelButton(),more=new PanelButton();
  readonly PanelButton[] filters={new PanelButton(),new PanelButton(),new PanelButton()};
  readonly Panel inputFrame=new Panel(),resultsFrame=new Panel(),detail=new Panel(),actions=new Panel();
  readonly ProgressBar progress=new ProgressBar();
  readonly ToolTip tips=new ToolTip();
  readonly System.Windows.Forms.Timer debounce=new System.Windows.Forms.Timer();
  readonly System.Windows.Forms.Timer updateTimer=new System.Windows.Forms.Timer();
  readonly LinkLabel updateBanner=new LinkLabel();
  readonly UpdateState updateState=UpdateState.Load();
  CancellationTokenSource updateCancellation;
  WebsiteRelease availableUpdate;
  bool updateChecking;
  ContextMenuStrip moreMenu;
  ToolStripMenuItem refreshCommand,locateCommand,modelCommand,deleteCommand,selectAllCommand;
  readonly List<Font> ownedFonts=new List<Font>();
  readonly ImageList resultRowImages=new ImageList();
  Font resultSecondaryFont;
  LibraryRecord[] downloaded=new LibraryRecord[0],local=new LibraryRecord[0],online=new LibraryRecord[0],results=new LibraryRecord[0];
  LocalLibraryCatalog localCatalog;
  CancellationTokenSource cancellation,descriptionCancellation,filterCancellation,localScanCancellation;
  internal Func<string[],CancellationToken,Action<string>,string,bool,LocalIndexBuild> LocalScanOverride=null;
  string descriptionKey="";
  bool loaded,busy,nativeBusy,started,uiReady,layingOut,selectionWarning;
  int filter,searchVersion;
  DateTime lastLocalScan=DateTime.MinValue;
  bool localScanWarning,selectionChanging,localScanning;
  readonly string[] sourceQueries={"","",""};
  int activeSource;
  bool restoringSource,onlineResultView;
  public event Action<LibraryRecord> PlaceRequested;
  public Func<string,string> PlacementContext {get;set;}
  public event Action<LibraryRecord> OpenRequested;
  Font PanelFont(float size,FontStyle style){var value=new Font("Microsoft YaHei UI",size,style);ownedFonts.Add(value);return value;}
  public LibraryPanel(string distributionRoot,UserSettings initialSettings=null){
   // SDK-bound adapters inline the release that originally built them.
   if(!string.IsNullOrWhiteSpace(distributionRoot)){
    var parent=Directory.GetParent(Path.GetFullPath(distributionRoot));
    if(parent!=null&&parent.Name.Equals("LibrarySearchBeta",StringComparison.OrdinalIgnoreCase)){
     string current=Path.Combine(parent.FullName,ReleaseInfo.Version);
     if(File.Exists(Path.Combine(current,"Release.psd1"))&&File.Exists(Path.Combine(current,"online","bin","easyeda2altium.exe")))distributionRoot=current;
    }
   }
   distribution=distributionRoot;settings=initialSettings??UserSettings.Load();store=new LibraryStore(settings.LibraryRoot);
   AutoScaleMode=AutoScaleMode.None;Font=PanelFont(9F,FontStyle.Regular);ForeColor=Ink;BackColor=Surface;Dock=DockStyle.Fill;MinimumSize=Size.Empty;
   rootHost.BackColor=Surface;Controls.Add(rootHost);Controls.Add(pageHost);
   source.AccessibleName="元件来源";
   source.SelectedIndexChanged+=(sender,args)=>ChangeSource();
   ConfigureButton(more,"设置",PanelButtonKind.Tool);more.Click+=(sender,args)=>ShowMore();tips.SetToolTip(more,"符号外观、库目录、帮助与反馈");

   inputFrame.BackColor=PanelTheme.Input;
   inputFrame.Paint+=(sender,args)=>{
    using(var pen=new Pen(query.Focused?PanelTheme.Focus:PanelTheme.Border))args.Graphics.DrawRectangle(pen,0,0,Math.Max(0,inputFrame.Width-1),Math.Max(0,inputFrame.Height-1));
    int diameter=Math.Max(9,Math.Min(13,query.Font.Height-2)),top=(inputFrame.Height-diameter)/2-1;
    using(var pen=new Pen(Muted)){args.Graphics.DrawEllipse(pen,9,top,diameter-3,diameter-3);args.Graphics.DrawLine(pen,9+diameter-4,top+diameter-4,9+diameter,top+diameter);}
   };
   query.BorderStyle=BorderStyle.None;query.BackColor=PanelTheme.Input;query.ForeColor=Ink;query.AccessibleName="元件编号或关键词";
   query.Enter+=(sender,args)=>{UpdateInputHint();inputFrame.Invalidate();};query.Leave+=(sender,args)=>{UpdateInputHint();inputFrame.Invalidate();};
   query.KeyDown+=(sender,args)=>{if(args.KeyCode==Keys.Enter){SearchRequested(false);args.SuppressKeyPress=true;}};
   query.TextChanged+=(sender,args)=>{CancelFiltering();UpdateInputHint();if(restoringSource||busy||nativeBusy)return;SetCandidateMode(false);if(source.SelectedIndex==0){if(online.Length>0&&!query.Text.Trim().Equals(online[0].Code,StringComparison.OrdinalIgnoreCase)){online=new LibraryRecord[0];SetResults(online);empty.Text="获取一个新元件\r\n输入型号、厂家搜索，或输入 C 编号直接获取";}}else{debounce.Stop();debounce.Start();}};
   placeholder.BackColor=PanelTheme.Input;placeholder.ForeColor=Muted;placeholder.TextAlign=ContentAlignment.MiddleLeft;placeholder.AutoEllipsis=true;placeholder.Cursor=Cursors.IBeam;placeholder.Click+=(sender,args)=>query.Focus();
   inputFrame.Controls.Add(query);inputFrame.Controls.Add(placeholder);
   ConfigureButton(search,"获取",PanelButtonKind.Primary);search.Click+=(sender,args)=>SearchRequested(false);

   string[] kinds={"全部","原理图","PCB 封装"};
   for(int index=0;index<filters.Length;index++){int selected=index;ConfigureButton(filters[index],kinds[index],PanelButtonKind.Filter);filters[index].AccessibleName="筛选"+kinds[index];filters[index].Click+=(sender,args)=>{filter=selected;StyleFilters();FilterResults();};rootHost.Controls.Add(filters[index]);}
   resultCount.ForeColor=Muted;resultCount.TextAlign=ContentAlignment.MiddleRight;resultCount.AutoEllipsis=true;

   resultsFrame.BackColor=PanelTheme.Input;
   resultsFrame.Paint+=(sender,args)=>{using(var pen=new Pen(PanelTheme.Border))args.Graphics.DrawRectangle(pen,0,0,Math.Max(0,resultsFrame.Width-1),Math.Max(0,resultsFrame.Height-1));};
   list.View=View.Details;list.FullRowSelect=true;list.HideSelection=false;list.MultiSelect=true;list.VirtualMode=true;list.BorderStyle=BorderStyle.None;list.HeaderStyle=ColumnHeaderStyle.Nonclickable;list.AccessibleName="元件搜索结果";list.ShowItemToolTips=true;list.OwnerDraw=true;
   resultSecondaryFont=PanelFont(8F,FontStyle.Regular);resultRowImages.ImageSize=new Size(1,Math.Max(44,list.Font.Height*2+12));resultRowImages.Images.Add(new Bitmap(1,44));list.SmallImageList=resultRowImages;
   list.FontChanged+=(sender,args)=>{resultRowImages.ImageSize=new Size(1,Math.Min(256,Math.Max(44,list.Font.Height*2+12)));};
   list.DrawColumnHeader+=(sender,args)=>args.DrawDefault=true;list.DrawItem+=(sender,args)=>{};list.DrawSubItem+=DrawResultCell;
   list.Columns.Add("名称",180);list.Columns.Add("类型",54);list.Columns.Add("编号",90);
   list.RetrieveVirtualItem+=(sender,args)=>{if(args.ItemIndex<0||args.ItemIndex>=results.Length){args.Item=new ListViewItem("");return;}var record=results[args.ItemIndex];args.Item=new ListViewItem(new[]{record.Name,record.Kind=="Symbol"?"符号":"封装",record.Code});args.Item.ToolTipText=record.Name+"\r\n"+record.ShortDescription+"\r\n"+record.Code+" · "+record.KindLabel+" · "+(record.PackageName??record.RelatedName);};
   list.Resize+=(sender,args)=>ResizeColumns();list.SelectedIndexChanged+=(sender,args)=>{if(selectionChanging)return;ShowSelection();UpdateActions();var selected=SelectedRecord;if(!busy&&selected!=null&&selected.Package!=null)StartDescriptionFetch(selected,new LibraryStore(selected.LibraryRoot));};
   list.MouseDoubleClick+=(sender,args)=>{if(!busy&&!nativeBusy&&list.GetItemAt(args.X,args.Y)!=null)RequestPlace();};
   list.MouseUp+=(sender,args)=>{if(args.Button!=MouseButtons.Right||busy||nativeBusy)return;var hit=list.GetItemAt(args.X,args.Y);if(hit==null)return;if(!hit.Selected){list.SelectedIndices.Clear();hit.Selected=true;}ShowComponentMenu(args.Location);};
   list.KeyDown+=(sender,args)=>{if(args.KeyCode==Keys.Apps||(args.Shift&&args.KeyCode==Keys.F10)){ShowComponentMenu(new Point(12,28));args.SuppressKeyPress=true;}};
   list.KeyDown+=(sender,args)=>{if(args.Control&&args.KeyCode==Keys.A){SelectAllResults();args.SuppressKeyPress=true;}else if(args.KeyCode==Keys.Delete){DeleteSelectedCache();args.SuppressKeyPress=true;}else if(args.KeyCode==Keys.Enter){RequestPlace();args.SuppressKeyPress=true;}};
   empty.Text="搜索型号、厂家或 C 编号\r\n例如 TP4057 / C7473159";
   resultsFrame.Controls.Add(list);resultsFrame.Controls.Add(empty);
   modelResults.Visible=false;resultsFrame.Controls.Add(modelResults);
   modelResults.RunningChanged+=running=>{if(IsDisposed||Disposing)return;if(source.SelectedIndex==0)SetStatus(running?"正在搜索…":"选择元件后点击放置，或双击结果。",false);UpdateActions();};
   modelResults.FetchRequested+=candidate=>{SetCandidateMode(false);AcquireComponent(false,false,null,true,candidate.Code);};

   detail.BackColor=Surface;detail.Visible=false;
   detail.Paint+=(sender,args)=>{using(var pen=new Pen(PanelTheme.Border))args.Graphics.DrawLine(pen,0,0,detail.Width,0);};
   name.Font=PanelFont(9.5F,FontStyle.Bold);
   foreach(var label in new[]{name,description,meta,related,location,notice}){label.AutoEllipsis=true;label.TextAlign=ContentAlignment.MiddleLeft;label.Margin=Padding.Empty;detail.Controls.Add(label);}
   description.ForeColor=Ink;meta.ForeColor=Muted;location.ForeColor=Muted;notice.ForeColor=PanelTheme.Warning;
   ConfigureButton(place,"放置符号",PanelButtonKind.Primary);place.Click+=(sender,args)=>{if(SelectedRecords.Length>1)DeleteSelectedCache();else RequestPlace();};
   ConfigureButton(open,"查看库",PanelButtonKind.Command);open.Click+=(sender,args)=>{var record=SelectedRecord;if(record!=null&&OpenRequested!=null)OpenRequested(record);};
   ConfigureButton(model,"获取 3D",PanelButtonKind.Command);model.Click+=(sender,args)=>FetchModelForSelected();tips.SetToolTip(model,"按需下载并嵌入 STEP；完成后查看封装的 3D 显示。已有工程请更新库。");actions.Controls.Add(model);actions.Controls.Add(open);actions.Controls.Add(place);actions.Visible=false;

   status.ForeColor=Muted;status.AutoEllipsis=true;status.TextAlign=ContentAlignment.MiddleLeft;
   pathHint.ForeColor=Muted;pathHint.AutoEllipsis=true;pathHint.TextAlign=ContentAlignment.MiddleLeft;pathHint.Font=PanelFont(8F,FontStyle.Regular);
   progress.Style=ProgressBarStyle.Marquee;progress.Visible=false;
   ConfigureButton(cancel,"取消",PanelButtonKind.Tool);cancel.Visible=false;
   cancel.Click+=(sender,args)=>{if(source.SelectedIndex==2&&localCatalog!=null&&filterCancellation!=null){CancelFiltering();UpdateActions();SetStatus("已取消搜索，已有本地库保留。",false);return;}if(candidateMode)modelResults.CancelSearch();else if(cancellation!=null)cancellation.Cancel();else if(localScanCancellation!=null)localScanCancellation.Cancel();cancel.Enabled=false;SetStatus("正在取消…",false);};
   rootHost.Controls.AddRange(new Control[]{source,more,inputFrame,search,resultCount,resultsFrame,detail,actions,status,pathHint,progress,cancel});
   pageHost.PageClosed+=RestoreRootControls;
   updateBanner.AutoEllipsis=true;updateBanner.LinkColor=PanelTheme.Focus;updateBanner.ActiveLinkColor=PanelTheme.Ink;updateBanner.VisitedLinkColor=PanelTheme.Focus;updateBanner.Visible=false;updateBanner.LinkClicked+=(sender,args)=>ShowAvailableUpdate();rootHost.Controls.Add(updateBanner);
   updateTimer.Interval=4000;updateTimer.Tick+=(sender,args)=>{updateTimer.Interval=60*60*1000;CheckWebsiteUpdate(false);};
   foreach(var control in new Control[]{query,source,list,search,more,cancel,place,open,name,meta,related,location,notice,status,pathHint})control.FontChanged+=(sender,args)=>{if(uiReady&&!layingOut)PerformLayout();};
   tips.SetToolTip(query,"输入型号、厂家或 C 编号，按 Enter 搜索。");tips.SetToolTip(place,"放置所选元件；也可双击结果或按 Enter。");tips.SetToolTip(open,"在 AD 中打开元件库。");
   debounce.Interval=100;debounce.Tick+=(sender,args)=>{debounce.Stop();FilterResults();};
   uiReady=true;StyleFilters();SetStatus("正在读取元件库…",false);SetResults(new LibraryRecord[0]);UpdateStorageHint();UpdateInputHint();UpdateActions();PerformLayout();
  }
  static void ConfigureButton(PanelButton button,string text,PanelButtonKind kind){button.Text=text;button.AccessibleName=text;button.Kind=kind;button.Margin=Padding.Empty;}
  void ChangeSource(){
   debounce.Stop();CancelFiltering();sourceQueries[activeSource]=query.Text;
   if(activeSource==0){onlineResultView=candidateMode;if(modelResults.Running)modelResults.CancelSearch();}
   activeSource=source.SelectedIndex;SetCandidateMode(false);
   restoringSource=true;try{query.Text=sourceQueries[activeSource];}finally{restoringSource=false;}
   UpdateInputHint();UpdateStorageHint();
   if(activeSource==0&&onlineResultView&&modelResults.HasResults){SetCandidateMode(true);SetStatus("选择元件后点击放置，或双击结果。",false);}
   else{var records=activeSource==0?online:activeSource==1?downloaded:local;SetResults(records.Where(record=>filter==0||record.Kind==(filter==1?"Symbol":"Footprint")).Take(500).ToArray());FilterResults();}
   UpdateActions();
   if(activeSource==2&&loaded&&!busy&&!localScanning&&LocalRoots.Length>0&&(DateTime.UtcNow-lastLocalScan).TotalMinutes>5)StartLocalScan(LocalRoots);
  }
  int CaptionWidth(Control control,int padding){return TextRenderer.MeasureText(control.Text,control.Font).Width+padding;}
  void UpdateInputHint(){placeholder.Text=source.SelectedIndex==0?"搜索型号、厂家或 C 编号":source.SelectedIndex==2&&LocalRoots.Length==0&&local.Length==0?"选择本地库目录":"搜索名称、型号或 C 编号";placeholder.Visible=query.TextLength==0&&!query.Focused;placeholder.BringToFront();if(source.SelectedIndex==0)search.Text="搜索";}
  void StyleFilters(){for(int i=0;i<filters.Length;i++)filters[i].Active=i==filter;}
  protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);if(uiReady&&!layingOut)PerformLayout();}
  protected override void OnLayout(LayoutEventArgs e){
   base.OnLayout(e);if(rootHost!=null)rootHost.Bounds=ClientRectangle;if(pageHost!=null){pageHost.Bounds=ClientRectangle;if(pageHost.HasPage){pageHost.BringToFront();return;}}if(!uiReady||layingOut||IsDisposed)return;layingOut=true;
   try{
    int unit=Math.Max(Font.Height,query.Font.Height),gap=Math.Max(6,unit/3),pad=Math.Max(8,unit/2);
    int width=Math.Max(0,ClientSize.Width-pad*2),x=pad,y=pad;
    int controlHeight=Math.Max(query.PreferredHeight+gap*2,new[]{search.Font.Height,more.Font.Height,place.Font.Height,open.Font.Height}.Max()+gap*2);
    int moreWidth=Math.Max(52,CaptionWidth(more,18));
    int navHeight=Math.Max(source.PreferredTabHeight,controlHeight);
    source.Bounds=new Rectangle(x,y,Math.Max(0,Math.Min(source.PreferredTabWidth,width-moreWidth-gap)),navHeight);
    more.Bounds=new Rectangle(x+Math.Max(0,width-moreWidth),y,Math.Min(width,moreWidth),navHeight);y+=navHeight+gap;
    if(updateBanner.Visible){updateBanner.Bounds=new Rectangle(x,y,width,Math.Max(24,Font.Height+8));y+=updateBanner.Height+gap;}
    int searchWidth=Math.Max(64,CaptionWidth(search,26));
    inputFrame.Bounds=new Rectangle(x,y,Math.Max(0,width-searchWidth-gap),controlHeight);search.Bounds=new Rectangle(x+Math.Max(0,width-searchWidth),y,Math.Min(width,searchWidth),controlHeight);
    int inputLeft=Math.Max(28,query.Font.Height+14);
    query.Bounds=new Rectangle(inputLeft,Math.Max(0,(controlHeight-query.PreferredHeight)/2),Math.Max(0,inputFrame.Width-inputLeft-gap),query.PreferredHeight);
    placeholder.Bounds=new Rectangle(query.Left,1,query.Width,Math.Max(0,controlHeight-2));query.ForeColor=Ink;query.BackColor=PanelTheme.Input;
    y+=controlHeight+gap;
    int filterHeight=filters.Max(button=>button.Font.Height)+gap*2,filterX=x;
    int ideal=filters.Sum(button=>CaptionWidth(button,20)),countWidth=TextRenderer.MeasureText(resultCount.Text,resultCount.Font).Width+8;
    int allowed=Math.Max(0,width-Math.Min(countWidth,Math.Max(48,width/4))-gap);
    foreach(var button in filters){int w=ideal>allowed?allowed/3:CaptionWidth(button,20);button.Bounds=new Rectangle(filterX,y,Math.Max(0,w),filterHeight);filterX+=w;}
    resultCount.Bounds=new Rectangle(filterX+gap,y,Math.Max(0,x+width-filterX-gap),filterHeight);y+=filterHeight+gap;

    bool selected=SelectedRecords.Length>0;detail.Visible=selected;actions.Visible=selected;
    int footerHeight=status.Font.Height+pathHint.Font.Height+gap*2+4;
    int actionHeight=selected?controlHeight+gap:0;
    int noticeHeight=selectionWarning?Math.Min(notice.Font.Height*4+8,TextRenderer.MeasureText(notice.Text,notice.Font,new Size(Math.Max(40,width-4),int.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix).Height+4):0;
    int detailWanted=selected?name.Font.Height+description.Font.Height+meta.Font.Height+related.Font.Height+location.Font.Height+gap*6+(selectionWarning?noticeHeight+gap:0):0;
    int remaining=Math.Max(0,ClientSize.Height-pad-y-footerHeight-actionHeight);
    int detailHeight=selected?Math.Min(detailWanted,Math.Max(0,remaining-(unit*4+gap*2))):0;
    int resultHeight=Math.Max(0,remaining-detailHeight-gap);
    resultsFrame.Bounds=new Rectangle(x,y,width,resultHeight);list.Bounds=new Rectangle(1,1,Math.Max(0,width-2),Math.Max(0,resultHeight-2));modelResults.Bounds=list.Bounds;
    int headerHeight=list.Font.Height+10;
    empty.Bounds=new Rectangle(1,headerHeight,Math.Max(0,width-2),Math.Max(0,resultHeight-headerHeight-1));empty.Visible=!candidateMode&&results.Length==0;if(candidateMode)modelResults.BringToFront();else empty.BringToFront();
    y+=resultHeight+gap;
    if(selected){
     detail.Bounds=new Rectangle(x,y,width,detailHeight);int rowY=gap;
     foreach(var label in new[]{name,notice,description,meta,related,location}){
      int h=label==notice?noticeHeight+gap:label.Font.Height+gap;bool show=(label!=notice||selectionWarning)&&rowY+h<=detailHeight;
      label.Visible=show;if(show){label.Bounds=new Rectangle(2,rowY,Math.Max(0,width-4),h);rowY+=h;}
     }
     y+=detailHeight;actions.Bounds=new Rectangle(x,y,width,controlHeight);
     int placeWidth=Math.Max(112,CaptionWidth(place,30)),openWidth=open.Visible?Math.Max(76,CaptionWidth(open,22)):0;
     int modelWidth=model.Visible?Math.Max(76,CaptionWidth(model,18)):0;placeWidth=Math.Min(placeWidth,Math.Max(0,width-openWidth-gap-modelWidth-(model.Visible?gap:0)));
     place.Bounds=new Rectangle(Math.Max(0,width-placeWidth),0,placeWidth,controlHeight);
     open.Bounds=new Rectangle(Math.Max(0,width-placeWidth-gap-openWidth),0,Math.Min(openWidth,width),controlHeight);model.Bounds=new Rectangle(Math.Max(0,open.Left-gap-modelWidth),0,modelWidth,controlHeight);y+=actionHeight;
    }
    progress.Bounds=new Rectangle(x,y,width,3);y+=5;
    int cancelWidth=cancel.Visible?Math.Max(48,CaptionWidth(cancel,16)):0;
    status.Bounds=new Rectangle(x,y,Math.Max(0,width-cancelWidth),status.Font.Height+gap);
    cancel.Bounds=new Rectangle(x+Math.Max(0,width-cancelWidth),y,cancelWidth,status.Height);y+=status.Height;
    pathHint.Bounds=new Rectangle(x,y,width,pathHint.Font.Height+gap);
    ResizeColumns();inputFrame.Invalidate();
   }finally{layingOut=false;}
  }
  void DrawResultCell(object sender,DrawListViewSubItemEventArgs args){
   if(args.ItemIndex<0||args.ItemIndex>=results.Length)return;var record=results[args.ItemIndex];bool selected=list.SelectedIndices.Contains(args.ItemIndex);Color background=selected?PanelTheme.Selection:PanelTheme.Input;using(var brush=new SolidBrush(background))args.Graphics.FillRectangle(brush,args.Bounds);
   int padding=8,top=args.Bounds.Top+4;var cell=new Rectangle(args.Bounds.Left+padding,top,Math.Max(0,args.Bounds.Width-padding*2),list.Font.Height+3);TextFormatFlags flags=TextFormatFlags.Left|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine|TextFormatFlags.NoPadding;
   if(args.ColumnIndex==0){TextRenderer.DrawText(args.Graphics,record.Name,list.Font,cell,Ink,flags);cell.Y+=list.Font.Height+3;cell.Height=resultSecondaryFont.Height+3;TextRenderer.DrawText(args.Graphics,record.ShortDescription,resultSecondaryFont,cell,selected?Ink:Muted,flags);}
   else if(args.ColumnIndex==2){TextRenderer.DrawText(args.Graphics,record.Code,list.Font,cell,selected?Color.White:Color.FromArgb(104,188,181),flags);cell.Y+=list.Font.Height+3;cell.Height=resultSecondaryFont.Height+3;TextRenderer.DrawText(args.Graphics,string.IsNullOrWhiteSpace(record.PackageName)?record.RelatedName:record.PackageName,resultSecondaryFont,cell,selected?Ink:Muted,flags);}
   else TextRenderer.DrawText(args.Graphics,record.Kind=="Symbol"?"符号":"封装",resultSecondaryFont,new Rectangle(cell.X,args.Bounds.Top,cell.Width,args.Bounds.Height),selected?Ink:Muted,flags|TextFormatFlags.VerticalCenter);
   if(args.ColumnIndex==0&&selected){using(var accent=new SolidBrush(PanelTheme.Focus))args.Graphics.FillRectangle(accent,args.Bounds.Left,args.Bounds.Top,3,args.Bounds.Height);}
  }
  void ResizeColumns(){if(list.Columns.Count<3)return;list.Columns[2].Text=source.SelectedIndex==2?"来源库":"编号";int width=list.AvailableColumnWidth;int kind=Math.Min(TextRenderer.MeasureText("封装",list.Font).Width+18,width/4);int code=Math.Min(TextRenderer.MeasureText("C51933293",list.Font).Width+26,width/3);list.Columns[1].Width=kind;list.Columns[2].Width=code;list.Columns[0].Width=Math.Max(0,width-kind-code);}
  public LibraryRecord[] SelectedRecords {get{if(!list.IsHandleCreated)return new LibraryRecord[0];return list.SelectedIndices.Cast<int>().Where(index=>index>=0&&index<results.Length).Select(index=>results[index]).ToArray();}}
  public LibraryRecord SelectedRecord {get{if(!list.IsHandleCreated||list.SelectedIndices.Count!=1)return null;int index=list.SelectedIndices[0];return index>=0&&index<results.Length?results[index]:null;}}
  public string LibraryRoot {get{return store.Root;}}
  protected override void OnHandleCreated(EventArgs args){base.OnHandleCreated(args);if(started)return;started=true;ReloadLibraries();updateTimer.Start();Post(ShowFirstUseNotice);}
  protected override void OnVisibleChanged(EventArgs args){base.OnVisibleChanged(args);if(uiReady&&Visible&&IsHandleCreated)Post(ShowFirstUseNotice);}
  void ShowFirstUseNotice(){if(!Visible||usageNoticeShown||IsDisposed||Disposing)return;usageNoticeShown=true;if(settings.UsageNoticeVersion>=1)return;ShowUsageNotice(true);}
  void HideRootControls(){rootHost.Visible=false;}
  void RestoreRootControls(){if(pageHost.HasPage){HideRootControls();return;}rootHost.Visible=true;rootHost.BringToFront();ShowSelection();UpdateActions();PerformLayout();}
  void ShowPanelPage(string title,Control page,Action closed=null,bool allowBack=true){pageHost.ShowPage(title,page,closed,allowBack);HideRootControls();}
  void ShowUsageNotice(bool firstUse){
   var page=new NoticePage("使用前请核对元件\r\n\r\n请根据器件数据手册核对引脚与焊盘编号、尺寸、极性，并检查原理图与 PCB 的连接。\r\n\r\n符号、封装和 3D 模型来自嘉立创 EDA / EasyEDA。在线数据转换可能存在差异，请确认后用于设计。\r\n\r\n确认后不再自动提示，可在设置的帮助页面重新查看。");
   page.Accepted+=()=>Post(delegate{if(firstUse){settings.UsageNoticeVersion=1;try{settings.Save();}catch(Exception error){settings.UsageNoticeVersion=0;UserLog.Write("Save usage acknowledgement",error);SetStatus("使用须知确认状态未保存，下次使用会再次提示。",true);}}pageHost.ClosePage();});
   ShowPanelPage("元件使用须知",page,firstUse?(Action)null:ShowMore,!firstUse);
  }
  public void ShowUserMessage(string heading,string message){
   var page=new NoticePage(message,"返回");
   page.Accepted+=()=>Post(()=>pageHost.ClosePage());
   ShowPanelPage(heading,page);
  }
  void SetCandidateMode(bool value){candidateMode=value;modelResults.Visible=value;list.Visible=!value;empty.Visible=!value&&results.Length==0;if(value){SetResults(new LibraryRecord[0]);modelResults.BringToFront();resultCount.Text="搜索结果";}if(uiReady)PerformLayout();}
  protected override void Dispose(bool disposing){if(disposing){CancelFiltering();if(localScanCancellation!=null)localScanCancellation.Cancel();if(cancellation!=null)cancellation.Cancel();if(descriptionCancellation!=null)descriptionCancellation.Cancel();if(updateCancellation!=null)updateCancellation.Cancel();updateTimer.Dispose();if(moreMenu!=null)moreMenu.Dispose();debounce.Dispose();tips.Dispose();resultRowImages.Dispose();}base.Dispose(disposing);if(disposing)foreach(var font in ownedFonts)font.Dispose();}
  void Post(Action action){if(IsDisposed||Disposing||!IsHandleCreated)return;try{BeginInvoke(new Action(delegate{if(!IsDisposed&&!Disposing)action();}));}catch(InvalidOperationException){}}
  void ReloadLibraries(){
   var current=store;string indexPath=settings.LocalIndex;busy=true;UpdateActions();
   ThreadPool.QueueUserWorkItem(delegate{
    var rows=new LibraryRecord[0];var indexed=new LocalIndexBuild{Records=new LibraryRecord[0]};string warning="";
    try{rows=current.LoadDownloaded();}catch(Exception error){UserLog.Write("Load downloaded",error);warning="无法读取元件目录，请检查目录权限。";}
    try{indexed=AutomaticLocalIndex.Open(indexPath,Path.Combine(distribution,"online","bin","easyeda2altium.exe"));}catch(Exception error){UserLog.Write("Load local index",error);warning="本地库信息无法读取，请在设置中重新扫描。";}
    Post(delegate{downloaded=rows;local=indexed.Records;localCatalog=indexed.Catalog;loaded=true;busy=false;FilterResults();if(warning.Length>0)SetStatus(warning,true);UpdateActions();});
   });
  }
  void UpdateStorageHint(){pathHint.Text=source.SelectedIndex==2?(LocalRoots.Length==0?"本地库：未设置目录":"本地库  "+string.Join("; ",LocalRoots)):"保存位置  "+store.Root;tips.SetToolTip(pathHint,(source.SelectedIndex==2?string.Join("\r\n",LocalRoots):store.Root)+"\r\n在线元件放置时，配套库会同时保存到工程 Libraries\\LibrarySearch 下。");}
  public void SetStatus(string text,bool error){status.Text=text;status.ForeColor=error?PanelTheme.Error:Muted;tips.SetToolTip(status,text);}
  public void SetNativeBusy(bool value,string message){nativeBusy=value;SetStatus(message,false);UpdateActions();}
  void UpdateActions(){
   bool searching=source.SelectedIndex==0&&modelResults.Running;bool idle=loaded&&!busy&&!nativeBusy&&!searching;query.Enabled=idle&&!(source.SelectedIndex==2&&LocalRoots.Length==0&&local.Length==0);source.Enabled=loaded&&!busy&&!nativeBusy;search.Enabled=idle;foreach(var button in filters)button.Enabled=idle;list.Enabled=true;more.Enabled=idle;modelResults.ActionText=filter==2?"放置封装":"放置符号";
   var selected=SelectedRecords;bool batch=selected.Length>1&&selected.All(row=>row.Package!=null);var record=SelectedRecord;place.Enabled=idle&&(record!=null||batch);open.Visible=record!=null;open.Enabled=idle&&record!=null;model.Visible=record!=null&&record.Package!=null;model.Enabled=idle&&model.Visible&&record.Package.Model3dStatus!="unavailable";model.Text=record!=null&&record.Package!=null&&record.Package.Model3dStatus=="embedded"?"查看封装":"获取 3D";
   bool catalogSearching=source.SelectedIndex==2&&localCatalog!=null&&filterCancellation!=null;
   search.Text=source.SelectedIndex==0?"获取":source.SelectedIndex==2&&LocalRoots.Length==0&&local.Length==0?"选择目录":"搜索";progress.Visible=busy||nativeBusy||searching||catalogSearching||(localScanning&&source.SelectedIndex==2);cancel.Visible=(busy&&cancellation!=null)||searching||catalogSearching||(localScanning&&source.SelectedIndex==2);cancel.Enabled=cancel.Visible;
   place.Text=nativeBusy?"画布放置中…":batch?"删除所选元件（"+selected.Select(row=>row.Code).Distinct().Count()+"）":record==null?"放置":record.Kind=="Symbol"?"放置符号":"放置封装";search.AccessibleName=search.Text;place.AccessibleName=place.Text;UpdateInputHint();if(pageHost.Visible)HideRootControls();if(uiReady)PerformLayout();
  }
  void ShowSelection(){
   var record=SelectedRecord;selectionWarning=false;var selection=SelectedRecords;if(selection.Length>1){name.Text="已选 "+selection.Length+" 条记录 · "+selection.Select(row=>row.Code).Distinct().Count()+" 个编号";description.Text="Ctrl / Shift 多选；Ctrl+A 全选当前结果。";meta.Text=selection.All(row=>row.Package!=null)?"可批量删除所选编号的符号和封装缓存。":"选择一个元件后可查看或放置。";related.Text="";location.Text="";notice.Text="";if(uiReady)PerformLayout();return;}
   if(record==null){name.Text="";description.Text="";meta.Text="";related.Text="";location.Text="";notice.Text="";if(uiReady)PerformLayout();return;}
   name.Text=record.Name;description.Text=record.ShortDescription;tips.SetToolTip(description,record.ShortDescription+(record.Package!=null&&!string.IsNullOrWhiteSpace(record.Package.DescriptionSource)?"\r\n来源："+record.Package.DescriptionSource:""));meta.Text=record.Code+"   ·   "+record.KindLabel+"   ·   "+(record.Package==null?"本地库":"已保存"+(record.Package.Model3dStatus=="embedded"?" · 含 3D":"")+(record.Package.Appearance==null?"":" · "+record.Package.Appearance.Label));related.Text=(record.Kind=="Symbol"?"封装  ":"符号  ")+record.RelatedName;location.Text=record.SourcePath??record.FilePath;
   tips.SetToolTip(name,record.Name);tips.SetToolTip(meta,meta.Text);tips.SetToolTip(location,(record.SourcePath??record.FilePath)+(record.SourcePath!=null&&record.SourcePath!=record.FilePath?"\r\n缓存库："+record.FilePath:""));tips.SetToolTip(related,related.Text);
   string warning="";
   if(record.Package!=null&&record.Package.AcquisitionVersion!=ReleaseInfo.Acquisition)warning+=" 请重新获取此元件，以使用最新的封装转换规则。";
   if(record.Package!=null&&!string.IsNullOrWhiteSpace(record.Package.Warning))warning+=" "+record.Package.Warning;
   if(record.Package!=null&&record.Package.PinGeometryVersion!="svg-line-1")warning+=" 旧版库可能存在引脚过长，请右键元件重新下载。";notice.Text=warning.Trim();selectionWarning=notice.Text.Length>0;tips.SetToolTip(notice,notice.Text);if(uiReady)PerformLayout();
  }
  void SetResults(LibraryRecord[] rows){selectionChanging=true;list.BeginUpdate();try{if(list.IsHandleCreated)list.SelectedIndices.Clear();list.VirtualListSize=0;results=rows;list.VirtualListSize=rows.Length;}finally{list.EndUpdate();selectionChanging=false;}if(list.IsHandleCreated){list.ResetResultOrigin();list.Invalidate();}list.Visible=!candidateMode;empty.Visible=!candidateMode&&rows.Length==0;resultCount.Text=rows.Length+" 项";ShowSelection();UpdateActions();}
  void FilterResults(){
   if(candidateMode&&source.SelectedIndex==0){modelResults.ActionText=filter==2?"放置封装":"放置符号";modelResults.BringToFront();return;}
   if(!loaded||busy||nativeBusy)return;debounce.Stop();CancelFiltering();int version=searchVersion;string text=query.Text.Trim();int selectedSource=source.SelectedIndex;int kind=filter;var rows=selectedSource==0?online:selectedSource==1?downloaded:local;
   var operation=new CancellationTokenSource();var token=operation.Token;filterCancellation=operation;
   var catalog=selectedSource==2?localCatalog:null;UpdateActions();
   ThreadPool.QueueUserWorkItem(delegate{
    try{var matches=catalog==null?LibraryRecordSearch.Find(rows,text,kind,selectedSource==0,token):text.Length==0&&kind==0?new LibrarySearchMatch{Records=rows.Take(500).ToArray(),Total=catalog.Count}:catalog.Search(text,kind,token);var visible=matches.Records;
    Post(delegate{if(version!=searchVersion||busy||nativeBusy)return;SetResults(visible);resultCount.Text=matches.Total>500?"前 500 / "+matches.Total:matches.Total+" 项";
     if(visible.Length==0){empty.Text=selectedSource==0?"搜索型号、厂家或 C 编号\r\n例如 TP4057 / C7473159":selectedSource==1?(downloaded.Length==0?"还没有下载的元件\r\n在“在线库”中下载后，可在此离线查找":"没有匹配的元件\r\n试试型号、C 编号，或切换分类"):(local.Length==0?(LocalRoots.Length==0?"选择本地库文件夹\r\n自动扫描符号、封装和集成库，包含子目录":"未找到可用元件\r\n支持 SchLib、PcbLib、IntLib，可在“设置”重新选择目录"):"没有匹配结果，请调整关键词或分类");}
     if(selectedSource==2&&localScanWarning)SetStatus("找到 "+matches.Total+" 个元件；部分库未读取，可从“设置”导出反馈查看详情。",true);else SetStatus(selectedSource==0?(online.Length==0?"就绪":"已保存符号和封装；放置前请核对外形与引脚。"):"找到 "+matches.Total+" 条记录；选择后可查看或放置。",false);
    });
    }catch(OperationCanceledException){}catch(Exception error){UserLog.Write("Filter library records",error);Post(delegate{if(version==searchVersion)SetStatus(error.Message,true);});}finally{Post(delegate{if(filterCancellation==operation)filterCancellation=null;operation.Dispose();UpdateActions();});}
   });
  }
  void CancelFiltering(){Interlocked.Increment(ref searchVersion);if(filterCancellation!=null){filterCancellation.Cancel();filterCancellation=null;if(uiReady&&!IsDisposed&&!Disposing)UpdateActions();}}
  void SearchRequested(bool refresh,bool include3d=false,SymbolAppearance selectedAppearance=null){
   if(!loaded||busy||nativeBusy||(source.SelectedIndex==0&&modelResults.Running))return;if(source.SelectedIndex!=0){if(source.SelectedIndex==2&&LocalRoots.Length==0&&local.Length==0)ChooseLocalLibraryFolder();else FilterResults();return;}
   if(refresh||include3d||selectedAppearance!=null){AcquireComponent(refresh,include3d,selectedAppearance);return;}
   string exact=(query.Text??"").Trim().ToUpperInvariant();
   if(Regex.IsMatch(exact,@"\AC[0-9]{1,12}\z")&&File.Exists(Path.Combine(store.Root,"Index",exact+".json"))){
    // Fetch validates version, appearance and both hashes on the worker thread.
    // A valid pair must remain usable even when product search is offline.
    SetCandidateMode(false);AcquireComponent(false);return;
   }
   try{ModelSearchClient.ValidateQuery(query.Text);}catch(Exception error){SetStatus(error.Message,true);query.Focus();return;}
   SetCandidateMode(true);modelResults.Search(query.Text.Trim());
  }
  void AcquireComponent(bool refresh,bool include3d=false,SymbolAppearance selectedAppearance=null,bool placeAfter=false,string selectedCode=null){
   if(!loaded||busy||nativeBusy)return;
   string code;string placementKind=filter==2?"Footprint":"Symbol";string expectedContext=null;
   if(placeAfter&&PlacementContext!=null){try{expectedContext=PlacementContext(placementKind);}catch(Exception error){ShowUserMessage("放置元件",error.Message);return;}}
   try{code=LibraryStore.Normalize(selectedCode??query.Text);}catch(Exception error){SetStatus(error.Message,true);query.Focus();return;}
   if(descriptionCancellation!=null)descriptionCancellation.Cancel();CancelFiltering();busy=true;online=new LibraryRecord[0];SetResults(new LibraryRecord[0]);empty.Text="正在获取 "+code+"\r\n正在生成符号和封装";cancellation=new CancellationTokenSource();var operation=cancellation;var currentStore=store;var currentStyle=SymbolAppearance.Effective(selectedAppearance??settings.Appearance);var previousDownloads=downloaded;UpdateActions();SetStatus("正在检查本机元件…",false);
   ThreadPool.QueueUserWorkItem(delegate{
    try{
     var fetched=FetchOverride==null?currentStore.Fetch(code,Path.Combine(distribution,"online","bin","easyeda2altium.exe"),refresh,operation.Token,message=>Post(delegate{if(cancellation==operation)SetStatus(message,false);}),currentStyle,include3d):FetchOverride(code,operation.Token);var saved=previousDownloads.Where(record=>record.Code!=code).Concat(fetched.Records).OrderBy(record=>record.Code).ThenBy(record=>record.Kind).ToArray();
     Post(delegate{bool shouldPlace=placeAfter&&!operation.IsCancellationRequested;online=fetched.Records;downloaded=saved;busy=false;cancellation=null;operation.Dispose();SetResults(online.Where(record=>filter==0||record.Kind==(filter==1?"Symbol":"Footprint")).ToArray());int index=Array.FindIndex(results,row=>row.Kind==placementKind);if(index<0&&results.Length>0)index=0;if(index>=0)list.SelectedIndices.Add(index);SetStatus(include3d?(fetched.Records[0].Package.Model3dStatus=="embedded"?"3D 模型已保存，可打开封装查看。":"该元件暂无可用 3D 模型，符号与封装仍可使用。"):"元件已就绪。",false);UpdateActions();StartDescriptionFetch(fetched.Records[0],currentStore,refresh);if(shouldPlace){try{if(PlacementContext!=null&&!string.Equals(expectedContext,PlacementContext(placementKind),StringComparison.Ordinal)){ShowUserMessage("请重新选择放置位置","当前文档或工程已改变。元件已下载，请激活目标文档后重新放置。");return;}RequestPlace();}catch(Exception error){ShowUserMessage("放置元件",error.Message);}}});
    }catch(OperationCanceledException){Post(delegate{busy=false;cancellation=null;operation.Dispose();empty.Text="已取消获取\r\n输入新的编号即可重试";SetStatus("已取消。已有库文件保持可用。",false);UpdateActions();});}
    catch(Exception error){UserLog.Write("Fetch "+code,error);Post(delegate{busy=false;cancellation=null;operation.Dispose();empty.Text="暂时无法获取元件\r\n请检查 C 编号和网络，或从“设置”导出反馈";SetStatus(error.Message,true);UpdateActions();});}
   });
  }
  void StartDescriptionFetch(LibraryRecord record,LibraryStore target,bool refresh=false){
   refresh=refresh||(record.Package!=null&&record.Package.DescriptionRefreshRequired);
   if(record.Package==null||(!refresh&&record.Package.DescriptionVersion=="product-cn-1"))return;
   string key=record.LibraryRoot+"|"+record.Code+"|"+record.Package.RelativeFolder+(refresh?"|refresh":"");if(descriptionCancellation!=null&&!descriptionCancellation.IsCancellationRequested&&descriptionKey==key)return;
   if(descriptionCancellation!=null)descriptionCancellation.Cancel();
   var operation=new CancellationTokenSource();descriptionCancellation=operation;descriptionKey=key;
   ThreadPool.QueueUserWorkItem(delegate{
    try{var updated=target.EnrichDescription(record,Path.Combine(distribution,"online","bin","easyeda2altium.exe"),operation.Token,refresh);Post(delegate{
      if(descriptionCancellation!=operation||updated==null)return;
      foreach(var row in online.Concat(downloaded).Concat(results).Distinct())if(row.Code==updated.Code&&row.Package!=null&&row.Package.RelativeFolder==updated.RelativeFolder){row.Description=updated.Description;row.Package=updated;}
      list.Invalidate();ShowSelection();
     });}
    catch(OperationCanceledException){}catch(Exception error){UserLog.Write("Description enrichment",error);}
    finally{Post(delegate{if(descriptionCancellation==operation){descriptionCancellation=null;descriptionKey="";}operation.Dispose();});}
   });
  }
  void RequestPlace(){if(busy||nativeBusy)return;var record=SelectedRecord;if(record!=null&&PlaceRequested!=null)PlaceRequested(record);}
  string[] LocalRoots {get{return settings.LocalLibraryRoots??new string[0];}}
  void ChooseLocalLibraryFolder(){
   using(var dialog=new FolderBrowserDialog{Description="选择本地元件库总目录。自动扫描子目录中的 SchLib、PcbLib 和 IntLib，不需要制作索引。",SelectedPath=LocalRoots.FirstOrDefault()??store.Root,ShowNewFolderButton=false}){
    if(dialog.ShowDialog(this)!=DialogResult.OK)return;
    string root=new LibraryStore(dialog.SelectedPath).Root;
    StartLocalScan(new[]{root},true);
    source.SelectedIndex=2;
   }
  }
  void StartLocalScan(string[] roots,bool force=false){
   if(!loaded||busy||nativeBusy)return;
   if(localScanning){SetStatus("本地库正在后台更新，已有元件可以继续搜索。",false);return;}
   if(roots==null||roots.Length==0){ChooseLocalLibraryFolder();return;}
   localScanning=true;localScanCancellation=new CancellationTokenSource();var operation=localScanCancellation;string[] selectedRoots=roots.ToArray();string previousIndex=settings.LocalIndex;var timer=System.Diagnostics.Stopwatch.StartNew();
   UserLog.Write("Local scan start: roots="+string.Join("; ",selectedRoots)+" force="+force);
   if(local.Length==0)empty.Text="正在扫描本地元件库\r\n自动读取库内元件名，可随时取消";
   UpdateActions();SetStatus(local.Length>0?"正在后台更新本地库，已有元件可以继续搜索。":"正在查找本地库文件…",false);
   ThreadPool.QueueUserWorkItem(delegate{
    try{
     Action<string> report=message=>Post(delegate{if(localScanCancellation==operation&&source.SelectedIndex==2&&!busy&&local.Length==0)SetStatus(message,false);});
     var built=LocalScanOverride==null?AutomaticLocalIndex.Build(selectedRoots,Path.Combine(distribution,"online","bin","easyeda2altium.exe"),operation.Token,report,previousIndex,force):LocalScanOverride(selectedRoots,operation.Token,report,previousIndex,force);
     Post(delegate{
      if(localScanCancellation!=operation){operation.Dispose();return;}
      bool saved=false;
      try{
       operation.Token.ThrowIfCancellationRequested();
       var replacement=new UserSettings{UsageNoticeVersion=settings.UsageNoticeVersion,LibraryRoot=settings.LibraryRoot,LocalIndex=built.IndexPath,LocalLibraryRoots=selectedRoots,Appearance=settings.Appearance};
       replacement.Save();settings=replacement;local=built.Records;localCatalog=built.Catalog;lastLocalScan=DateTime.UtcNow;localScanWarning=(built.Data.Warnings??new string[0]).Length>0;saved=true;
       UserLog.Write("Local index published: files="+built.Data.ScannedFiles+" records="+(localCatalog==null?local.Length:localCatalog.Count)+" preview="+local.Length+" cache_hits="+built.Data.CacheHits+" elapsed_ms="+timer.ElapsedMilliseconds);
      }catch(OperationCanceledException){SetStatus("已取消扫描，原索引保留。",false);}
      catch(Exception error){UserLog.Write("Publish local index",error);SetStatus("索引未保存："+error.Message,true);}
      finally{localScanning=false;localScanCancellation=null;operation.Dispose();UpdateActions();UpdateStorageHint();}
      if(saved&&source.SelectedIndex==2&&!busy&&!nativeBusy)FilterResults();
     });
    }catch(OperationCanceledException){Post(delegate{if(localScanCancellation!=operation){operation.Dispose();return;}localScanning=false;localScanCancellation=null;operation.Dispose();if(local.Length==0)empty.Text="已取消扫描\r\n选择目录后可重新建立索引";if(source.SelectedIndex==2&&!busy)SetStatus("已取消后台更新，已有元件仍可搜索。",false);UpdateActions();});}
    catch(Exception error){UserLog.Write("Scan local libraries",error);Post(delegate{if(localScanCancellation!=operation){operation.Dispose();return;}localScanning=false;localScanCancellation=null;operation.Dispose();if(local.Length==0)empty.Text="本地库扫描未完成\r\n请检查目录权限，或从“设置”导出反馈";if(source.SelectedIndex==2&&!busy)SetStatus(error.Message,true);UpdateActions();});}
   });
  }

  void ShowMore(){
   if(IsDisposed||Disposing||busy||nativeBusy)return;
   var commands=new Dictionary<string,Action>{
    {"changeRoot",ChangeRoot},{"openRoot",()=>OpenFolder(store.Root)},{"reload",ReloadLibraries},
    {"chooseLocal",ChooseLocalLibraryFolder},{"scanLocal",()=>StartLocalScan(LocalRoots,true)},{"appearance",ChooseAppearance},
    {"checkUpdate",()=>CheckWebsiteUpdate(true)},{"website",()=>OpenWebsite("download.html")},{"help",OpenHelp},{"onlineHelp",()=>OpenWebsite("guide.html")},
    {"notice",()=>ShowUsageNotice(false)},{"feedback",ExportFeedback},{"logs",()=>OpenFolder(UserLog.Folder)},
    {"feedbackPage",()=>OpenWebsite("about.html#feedback")},{"about",ShowAbout}
   };
   var page=new SettingsPage(store.Root,string.Join("\r\n",LocalRoots),SymbolAppearance.Effective(settings.Appearance).Label,updateState.Automatic,updateState.IncludeBeta,commands);
   page.CloseRequested+=()=>Post(()=>pageHost.ClosePage());
   page.ActionRequested+=action=>Post(delegate{pageHost.ClosePage();QueueMenuAction(action);});
   ShowPanelPage("设置",page,()=>SaveSettingsPreferences(page));
  }
  void SaveSettingsPreferences(SettingsPage page){
   bool changed=updateState.Automatic!=page.Automatic||updateState.IncludeBeta!=page.IncludeBeta;
   updateState.Automatic=page.Automatic;updateState.IncludeBeta=page.IncludeBeta;
   if(availableUpdate!=null&&!UpdateClient.IsNewer(availableUpdate,ReleaseInfo.Version,updateState.IncludeBeta)){availableUpdate=null;updateBanner.Visible=false;PerformLayout();}
   if(changed)SaveUpdateState();
  }
  void ShowComponentMenu(Point point){
   if(busy||nativeBusy||SelectedRecords.Length==0)return;
   if(moreMenu==null||moreMenu.IsDisposed){
    moreMenu=new ContextMenuStrip{Renderer=new ToolStripProfessionalRenderer(new PanelMenuColors()){RoundedEdges=false},BackColor=Surface,ForeColor=Ink,Font=Font,ShowImageMargin=false};
    refreshCommand=AddMenuAction("重新获取当前元件",RefetchCurrentComponent);
    modelCommand=AddMenuAction("获取 / 查看 3D",FetchModelForSelected);
    locateCommand=AddMenuAction("定位库文件",()=>{var record=SelectedRecord;if(record!=null)OpenFolder(Path.GetDirectoryName(record.FilePath));});
    moreMenu.Items.Add(new ToolStripSeparator());
    selectAllCommand=AddMenuAction("全选当前结果（Ctrl+A）",SelectAllResults);
    deleteCommand=AddMenuAction("删除所选下载缓存…",DeleteSelectedCache);
   }
   var selected=SelectedRecord;
   refreshCommand.Enabled=selected!=null&&selected.Package!=null;modelCommand.Enabled=refreshCommand.Enabled;locateCommand.Enabled=selected!=null;
   selectAllCommand.Enabled=results.Length>0;deleteCommand.Enabled=SelectedRecords.All(row=>row.Package!=null);
   moreMenu.Show(list,point);
  }
  ToolStripMenuItem AddMenuAction(string text,Action action){
   var item=new ToolStripMenuItem(text);item.Click+=(sender,args)=>QueueMenuAction(action);moreMenu.Items.Add(item);return item;
  }
  void SaveUpdateState(){try{updateState.Save();}catch(Exception error){UserLog.Write("Save update preferences",error);SetStatus("更新偏好保存失败："+error.Message,true);}}
  void OpenWebsite(string path){try{UpdateClient.OpenPage(path);}catch(Exception error){SetStatus("无法打开官网："+error.Message,true);}}
  void ShowAvailableUpdate(){if(availableUpdate==null||busy||nativeBusy)return;var page=new UpdatePage(availableUpdate);page.CloseRequested+=()=>Post(()=>pageHost.ClosePage());ShowPanelPage("版本更新",page,ShowMore);}
  void CheckWebsiteUpdate(bool manual){
   if(ReleaseInfo.AdMajor<18){if(manual)ShowUserMessage("版本更新","请在 AD 中打开 LibrarySearch 后检查对应版本的更新。");return;}
   if(updateChecking){if(manual)SetStatus("正在检查更新…",false);return;}
   if(!manual&&!updateState.Due(DateTime.UtcNow))return;
   updateChecking=true;updateState.LastAttemptUtc=DateTime.UtcNow.ToString("o");SaveUpdateState();var pending=new CancellationTokenSource();updateCancellation=pending;bool beta=updateState.IncludeBeta;
   ThreadPool.QueueUserWorkItem(delegate{
    WebsiteRelease release=null;Exception failure=null;
    try{release=UpdateClient.Check(ReleaseInfo.AdMajor,pending.Token,beta);}catch(OperationCanceledException){}catch(Exception error){failure=error;UserLog.Write("Check website update",error);}
    Post(delegate{
     updateChecking=false;if(updateCancellation==pending)updateCancellation=null;pending.Dispose();
     if(failure!=null){if(manual)ShowUserMessage("版本更新","暂时无法检查更新，请稍后重试或访问官网下载页。");return;}
     if(release==null)return;
     if(UpdateClient.IsNewer(release,ReleaseInfo.Version,beta)){
      availableUpdate=release;
      if(manual||(updateState.Automatic&&updateState.NotifiedVersion!=release.Version)){updateBanner.Text="发现新版本 "+release.Version+" · 查看更新与下载";updateBanner.Visible=true;updateState.NotifiedVersion=release.Version;SaveUpdateState();PerformLayout();if(manual)ShowAvailableUpdate();}
     }else{availableUpdate=null;updateBanner.Visible=false;PerformLayout();if(manual)ShowUserMessage("版本更新","当前已是最新可用版本。\r\n\r\n"+ReleaseInfo.Version);}
    });
   });
  }
  void FetchModelForSelected(){
   if(busy||nativeBusy)return;var record=SelectedRecord;if(record==null||record.Package==null)return;if(record.Package.Model3dStatus=="embedded"&&record.Package.AcquisitionVersion==ReleaseInfo.Acquisition){if(OpenRequested!=null)OpenRequested(new LibraryStore(record.LibraryRoot).Records(record.Package,true).First(row=>row.Kind=="Footprint"));return;}string code=record.Code;var style=record.Package.Appearance;
   if(source.SelectedIndex!=0){source.SelectedIndex=0;query.Text=record.Name;}SetCandidateMode(false);AcquireComponent(false,true,style,false,code);
  }
  void SelectAllResults(){
   if(busy||nativeBusy)return;selectionChanging=true;list.BeginUpdate();try{for(int index=0;index<results.Length;index++)list.SelectedIndices.Add(index);}finally{list.EndUpdate();selectionChanging=false;}ShowSelection();UpdateActions();
  }
  void DeleteSelectedCache(){
   if(busy||nativeBusy)return;var records=SelectedRecords;if(records.Length==0||records.Any(row=>row.Package==null))return;
   string root=records[0].LibraryRoot;if(records.Any(row=>!string.Equals(root,row.LibraryRoot,StringComparison.OrdinalIgnoreCase)))throw new IOException("请选择同一缓存目录中的元件。");
   var codes=records.Select(row=>row.Code).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(code=>code).ToArray();
   string summary=string.Join("、",codes.Take(8))+(codes.Length>8?"…":"");
   var confirmation=new NoticePage("删除所选 "+codes.Length+" 个元件的本机下载记录？\r\n\r\n"+summary+"\r\n\r\n将同时删除这些元件已下载的符号、封装和历史版本。工程内的库和本地库不会删除。","确认删除");
   confirmation.Accepted+=()=>Post(delegate{pageHost.ClosePage();DeleteConfirmed(records);});ShowPanelPage("删除已下载元件",confirmation);
  }
  void DeleteConfirmed(LibraryRecord[] records){
   if(busy||nativeBusy)return;string root=records[0].LibraryRoot;var codes=records.Select(row=>row.Code).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(code=>code).ToArray();
   if(descriptionCancellation!=null)descriptionCancellation.Cancel();busy=true;cancellation=new CancellationTokenSource();var operation=cancellation;UpdateActions();SetStatus("正在删除所选下载缓存…",false);
   ThreadPool.QueueUserWorkItem(delegate{
    try{var removed=new LibraryStore(root).DeleteDownloadedBatch(codes,operation.Token,message=>Post(delegate{if(cancellation==operation)SetStatus(message,false);}));Post(delegate{
     var deleted=new HashSet<string>(removed.DeletedCodes,StringComparer.OrdinalIgnoreCase);downloaded=downloaded.Where(row=>!deleted.Contains(row.Code)).ToArray();online=online.Where(row=>!deleted.Contains(row.Code)).ToArray();busy=false;cancellation=null;operation.Dispose();
     var rows=source.SelectedIndex==0?online:source.SelectedIndex==1?downloaded:local;SetResults(rows.Where(row=>filter==0||row.Kind==(filter==1?"Symbol":"Footprint")).Where(row=>source.SelectedIndex==0||query.TextLength==0||row.Name.IndexOf(query.Text,StringComparison.OrdinalIgnoreCase)>=0||row.Code.IndexOf(query.Text,StringComparison.OrdinalIgnoreCase)>=0||row.RelatedName.IndexOf(query.Text,StringComparison.OrdinalIgnoreCase)>=0||(row.Description??"").IndexOf(query.Text,StringComparison.OrdinalIgnoreCase)>=0||(row.PackageName??"").IndexOf(query.Text,StringComparison.OrdinalIgnoreCase)>=0).Take(500).ToArray());
     string message=(removed.Cancelled?"已停止；":"")+"已删除 "+deleted.Count+" 个编号、"+removed.Versions+" 个版本"+(removed.FailedCodes.Length>0?"；"+removed.FailedCodes.Length+" 个编号未删除":"")+(removed.ResidualPaths.Length>0?"；部分暂存文件仍被占用":"")+"。";SetStatus(message,removed.FailedCodes.Length>0||removed.ResidualPaths.Length>0);UpdateActions();
     if(removed.Errors.Length>0||removed.ResidualPaths.Length>0)ShowUserMessage("部分元件未删除","部分文件正在使用或无法访问，请关闭相关库文件后重试。可从设置导出诊断信息。");
    });}
    catch(Exception error){UserLog.Write("Delete downloaded cache",error);Post(delegate{busy=false;cancellation=null;operation.Dispose();SetStatus("未完成："+error.Message,true);UpdateActions();});}
   });
  }

  void RefetchCurrentComponent(){
   if(busy||nativeBusy)return;var record=SelectedRecord;string code=record!=null&&record.Package!=null?record.Code:source.SelectedIndex==0&&ModelSearchClient.IsCode(query.Text)?query.Text:"";
   if(string.IsNullOrWhiteSpace(code))return;if(source.SelectedIndex!=0){source.SelectedIndex=0;query.Text=record.Name;}SetCandidateMode(false);AcquireComponent(true,false,null,false,code);
  }
  void QueueMenuAction(Action action){
   // Let ToolStrip finish its click/close processing before opening a modal
   // dialog or changing panel state. The menu itself lives with this panel.
   Post(delegate{if(busy||nativeBusy)return;try{action();}catch(Exception error){UserLog.Write("More menu action",error);SetStatus("操作未完成："+error.Message,true);}});
  }
  void ShowAbout(){
   var page=new NoticePage("LibrarySearch\r\n版本 "+ReleaseInfo.Version+"\r\n当前环境："+ReleaseInfo.HostTarget+" / Windows x64\r\n\r\n搜索元件、放置符号与封装，并随工程保存配套库。\r\n\r\n数据来源：嘉立创 EDA / EasyEDA","返回设置");
   page.Accepted+=()=>Post(()=>pageHost.ClosePage());ShowPanelPage("关于 LibrarySearch",page,ShowMore);
  }
  void ChangeRoot(){
   using(var dialog=new FolderBrowserDialog{Description="选择元件保存目录。已有下载不会自动搬移；原工程中的库文件不受影响。",SelectedPath=store.Root,ShowNewFolderButton=true}){
    if(dialog.ShowDialog(this)!=DialogResult.OK)return;
    try{string newRoot=Path.GetFullPath(dialog.SelectedPath);Directory.CreateDirectory(newRoot);string probe=Path.Combine(newRoot,".librarysearch-"+Guid.NewGuid().ToString("N"));using(File.Create(probe)){}File.Delete(probe);var replacement=new UserSettings{UsageNoticeVersion=settings.UsageNoticeVersion,LibraryRoot=newRoot,LocalIndex=settings.LocalIndex,LocalLibraryRoots=LocalRoots,Appearance=settings.Appearance};replacement.Save();settings=replacement;store=new LibraryStore(newRoot);online=new LibraryRecord[0];UpdateStorageHint();ReloadLibraries();}
    catch(Exception error){UserLog.Write("Change library root",error);SetStatus("无法保存目录设置："+error.Message,true);}
   }
  }
  void ChooseAppearance(){
   var page=new AppearancePage(settings.Appearance);
   page.Saved+=style=>Post(delegate{
    try{
     var replacement=new UserSettings{UsageNoticeVersion=settings.UsageNoticeVersion,LibraryRoot=settings.LibraryRoot,LocalIndex=settings.LocalIndex,LocalLibraryRoots=LocalRoots,Appearance=style};replacement.Save();settings=replacement;
     online=new LibraryRecord[0];if(source.SelectedIndex==0&&!candidateMode)SetResults(online);
     pageHost.ClosePage();SetStatus("符号外观已保存。",false);
    }catch(Exception error){UserLog.Write("Save appearance",error);SetStatus("外观未能保存，请重试。",true);}
   });
   ShowPanelPage("符号外观",page,ShowMore);
  }
  void ExportFeedback(){using(var dialog=new SaveFileDialog{Title="保存诊断信息",Filter="ZIP 文件 (*.zip)|*.zip",FileName="LibrarySearch-feedback-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".zip"}){if(dialog.ShowDialog(this)!=DialogResult.OK)return;try{Diagnostics.Export(dialog.FileName,store.Root);SetStatus("诊断信息已保存，请核对后提交反馈。",false);}catch(Exception error){SetStatus("保存失败，请选择其他位置后重试。",true);UserLog.Write("Export feedback",error);}}}
  void OpenHelp(){try{Process.Start(new ProcessStartInfo(Path.Combine(distribution,"docs","使用指南.html")){UseShellExecute=true});}catch(Exception error){SetStatus("无法打开说明："+error.Message,true);}}
  void OpenFolder(string path){try{Directory.CreateDirectory(path);Process.Start("explorer.exe","\""+Path.GetFullPath(path)+"\"");}catch(Exception error){SetStatus("无法打开目录："+error.Message,true);}}
 }
}
