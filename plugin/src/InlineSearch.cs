using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace LibrarySearch {
 internal sealed class InlineModelResults : UserControl {
  readonly ListView rows=new ResultList();
  readonly Label summary=new Label();
  readonly PanelButton previous=new PanelButton(),next=new PanelButton(),fetch=new PanelButton();
  readonly TableLayoutPanel layout=new TableLayoutPanel();
  readonly ImageList rowImages=new ImageList();
  readonly Label loading=new Label();
  string actionText="放置符号";
  public string ActionText {get{return actionText;}set{actionText=value;fetch.Text=value;}}
  CancellationTokenSource pending;
  ModelSearchPage current;
  string keyword="";
  bool running;
  public bool Running {get{return running;}}
  public bool HasResults {get{return current!=null;}}
  internal Func<string,int,CancellationToken,ModelSearchPage> SearchProvider=null;
  internal Func<string,int,CancellationToken,int,ModelSearchPage> SizedSearchProvider=ModelSearchClient.Search;
  public event Action<bool> RunningChanged;
  public event Action<ModelCandidate> FetchRequested;
  public InlineModelResults(){
   Dock=DockStyle.None;BackColor=PanelTheme.Input;ForeColor=PanelTheme.Ink;
   layout.Dock=DockStyle.Fill;layout.ColumnCount=1;layout.RowCount=3;layout.Padding=new Padding(1);layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,32));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,36));Controls.Add(layout);
   var viewport=new Panel{Dock=DockStyle.Fill,BackColor=PanelTheme.Input,Margin=Padding.Empty};layout.Controls.Add(viewport,0,0);rows.Dock=DockStyle.Fill;rows.View=View.Details;rows.FullRowSelect=true;rows.MultiSelect=false;rows.HideSelection=false;rows.BorderStyle=BorderStyle.None;rows.ShowItemToolTips=true;rows.OwnerDraw=true;rows.AccessibleName="在线元件搜索结果";rows.Columns.Add("型号 / 厂家",180);rows.Columns.Add("封装",120);rows.Columns.Add("C 编号",100);viewport.Controls.Add(rows);
   loading.Dock=DockStyle.Fill;loading.BackColor=PanelTheme.Input;loading.ForeColor=PanelTheme.Muted;loading.Text="正在搜索…";loading.TextAlign=ContentAlignment.MiddleCenter;loading.Visible=false;viewport.Controls.Add(loading);
   rowImages.ImageSize=new Size(1,46);rows.SmallImageList=rowImages;rows.DrawSubItem+=(s,e)=>{bool selected=e.Item.Selected;using(var brush=new SolidBrush(selected?PanelTheme.Selection:PanelTheme.Input))e.Graphics.FillRectangle(brush,e.Bounds);var item=(ModelCandidate)e.Item.Tag;if(item==null)return;string first=e.ColumnIndex==0?item.Model:e.ColumnIndex==1?item.Package:item.Code;string second=e.ColumnIndex==0?item.Manufacturer:e.ColumnIndex==1&&!item.HasLibrary?"暂无符号和封装":"";var rect=new Rectangle(e.Bounds.X+6,e.Bounds.Y+5,Math.Max(0,e.Bounds.Width-12),Font.Height+2);var flags=TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix;TextRenderer.DrawText(e.Graphics,first,Font,rect,item.HasLibrary?PanelTheme.Ink:PanelTheme.Muted,flags);rect.Y+=Font.Height+3;TextRenderer.DrawText(e.Graphics,second,Font,rect,selected?PanelTheme.Ink:PanelTheme.Muted,flags);};
   rows.Resize+=(s,e)=>{int w=((ResultList)rows).AvailableColumnWidth;rows.Columns[2].Width=Math.Min(100,w/3);rows.Columns[1].Width=Math.Min(120,w/3);rows.Columns[0].Width=Math.Max(0,w-rows.Columns[1].Width-rows.Columns[2].Width);};
   summary.Dock=DockStyle.Fill;summary.AutoEllipsis=true;summary.ForeColor=PanelTheme.Muted;summary.TextAlign=ContentAlignment.MiddleLeft;layout.Controls.Add(summary,0,1);
   var footer=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1};footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,28));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,28));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,44));layout.Controls.Add(footer,0,2);
   previous.Text="上一页";next.Text="下一页";fetch.Text=actionText;foreach(var b in new[]{previous,next,fetch}){b.Dock=DockStyle.Fill;b.Kind=PanelButtonKind.Command;}fetch.Kind=PanelButtonKind.Primary;footer.Controls.Add(previous,0,0);footer.Controls.Add(next,1,0);footer.Controls.Add(fetch,2,0);
   previous.Click+=(s,e)=>Search(keyword,current.Page-1);next.Click+=(s,e)=>Search(keyword,current.Page+1);fetch.Click+=(s,e)=>Choose();rows.DoubleClick+=(s,e)=>Choose();rows.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter){Choose();e.SuppressKeyPress=true;}};rows.SelectedIndexChanged+=(s,e)=>UpdateActions();UpdateActions();
  }
  void UpdateActions(){previous.Enabled=!running&&current!=null&&current.Page>1;next.Enabled=!running&&current!=null&&current.Page<1000&&(long)current.Page*current.PageSize<current.Total;fetch.Enabled=!running&&rows.SelectedItems.Count==1&&((ModelCandidate)rows.SelectedItems[0].Tag).HasLibrary;rows.Enabled=true;loading.Visible=running;if(running)loading.BringToFront();}
  void Choose(){if(fetch.Enabled&&FetchRequested!=null)FetchRequested((ModelCandidate)rows.SelectedItems[0].Tag);}
  public void CancelSearch(){var operation=pending;if(operation==null)return;pending=null;operation.Cancel();running=false;UpdateActions();summary.Text="已取消搜索。";if(RunningChanged!=null)RunningChanged(false);}
  public void Search(string text,int page=1){
   if(running)return;try{text=ModelSearchClient.ValidateQuery(text);}catch(Exception error){summary.Text=error.Message;return;}
   // Keep the page size while paging, so resizing cannot skip or repeat items.
   int pageSize=current!=null&&text==keyword?current.PageSize:Math.Max(10,Math.Min(50,(int)Math.Ceiling((double)Math.Max(0,rows.ClientSize.Height-28)/Math.Max(46,rowImages.ImageSize.Height))));
   running=true;current=null;rows.Items.Clear();summary.Text="正在搜索 "+text+"…";pending=new CancellationTokenSource();var operation=pending;UpdateActions();if(RunningChanged!=null)RunningChanged(true);
   string requested=text;
   ThreadPool.QueueUserWorkItem(delegate{
    ModelSearchPage found=null;Exception failure=null;
    try{found=SearchProvider!=null?SearchProvider(requested,page,operation.Token):SizedSearchProvider(requested,page,operation.Token,pageSize);}catch(Exception error){failure=error;}
    Action finish=delegate{
     if(pending!=operation){operation.Dispose();return;}
     try{
      if(IsDisposed||Disposing)return;
      if(operation.IsCancellationRequested||failure is OperationCanceledException)summary.Text="已取消搜索。";
      else if(failure!=null){summary.Text=failure.Message;UserLog.Write("Inline model search",failure);}
      else{
       keyword=requested;current=found;rows.BeginUpdate();try{
       foreach(var item in found.Items){var row=new ListViewItem(new[]{item.Model,item.Package,item.Code});row.Tag=item;row.ToolTipText=item.Model+"\r\n"+item.Manufacturer+" · "+item.Package+"\r\n"+item.Description+(item.HasLibrary?"":"\r\n该元件暂无符号和封装");if(!item.HasLibrary)row.ForeColor=PanelTheme.Muted;rows.Items.Add(row);}
       }finally{rows.EndUpdate();rows.Invalidate();}
       summary.Text=found.Total==0?"没有找到元件，请调整关键词。":"第 "+page+" 页 · 共 "+found.Total+" 个结果";
      }
     }finally{if(pending==operation)pending=null;operation.Dispose();running=false;if(!IsDisposed&&!Disposing){UpdateActions();if(RunningChanged!=null)RunningChanged(false);}}
    };
    if(IsDisposed||Disposing||!IsHandleCreated){operation.Dispose();return;}
    try{BeginInvoke(finish);}catch(InvalidOperationException){operation.Dispose();}
   });
  }
  protected override void Dispose(bool disposing){if(disposing)CancelSearch();base.Dispose(disposing);if(disposing)rowImages.Dispose();}
 }
}
