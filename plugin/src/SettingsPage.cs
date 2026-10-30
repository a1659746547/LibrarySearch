using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace LibrarySearch {
 internal sealed class SettingsPage : UserControl {
  readonly Panel content=new Panel();
  readonly PanelButton[] navigation=new PanelButton[4];
  readonly CheckBox automatic=new CheckBox(),beta=new CheckBox();
  readonly Dictionary<string,Action> actions;
  readonly string root,local,appearance;
  public Action SelectedAction {get;private set;}
  public event Action CloseRequested;
  public event Action<Action> ActionRequested;
  public bool Automatic {get{return automatic.Checked;}}
  public bool IncludeBeta {get{return beta.Checked;}}
  public SettingsPage(string libraryRoot,string localRoots,string appearanceLabel,bool autoUpdate,bool includeBeta,Dictionary<string,Action> commands){
   root=libraryRoot;local=localRoots;appearance=appearanceLabel;actions=commands;
   AutoScaleMode=AutoScaleMode.None;BackColor=PanelTheme.Surface;ForeColor=PanelTheme.Ink;
   var shell=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(2)};shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));shell.RowStyles.Add(new RowStyle(SizeType.Absolute,44));shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));shell.RowStyles.Add(new RowStyle(SizeType.Absolute,44));Controls.Add(shell);
   var nav=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=4,RowCount=1};for(int i=0;i<4;i++)nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25));shell.Controls.Add(nav,0,0);
   string[] names={"库目录","外观","更新","帮助"};for(int i=0;i<names.Length;i++){int index=i;var button=new PanelButton{Text=names[i],Dock=DockStyle.Fill,Kind=PanelButtonKind.Filter,Margin=new Padding(0,0,4,6)};button.Click+=(s,e)=>ShowSection(index);navigation[i]=button;nav.Controls.Add(button,i,0);}
   content.Dock=DockStyle.Fill;content.AutoScroll=true;content.Padding=new Padding(4,0,4,0);shell.Controls.Add(content,0,1);
   var footer=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,80));shell.Controls.Add(footer,0,2);
   footer.Controls.Add(new Label{Text="LibrarySearch "+ReleaseInfo.Version,Dock=DockStyle.Fill,ForeColor=PanelTheme.Muted,TextAlign=ContentAlignment.MiddleLeft},0,0);var close=new PanelButton{Text="完成",Dock=DockStyle.Fill,Kind=PanelButtonKind.Primary};close.Click+=(s,e)=>{if(CloseRequested!=null)CloseRequested();};footer.Controls.Add(close,1,0);
   automatic.Text="每天自动检查更新";automatic.Checked=autoUpdate;beta.Text="接收预览版更新";beta.Checked=includeBeta;foreach(var box in new[]{automatic,beta}){box.AutoSize=true;box.Margin=new Padding(0,8,0,8);box.ForeColor=PanelTheme.Ink;}
   ShowSection(0);
  }
  void LabelRow(TableLayoutPanel table,string text,bool heading=false){table.RowStyles.Add(new RowStyle(SizeType.AutoSize));var label=new Label{Text=text,AutoSize=true,Dock=DockStyle.Top,MaximumSize=new Size(Math.Max(80,content.ClientSize.Width-28),0),ForeColor=heading?PanelTheme.Ink:PanelTheme.Muted,Margin=new Padding(0,heading?10:2,0,heading?8:12)};table.SizeChanged+=(s,e)=>{if(!label.IsDisposed)label.MaximumSize=new Size(Math.Max(80,table.ClientSize.Width-10),0);};table.Controls.Add(label,0,table.RowCount++);}
  void Command(TableLayoutPanel table,string title,string key){var button=new PanelButton{Text=title,Dock=DockStyle.Top,Height=34,Kind=PanelButtonKind.Command,Margin=new Padding(0,0,0,8)};button.Click+=(s,e)=>{Action action;if(actions.TryGetValue(key,out action)){SelectedAction=action;if(ActionRequested!=null)ActionRequested(action);}};table.Controls.Add(button,0,table.RowCount++);}
  public void ShowSection(int index){
   // Keep the update controls alive when their category is not displayed.
   if(automatic.Parent!=null)automatic.Parent.Controls.Remove(automatic);if(beta.Parent!=null)beta.Parent.Controls.Remove(beta);
   while(content.Controls.Count>0){var old=content.Controls[0];content.Controls.RemoveAt(0);old.Dispose();}
   for(int i=0;i<navigation.Length;i++)navigation[i].Active=i==index;
   var table=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,RowCount=0,Padding=new Padding(0,0,8,12)};table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));content.Controls.Add(table);
   if(index==0){LabelRow(table,"元件保存位置",true);LabelRow(table,root);LabelRow(table,"下载的符号、封装和模型保存在这里；放置时配套库会复制到工程 Libraries\\LibrarySearch。更改目录不会搬移已有文件。");Command(table,"更改保存目录…","changeRoot");Command(table,"打开保存目录","openRoot");Command(table,"刷新已下载元件","reload");LabelRow(table,"本地元件库",true);LabelRow(table,string.IsNullOrWhiteSpace(local)?"尚未选择本地库目录":local);Command(table,"选择本地库目录…","chooseLocal");Command(table,"重新扫描本地库","scanLocal");}
   else if(index==1){LabelRow(table,"符号外观",true);LabelRow(table,"当前方案："+appearance+"\r\n外观用于后续在线获取，已放置的工程元件不会自动改变。");Command(table,"调整符号外观…","appearance");}
   else if(index==2){LabelRow(table,"版本更新",true);LabelRow(table,"当前版本："+ReleaseInfo.Version+"\r\n安装更新前请保存工程并关闭 AD。");table.Controls.Add(automatic,0,table.RowCount++);table.Controls.Add(beta,0,table.RowCount++);LabelRow(table,"更新偏好会在离开设置时保存。");Command(table,"立即检查更新","checkUpdate");Command(table,"打开官网下载页","website");}
   else{LabelRow(table,"使用帮助",true);Command(table,"使用指南","onlineHelp");Command(table,"离线使用指南","help");Command(table,"元件使用须知","notice");LabelRow(table,"问题反馈",true);LabelRow(table,"遇到问题时可导出诊断信息并提交反馈。文件不会自动发送。");Command(table,"导出诊断信息…","feedback");Command(table,"打开反馈页面","feedbackPage");Command(table,"关于 LibrarySearch","about");}
  }
  protected override void Dispose(bool disposing){if(disposing){automatic.Dispose();beta.Dispose();}base.Dispose(disposing);}
 }
}
