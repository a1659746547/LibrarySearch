using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LibrarySearch {
 internal sealed class PanelPageHost : UserControl {
  readonly Label title=new Label();
  readonly PanelButton back=new PanelButton();
  readonly Panel body=new Panel();
  Action closed;
  public event Action PageClosed;
  public bool HasPage {get{return body.Controls.Count>0;}}
  public PanelPageHost(){
   BackColor=PanelTheme.Surface;ForeColor=PanelTheme.Ink;Visible=false;
   var shell=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Padding=new Padding(10)};shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));shell.RowStyles.Add(new RowStyle(SizeType.Absolute,42));shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));Controls.Add(shell);
   var header=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,64));header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));shell.Controls.Add(header,0,0);
   back.Text="返回";back.Kind=PanelButtonKind.Command;back.Dock=DockStyle.Fill;back.Click+=(s,e)=>ClosePage();header.Controls.Add(back,0,0);title.Dock=DockStyle.Fill;title.TextAlign=ContentAlignment.MiddleLeft;title.Padding=new Padding(12,0,0,0);header.Controls.Add(title,1,0);
   body.Dock=DockStyle.Fill;shell.Controls.Add(body,0,1);
  }
  public void ShowPage(string heading,Control page,Action onClose=null,bool allowBack=true){
   while(body.Controls.Count>0){var old=body.Controls[0];body.Controls.RemoveAt(0);old.Dispose();}
   closed=onClose;title.Text=heading;back.Visible=allowBack;page.Dock=DockStyle.Fill;body.Controls.Add(page);Visible=true;BringToFront();page.Select();
  }
  public void ClosePage(){var callback=closed;closed=null;Visible=false;var old=body.Controls.Cast<Control>().ToArray();body.Controls.Clear();try{if(callback!=null)callback();}finally{foreach(var page in old)page.Dispose();if(PageClosed!=null)PageClosed();}}
 }
 internal sealed class NoticePage : UserControl {
  public event Action Accepted;
  public NoticePage(string text,string buttonText="我知道了"){
   BackColor=PanelTheme.Surface;ForeColor=PanelTheme.Ink;
   var shell=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Padding=new Padding(6,12,6,4)};shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));shell.RowStyles.Add(new RowStyle(SizeType.Absolute,44));Controls.Add(shell);
   var scroll=new Panel{Dock=DockStyle.Fill,AutoScroll=true};shell.Controls.Add(scroll,0,0);var copy=new Label{Text=text,Dock=DockStyle.Top,AutoSize=true,Padding=new Padding(4),ForeColor=PanelTheme.Ink};scroll.Controls.Add(copy);scroll.Resize+=(s,e)=>copy.MaximumSize=new Size(Math.Max(60,scroll.ClientSize.Width-SystemInformation.VerticalScrollBarWidth-4),0);
   var ok=new PanelButton{Text=buttonText,Dock=DockStyle.Fill,Kind=PanelButtonKind.Primary};ok.Click+=(s,e)=>{if(Accepted!=null)Accepted();};shell.Controls.Add(ok,0,1);
  }
 }
 internal sealed class ColorField : UserControl {
  readonly TextBox input=new TextBox();
  readonly Panel sample=new Panel();
  public event Action Changed;
  public string Value {get{return input.Text.Trim().ToUpperInvariant();}set{input.Text=value;}}
  public ColorField(){
   Height=62;BackColor=PanelTheme.Surface;var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=2,Padding=Padding.Empty};layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,30));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,28));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,26));Controls.Add(layout);
   input.Dock=DockStyle.Fill;input.BackColor=PanelTheme.Input;input.ForeColor=PanelTheme.Ink;input.BorderStyle=BorderStyle.FixedSingle;input.MaxLength=7;sample.Dock=DockStyle.Fill;sample.Margin=new Padding(4,0,0,3);layout.Controls.Add(input,0,0);layout.Controls.Add(sample,1,0);
   var palette=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty};layout.Controls.Add(palette,0,1);layout.SetColumnSpan(palette,2);
   foreach(string color in new[]{"#FFFFB0","#800000","#000080","#333333","#FFFFFF","#9B3030"}){string chosen=color;var chip=new Button{BackColor=ColorTranslator.FromHtml(color),FlatStyle=FlatStyle.Flat,Width=20,Height=20,Margin=new Padding(0,1,4,1),TabStop=true,AccessibleName="颜色 "+color,UseVisualStyleBackColor=false};chip.Click+=(s,e)=>Value=chosen;palette.Controls.Add(chip);}
   input.TextChanged+=(s,e)=>{try{if(System.Text.RegularExpressions.Regex.IsMatch(Value,"^#[0-9A-F]{6}$"))sample.BackColor=ColorTranslator.FromHtml(Value);}catch{}if(Changed!=null)Changed();};
  }
 }
 internal sealed class AppearancePage : UserControl {
  readonly ComboBox preset=new ComboBox(),font=new ComboBox(),background=new ComboBox(),width=new ComboBox();
  readonly NumericUpDown nameSize=new NumericUpDown(),numberSize=new NumericUpDown(),labelSize=new NumericUpDown(),radius=new NumericUpDown();
  readonly CheckBox fill=new CheckBox(),round=new CheckBox(),deduplicate=new CheckBox(),comment=new CheckBox();
  readonly Dictionary<string,ColorField> colors=new Dictionary<string,ColorField>();
  readonly AppearancePreview preview=new AppearancePreview();
  readonly Label error=new Label();
  readonly TableLayoutPanel fields=new TableLayoutPanel();
  bool loading;
  public event Action<SymbolAppearance> Saved;
  public AppearancePage(SymbolAppearance initial){
   BackColor=PanelTheme.Surface;ForeColor=PanelTheme.Ink;
   var shell=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));shell.RowStyles.Add(new RowStyle(SizeType.Absolute,72));Controls.Add(shell);
   var scroll=new Panel{Dock=DockStyle.Fill,AutoScroll=true};shell.Controls.Add(scroll,0,0);var stack=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,Padding=new Padding(4,0,12,8)};stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));scroll.Controls.Add(stack);
   preview.Height=330;preview.Dock=DockStyle.Top;stack.Controls.Add(preview,0,0);
   fields.Dock=DockStyle.Top;fields.AutoSize=true;fields.ColumnCount=2;fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,112));fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));stack.Controls.Add(fields,0,1);
   preset.Items.AddRange(new object[]{"现代浅色","经典 AD","自定义"});Row("外观方案",preset);background.Items.AddRange(new object[]{"白色","浅灰"});Row("预览底色",background);
   foreach(var pair in new[]{new[]{"器件填充","BodyFill"},new[]{"边框与引脚","BorderColor"},new[]{"名称与正文","TextColor"},new[]{"引脚编号","PinNumberColor"},new[]{"极性文字","AccentColor"}}){var editor=new ColorField();colors.Add(pair[1],editor);Row(pair[0],editor);editor.Changed+=CustomChanged;}
   using(var installed=new System.Drawing.Text.InstalledFontCollection())font.Items.AddRange(installed.Families.Select(f=>f.Name).OrderBy(n=>n).Cast<object>().ToArray());Row("字体",font);
   SizeRow("引脚名称字号",nameSize,5,14);SizeRow("引脚编号字号",numberSize,5,14);SizeRow("位号字号",labelSize,5,14);width.Items.AddRange(new object[]{"细","中","粗"});Row("图形线宽",width);SizeRow("圆角半径 (mil)",radius,10,50);
   CheckRow(fill,"填充主体");CheckRow(round,"矩形主体圆角");CheckRow(deduplicate,"隐藏与编号相同的引脚名称");CheckRow(comment,"显示型号");
   var note=new Label{Text="外观应用于之后下载的元件，已有工程中的元件不会自动改变。",Dock=DockStyle.Top,AutoSize=true,ForeColor=PanelTheme.Muted,Margin=new Padding(4,10,4,10)};stack.Controls.Add(note,0,2);scroll.Resize+=(s,e)=>note.MaximumSize=new Size(Math.Max(100,scroll.ClientSize.Width-30),0);
   var footer=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));footer.RowStyles.Add(new RowStyle(SizeType.Absolute,28));footer.RowStyles.Add(new RowStyle(SizeType.Percent,100));shell.Controls.Add(footer,0,1);error.Dock=DockStyle.Fill;error.ForeColor=PanelTheme.Error;error.AutoEllipsis=true;footer.Controls.Add(error,0,0);var save=new PanelButton{Text="保存外观",Dock=DockStyle.Fill,Kind=PanelButtonKind.Primary};save.Click+=(s,e)=>{try{var style=SymbolAppearance.Effective(Read());if(Saved!=null)Saved(style);}catch(Exception failure){error.Text=failure.Message;}};footer.Controls.Add(save,0,1);
   preset.SelectedIndexChanged+=(s,e)=>{if(loading)return;if(preset.SelectedIndex==0)LoadValues(new SymbolAppearance());else if(preset.SelectedIndex==1)LoadValues(SymbolAppearance.Classic());UpdatePreview();};foreach(var box in new[]{font,background,width})box.SelectedIndexChanged+=(s,e)=>CustomChanged();foreach(var size in new[]{nameSize,numberSize,labelSize,radius})size.ValueChanged+=(s,e)=>CustomChanged();foreach(var box in new[]{fill,round,deduplicate,comment})box.CheckedChanged+=(s,e)=>CustomChanged();LoadValues(SymbolAppearance.Effective(initial));UpdatePreview();
  }
  void Row(string text,Control control){int row=fields.RowCount++;fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));fields.Controls.Add(new Label{Text=text,AutoSize=true,Anchor=AnchorStyles.Left,Margin=new Padding(0,8,4,8)},0,row);control.Dock=DockStyle.Fill;control.Margin=new Padding(0,5,0,5);control.BackColor=PanelTheme.Input;control.ForeColor=PanelTheme.Ink;var combo=control as ComboBox;if(combo!=null){combo.DropDownStyle=ComboBoxStyle.DropDownList;combo.FlatStyle=FlatStyle.Flat;}fields.Controls.Add(control,1,row);}
  void SizeRow(string text,NumericUpDown input,int min,int max){input.Minimum=min;input.Maximum=max;Row(text,input);}
  void CheckRow(CheckBox box,string text){box.Text=text;box.AutoSize=true;Row("",box);}
  void CustomChanged(){if(loading)return;loading=true;preset.SelectedIndex=2;loading=false;UpdatePreview();}
  void LoadValues(SymbolAppearance style){loading=true;try{preset.SelectedIndex=style.Mode=="Classic"?1:style.Mode=="Custom"?2:0;background.SelectedIndex=style.Background=="#F8F9FA"?1:0;colors["BodyFill"].Value=style.BodyFill;colors["BorderColor"].Value=style.BorderColor;colors["TextColor"].Value=style.TextColor;colors["PinNumberColor"].Value=style.PinNumberColor;colors["AccentColor"].Value=style.AccentColor;if(!font.Items.Contains(style.FontFamily))font.Items.Add(style.FontFamily);font.SelectedItem=style.FontFamily;nameSize.Value=style.PinNameSize;numberSize.Value=style.PinNumberSize;labelSize.Value=style.LabelSize;width.SelectedIndex=style.LineWidth;radius.Value=style.CornerRadius;fill.Checked=style.FillBody;round.Checked=style.RoundedCorners;deduplicate.Checked=style.HideDuplicatePinNames;comment.Checked=style.ShowComment;}finally{loading=false;}}
  SymbolAppearance Read(){return new SymbolAppearance{Mode=preset.SelectedIndex==1?"Classic":preset.SelectedIndex==2?"Custom":"Modern",Background=background.SelectedIndex==1?"#F8F9FA":"#FFFFFF",BodyFill=colors["BodyFill"].Value,BorderColor=colors["BorderColor"].Value,TextColor=colors["TextColor"].Value,PinNumberColor=colors["PinNumberColor"].Value,AccentColor=colors["AccentColor"].Value,FontFamily=Convert.ToString(font.SelectedItem),PinNameSize=(int)nameSize.Value,PinNumberSize=(int)numberSize.Value,LabelSize=(int)labelSize.Value,LineWidth=width.SelectedIndex,CornerRadius=(int)radius.Value,FillBody=fill.Checked,RoundedCorners=round.Checked,HideDuplicatePinNames=deduplicate.Checked,ShowComment=comment.Checked};}
  void UpdatePreview(){if(loading)return;try{preview.Style=SymbolAppearance.Effective(Read());preview.Invalidate();error.Text="";}catch(Exception failure){error.Text=failure.Message;}}
 }
}
