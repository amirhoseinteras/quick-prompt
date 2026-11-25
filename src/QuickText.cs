using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Threading;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Win32;
using System.Drawing.Drawing2D;

[assembly: System.Reflection.AssemblyVersion("4.7.1.0")]
[assembly: System.Reflection.AssemblyFileVersion("4.7.1.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("4.7.1")]

namespace QuickPromptApp
{
    public static class AppInfo
    {
        public const string Version="4.7.1";
        public const string GitHubUrl="https://github.com/amirhoseinteras/quick-prompt";
    }

    public class PromptLink
    {
        public int Start {get;set;}
        public int Length {get;set;}
        public string Text {get;set;}
        public string Url {get;set;}
    }

    public class PromptItem
    {
        public string Id { get; set; }
        public int Number { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public string TitleRtf { get; set; }
        public string TitleHtml { get; set; }
        public string ContentRtf { get; set; }
        public string ContentHtml { get; set; }
        public List<PromptLink> Links { get; set; }
        public string Category { get; set; }
        public string Modified { get; set; }
        public bool IsPinned { get; set; }
    }

    public class PromptStore
    {
        private readonly string file;
        public List<PromptItem> Items = new List<PromptItem>();
        private JavaScriptSerializer serializer = new JavaScriptSerializer();

        public PromptStore(string filePath)
        {
            file = filePath;
            serializer.MaxJsonLength = int.MaxValue;
            Load();
        }

        private void Load()
        {
            if (!File.Exists(file)) return;
            string data = File.ReadAllText(file, Encoding.UTF8);
            Items = serializer.Deserialize<List<PromptItem>>(data) ?? new List<PromptItem>();
            if(PromptNumbering.Normalize(Items)) Save();
        }

        public void Save()
        {
            PromptNumbering.Normalize(Items);
            string folder = Path.GetDirectoryName(file);
            Directory.CreateDirectory(folder);
            string temp = file + ".tmp";
            File.WriteAllText(temp, serializer.Serialize(Items), new UTF8Encoding(false));
            if (File.Exists(file)) File.Replace(temp, file, null);
            else File.Move(temp, file);
        }

        public static string DataPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickPrompt", "prompts.json"); }
        }
    }

    public static class PromptNumbering
    {
        public static bool Normalize(List<PromptItem> items)
        {
            if(items==null)return false;
            bool changed=false;
            HashSet<int> used=new HashSet<int>();
            foreach(PromptItem item in items){
                if(item.Number>0 && used.Add(item.Number))continue;
                if(item.Number!=0){item.Number=0;changed=true;}
            }
            foreach(PromptItem item in items){
                if(item.Number>0)continue;
                int candidate=1;
                while(used.Contains(candidate) && candidate<Int32.MaxValue)candidate++;
                if(candidate<=0 || used.Contains(candidate))throw new InvalidOperationException("No free item number is available.");
                item.Number=candidate;used.Add(candidate);changed=true;
            }
            return changed;
        }
        public static int NextAvailableNumber(IEnumerable<PromptItem> items)
        {
            HashSet<int> used=new HashSet<int>((items??Enumerable.Empty<PromptItem>()).Where(p=>p.Number>0).Select(p=>p.Number));
            int candidate=1;
            while(used.Contains(candidate) && candidate<Int32.MaxValue)candidate++;
            if(candidate<=0 || used.Contains(candidate))throw new InvalidOperationException("No free item number is available.");
            return candidate;
        }
        public static bool IsNumberTaken(IEnumerable<int> numbers,int number)
        {
            return (numbers??Enumerable.Empty<int>()).Any(n=>n==number);
        }
    }

    public static class PromptLogic
    {
        public static bool IsRtlText(string value)
        {
            if(String.IsNullOrEmpty(value))return false;
            foreach(char ch in value){
                if(Char.IsLetter(ch)){
                    return (ch>='\u0590' && ch<='\u08FF') || (ch>='\uFB1D' && ch<='\uFEFC');
                }
            }
            return false;
        }
        public static List<PromptItem> Order(List<PromptItem> items, string query)
        {
            string q = (query ?? "").Trim();
            if(q.Length>0){
                string numberText=q.StartsWith("#",StringComparison.Ordinal)?q.Substring(1):q;
                int exactNumber;
                if(Int32.TryParse(numberText,out exactNumber) && exactNumber>0){
                    List<PromptItem> exact=items.Where(p=>p.Number==exactNumber).OrderByDescending(p=>p.IsPinned).ToList();
                    if(exact.Count>0)return exact;
                }
            }
            return items
                .Where(p => q.Length == 0 || (p.Title ?? "").IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                                           (p.Content ?? "").IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                                           (p.Category ?? "").IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0)
                .OrderByDescending(p => p.IsPinned)
                .ToList();
        }

        public static bool Move(List<PromptItem> items, PromptItem item, int direction)
        {
            if (direction != -1 && direction != 1) return false;
            var group = items.Where(p => p.IsPinned == item.IsPinned).ToList();
            int position = group.IndexOf(item);
            if (position < 0 || position + direction < 0 || position + direction >= group.Count) return false;
            int index = items.IndexOf(item);
            int target = items.IndexOf(group[position + direction]);
            PromptItem swapped = items[index];
            items[index] = items[target];
            items[target] = swapped;
            return true;
        }
    }


    public class SoftCard : Panel
    {
        public bool DropHighlight = false;
        public SoftCard()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(250,251,254);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }
        private static System.Drawing.Drawing2D.GraphicsPath Rounded(Rectangle r, int radius)
        {
            System.Drawing.Drawing2D.GraphicsPath p = new System.Drawing.Drawing2D.GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.Left, r.Top, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? Color.FromArgb(246,248,252) : Parent.BackColor);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Rectangle shadow = new Rectangle(3,4,Width-7,Height-7);
            using (var p = Rounded(shadow,12))
            using (var b = new SolidBrush(Color.FromArgb(228,231,239))) e.Graphics.FillPath(b,p);
            Rectangle surface = new Rectangle(1,0,Width-5,Height-6);
            using (var p = Rounded(surface,12))
            {
                using (var b = new System.Drawing.Drawing2D.LinearGradientBrush(surface,Color.White,Color.FromArgb(248,250,254),90f))
                    e.Graphics.FillPath(b,p);
                using (var pen = new Pen(DropHighlight ? Color.FromArgb(91,98,211) : Color.FromArgb(228,232,241),DropHighlight ? 2f : 1f))
                    e.Graphics.DrawPath(pen,p);
            }
        }
    }

    public class SoftButton : Button
    {
        public Color Fill = Color.FromArgb(242,245,251);
        public Color HoverFill = Color.FromArgb(230,234,246);
        public int Radius = 10;
        private bool hovering;
        public SoftButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI",10F,FontStyle.Regular);
            DoubleBuffered = true;
            TabStop = true;
        }
        protected override void OnMouseEnter(EventArgs e) { hovering = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovering = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent == null ? Color.White : Parent.BackColor);
            Rectangle r = new Rectangle(1,1,Width-3,Height-3);
            using (var p = new System.Drawing.Drawing2D.GraphicsPath())
            {
                int d = Math.Min(Radius*2,Math.Min(r.Width,r.Height));
                p.AddArc(r.Left,r.Top,d,d,180,90);
                p.AddArc(r.Right-d,r.Top,d,d,270,90);
                p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);
                p.AddArc(r.Left,r.Bottom-d,d,d,90,90);
                p.CloseFigure();
                using (var b = new SolidBrush(hovering ? HoverFill : Fill)) e.Graphics.FillPath(b,p);
            }
            TextRenderer.DrawText(e.Graphics,Text,Font,r,ForeColor,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
        }
    }


    public static class UiSizeMemory
    {
        private static readonly string Folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"QuickPrompt");
        private static string PathFor(string key) { return Path.Combine(Folder,"quicktext-"+key+"-size.txt"); }
        public static Size Load(string key,Size initial,Size minimum)
        {
            try {
                string file=PathFor(key);
                if(!File.Exists(file)) return initial;
                string[] a=File.ReadAllText(file,Encoding.UTF8).Trim().Split('x');
                if(a.Length!=2)return initial;
                int w,h;
                if(!int.TryParse(a[0],out w)||!int.TryParse(a[1],out h))return initial;
                Rectangle area=Screen.PrimaryScreen.WorkingArea;
                w=Math.Max(minimum.Width,Math.Min(w,Math.Max(minimum.Width,area.Width-25)));
                h=Math.Max(minimum.Height,Math.Min(h,Math.Max(minimum.Height,area.Height-25)));
                return new Size(w,h);
            }catch {return initial;}
        }
        public static void Save(string key,Size value,Size minimum)
        {
            if(value.Width<minimum.Width || value.Height<minimum.Height)return;
            try {
                Directory.CreateDirectory(Folder);
                string file=PathFor(key);
                string temp=file+".tmp";
                File.WriteAllText(temp,value.Width.ToString()+"x"+value.Height.ToString(),new UTF8Encoding(false));
                if(File.Exists(file)) File.Replace(temp,file,null);
                else File.Move(temp,file);
            }catch{}
        }
    }



    public static class RichDocument
    {
        public static void Load(RichTextBox box,string rtf,string plain)
        {
            if(!String.IsNullOrWhiteSpace(rtf)){
                try{box.Rtf=rtf;return;}catch{}
            }
            box.Text=plain??"";
        }
        public static string ReadRtf(RichTextBox box)
        {
            try {return box.Rtf;}catch {return null;}
        }
        public static string EscapeRtfText(string value)
        {
            StringBuilder result=new StringBuilder();
            foreach(char ch in value??""){
                if(ch=='\\' || ch=='{' || ch=='}')result.Append('\\').Append(ch);
                else if(ch=='\r')continue;
                else if(ch=='\n')result.Append("\\line ");
                else if(ch<32 || ch>127)result.Append("\\u").Append(((short)ch).ToString(CultureInfo.InvariantCulture)).Append('?');
                else result.Append(ch);
            }
            return result.ToString();
        }
        private static string HtmlEncode(string value)
        {
            return (value??"").Replace("&","&amp;").Replace("<","&lt;").Replace(">","&gt;").Replace("\"","&quot;").Replace("'","&#39;");
        }
        private static string HtmlText(string value)
        {
            return HtmlEncode(value).Replace("\r\n","<br>").Replace("\n","<br>").Replace("\r","<br>");
        }
        public static string BuildHtmlWithLinks(string text,IEnumerable<PromptLink> links)
        {
            string content=text??"";
            List<PromptLink> valid=(links??Enumerable.Empty<PromptLink>())
                .Where(l=>l!=null && l.Start>=0 && l.Length>0 && l.Start+l.Length<=content.Length &&
                    String.Equals(content.Substring(l.Start,l.Length),l.Text??"",StringComparison.Ordinal))
                .OrderBy(l=>l.Start).ToList();
            StringBuilder fragment=new StringBuilder();
            int position=0;
            foreach(PromptLink link in valid){
                if(link.Start<position)continue;
                fragment.Append(HtmlText(content.Substring(position,link.Start-position)));
                string label=content.Substring(link.Start,link.Length);
                fragment.Append("<a href=\"").Append(HtmlEncode(link.Url)).Append("\">")
                    .Append(HtmlText(label)).Append("</a>");
                position=link.Start+link.Length;
            }
            fragment.Append(HtmlText(content.Substring(position)));
            string start="<html><head><meta charset=\"utf-8\"></head><body><!--StartFragment-->";
            string end="<!--EndFragment--></body></html>";
            string template="Version:1.0\r\nStartHTML:0000000000\r\nEndHTML:0000000000\r\nStartFragment:0000000000\r\nEndFragment:0000000000\r\n";
            int startHtml=Encoding.UTF8.GetByteCount(template);
            int startFragment=startHtml+Encoding.UTF8.GetByteCount(start);
            int endFragment=startFragment+Encoding.UTF8.GetByteCount(fragment.ToString());
            int endHtml=endFragment+Encoding.UTF8.GetByteCount(end);
            string header=String.Format(CultureInfo.InvariantCulture,
                "Version:1.0\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n",
                startHtml,endHtml,startFragment,endFragment);
            return header+start+fragment.ToString()+end;
        }
        public static void OpenLink(string value)
        {
            Uri uri;
            if(!Uri.TryCreate(value,UriKind.Absolute,out uri))return;
            string scheme=uri.Scheme.ToLowerInvariant();
            if(scheme!="http" && scheme!="https" && scheme!="ftp" && scheme!="mailto")return;
            try{Process.Start(new ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true});}catch{}
        }
        public static void Copy(PromptItem item)
        {
            if(item==null || String.IsNullOrEmpty(item.Content))return;
            if(item.Links!=null && item.Links.Count>0){
                DataObject linked=new DataObject();
                linked.SetText(item.Content,TextDataFormat.UnicodeText);
                if(!String.IsNullOrEmpty(item.ContentRtf))linked.SetData(DataFormats.Rtf,item.ContentRtf);
                linked.SetData(DataFormats.Html,BuildHtmlWithLinks(item.Content,item.Links));
                Clipboard.SetDataObject(linked,true,5,100);
                return;
            }
            if(!String.IsNullOrEmpty(item.ContentHtml)){
                DataObject data=new DataObject();
                data.SetText(item.Content,TextDataFormat.UnicodeText);
                if(!String.IsNullOrEmpty(item.ContentRtf))data.SetData(DataFormats.Rtf,item.ContentRtf);
                data.SetData(DataFormats.Html,item.ContentHtml);
                Clipboard.SetDataObject(data,true,5,100);
                return;
            }
            if(!String.IsNullOrEmpty(item.ContentRtf)){
                using(RichTextBox box=new RichTextBox()){
                    Load(box,item.ContentRtf,item.Content);
                    box.SelectAll();
                    box.Copy();
                    return;
                }
            }
            Clipboard.SetText(item.Content,TextDataFormat.UnicodeText);
        }
    }


    public class WheelRichBox : RichTextBox
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam);
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if(e.Delta==0)return;
            int lines=Math.Max(1,Math.Abs(e.Delta)/120)*3;
            if(e.Delta>0)lines=-lines;
            SendMessage(Handle,0xB6,IntPtr.Zero,new IntPtr(lines));
        }
    }

    public class RichEditBox : WheelRichBox
    {
        private bool pasting;
        private string html;
        public string Html
        {
            get{return html;}
            set{html=value;}
        }
        protected override void OnTextChanged(EventArgs e)
        {
            if(!pasting)html=null;
            base.OnTextChanged(e);
        }
        protected override void WndProc(ref Message m)
        {
            if(m.Msg==0x0302){
                string candidate=null;
                try{
                    bool all=TextLength==0 || (SelectionStart==0 && SelectionLength==TextLength);
                    if(all && Clipboard.ContainsData(DataFormats.Html)){
                        object content=Clipboard.GetData(DataFormats.Html);
                        if(content is string)candidate=(string)content;
                    }
                }catch{}
                pasting=true;
                try{base.WndProc(ref m);}finally{
                    pasting=false;
                    html=candidate;
                }
                return;
            }
            base.WndProc(ref m);
        }
    }

    public class RichTitle : RichTextBox
    {
        public RichTitle()
        {
            Multiline=false;ReadOnly=true;WordWrap=false;
            ScrollBars=RichTextBoxScrollBars.None;
            BorderStyle=BorderStyle.None;
            BackColor=Color.FromArgb(250,251,254);
            Font=new Font("IRANSans",10F,FontStyle.Regular);
            RightToLeft=RightToLeft.Yes;
            DetectUrls=false;TabStop=false;HideSelection=true;ShortcutsEnabled=false;
            Cursor=Cursors.Hand;
        }
    }
    public class NoBarCardList : Panel
    {
        private int scrollOffset;
        public NoBarCardList()
        {
            DoubleBuffered=true;
            BackColor=Color.FromArgb(246,248,252);
            AutoScroll=false;
            TabStop=true;
            AllowDrop=true;
            Resize+=delegate {LayoutCards();};
        }
        public void LayoutCards()
        {
            int n=Controls.Count;
            int step=51;
            int maximum=Math.Max(0,n*step+7-ClientSize.Height);
            scrollOffset=Math.Max(0,Math.Min(scrollOffset,maximum));
            int width=Math.Max(130,ClientSize.Width-22);
            for(int i=0;i<n;i++){
                Control c=Controls[i];
                c.SetBounds(10,7+i*step-scrollOffset,width,c is SoftCard?46:70);
            }
        }
        public void ResetScroll(){scrollOffset=0;LayoutCards();}
        public void ScrollWheel(int delta)
        {
            int maximum=Math.Max(0,Controls.Count*51+7-ClientSize.Height);
            scrollOffset=Math.Max(0,Math.Min(maximum,scrollOffset-(delta/120)*117));
            LayoutCards();
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);ScrollWheel(e.Delta);
        }
    }




    public static class RichHighlight
    {
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential,
            CharSet=System.Runtime.InteropServices.CharSet.Unicode)]
        private struct CharFormat2
        {
            public uint cbSize,dwMask,dwEffects;
            public int yHeight,yOffset,crTextColor;
            public byte bCharSet,bPitchAndFamily;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr,SizeConst=32)]
            public string szFaceName;
            public ushort wWeight;
            public short sSpacing;
            public int crBackColor,lcid,dwReserved;
            public short sStyle;
            public ushort wKerning;
            public byte bUnderlineType,bAnimation,bRevAuthor,bUnderlineColor;
        }
        [System.Runtime.InteropServices.DllImport("user32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr h,int message,IntPtr w,ref CharFormat2 value);
        public static void Clear(RichTextBox editor)
        {
            CharFormat2 cf=new CharFormat2();
            cf.cbSize=(uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(CharFormat2));
            cf.dwMask=0x04000000;
            cf.dwEffects=0x04000000;
            cf.szFaceName="";
            if(SendMessage(editor.Handle,0x444,new IntPtr(1),ref cf)==IntPtr.Zero)
                editor.SelectionBackColor=editor.BackColor;
        }
    }

    public class ColorSwatchButton : SoftButton
    {
        public Color Swatch=Color.Black;
        public bool NoColor=false;
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Rectangle r=new Rectangle(5,Height-8,Math.Max(8,Width-10),3);
            using(Brush brush=new SolidBrush(NoColor?Color.FromArgb(214,221,229):Swatch))
                e.Graphics.FillRectangle(brush,r);
            if(NoColor) {
                using(Pen p=new Pen(Color.FromArgb(173,69,86),1.5F))
                    e.Graphics.DrawLine(p,r.Left,r.Bottom,r.Right,r.Top);
            }
        }
    }

    public class EditorDialog : Form
    {
        private RichEditBox titleBox,contentBox,active;
        private ContextMenuStrip titleContextMenu,contentContextMenu;
        private System.Windows.Forms.Timer statusTimer;
        private bool statusMessageActive;
        private Panel top;
        private Label titleLabel,bodyLabel,hint;
        private RoundedSearch titleSurface,bodySurface;
        private SoftButton save,cancel,close,btnB,btnI,btnU,inkArrow,highlightArrow,left,center,right,justify,linkButton;
        private ColorSwatchButton ink,highlight;
        private static Color savedInk=Color.Black,savedHighlight=Color.Empty;
        private NumericUpDown size,numberBox;
        private ComboBox categoryBox;
        private Label numberLabel;
        private readonly Size minSize=new Size(445,310);
        private readonly HashSet<int> reservedNumbers;
        private bool updating,categoryPlaceholder;
        private List<PromptLink> links=new List<PromptLink>();
        private string lastContentText="";
        public string EntryTitle {get{return titleBox.Text.Trim();}}
        public string EntryContent {get{return contentBox.Text;}}
        public string EntryCategory {get{return categoryPlaceholder?"":categoryBox.Text.Trim();}}
        public int EntryNumber {get{return (int)numberBox.Value;}}
        public string EntryTitleRtf {get{return RichDocument.ReadRtf(titleBox);}}
        public string EntryContentRtf {get{return RichDocument.ReadRtf(contentBox);}}
        public string EntryTitleHtml {get{return titleBox.Html;}}
        public string EntryContentHtml {get{return contentBox.Html;}}
        public List<PromptLink> EntryLinks {get{return links.Select(l=>new PromptLink{Start=l.Start,Length=l.Length,Text=l.Text,Url=l.Url}).ToList();}}
        public EditorDialog(string title,string content):this(title,content,null,null,null,null,null,null,1,null,null){}
        public EditorDialog(string title,string content,string titleRtf,string contentRtf):this(title,content,titleRtf,contentRtf,null,null,null,null,1,null,null){}
        public EditorDialog(string title,string content,string titleRtf,string contentRtf,string titleHtml,string contentHtml):this(title,content,titleRtf,contentRtf,titleHtml,contentHtml,null,null,1,null,null){}
        public EditorDialog(string title,string content,string titleRtf,string contentRtf,string titleHtml,string contentHtml,string category,IEnumerable<string> knownCategories):this(title,content,titleRtf,contentRtf,titleHtml,contentHtml,category,knownCategories,1,null,null){}
        public EditorDialog(string title,string content,string titleRtf,string contentRtf,string titleHtml,string contentHtml,string category,IEnumerable<string> knownCategories,int number,IEnumerable<int> usedNumbers):this(title,content,titleRtf,contentRtf,titleHtml,contentHtml,category,knownCategories,number,usedNumbers,null){}
        public EditorDialog(string title,string content,string titleRtf,string contentRtf,string titleHtml,string contentHtml,string category,IEnumerable<string> knownCategories,int number,IEnumerable<int> usedNumbers,IEnumerable<PromptLink> existingLinks)
        {
            reservedNumbers=new HashSet<int>(usedNumbers??Enumerable.Empty<int>());
            Text=String.IsNullOrEmpty(title)?"New Text":"Edit Text";
            Font=new Font("IRANSans",9F);
            ForeColor=Color.FromArgb(40,56,65);BackColor=Color.FromArgb(252,253,254);
            RightToLeft=RightToLeft.No;
            StartPosition=FormStartPosition.CenterParent;
            FormBorderStyle=FormBorderStyle.None;
            MinimumSize=minSize;
            Size=UiSizeMemory.Load("editor",new Size(445,370),minSize);
            ShowInTaskbar=false;
            Icon=PromptWindow.TryIcon();DoubleBuffered=true;
            top=new Panel {BackColor=Color.White,Height=48,Cursor=Cursors.SizeAll};
            top.MouseDown+=delegate(object sender,MouseEventArgs e){StartMove(e);};
            close=Make("×"); close.Click+=delegate{DialogResult=DialogResult.Cancel;Close();};
            btnB=Make("B");btnI=Make("I");btnU=Make("U");
            justify=Make("☷");linkButton=Make("↗");
            btnB.Font=new Font("Segoe UI",10F,FontStyle.Bold);
            btnI.Font=new Font("Segoe UI",10F,FontStyle.Italic);
            btnU.Font=new Font("Segoe UI",10F,FontStyle.Underline);
            ink=new ColorSwatchButton {Text="A",Radius=8,Fill=Color.FromArgb(245,247,251),
                HoverFill=Color.FromArgb(230,237,242),ForeColor=Color.FromArgb(48,69,83),
                Font=new Font("Segoe UI",10F,FontStyle.Bold),Swatch=savedInk};
            highlight=new ColorSwatchButton {Text="▤",Radius=8,Fill=Color.FromArgb(245,247,251),
                HoverFill=Color.FromArgb(230,237,242),ForeColor=Color.FromArgb(48,69,83),
                Font=new Font("Segoe UI",10F),Swatch=savedHighlight,NoColor=savedHighlight.IsEmpty};
            inkArrow=Make("▾");highlightArrow=Make("▾");
            inkArrow.Font=new Font("Segoe UI",8F);
            highlightArrow.Font=new Font("Segoe UI",8F);
            ink.ForeColor=Color.FromArgb(51,96,140);
            left=Make("≡");center=Make("☰");right=Make("≡");
            size=new NumericUpDown {Minimum=7,Maximum=72,Value=10,DecimalPlaces=0,TextAlign=HorizontalAlignment.Center,
                Font=new Font("Segoe UI",9F),BorderStyle=BorderStyle.FixedSingle,BackColor=Color.FromArgb(248,250,252)};
            top.Controls.AddRange(new Control[] {close,btnB,btnI,btnU,ink,inkArrow,highlight,highlightArrow,left,center,right,justify,linkButton,size});
            Controls.Add(top);
            titleLabel=new Label {Text="Title",ForeColor=Color.FromArgb(70,91,103),Font=new Font("IRANSans",9F,FontStyle.Bold),TextAlign=ContentAlignment.MiddleLeft,RightToLeft=RightToLeft.No};
            bodyLabel=new Label {Text="Content",ForeColor=Color.FromArgb(70,91,103),Font=new Font("IRANSans",9F,FontStyle.Bold),TextAlign=ContentAlignment.MiddleLeft,RightToLeft=RightToLeft.No};
            titleSurface=new RoundedSearch {BackColor=Color.White};
            bodySurface=new RoundedSearch {BackColor=Color.White};
            titleBox=MakeRich(false);
            contentBox=MakeRich(true);
            RichDocument.Load(titleBox,titleRtf,title);
            RichDocument.Load(contentBox,contentRtf,content);
            titleBox.Html=titleHtml;
            contentBox.Html=contentHtml;
            links=(existingLinks??Enumerable.Empty<PromptLink>())
                .Where(l=>l!=null && l.Start>=0 && l.Length>0 && l.Start+l.Length<=(contentBox.Text??"").Length)
                .Select(l=>new PromptLink{Start=l.Start,Length=l.Length,Text=l.Text,Url=l.Url}).ToList();
            lastContentText=contentBox.Text??"";
            titleSurface.Controls.Add(titleBox);bodySurface.Controls.Add(contentBox);
            save=Make("Save");cancel=Make("Cancel");
            save.Fill=Color.FromArgb(40,68,84);save.HoverFill=Color.FromArgb(30,54,69);save.ForeColor=Color.White;
            save.Font=new Font("IRANSans",9F,FontStyle.Bold);
            cancel.Font=new Font("IRANSans",9F);
            hint=new Label {Text="",ForeColor=Color.FromArgb(181,57,75),Font=new Font("IRANSans",8F),TextAlign=ContentAlignment.MiddleRight,RightToLeft=RightToLeft.Yes};
            statusTimer=new System.Windows.Forms.Timer {Interval=1700};
            statusTimer.Tick+=delegate{
                statusTimer.Stop();
                if(statusMessageActive){statusMessageActive=false;hint.Text="";}
            };
            titleContextMenu=BuildTextContextMenu(titleBox);
            contentContextMenu=BuildTextContextMenu(contentBox);
            titleBox.ContextMenuStrip=titleContextMenu;
            contentBox.ContextMenuStrip=contentContextMenu;
            Controls.Add(titleLabel);Controls.Add(titleSurface);Controls.Add(bodyLabel);Controls.Add(bodySurface);
            numberLabel=new Label {Text="Number",Font=new Font("Segoe UI",8.5F,FontStyle.Bold),
                ForeColor=Color.FromArgb(108,123,136),TextAlign=ContentAlignment.MiddleLeft,RightToLeft=RightToLeft.No};
            numberBox=new NumericUpDown {Minimum=1,Maximum=999999999,Value=Math.Max(1,Math.Min(999999999,number)),
                Font=new Font("Segoe UI",9F),TextAlign=HorizontalAlignment.Center,BorderStyle=BorderStyle.FixedSingle,
                BackColor=Color.FromArgb(245,247,252),ThousandsSeparator=false};
            Controls.Add(numberLabel);Controls.Add(numberBox);
            categoryBox=new ComboBox {DropDownStyle=ComboBoxStyle.DropDown,
                AutoCompleteMode=AutoCompleteMode.SuggestAppend,AutoCompleteSource=AutoCompleteSource.ListItems,
                Font=new Font("IRANSans",9F),RightToLeft=RightToLeft.Yes,FlatStyle=FlatStyle.Flat};
            if(knownCategories!=null){
                foreach(string name in knownCategories.Where(x=>!String.IsNullOrWhiteSpace(x))
                    .Select(x=>x.Trim()).Distinct(StringComparer.CurrentCultureIgnoreCase)
                    .OrderBy(x=>x,StringComparer.CurrentCultureIgnoreCase))categoryBox.Items.Add(name);
            }
            categoryBox.Text=category??"";
            categoryBox.Enter+=delegate{
                if(categoryPlaceholder){categoryBox.Text="";categoryBox.ForeColor=Color.FromArgb(36,56,67);categoryPlaceholder=false;}
            };
            categoryBox.Leave+=delegate{
                if(String.IsNullOrWhiteSpace(categoryBox.Text))ShowCategoryPlaceholder();
            };
            categoryBox.SelectionChangeCommitted+=delegate{categoryPlaceholder=false;categoryBox.ForeColor=Color.FromArgb(36,56,67);};
            if(String.IsNullOrWhiteSpace(categoryBox.Text))ShowCategoryPlaceholder();
            Controls.Add(categoryBox);
            Controls.Add(hint);Controls.Add(save);Controls.Add(cancel);
            save.Click+=delegate{
                if(EntryTitle.Length==0){
                    hint.Text="Please enter a title before saving."; titleBox.Focus();return;
                }
                if(reservedNumbers.Contains(EntryNumber)){
                    hint.Text="This number is already in use. Choose another number.";numberBox.Focus();return;
                }
                DialogResult=DialogResult.OK;Close();
            };
            cancel.Click+=delegate{DialogResult=DialogResult.Cancel;Close();};
            titleBox.TextChanged+=delegate {if(statusMessageActive){statusMessageActive=false;statusTimer.Stop();}hint.Text="";};
            AcceptButton=save;CancelButton=cancel;
            titleBox.Enter+=delegate {active=titleBox;UpdateSelection();};
            contentBox.Enter+=delegate {active=contentBox;UpdateSelection();};
            contentBox.TextChanged+=delegate {TrackLinkEdits();};
            contentBox.MouseUp+=delegate(object sender,MouseEventArgs e){
                if(e.Button!=MouseButtons.Left || (ModifierKeys & Keys.Control)==0)return;
                int index=contentBox.GetCharIndexFromPosition(e.Location);
                PromptLink link=links.FirstOrDefault(l=>index>=l.Start && index<l.Start+l.Length);
                if(link!=null)RichDocument.OpenLink(link.Url);
            };
            titleBox.SelectionChanged+=delegate {if(active==titleBox)UpdateSelection();};
            contentBox.SelectionChanged+=delegate {if(active==contentBox)UpdateSelection();};
            active=contentBox;
            btnB.Click+=delegate {Toggle(FontStyle.Bold);};
            btnI.Click+=delegate {Toggle(FontStyle.Italic);};
            btnU.Click+=delegate {Toggle(FontStyle.Underline);};
            ink.Click+=delegate {ApplyCurrentColor(false);};
            highlight.Click+=delegate {ApplyCurrentColor(true);};
            inkArrow.Click+=delegate {ShowColorPalette(false,inkArrow);};
            highlightArrow.Click+=delegate {ShowColorPalette(true,highlightArrow);};
            left.Click+=delegate {Align(HorizontalAlignment.Left);};
            center.Click+=delegate {Align(HorizontalAlignment.Center);};
            right.Click+=delegate {Align(HorizontalAlignment.Right);};
            justify.Click+=delegate {AlignJustified();};
            linkButton.Click+=delegate {InsertLink();};
            contentBox.LinkClicked+=delegate(object sender,LinkClickedEventArgs e){RichDocument.OpenLink(e.LinkText);};
            size.ValueChanged+=delegate {
                if(updating)return;
                if(active==null)return;
                Font baseFont=active.SelectionFont??active.Font;
                active.Html=null;active.SelectionFont=new Font(baseFont.FontFamily,(float)size.Value,baseFont.Style);
                active.Focus();
            };
            Resize+=delegate{Arrange();ApplyRounded();};
            ResizeEnd+=delegate{Remember();};
            Arrange();ApplyRounded();
            Shown+=delegate{titleBox.Focus();active=titleBox;};
        }
        private SoftButton Make(string name)
        {
            return new SoftButton {Text=name,Fill=Color.FromArgb(245,247,251),HoverFill=Color.FromArgb(230,237,242),Radius=8,
                ForeColor=Color.FromArgb(67,81,92),Font=new Font("Segoe UI",10F)};
        }
        private RichEditBox MakeRich(bool multi)
        {
            RichEditBox box=new RichEditBox {BorderStyle=BorderStyle.None,Multiline=multi,
                ScrollBars=RichTextBoxScrollBars.None,WordWrap=multi,AcceptsTab=true,
                DetectUrls=multi,RightToLeft=RightToLeft.Yes,HideSelection=false,BackColor=Color.FromArgb(245,247,252),
                ForeColor=Color.FromArgb(36,56,67),Font=new Font("IRANSans",10F)};
            return box;
        }
        private ContextMenuStrip BuildTextContextMenu(RichEditBox target)
        {
            ContextMenuStrip menu=new ContextMenuStrip {RightToLeft=RightToLeft.No,Font=new Font("Segoe UI",9.5F),BackColor=Color.White};
            ToolStripMenuItem cutItem=new ToolStripMenuItem("Cut") {ShortcutKeys=Keys.Control|Keys.X};
            ToolStripMenuItem copyItem=new ToolStripMenuItem("Copy") {ShortcutKeys=Keys.Control|Keys.C};
            ToolStripMenuItem pasteItem=new ToolStripMenuItem("Paste") {ShortcutKeys=Keys.Control|Keys.V};
            ToolStripMenuItem selectAllItem=new ToolStripMenuItem("Select All") {ShortcutKeys=Keys.Control|Keys.A};
            menu.Items.AddRange(new ToolStripItem[]{cutItem,copyItem,pasteItem,selectAllItem});
            menu.Opening+=delegate{
                cutItem.Enabled=target.SelectionLength>0;
                copyItem.Enabled=target.SelectionLength>0;
                bool canPaste=false;
                try{canPaste=Clipboard.ContainsText()||Clipboard.ContainsData(DataFormats.Rtf)||Clipboard.ContainsData(DataFormats.Html);}catch{}
                pasteItem.Enabled=canPaste;
                selectAllItem.Enabled=target.TextLength>0;
            };
            cutItem.Click+=delegate{
                try{if(target.SelectionLength>0){target.Cut();ShowStatus("Cut!");}else ShowStatus("Nothing selected!");}
                catch{ShowStatus("Cut failed!");}
            };
            copyItem.Click+=delegate{
                try{if(target.SelectionLength>0){target.Copy();ShowStatus("Copied!");}else ShowStatus("Nothing selected!");}
                catch{ShowStatus("Copy failed!");}
            };
            pasteItem.Click+=delegate{
                try{
                    bool canPaste=Clipboard.ContainsText()||Clipboard.ContainsData(DataFormats.Rtf)||Clipboard.ContainsData(DataFormats.Html);
                    if(canPaste){target.Paste();ShowStatus("Pasted!");}else ShowStatus("Nothing to paste!");
                }catch{ShowStatus("Paste failed!");}
            };
            selectAllItem.Click+=delegate{
                target.SelectAll();
                ShowStatus("Selected all!");
            };
            return menu;
        }
        private void ShowStatus(string message)
        {
            if(hint==null)return;
            statusMessageActive=true;
            hint.ForeColor=Color.FromArgb(210,35,48);
            hint.Text=message.EndsWith("!",StringComparison.Ordinal)?message:message+"!";
            hint.BringToFront();
            hint.Refresh();
            statusTimer.Stop();
            statusTimer.Start();
        }
        private void Toggle(FontStyle flag)
        {
            if(active==null)return;
            Font current=active.SelectionFont??active.Font;
            FontStyle next=current.Style^flag;
            active.Html=null;active.SelectionFont=new Font(current.FontFamily,current.Size,next);
            active.Focus();UpdateSelection();
        }
        private void ShowCategoryPlaceholder()
        {
            categoryPlaceholder=true;
            categoryBox.Text="Category";
            categoryBox.ForeColor=Color.FromArgb(145,155,166);
        }
        private void TrackLinkEdits()
        {
            if(contentBox==null)return;
            string current=contentBox.Text??"";
            string previous=lastContentText??"";
            if(String.Equals(current,previous,StringComparison.Ordinal))return;
            int prefix=0;
            while(prefix<previous.Length && prefix<current.Length && previous[prefix]==current[prefix])prefix++;
            int oldEnd=previous.Length,newEnd=current.Length;
            while(oldEnd>prefix && newEnd>prefix && previous[oldEnd-1]==current[newEnd-1]){oldEnd--;newEnd--;}
            int delta=newEnd-oldEnd;
            for(int i=links.Count-1;i>=0;i--){
                PromptLink link=links[i];
                int linkEnd=link.Start+link.Length;
                if(linkEnd<=prefix)continue;
                if(link.Start>=oldEnd)link.Start+=delta;
                else links.RemoveAt(i);
            }
            lastContentText=current;
        }
        private void InsertLink()
        {
            if(active!=contentBox){active=contentBox;contentBox.Focus();}
            string selected=contentBox.SelectedText;
            using(HyperlinkDialog dlg=new HyperlinkDialog(selected)){
                if(dlg.ShowDialog(this)!=DialogResult.OK)return;
                int start=contentBox.SelectionStart;
                contentBox.SelectedText=dlg.LinkText;
                contentBox.Select(start,dlg.LinkText.Length);
                contentBox.SelectionColor=Color.FromArgb(28,116,192);
                Font current=contentBox.SelectionFont??contentBox.Font;
                contentBox.SelectionFont=new Font(current.FontFamily,current.Size,current.Style|FontStyle.Underline);
                contentBox.Select(start+dlg.LinkText.Length,0);
                links.RemoveAll(l=>l.Start<start+dlg.LinkText.Length && l.Start+l.Length>start);
                links.Add(new PromptLink{Start=start,Length=dlg.LinkText.Length,Text=dlg.LinkText,Url=dlg.LinkUrl});
                lastContentText=contentBox.Text??"";
                contentBox.Focus();
            }
        }
        private void ApplyCurrentColor(bool background)
        {
            if(active==null)return;
            active.Html=null;
            if(background){
                // RichEdit treats an empty color as "clear highlight".
                if(savedHighlight.IsEmpty)RichHighlight.Clear(active);
                else active.SelectionBackColor=savedHighlight;
            }else active.SelectionColor=savedInk;
            active.Focus();
        }
        private void SelectPaletteColor(bool background,Color color)
        {
            if(background){
                savedHighlight=color;
                highlight.Swatch=color;
                highlight.NoColor=color.IsEmpty;
                highlight.Invalidate();
            }else{
                savedInk=color;
                ink.Swatch=color;
                ink.NoColor=false;
                ink.Invalidate();
            }
            ApplyCurrentColor(background);
        }
        private void ShowColorPalette(bool background,Control anchor)
        {
            ToolStripDropDown pop=new ToolStripDropDown {AutoClose=true,Padding=new Padding(4)};
            Panel panel=new Panel {Size=new Size(218,183),BackColor=Color.White};
            Label caption=new Label {Text=background?"Highlight color":"Text color",Font=new Font("IRANSans",9F,FontStyle.Bold),
                TextAlign=ContentAlignment.MiddleRight,RightToLeft=RightToLeft.Yes};
            caption.SetBounds(8,4,202,21);panel.Controls.Add(caption);
            Color[] colors=new Color[]{
                Color.Black,Color.FromArgb(75,85,99),Color.FromArgb(125,139,154),
                Color.White,Color.FromArgb(161,37,46),Color.Red,
                Color.FromArgb(255,123,66),Color.FromArgb(255,184,54),
                Color.FromArgb(255,238,73),Color.FromArgb(69,160,81),
                Color.FromArgb(28,133,126),Color.FromArgb(28,116,192),
                Color.FromArgb(59,83,207),Color.FromArgb(135,74,193),
                Color.FromArgb(211,77,155),Color.FromArgb(255,205,213),
                Color.FromArgb(195,229,255),Color.FromArgb(204,243,208)
            };
            ToolTip paletteTips=new ToolTip();
            for(int i=0;i<colors.Length;i++){
                Color swatch=colors[i];
                Button tile=new Button {FlatStyle=FlatStyle.Flat,BackColor=swatch,
                    Cursor=Cursors.Hand,TabStop=false};
                tile.FlatAppearance.BorderColor=Color.FromArgb(217,224,230);
                tile.FlatAppearance.BorderSize=1;
                tile.SetBounds(9+(i%9)*23,31+(i/9)*27,20,22);
                tile.Click+=delegate {SelectPaletteColor(background,swatch);pop.Close();};
                panel.Controls.Add(tile);
            }
            Button noColor=new Button {Text=background?"No color":"Black",FlatStyle=FlatStyle.Flat,
                ForeColor=Color.FromArgb(63,80,92),BackColor=Color.FromArgb(246,248,252),
                Font=new Font("IRANSans",8F),Cursor=Cursors.Hand};
            noColor.FlatAppearance.BorderSize=0;
            noColor.SetBounds(8,94,94,31);
            noColor.Click+=delegate {SelectPaletteColor(background,background?Color.Empty:Color.Black);pop.Close();};
            panel.Controls.Add(noColor);
            Button extra=new Button {Text="Custom color...",FlatStyle=FlatStyle.Flat,
                ForeColor=Color.FromArgb(63,80,92),BackColor=Color.FromArgb(240,245,249),
                Font=new Font("IRANSans",8F),Cursor=Cursors.Hand};
            extra.FlatAppearance.BorderSize=0;
            extra.SetBounds(108,94,102,31);
            extra.Click+=delegate {
                using(ColorDialog dialog=new ColorDialog {FullOpen=true,
                    Color=background?(savedHighlight.IsEmpty?Color.Yellow:savedHighlight):savedInk}){
                    if(dialog.ShowDialog(this)==DialogResult.OK)SelectPaletteColor(background,dialog.Color);
                }
                pop.Close();
            };
            panel.Controls.Add(extra);
            Label note=new Label {Text=background?"Click a color to apply highlight.":"Click a color to apply text color.",
                Font=new Font("IRANSans",7.5F),ForeColor=Color.FromArgb(119,135,145),
                TextAlign=ContentAlignment.MiddleRight,RightToLeft=RightToLeft.Yes};
            note.SetBounds(6,134,204,33);
            panel.Controls.Add(note);
            ToolStripControlHost host=new ToolStripControlHost(panel){AutoSize=false,Size=panel.Size,Margin=Padding.Empty,Padding=Padding.Empty};
            pop.Items.Add(host);
            pop.Closed+=delegate {
                if(!IsDisposed && IsHandleCreated) {
                    try {
                        BeginInvoke((MethodInvoker)delegate {
                            if(!pop.IsDisposed)pop.Dispose();
                            paletteTips.Dispose();
                        });
                    }catch {paletteTips.Dispose();}
                }else paletteTips.Dispose();
            };
            pop.Show(anchor,new Point(0,anchor.Height));
        }
        private void Align(HorizontalAlignment alignment)
        {
            if(active==null)return;
            active.Html=null;active.SelectionAlignment=alignment;active.Focus();
        }
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct ParaFormat2
        {
            public uint cbSize,dwMask;
            public ushort wNumbering,wEffects;
            public int dxStartIndent,dxRightIndent,dxOffset;
            public ushort wAlignment;
            public short cTabCount;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValArray,SizeConst=32)]
            public int[] rgxTabs;
            public int dySpaceBefore,dySpaceAfter,dyLineSpacing;
            public short sStyle;
            public byte bLineSpacingRule,bOutlineLevel;
            public ushort wShadingWeight,wShadingStyle,wNumberingStart,wNumberingStyle,wNumberingTab,wBorderSpace,wBorderWidth,wBorders;
        }
        [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="SendMessageW",CharSet=System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr SendParaFormat(IntPtr hwnd,int message,IntPtr wParam,ref ParaFormat2 value);
        private void AlignJustified()
        {
            if(active==null)return;
            active.Html=null;
            ParaFormat2 format=new ParaFormat2();
            format.cbSize=(uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(ParaFormat2));
            format.dwMask=0x00000008;
            format.wAlignment=4;
            format.rgxTabs=new int[32];
            SendParaFormat(active.Handle,0x0447,new IntPtr(1),ref format);
            active.Focus();
        }
        private void UpdateSelection()
        {
            if(active==null||updating)return;
            updating=true;
            try {
                Font current=active.SelectionFont??active.Font;
                decimal selected=(decimal)Math.Max((float)size.Minimum,Math.Min((float)size.Maximum,current.Size));
                size.Value=selected;
                btnB.Fill=(current.Style & FontStyle.Bold)!=0?Color.FromArgb(220,238,241):Color.FromArgb(245,247,251);
                btnI.Fill=(current.Style & FontStyle.Italic)!=0?Color.FromArgb(220,238,241):Color.FromArgb(245,247,251);
                btnU.Fill=(current.Style & FontStyle.Underline)!=0?Color.FromArgb(220,238,241):Color.FromArgb(245,247,251);
                foreach(var b in new SoftButton[]{btnB,btnI,btnU})b.Invalidate();
            }finally{updating=false;}
        }
        private void Arrange()
        {
            int w=ClientSize.Width,h=ClientSize.Height;
            top.SetBounds(0,0,w,48);
            close.SetBounds(8,9,27,28);
            btnB.SetBounds(41,9,27,28);btnI.SetBounds(70,9,27,28);btnU.SetBounds(99,9,27,28);
            ink.SetBounds(128,9,27,28);inkArrow.SetBounds(156,9,17,28);
            highlight.SetBounds(178,9,27,28);highlightArrow.SetBounds(206,9,17,28);
            left.SetBounds(226,9,27,28);center.SetBounds(255,9,27,28);right.SetBounds(284,9,27,28);
            justify.SetBounds(313,9,27,28);linkButton.SetBounds(342,9,27,28);
            size.SetBounds(373,11,60,25);
            titleLabel.SetBounds(17,52,Math.Max(120,w-145),21);
            numberLabel.SetBounds(w-110,52,90,21);
            titleSurface.SetBounds(17,75,Math.Max(120,w-145),37);
            titleBox.SetBounds(9,8,titleSurface.Width-18,23);
            numberBox.SetBounds(w-110,81,90,25);
            bodyLabel.SetBounds(17,118,w-34,21);
            int bh=Math.Max(78,h-221);
            bodySurface.SetBounds(17,141,w-34,bh);
            contentBox.SetBounds(10,8,bodySurface.Width-20,Math.Max(65,bh-17));
            categoryBox.SetBounds(17,h-43,Math.Max(110,w-248),29);
            hint.SetBounds(17,h-73,Math.Max(120,w-34),18);
            save.SetBounds(w-115,h-43,95,32);
            cancel.SetBounds(w-215,h-43,95,32);
        }
        private void Remember(){if(WindowState==FormWindowState.Normal)UiSizeMemory.Save("editor",Size,MinimumSize);}
        protected override void OnFormClosing(FormClosingEventArgs e){Remember();base.OnFormClosing(e);}
        protected override void Dispose(bool disposing){
            if(disposing){
                if(statusTimer!=null){statusTimer.Stop();statusTimer.Dispose();statusTimer=null;}
                if(titleContextMenu!=null){titleContextMenu.Dispose();titleContextMenu=null;}
                if(contentContextMenu!=null){contentContextMenu.Dispose();contentContextMenu=null;}
            }
            base.Dispose(disposing);
        }
        protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);ApplyRounded();}
        private void ApplyRounded(){
            if(Width<35||Height<35)return;            using(var p=RoundedSearch.MakePath(new Rectangle(0,0,Width-1,Height-1),18)){
                Region old=Region;Region=new Region(p);if(old!=null)old.Dispose();
            }
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")]private static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll")]private static extern IntPtr SendMessage(IntPtr h,int msg,IntPtr a,IntPtr b);
        private void StartMove(MouseEventArgs e){
            if(e.Button!=MouseButtons.Left)return;
            ReleaseCapture();SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero);
        }
        protected override void WndProc(ref Message m){
            if(m.Msg==0x84&&WindowState==FormWindowState.Normal){
                long v=m.LParam.ToInt64();
                Point p=PointToClient(new Point((short)(v&65535),(short)((v>>16)&65535)));
                bool l=p.X<9,r=p.X>=Width-9,t=p.Y<9,b=p.Y>=Height-9;
                int hit=0;
                if(l&&t)hit=13;else if(r&&t)hit=14;else if(l&&b)hit=16;else if(r&&b)hit=17;
                else if(l)hit=10;else if(r)hit=11;else if(t)hit=12;else if(b)hit=15;
                if(hit!=0){m.Result=(IntPtr)hit;return;}
            }
            base.WndProc(ref m);
        }
    }


    public class HyperlinkDialog : Form
    {
        private readonly TextBox labelBox,urlBox;
        public string LinkText {get;private set;}
        public string LinkUrl {get;private set;}
        public HyperlinkDialog(string selectedText)
        {
            Text="Insert Link";
            Font=new Font("Segoe UI",9F);
            BackColor=Color.FromArgb(250,252,254);
            FormBorderStyle=FormBorderStyle.FixedDialog;
            StartPosition=FormStartPosition.CenterParent;
            ShowInTaskbar=false;MinimizeBox=false;MaximizeBox=false;
            Size=new Size(410,220);Icon=PromptWindow.TryIcon();
            Label labelCaption=new Label {Text="Link text",AutoSize=true,ForeColor=Color.FromArgb(70,91,103)};
            labelCaption.SetBounds(18,16,100,20);
            labelBox=new TextBox {Text=selectedText??"",Font=new Font("Segoe UI",9.5F)};
            labelBox.SetBounds(18,38,358,27);
            Label urlCaption=new Label {Text="URL",AutoSize=true,ForeColor=Color.FromArgb(70,91,103)};
            urlCaption.SetBounds(18,75,100,20);
            urlBox=new TextBox {Font=new Font("Segoe UI",9.5F)};
            urlBox.SetBounds(18,97,358,27);
            SoftButton save=new SoftButton {Text="Insert",Fill=Color.FromArgb(40,68,84),HoverFill=Color.FromArgb(30,54,69),ForeColor=Color.White,Radius=8,Font=new Font("Segoe UI",9F,FontStyle.Bold)};
            SoftButton cancel=new SoftButton {Text="Cancel",Radius=8};
            save.SetBounds(272,145,104,31);cancel.SetBounds(160,145,104,31);
            Controls.AddRange(new Control[]{labelCaption,labelBox,urlCaption,urlBox,save,cancel});
            save.Click+=delegate{
                string text=labelBox.Text.Trim();
                string target=urlBox.Text.Trim();
                if(text.Length==0){MessageBox.Show(this,"Enter the link text.","QuickText",MessageBoxButtons.OK,MessageBoxIcon.Information);labelBox.Focus();return;}
                if(target.Length==0){MessageBox.Show(this,"Enter a URL.","QuickText",MessageBoxButtons.OK,MessageBoxIcon.Information);urlBox.Focus();return;}
                Uri uri;
                if(!target.Contains("://") && !target.StartsWith("mailto:",StringComparison.OrdinalIgnoreCase))target="https://"+target;
                if(!Uri.TryCreate(target,UriKind.Absolute,out uri) || (uri.Scheme!="http" && uri.Scheme!="https" && uri.Scheme!="ftp" && uri.Scheme!="mailto")){
                    MessageBox.Show(this,"Use a valid web, FTP or email link.","QuickText",MessageBoxButtons.OK,MessageBoxIcon.Warning);urlBox.Focus();return;
                }
                LinkText=text;LinkUrl=uri.AbsoluteUri;DialogResult=DialogResult.OK;Close();
            };
            cancel.Click+=delegate{DialogResult=DialogResult.Cancel;Close();};
            AcceptButton=save;CancelButton=cancel;
        }
    }

    public class ViewerDialog : Form
    {
        private readonly PromptItem item;
        private RichTextBox title,body;
        private Panel top;
        private RoundedFramePanel titleFrame,bodyFrame;
        private Label numberBadge;
        private SoftButton close,copy;
        private ContextMenuStrip textContextMenu;
        private Label statusLabel;
        private System.Windows.Forms.Timer statusTimer;
        private readonly Size minSize=new Size(280,250);
        public ViewerDialog(PromptItem value)
        {
            item=value??new PromptItem();
            Text="View Text";
            Font=new Font("IRANSans",9F);
            BackColor=Color.FromArgb(252,253,254);
            FormBorderStyle=FormBorderStyle.None;
            StartPosition=FormStartPosition.CenterParent;
            MinimumSize=minSize;
            Size=LoadViewerSize(minSize);
            Icon=PromptWindow.TryIcon();
            ShowInTaskbar=false;
            statusTimer=new System.Windows.Forms.Timer {Interval=1700};
            statusTimer.Tick+=delegate{statusTimer.Stop();if(statusLabel!=null)statusLabel.Visible=false;};

            top=new Panel {BackColor=Color.White,Cursor=Cursors.SizeAll};
            top.MouseDown+=delegate(object sender,MouseEventArgs e){MoveWindow(e);};
            close=new SoftButton {Text="×",Radius=9,Fill=Color.FromArgb(245,247,251),ForeColor=Color.FromArgb(70,88,101)};
            close.Click+=delegate{Close();};
            copy=new SoftButton {Text="Copy",Radius=9,Fill=Color.FromArgb(227,241,238),ForeColor=Color.FromArgb(29,97,90),Font=new Font("IRANSans",9F)};
            copy.Click+=delegate {
                try{RichDocument.Copy(item);ShowStatus("Copied!");}catch{ShowStatus("Copy failed!");}
            };
            statusLabel=new Label {AutoEllipsis=true,TextAlign=ContentAlignment.MiddleCenter,
                ForeColor=Color.FromArgb(210,35,48),Font=new Font("Segoe UI",8.5F,FontStyle.Bold),Visible=false};
            top.Controls.Add(close);top.Controls.Add(copy);top.Controls.Add(statusLabel);

            numberBadge=new Label {
                Text="#"+item.Number.ToString(CultureInfo.InvariantCulture),
                AutoSize=false,TextAlign=ContentAlignment.MiddleCenter,
                BackColor=Color.FromArgb(237,243,250),ForeColor=Color.FromArgb(67,91,119),
                Font=new Font("Segoe UI",8.5F,FontStyle.Bold),BorderStyle=BorderStyle.FixedSingle
            };
            titleFrame=new RoundedFramePanel {FillColor=Color.White,BorderColor=Color.FromArgb(210,220,230),CornerRadius=12};
            bodyFrame=new RoundedFramePanel {FillColor=Color.White,BorderColor=Color.FromArgb(210,220,230),CornerRadius=12};

            title=new RichTextBox {
                ReadOnly=true,Multiline=true,BorderStyle=BorderStyle.None,
                ScrollBars=RichTextBoxScrollBars.None,WordWrap=true,
                BackColor=Color.White,RightToLeft=RightToLeft.Yes,
                Font=new Font("IRANSans",10.5F,FontStyle.Bold),TabStop=true,DetectUrls=false,
                Cursor=Cursors.Arrow,ShortcutsEnabled=true
            };
            body=new WheelRichBox {
                ReadOnly=true,BorderStyle=BorderStyle.None,
                ScrollBars=RichTextBoxScrollBars.Vertical,WordWrap=true,
                BackColor=Color.White,RightToLeft=RightToLeft.Yes,
                Font=new Font("IRANSans",9.5F),DetectUrls=true,
                Cursor=Cursors.Arrow,TabStop=true,ShortcutsEnabled=true
            };
            body.LinkClicked+=delegate(object sender,LinkClickedEventArgs e){RichDocument.OpenLink(e.LinkText);};
            RichDocument.Load(title,item.TitleRtf,item.Title);
            RichDocument.Load(body,item.ContentRtf,item.Content);

            textContextMenu=new ContextMenuStrip {RightToLeft=RightToLeft.No,Font=new Font("Segoe UI",9.5F),BackColor=Color.White};
            ToolStripMenuItem cutItem=new ToolStripMenuItem("Cut") {Enabled=false,ShortcutKeys=Keys.Control|Keys.X};
            ToolStripMenuItem copyItem=new ToolStripMenuItem("Copy") {Enabled=false,ShortcutKeys=Keys.Control|Keys.C};
            ToolStripMenuItem pasteItem=new ToolStripMenuItem("Paste") {Enabled=false,ShortcutKeys=Keys.Control|Keys.V};
            ToolStripMenuItem selectAllItem=new ToolStripMenuItem("Select All") {Enabled=true,ShortcutKeys=Keys.Control|Keys.A};
            textContextMenu.Items.AddRange(new ToolStripItem[]{cutItem,copyItem,pasteItem,selectAllItem});
            textContextMenu.Opening+=delegate{
                RichTextBox active=textContextMenu.SourceControl as RichTextBox;
                cutItem.Enabled=false;
                pasteItem.Enabled=false;
                copyItem.Enabled=active!=null && active.SelectionLength>0;
                selectAllItem.Enabled=active!=null && active.TextLength>0;
            };
            copyItem.Click+=delegate{
                RichTextBox active=textContextMenu.SourceControl as RichTextBox;
                try{if(active!=null && active.SelectionLength>0){active.Copy();ShowStatus("Copied!");}else ShowStatus("Nothing selected!");}
                catch{ShowStatus("Copy failed!");}
            };
            selectAllItem.Click+=delegate{
                RichTextBox active=textContextMenu.SourceControl as RichTextBox;
                if(active!=null){active.SelectAll();ShowStatus("Selected all!");}
            };
            title.ContextMenuStrip=textContextMenu;
            body.ContextMenuStrip=textContextMenu;

            body.MouseUp+=delegate(object sender,MouseEventArgs e){
                if(e.Button!=MouseButtons.Left)return;
                int index=body.GetCharIndexFromPosition(e.Location);
                PromptLink link=(item.Links??new List<PromptLink>()).FirstOrDefault(l=>index>=l.Start && index<l.Start+l.Length);
                if(link!=null)RichDocument.OpenLink(link.Url);
            };

            titleFrame.Controls.Add(title);
            bodyFrame.Controls.Add(body);
            Controls.Add(top);
            Controls.Add(numberBadge);
            Controls.Add(titleFrame);
            Controls.Add(bodyFrame);

            Resize+=delegate{Arrange();Round();};
            ResizeEnd+=delegate{Remember();};
            Arrange();Round();
        }
        private void ShowStatus(string message)
        {
            if(statusLabel==null)return;
            statusLabel.Text=message.EndsWith("!",StringComparison.Ordinal)?message:message+"!";
            statusLabel.Visible=true;
            statusLabel.BringToFront();
            statusTimer.Stop();
            statusTimer.Start();
        }
        private static Size LoadViewerSize(Size minimum)
        {
            Size main=UiSizeMemory.Load("main",new Size(332,385),new Size(260,235));
            Size saved=UiSizeMemory.Load("viewer",main,minimum);
            int maxWidth=Math.Max(minimum.Width,main.Width);
            int maxHeight=Math.Max(minimum.Height,main.Height);
            return new Size(Math.Max(minimum.Width,Math.Min(saved.Width,maxWidth)),
                Math.Max(minimum.Height,Math.Min(saved.Height,maxHeight)));
        }
        private void Arrange()
        {
            int w=ClientSize.Width,h=ClientSize.Height;
            top.SetBounds(0,0,w,42);
            close.SetBounds(10,7,30,28);
            copy.SetBounds(w-96,7,82,28);
            statusLabel.SetBounds(46,7,Math.Max(24,w-156),28);
            numberBadge.SetBounds(14,49,54,22);
            titleFrame.SetBounds(14,77,Math.Max(100,w-28),55);
            title.SetBounds(8,6,Math.Max(50,titleFrame.ClientSize.Width-16),Math.Max(20,titleFrame.ClientSize.Height-12));
            bodyFrame.SetBounds(14,142,Math.Max(100,w-28),Math.Max(90,h-156));
            body.SetBounds(8,7,Math.Max(50,bodyFrame.ClientSize.Width-16),Math.Max(70,bodyFrame.ClientSize.Height-14));
        }
        private void Remember(){if(WindowState==FormWindowState.Normal)UiSizeMemory.Save("viewer",Size,MinimumSize);}
        protected override void OnFormClosing(FormClosingEventArgs e){Remember();base.OnFormClosing(e);}
        protected override void Dispose(bool disposing){
            if(disposing){
                if(textContextMenu!=null){textContextMenu.Dispose();textContextMenu=null;}
                if(statusTimer!=null){statusTimer.Stop();statusTimer.Dispose();statusTimer=null;}
            }
            base.Dispose(disposing);
        }
        protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);Round();}
        private void Round(){
            if(Width<35||Height<35)return;
            using(var p=RoundedSearch.MakePath(new Rectangle(0,0,Width-1,Height-1),18)){
                Region old=Region;Region=new Region(p);if(old!=null)old.Dispose();
            }
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")]private static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll")]private static extern IntPtr SendMessage(IntPtr h,int msg,IntPtr a,IntPtr b);
        private void MoveWindow(MouseEventArgs e){
            if(e.Button!=MouseButtons.Left)return;
            ReleaseCapture();SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero);
        }
        protected override void WndProc(ref Message m){
            if(m.Msg==0x84&&WindowState==FormWindowState.Normal){
                long v=m.LParam.ToInt64();
                Point p=PointToClient(new Point((short)(v&65535),(short)((v>>16)&65535)));
                bool l=p.X<9,r=p.X>=Width-9,t=p.Y<9,b=p.Y>=Height-9;
                int hit=0;
                if(l&&t)hit=13;else if(r&&t)hit=14;else if(l&&b)hit=16;else if(r&&b)hit=17;
                else if(l)hit=10;else if(r)hit=11;else if(t)hit=12;else if(b)hit=15;
                if(hit!=0){m.Result=(IntPtr)hit;return;}
            }
            base.WndProc(ref m);
        }
    }

    public class RoundedSearch : Panel
    {
        public RoundedSearch(){DoubleBuffered=true;SetStyle(ControlStyles.SupportsTransparentBackColor,true);}
        public static System.Drawing.Drawing2D.GraphicsPath MakePath(Rectangle r,int radius){
            var p=new System.Drawing.Drawing2D.GraphicsPath();
            int d=Math.Min(radius*2,Math.Min(r.Width,r.Height));
            p.AddArc(r.Left,r.Top,d,d,180,90);p.AddArc(r.Right-d,r.Top,d,d,270,90);
            p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.Left,r.Bottom-d,d,d,90,90);
            p.CloseFigure();return p;
        }
        protected override void OnPaintBackground(PaintEventArgs e){
            e.Graphics.Clear(Parent==null?Color.White:Parent.BackColor);
            e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using(var path=MakePath(new Rectangle(1,1,Width-3,Height-3),12)){
                using(var brush=new SolidBrush(Color.FromArgb(245,247,252)))e.Graphics.FillPath(brush,path);
                using(var pen=new Pen(Color.FromArgb(219,228,231)))e.Graphics.DrawPath(pen,path);
            }
        }
    }
    public class RoundedFramePanel : Panel
    {
        public Color FillColor {get;set;}
        public Color BorderColor {get;set;}
        public int CornerRadius {get;set;}
        public RoundedFramePanel(){
            DoubleBuffered=true;
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            FillColor=Color.White;
            BorderColor=Color.FromArgb(210,220,230);
            CornerRadius=12;
        }
        protected override void OnResize(EventArgs e){base.OnResize(e);ApplyRoundedRegion();}
        protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);ApplyRoundedRegion();}
        private void ApplyRoundedRegion(){
            if(Width<4||Height<4)return;
            using(GraphicsPath path=RoundedSearch.MakePath(new Rectangle(0,0,Width-1,Height-1),CornerRadius)){
                Region old=Region;
                Region=new Region(path);
                if(old!=null)old.Dispose();
            }
        }
        protected override void OnPaintBackground(PaintEventArgs e){
            e.Graphics.Clear(Parent==null?Color.White:Parent.BackColor);
            if(Width<4||Height<4)return;
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(GraphicsPath path=RoundedSearch.MakePath(new Rectangle(0,0,Width-1,Height-1),CornerRadius))
            using(Brush brush=new SolidBrush(FillColor))
            using(Pen pen=new Pen(BorderColor,1F)){
                e.Graphics.FillPath(brush,path);
                e.Graphics.DrawPath(pen,path);
            }
        }
    }
    public class OneLineTitle : Control
    {
        public OneLineTitle(){
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.SupportsTransparentBackColor,true);
            BackColor=Color.Transparent;
            Font=new Font("IRANSans",10F,FontStyle.Regular);
            ForeColor=Color.FromArgb(38,53,63);
            RightToLeft=RightToLeft.Yes;Cursor=Cursors.Hand;
        }
        protected override void OnPaint(PaintEventArgs e){
            TextFormatFlags flags=TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.Right|TextFormatFlags.RightToLeft|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,ForeColor,flags);
        }
        protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);Invalidate();}
    }

    public class CategoryChoice
    {
        public readonly string Category;
        public readonly int Kind; // 0 = all, 1 = uncategorized, 2 = category
        public CategoryChoice(int kind,string category) { Kind=kind;Category=category??""; }
        public string Key { get { return Kind.ToString()+":"+Category.ToLowerInvariant(); } }
        public override string ToString() { return Kind==0?"Category":Kind==1?"Uncategorized":Category; }
    }
    public class PromptWindow : Form
    {
        private readonly PromptStore store;
        private readonly NoBarCardList list;
        private readonly Panel header,footer;private readonly RoundedSearch searchShell;
        private readonly TextBox search;
        private readonly ComboBox categoryFilter;
        private bool updatingCategoryFilter;
        private readonly Label heading,glass;
        private readonly SoftButton topButton,minButton,closeButton,addButton,clearButton,optionsButton;
        private readonly ToolTip tips;
        private readonly Label copyStatusLabel;
        private readonly System.Windows.Forms.Timer copyStatusTimer;

        private readonly string pinSettingPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"QuickPrompt","keep-on-top.txt");
        private bool allowExit=false;private readonly Control[] resizeHandles=new Control[4];

        public PromptWindow(PromptStore source)
        {
            store=source;
            Text="QuickText";
            Icon=TryIcon();
            Font=new Font("IRANSans",9F);
            BackColor=Color.FromArgb(246,248,252);
            ForeColor=Color.FromArgb(29,39,61);
            RightToLeft=RightToLeft.No;
            FormBorderStyle=FormBorderStyle.None;
            MinimumSize=new Size(260,235);
            Size=UiSizeMemory.Load("main",new Size(332,385),MinimumSize);
            StartPosition=FormStartPosition.Manual;
            ShowInTaskbar=false;
            try { Icon=TryIcon(); } catch {}
            try { TopMost=File.Exists(pinSettingPath) && File.ReadAllText(pinSettingPath).Trim()=="1"; } catch {}
            tips=new ToolTip { InitialDelay=500,ReshowDelay=100 };
            header=new Panel { Dock=DockStyle.Top,Height=96,BackColor=Color.White };
            footer=new Panel { Dock=DockStyle.Bottom,Height=51,BackColor=Color.FromArgb(246,248,252) };
            list=new NoBarCardList { Dock=DockStyle.Fill,BackColor=Color.FromArgb(246,248,252),AllowDrop=true };
            list.Resize += delegate { ResizeCards(); };
            list.DragEnter += delegate(object sender,DragEventArgs e) { e.Effect=e.Data.GetDataPresent(typeof(PromptItem)) ? DragDropEffects.Move : DragDropEffects.None; };
            list.DragOver += delegate(object sender,DragEventArgs e) { e.Effect=e.Data.GetDataPresent(typeof(PromptItem)) ? DragDropEffects.Move : DragDropEffects.None; };
            list.DragDrop += delegate(object sender,DragEventArgs e) {
                PromptItem dragged=e.Data.GetData(typeof(PromptItem)) as PromptItem;
                if (dragged==null || store.Items.Count==0) return;
                var ordered=PromptLogic.Order(store.Items,"");
                PromptItem target=ordered[ordered.Count-1];
                if (target!=dragged) Reorder(dragged,target,true);
            };
            Panel frame=new Panel { Dock=DockStyle.Fill,Padding=new Padding(1),BackColor=Color.FromArgb(213,227,228) };
            frame.Controls.Add(list);frame.Controls.Add(header);Controls.Add(frame);
            heading=new Label { Text="Quick Text",Font=new Font("Segoe UI",16F,FontStyle.Bold),ForeColor=Color.FromArgb(37,45,71),TextAlign=ContentAlignment.MiddleRight,Cursor=Cursors.SizeAll };
            header.MouseDown+=BeginWindowMove;
            // Compact interface intentionally omits a heading.
            topButton=new SoftButton { Text="📌",Fill=Color.FromArgb(245,247,251),ForeColor=Color.FromArgb(92,99,122),Radius=9,Font=new Font("Segoe UI Emoji",11F) };
            minButton=new SoftButton { Text="−",Fill=Color.FromArgb(245,247,251),ForeColor=Color.FromArgb(72,81,105),Radius=9 };
            closeButton=new SoftButton { Text="×",Fill=Color.FromArgb(245,247,251),HoverFill=Color.FromArgb(255,226,229),ForeColor=Color.FromArgb(82,88,105),Radius=9 };
            optionsButton=new SoftButton {Text="⚙",Fill=Color.FromArgb(245,247,251),HoverFill=Color.FromArgb(230,237,242),ForeColor=Color.FromArgb(72,81,105),Radius=9,Font=new Font("Segoe UI Symbol",11F)};
            topButton.Click += delegate { ToggleTop(); };
            minButton.Click += delegate { HideToTray(); };
            closeButton.Click += delegate { HideToTray(); };
            optionsButton.Click += delegate { ShowOptions(); };
            header.Controls.Add(topButton); header.Controls.Add(minButton); header.Controls.Add(closeButton); header.Controls.Add(optionsButton);
            tips.SetToolTip(topButton,"Always on top");
            tips.SetToolTip(minButton,"Minimize to tray");
            tips.SetToolTip(closeButton,"Close to tray"); tips.SetToolTip(optionsButton,"Options");
            searchShell=new RoundedSearch { BackColor=Color.White };

            header.Controls.Add(searchShell);
            glass=new Label { Text="⌕",Font=new Font("Segoe UI Symbol",17F),ForeColor=Color.FromArgb(123,132,158),TextAlign=ContentAlignment.MiddleCenter };
            search=new TextBox { BorderStyle=BorderStyle.None,Font=new Font("Tahoma",10F),BackColor=Color.FromArgb(245,247,252),ForeColor=Color.FromArgb(35,44,68),RightToLeft=RightToLeft.No };
            search.TextChanged+=delegate { Rebuild(); clearButton.Visible=search.Text.Length>0; };
            clearButton=new SoftButton { Text="×",Fill=Color.FromArgb(245,247,252),HoverFill=Color.FromArgb(226,231,241),ForeColor=Color.FromArgb(112,120,143),Radius=8,Visible=false };
            clearButton.Click+=delegate { search.Clear(); search.Focus(); };
            searchShell.Controls.Add(search); searchShell.Controls.Add(clearButton); NativePlaceHolder(search,"Search");
            addButton=new SoftButton { Text="+  New Chat",Radius=9,Fill=Color.FromArgb(245,247,251),HoverFill=Color.FromArgb(227,233,239),ForeColor=Color.FromArgb(72,81,105),Font=new Font("Segoe UI",8.5F,FontStyle.Bold) };
            addButton.Click+=delegate { AddNew(); };
            header.Controls.Add(addButton);
            categoryFilter=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,FlatStyle=FlatStyle.Flat,
                RightToLeft=RightToLeft.No,Font=new Font("Segoe UI",9F),
                BackColor=Color.FromArgb(244,247,250),ForeColor=Color.FromArgb(52,71,82)};
            header.Controls.Add(categoryFilter);
            RefreshFilterChoices();
            categoryFilter.SelectedIndexChanged+=delegate {if(!updatingCategoryFilter)Rebuild();};
            tips.SetToolTip(addButton,"New Chat");
            copyStatusLabel=new Label {AutoEllipsis=true,TextAlign=ContentAlignment.MiddleCenter,
                ForeColor=Color.FromArgb(210,35,48),Font=new Font("Segoe UI",8.5F,FontStyle.Bold),
                BackColor=Color.Transparent,Visible=false};
            copyStatusTimer=new System.Windows.Forms.Timer {Interval=1800};
            copyStatusTimer.Tick+=delegate{copyStatusTimer.Stop();copyStatusLabel.Visible=false;};
            header.Controls.Add(copyStatusLabel);

            SetupResizeHandles(); Resize+=delegate { LayoutUi(); ResizeCards(); ApplyRoundedRegion(); };ResizeEnd+=delegate { RememberMainSize(); };
            LayoutUi(); UpdateTopButton(); Rebuild();ApplyRoundedRegion();
            // Search starts empty with no decorative placeholder.
        }
        public static Icon TryIcon()
        {
            try {
                string f=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"QuickText.ico");
                if (File.Exists(f)) return new Icon(f);
                f=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"QuickPrompt.ico");
                if (File.Exists(f)) return new Icon(f);
            } catch {}
            return SystemIcons.Application;
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll",CharSet=System.Runtime.InteropServices.CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd,int msg,IntPtr wParam,IntPtr lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd,int msg,IntPtr wParam,string text);
        private static void NativePlaceHolder(TextBox box,string value)
        {
            if (!box.IsHandleCreated) { IntPtr h=box.Handle; }
            SendMessage(box.Handle,0x1501,new IntPtr(1),value);
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hWnd,int attribute,ref int value,int size);
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);ApplyRoundedRegion();
        }
        private void ApplyRoundedRegion()
        {
            if(Width<35 || Height<35)return;
            using(var gp=RoundedSearch.MakePath(new Rectangle(0,0,Width-1,Height-1),17)){
                Region old=Region;Region=new Region(gp);if(old!=null)old.Dispose();
            }
            if(IsHandleCreated){
                try{int radius=2;DwmSetWindowAttribute(Handle,33,ref radius,4);}catch{}
            }
        }
        private void SetupResizeHandles()
        {
            for(int i=0;i<4;i++){
                int hit=(i==0)?13:(i==1)?14:(i==2)?16:17;
                Label grip=new Label {Size=new Size(16,16),BackColor=Color.Transparent,Cursor=(i==0 || i==3)?Cursors.SizeNWSE:Cursors.SizeNESW};
                if(i>=2)grip.Paint+=delegate(object sender,PaintEventArgs e){
                    e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    using(var p=new Pen(Color.FromArgb(143,168,174),1.2F)){
                        e.Graphics.DrawLine(p,7,13,13,7);
                        e.Graphics.DrawLine(p,10,13,13,10);
                    }
                };
                grip.MouseDown+=delegate(object sender,MouseEventArgs e){
                    if(e.Button!=MouseButtons.Left)return;
                    ReleaseCapture();
                    SendMessage(Handle,0xA1,new IntPtr(hit),IntPtr.Zero);
                };
                Controls.Add(grip);grip.BringToFront();resizeHandles[i]=grip;
            }
        }

        private void BeginWindowMove(object sender,MouseEventArgs e)
        {
            if (e.Button!=MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero);
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg==0x84 && WindowState==FormWindowState.Normal) {
                long v=m.LParam.ToInt64();Point p=PointToClient(new Point((short)(v & 0xFFFF),(short)((v>>16) & 0xFFFF))); int z=9;
                bool l=p.X<z,r=p.X>=Width-z,t=p.Y<z,b=p.Y>=Height-z;
                int hit=0;
                if (l&&t) hit=13; else if (r&&t) hit=14;
                else if (l&&b) hit=16; else if (r&&b) hit=17;
                else if (l) hit=10; else if (r) hit=11;
                else if (t) hit=12; else if (b) hit=15;
                if (hit!=0) { m.Result=(IntPtr)hit; return; }
            }
            base.WndProc(ref m);
        }

        private void LayoutUi()
        {
            int w=ClientSize.Width;
            const int buttonSize=27;
            int right=w-13;
            closeButton.SetBounds(right-buttonSize,10,buttonSize,buttonSize);
            minButton.SetBounds(right-buttonSize-31,10,buttonSize,buttonSize);
            topButton.SetBounds(right-buttonSize-62,10,buttonSize,buttonSize);
            optionsButton.SetBounds(right-buttonSize-93,10,buttonSize,buttonSize);
            addButton.SetBounds(13,10,104,27);
            searchShell.SetBounds(13,57,Math.Max(90,w-157),31);
            categoryFilter.SetBounds(w-132,57,119,31);
            copyStatusLabel.SetBounds(13,38,Math.Max(90,w-26),17);
            clearButton.SetBounds(4,4,23,23);
            search.SetBounds(30,8,Math.Max(35,searchShell.Width-38),18);

            if(resizeHandles[0]!=null){
                resizeHandles[0].SetBounds(0,0,16,16);
                resizeHandles[1].SetBounds(w-16,0,16,16);
                resizeHandles[2].SetBounds(0,Height-16,16,16);
                resizeHandles[3].SetBounds(w-16,Height-16,16,16);
            }
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!allowExit && e.CloseReason!=CloseReason.WindowsShutDown) {
                e.Cancel=true;
                HideToTray();
                return;
            }
            base.OnFormClosing(e);
        }
        private void RememberMainSize(){if(WindowState==FormWindowState.Normal)UiSizeMemory.Save("main",Size,MinimumSize);}
        public void HideToTray(){RememberMainSize();Hide();ShowInTaskbar=false;if(WindowState!=FormWindowState.Normal)WindowState=FormWindowState.Normal;}
        public void ExitNow() { RememberMainSize();allowExit=true; Close(); }
        public void PlaceNearClock()
        {
            Rectangle w=Screen.FromPoint(Cursor.Position).WorkingArea;
            if(WindowState!=FormWindowState.Normal)WindowState=FormWindowState.Normal;
            Location=new Point(Math.Max(w.Left+8,w.Right-Width-9),Math.Max(w.Top+8,w.Bottom-Height-9));
        }
        private void ShowOptions()
        {
            using(OptionsDialog dlg=new OptionsDialog()){
                if(dlg.ShowDialog(this)!=DialogResult.OK)return;
                bool startupOk=StartupRegistration.SetEnabled(dlg.RunAtWindowsStartup);
                try{AppSettings.SetStartMinimized(dlg.StartMinimized);}catch(Exception ex){Error(ex);return;}
                if(!startupOk){
                    MessageBox.Show(this,"Could not update Windows startup settings.","QuickText",MessageBoxButtons.OK,MessageBoxIcon.Warning);
                    return;
                }
                Notify("Options saved");
            }
        }
        private void UpdateTopButton()
        {
            topButton.Text="📌";
            topButton.Fill=TopMost?Color.FromArgb(231,232,252):Color.FromArgb(245,247,251);
            topButton.ForeColor=TopMost?Color.FromArgb(83,98,214):Color.FromArgb(92,99,122);
            topButton.Invalidate();
            tips.SetToolTip(topButton,TopMost?"Unpin window":"Always on top");
        }
        private void ToggleTop()
        {
            bool next=!TopMost;
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(pinSettingPath));
                File.WriteAllText(pinSettingPath,next?"1":"0",Encoding.ASCII);
                TopMost=next; UpdateTopButton();
                Notify(next?"Always on top enabled":"Always on top disabled");
            } catch(Exception ex) { Error(ex); }
        }
        private void Notify(string text)
        {
            if(!String.Equals(text,"Copied",StringComparison.Ordinal))return;
            copyStatusLabel.Text="Copied!";
            copyStatusLabel.ForeColor=Color.FromArgb(210,35,48);
            copyStatusLabel.Visible=true;
            copyStatusLabel.BringToFront();
            copyStatusLabel.Refresh();
            copyStatusTimer.Stop();
            copyStatusTimer.Start();
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing){
                if(copyStatusTimer!=null){copyStatusTimer.Stop();copyStatusTimer.Dispose();}
                if(tips!=null)tips.Dispose();
            }
            base.Dispose(disposing);
        }
        private void Error(Exception ex) { MessageBox.Show(this,"Error: "+ex.Message,"QuickText",MessageBoxButtons.OK,MessageBoxIcon.Warning); }
        public void AddNew()
        {
            int nextNumber=PromptNumbering.NextAvailableNumber(store.Items);
            int[] usedNumbers=store.Items.Select(p=>p.Number).ToArray();
            using (EditorDialog dlg=new EditorDialog("","",null,null,null,null,null,AllCategories(),nextNumber,usedNumbers)) {
                if (dlg.ShowDialog(this)!=DialogResult.OK) return;
                PromptItem p=new PromptItem { Id=Guid.NewGuid().ToString("N"),Number=dlg.EntryNumber,Title=dlg.EntryTitle,Content=dlg.EntryContent,TitleRtf=dlg.EntryTitleRtf,ContentRtf=dlg.EntryContentRtf,TitleHtml=dlg.EntryTitleHtml,ContentHtml=dlg.EntryContentHtml,Links=dlg.EntryLinks,Category=dlg.EntryCategory,Modified=DateTime.UtcNow.ToString("o") };
                store.Items.Insert(0,p);
                try { store.Save(); } catch(Exception ex) { store.Items.Remove(p); Error(ex); return; }
                RefreshFilterChoices();search.Clear(); Rebuild(); Notify("Text added");
            }
        }
        private void Edit(PromptItem item)
        {
            int[] usedNumbers=store.Items.Where(p=>p.Id!=item.Id).Select(p=>p.Number).ToArray();
            using (EditorDialog dlg=new EditorDialog(item.Title,item.Content,item.TitleRtf,item.ContentRtf,item.TitleHtml,item.ContentHtml,item.Category,AllCategories(),item.Number,usedNumbers,item.Links)) {
                if (dlg.ShowDialog(this)!=DialogResult.OK) return;
                string a=item.Title,b=item.Content,c=item.Modified,d=item.TitleRtf,e=item.ContentRtf,f=item.TitleHtml,g=item.ContentHtml,oldCategory=item.Category;
                List<PromptLink> oldLinks=item.Links;
                int oldNumber=item.Number;
                item.Number=dlg.EntryNumber;
                item.Title=dlg.EntryTitle; item.Content=dlg.EntryContent;item.TitleRtf=dlg.EntryTitleRtf;item.ContentRtf=dlg.EntryContentRtf;item.TitleHtml=dlg.EntryTitleHtml;item.ContentHtml=dlg.EntryContentHtml;item.Links=dlg.EntryLinks;item.Category=dlg.EntryCategory; item.Modified=DateTime.UtcNow.ToString("o");
                try { store.Save(); } catch(Exception ex) { item.Number=oldNumber;item.Title=a; item.Content=b;item.TitleRtf=d;item.ContentRtf=e;item.TitleHtml=f;item.ContentHtml=g;item.Links=oldLinks;item.Category=oldCategory; item.Modified=c; Error(ex); return; }
                RefreshFilterChoices();Rebuild(); Notify("Changes saved");
            }
        }
        private void Delete(PromptItem item)
        {
            // حذف فوری با انتخاب گزینه حذف، بدون پرسش مجدد
            int index=store.Items.IndexOf(item);
            store.Items.Remove(item);
            try { store.Save(); } catch(Exception ex) { store.Items.Insert(index,item); Error(ex); return; }
            RefreshFilterChoices();Rebuild(); Notify("Text deleted");
        }
        private void Pin(PromptItem item)
        {
            bool old=item.IsPinned;
            int index=store.Items.IndexOf(item);
            store.Items.RemoveAt(index);
            item.IsPinned=!old;
            if (item.IsPinned) store.Items.Insert(0,item);
            else {
                int at=store.Items.FindLastIndex(delegate(PromptItem p) { return p.IsPinned; });
                store.Items.Insert(at+1,item);
            }
            try { store.Save(); } catch(Exception ex) { store.Items.Remove(item); item.IsPinned=old;store.Items.Insert(index,item);Error(ex);return; }
            Rebuild(); Notify(item.IsPinned?"Pinned":"Unpinned");
        }
        public void Copy(PromptItem item)
        {
            try {
                if(String.IsNullOrEmpty(item.Content)){Notify("Nothing to copy");return;}
                RichDocument.Copy(item);
                Notify("Copied");
            }catch(Exception ex){Error(ex);}
        }
        private void View(PromptItem item)
        {
            using(ViewerDialog dlg=new ViewerDialog(item))dlg.ShowDialog(this);
        }
        private void Reorder(PromptItem item,PromptItem target,bool after)
        {
            CategoryChoice filter=categoryFilter.SelectedItem as CategoryChoice;
            if (search.Text.Trim().Length>0 || (filter!=null && filter.Kind!=0)) {
                Notify("Select All to reorder");return;
            }
            if (item==target) return;
            var oldOrder=new List<PromptItem>(store.Items);
            bool oldPinned=item.IsPinned;
            store.Items.Remove(item);
            item.IsPinned=target.IsPinned;
            int at=store.Items.IndexOf(target);
            store.Items.Insert(at+(after?1:0),item);
            try { store.Save(); } catch(Exception ex) { store.Items.Clear();store.Items.AddRange(oldOrder);item.IsPinned=oldPinned;Error(ex);return; }
            Rebuild(); Notify("Order saved");
        }
        private static readonly Image ViewEyeIcon=CreateEyeIcon();
        private static Image CreateEyeIcon()
        {
            Bitmap bitmap=new Bitmap(18,18);
            using(Graphics g=Graphics.FromImage(bitmap)){
                g.SmoothingMode=SmoothingMode.AntiAlias;
                using(Pen outline=new Pen(Color.FromArgb(71,88,106),1.65F))
                using(Brush pupil=new SolidBrush(Color.FromArgb(71,88,106))){
                    g.DrawBezier(outline,new PointF(1.4F,9),new PointF(5,3),new PointF(13,3),new PointF(16.6F,9));
                    g.DrawBezier(outline,new PointF(16.6F,9),new PointF(13,15),new PointF(5,15),new PointF(1.4F,9));
                    g.FillEllipse(pupil,7,7,4,4);
                }
            }
            return bitmap;
        }
        private ContextMenuStrip MenuFor(PromptItem item)
        {
            ContextMenuStrip menu=new ContextMenuStrip();
            menu.RightToLeft=RightToLeft.No;
            menu.Font=new Font("Segoe UI",9.5F);
            menu.BackColor=Color.White;
            menu.Items.Add("View",ViewEyeIcon,delegate { View(item); });
            menu.Items.Add("Edit",null,delegate { Edit(item); });
            menu.Items.Add(item.IsPinned?"Unpin":"Pin",null,delegate { Pin(item); });
            menu.Items.Add(new ToolStripSeparator());
            ToolStripItem delete=menu.Items.Add("Delete",null,delegate { Delete(item); });
            delete.ForeColor=Color.FromArgb(188,62,76);
            return menu;
        }
        private List<string> AllCategories()
        {
            return store.Items.Select(p=>p.Category).Where(c=>!String.IsNullOrWhiteSpace(c))
                .Select(c=>c.Trim()).Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(c=>c,StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        private void RefreshFilterChoices()
        {
            if(categoryFilter==null)return;
            CategoryChoice selected=categoryFilter.SelectedItem as CategoryChoice;
            string previous=selected==null?"0:":selected.Key;
            updatingCategoryFilter=true;
            try {
                categoryFilter.BeginUpdate();
                categoryFilter.Items.Clear();
                categoryFilter.Items.Add(new CategoryChoice(0,""));
                categoryFilter.Items.Add(new CategoryChoice(1,""));
                foreach(string name in AllCategories())categoryFilter.Items.Add(new CategoryChoice(2,name));
                int index=0;
                for(int i=0;i<categoryFilter.Items.Count;i++){
                    CategoryChoice option=(CategoryChoice)categoryFilter.Items[i];
                    if(option.Key==previous){index=i;break;}
                }
                categoryFilter.SelectedIndex=index;
            }finally{
                categoryFilter.EndUpdate();
                updatingCategoryFilter=false;
            }
        }
        private void ResizeCards()
        {
            if(list==null)return;
            int width=Math.Max(130,list.ClientSize.Width-22);
            foreach(Control c in list.Controls){
                if(c is SoftCard){c.Width=width;LayoutCard((SoftCard)c);}
            }
            list.LayoutCards();
        }
        private void LayoutCard(SoftCard card)
        {
            foreach(Control c in card.Controls){
                if(c.Tag as string=="title")c.SetBounds(94,13,Math.Max(65,card.Width-112),27);
                if(c.Tag as string=="number")c.SetBounds(53,13,34,21);
                if(c.Tag as string=="pin")c.SetBounds(40,16,9,13);
            }
        }
        private void Rebuild()
        {
            if (list==null) return;
            string q=search.Text.Trim();
            var ordered=PromptLogic.Order(store.Items,q);
            CategoryChoice choice=categoryFilter==null?null:categoryFilter.SelectedItem as CategoryChoice;
            if(choice!=null){
                if(choice.Kind==1)ordered=ordered.Where(p=>String.IsNullOrWhiteSpace(p.Category)).ToList();
                else if(choice.Kind==2)ordered=ordered.Where(p=>String.Equals((p.Category??"").Trim(),choice.Category,StringComparison.CurrentCultureIgnoreCase)).ToList();
            }
            list.SuspendLayout();
            list.Controls.Clear();
            if (ordered.Count==0) {
                Label empty=new Label { Text=q.Length>0?"No results found":"No snippets yet",Font=new Font("IRANSans",9.5F),ForeColor=Color.FromArgb(127,135,154),TextAlign=ContentAlignment.MiddleCenter,Size=new Size(Math.Max(180,list.Width-24),70) };
                list.Controls.Add(empty);
            }
            foreach(PromptItem item in ordered)
            {
                SoftCard card=new SoftCard { Width=300,Height=46,Margin=new Padding(1,2,3,4),AllowDrop=true,Cursor=Cursors.Hand,Tag=item };
                bool titleRtl=PromptLogic.IsRtlText(item.Title);
                RichTitle title=new RichTitle { Tag="title",RightToLeft=titleRtl?RightToLeft.Yes:RightToLeft.No };
                RichDocument.Load(title,item.TitleRtf,item.Title);
                title.SelectAll();
                title.SelectionFont=new Font("IRANSans",10F,FontStyle.Regular);
                title.SelectionAlignment=titleRtl?HorizontalAlignment.Right:HorizontalAlignment.Left;
                title.Select(0,0);
                Label numberLabel=new Label {Tag="number",Text="#"+item.Number.ToString(CultureInfo.InvariantCulture),
                    Font=new Font("Segoe UI",8F,FontStyle.Bold),ForeColor=Color.FromArgb(124,137,153),
                    TextAlign=ContentAlignment.MiddleCenter,BackColor=Color.Transparent};
                SoftButton menuButton=new SoftButton { Text="⋯",Radius=9,Fill=Color.FromArgb(245,247,252),HoverFill=Color.FromArgb(230,234,248),ForeColor=Color.FromArgb(91,99,122),Font=new Font("Segoe UI",17F) };
                menuButton.SetBounds(9,9,30,29);
                menuButton.Click+=delegate { ContextMenuStrip m=MenuFor(item); m.Show(menuButton,new Point(0,menuButton.Height)); };
                card.Controls.Add(title);card.Controls.Add(numberLabel);card.Controls.Add(menuButton);
                if(item.IsPinned) {
                    Label pin=new Label { Tag="pin",Text="◆",ForeColor=Color.FromArgb(102,102,206),Font=new Font("Segoe UI",7F),BackColor=Color.Transparent };
                    card.Controls.Add(pin);
                    tips.SetToolTip(pin,"Pin snippet");
                }
                LayoutCard(card);
                Point down=Point.Empty;
                bool press=false,dragging=false,copied=false;
                MouseEventHandler onDown=delegate(object sender,MouseEventArgs e) {
                    if(e.Button==MouseButtons.Right) { ContextMenuStrip m=MenuFor(item);m.Show((Control)sender,e.Location);return; }
                    if(e.Button!=MouseButtons.Left) return;
                    press=true; dragging=false;copied=false; down=((Control)sender).PointToScreen(e.Location);
                };
                MouseEventHandler onMove=delegate(object sender,MouseEventArgs e) {
                    if(!press || (Control.MouseButtons & MouseButtons.Left)==0) return;
                    Point p=((Control)sender).PointToScreen(e.Location);
                    if(Math.Abs(p.X-down.X)+Math.Abs(p.Y-down.Y)<SystemInformation.DragSize.Width/2+3) return;
                    press=false; dragging=true;
                    CategoryChoice filter=categoryFilter.SelectedItem as CategoryChoice;
                    if(q.Length>0 || (filter!=null && filter.Kind!=0)) {
                        Notify("Select All to reorder");return;
                    }
                    card.DoDragDrop(item,DragDropEffects.Move);
                };
                MouseEventHandler onUp=delegate(object sender,MouseEventArgs e) {
                    if(e.Button==MouseButtons.Left && press && !dragging) {Copy(item);copied=true;}
                    press=false;
                };
                EventHandler onClick=delegate{if(!dragging && !copied){Copy(item);copied=true;}};
                card.Click+=onClick;title.Click+=onClick;
                card.MouseDown+=onDown; card.MouseMove+=onMove; card.MouseUp+=onUp;
                title.MouseDown+=onDown; title.MouseMove+=onMove; title.MouseUp+=onUp;
                card.DragEnter+=delegate(object sender,DragEventArgs e) { e.Effect=e.Data.GetDataPresent(typeof(PromptItem))?DragDropEffects.Move:DragDropEffects.None; };
                card.DragOver+=delegate(object sender,DragEventArgs e) { e.Effect=e.Data.GetDataPresent(typeof(PromptItem))?DragDropEffects.Move:DragDropEffects.None;card.DropHighlight=e.Effect==DragDropEffects.Move;card.Invalidate(); };
                card.DragLeave+=delegate { card.DropHighlight=false;card.Invalidate(); };
                card.DragDrop+=delegate(object sender,DragEventArgs e) {
                    card.DropHighlight=false;
                    PromptItem dragged=e.Data.GetData(typeof(PromptItem)) as PromptItem;
                    if(dragged==null) return;
                    Point local=card.PointToClient(new Point(e.X,e.Y));
                    Reorder(dragged,item,local.Y>card.Height/2);
                };
                title.AllowDrop=true;
                title.DragEnter+=delegate(object sender,DragEventArgs e) { e.Effect=e.Data.GetDataPresent(typeof(PromptItem))?DragDropEffects.Move:DragDropEffects.None; };
                title.DragOver+=delegate(object sender,DragEventArgs e) { e.Effect=e.Data.GetDataPresent(typeof(PromptItem))?DragDropEffects.Move:DragDropEffects.None; };
                title.DragDrop+=delegate(object sender,DragEventArgs e) {
                    PromptItem dragged=e.Data.GetData(typeof(PromptItem)) as PromptItem;
                    if(dragged==null) return;
                    Reorder(dragged,item,card.PointToClient(new Point(e.X,e.Y)).Y>card.Height/2);
                };
                tips.SetToolTip(title,"Click to copy | Drag to reorder");
                tips.SetToolTip(menuButton,"View, edit, pin or delete");
                card.MouseWheel+=delegate(object sender,MouseEventArgs e){list.ScrollWheel(e.Delta);};
                title.MouseWheel+=delegate(object sender,MouseEventArgs e){list.ScrollWheel(e.Delta);};
                menuButton.MouseWheel+=delegate(object sender,MouseEventArgs e){list.ScrollWheel(e.Delta);};
                list.Controls.Add(card);
            }
            list.ResumeLayout();
            ResizeCards();
        }
    }
    public class OptionsDialog : Form
    {
        private readonly CheckBox runAtStartup;
        private readonly CheckBox startMinimized;
        private readonly Panel titleBar;

        private readonly Label titleLabel;
        private readonly SoftButton closeButton;
        private Bitmap appIconBitmap;
        public bool RunAtWindowsStartup { get { return runAtStartup.Checked; } }
        public bool StartMinimized { get { return startMinimized.Checked; } }

        public OptionsDialog()
        {
            Text="Options";
            Font=new Font("Segoe UI",9.5F);
            BackColor=Color.FromArgb(250,252,254);
            ForeColor=Color.FromArgb(39,54,67);
            FormBorderStyle=FormBorderStyle.None;
            StartPosition=FormStartPosition.CenterParent;
            ShowInTaskbar=false;
            ShowIcon=true;
            MinimizeBox=false;
            MaximizeBox=false;
            Size=new Size(360,275);
            Icon=PromptWindow.TryIcon();

            string optionsLogoPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"QuickText-4.7-logo.png");
            if(File.Exists(optionsLogoPath)){
                using(Image sourceLogo=Image.FromFile(optionsLogoPath)){
                    appIconBitmap=new Bitmap(sourceLogo);
                }
            }else{
                using(Icon sourceIcon=PromptWindow.TryIcon()){
                    appIconBitmap=sourceIcon.ToBitmap();
                }
            }
            titleBar=new Panel {BackColor=Color.FromArgb(246,248,252),Cursor=Cursors.SizeAll};
            titleBar.SetBounds(0,0,360,32);
            titleBar.MouseDown+=delegate(object sender,MouseEventArgs e){MoveWindow(e);};
            titleBar.Paint+=delegate(object sender,PaintEventArgs e){
                if(appIconBitmap!=null){
                    e.Graphics.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    e.Graphics.DrawImage(appIconBitmap,new Rectangle(9,4,24,24));
                }
            };
            titleLabel=new Label {Text="Options",Font=new Font("Segoe UI",9F),ForeColor=Color.FromArgb(95,105,116),TextAlign=ContentAlignment.MiddleLeft};
            titleLabel.SetBounds(39,5,120,22);
            titleLabel.MouseDown+=delegate(object sender,MouseEventArgs e){MoveWindow(e);};
            closeButton=new SoftButton {Text="×",Radius=7,Fill=Color.FromArgb(246,248,252),HoverFill=Color.FromArgb(232,237,244),ForeColor=Color.FromArgb(92,104,116),Font=new Font("Segoe UI",12F)};
            closeButton.SetBounds(326,4,26,24);
            closeButton.Click+=delegate{DialogResult=DialogResult.Cancel;Close();};
            titleBar.Controls.AddRange(new Control[]{titleLabel,closeButton});

            runAtStartup=new CheckBox {Text="Run at Windows startup",Checked=StartupRegistration.IsEnabled(),AutoSize=true,Font=new Font("Segoe UI",9.5F)};
            runAtStartup.SetBounds(20,52,310,24);
            startMinimized=new CheckBox {Text="Start minimized",Checked=AppSettings.GetStartMinimized(),AutoSize=true,Font=new Font("Segoe UI",9.5F)};
            startMinimized.SetBounds(20,84,310,24);
            Label note=new Label {Text="When unchecked, the window opens at startup.",Font=new Font("Segoe UI",8F),ForeColor=Color.FromArgb(119,135,145)};
            note.SetBounds(40,108,290,18);
            Label appName=new Label {Text="QuickText",Font=new Font("Segoe UI",10F,FontStyle.Bold),ForeColor=Color.FromArgb(45,66,82)};
            appName.SetBounds(20,145,150,20);
            Label version=new Label {Text="Version "+AppInfo.Version,Font=new Font("Segoe UI",8.5F),ForeColor=Color.FromArgb(119,135,145)};
            version.SetBounds(20,166,150,18);
            LinkLabel github=new LinkLabel {Text="GitHub",AutoSize=true,Font=new Font("Segoe UI",8.5F,FontStyle.Bold),LinkColor=Color.FromArgb(43,112,184),ActiveLinkColor=Color.FromArgb(28,83,142)};
            github.SetBounds(20,188,90,20);
            github.LinkClicked+=delegate{try{Process.Start(new ProcessStartInfo(AppInfo.GitHubUrl){UseShellExecute=true});}catch{}};
            SoftButton save=new SoftButton {Text="Save",Fill=Color.FromArgb(40,68,84),HoverFill=Color.FromArgb(30,54,69),ForeColor=Color.White,Radius=8,Font=new Font("Segoe UI",9F,FontStyle.Bold)};
            SoftButton cancel=new SoftButton {Text="Cancel",Radius=8,Font=new Font("Segoe UI",9F)};
            save.SetBounds(224,225,104,30);
            cancel.SetBounds(110,225,104,30);
            Controls.AddRange(new Control[]{titleBar,runAtStartup,startMinimized,note,appName,version,github,save,cancel});
            save.Click+=delegate{DialogResult=DialogResult.OK;Close();};
            cancel.Click+=delegate{DialogResult=DialogResult.Cancel;Close();};
            AcceptButton=save;
            CancelButton=cancel;
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")]private static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll")]private static extern IntPtr SendMessage(IntPtr h,int msg,IntPtr a,IntPtr b);
        private void MoveWindow(MouseEventArgs e){
            if(e.Button!=MouseButtons.Left)return;
            ReleaseCapture();SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero);
        }
        protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);ApplyRoundedRegion();}
        protected override void OnResize(EventArgs e){base.OnResize(e);ApplyRoundedRegion();}
        private void ApplyRoundedRegion(){
            if(Width<35||Height<35)return;
            using(var path=RoundedSearch.MakePath(new Rectangle(0,0,Width-1,Height-1),12)){
                Region old=Region;Region=new Region(path);if(old!=null)old.Dispose();
            }
        }
        protected override void OnPaint(PaintEventArgs e){
            base.OnPaint(e);
            e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using(var path=RoundedSearch.MakePath(new Rectangle(0,0,Width-1,Height-1),12))
            using(var pen=new Pen(Color.FromArgb(210,220,230)))e.Graphics.DrawPath(pen,path);
        }
        protected override void Dispose(bool disposing){
            if(disposing && appIconBitmap!=null){appIconBitmap.Dispose();appIconBitmap=null;}
            base.Dispose(disposing);
        }
    }

    public class TrayContext : ApplicationContext
    {
        private PromptWindow window;
        private NotifyIcon trayIcon;
        private Icon mainIcon;
        private Icon pulseIcon;
        private System.Windows.Forms.Timer pulseTimer;
        private bool pulseState=false;
        private readonly EventWaitHandle showRequest;
        private System.Windows.Forms.Timer openPoll;

        public TrayContext(bool autoStart, EventWaitHandle request, bool startMinimized)
        {
            showRequest=request;
            PromptStore store;
            try { store = new PromptStore(PromptStore.DataPath); }
            catch (Exception ex)
            {
                MessageBox.Show("Saved text could not be read. To protect your data, QuickText will not start.\r\n" + ex.Message, "QuickText");
                ExitThread();
                return;
            }
            window = new PromptWindow(store);

            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "QuickText.ico");
            mainIcon = new Icon(iconPath);
            string pulsePath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"QuickText-pulse.ico");
            if(File.Exists(pulsePath)){try{pulseIcon=new Icon(pulsePath);}catch{pulseIcon=null;}}
            trayIcon = new NotifyIcon();
            trayIcon.Icon = mainIcon;
            trayIcon.Text = "QuickText";
            trayIcon.Visible = true;
            trayIcon.MouseClick += delegate(object sender, MouseEventArgs e) {
                if (e.Button == MouseButtons.Left) Open();
            };
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.RightToLeft = RightToLeft.No;
            menu.Font = new Font("Segoe UI",9.5F);
            menu.Items.Add("Open QuickText", null, delegate { Open(); });
            menu.Items.Add("New Chat", null, delegate { Open(); window.AddNew(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { CloseAll(); });
            trayIcon.ContextMenuStrip = menu;
            if(pulseIcon!=null){
                pulseTimer=new System.Windows.Forms.Timer {Interval=750};
                pulseTimer.Tick+=delegate{
                    if(trayIcon==null || mainIcon==null || pulseIcon==null)return;
                    pulseState=!pulseState;
                    trayIcon.Icon=pulseState?pulseIcon:mainIcon;
                };
                pulseTimer.Start();
            }

            openPoll=new System.Windows.Forms.Timer();
            openPoll.Interval=160;
            openPoll.Tick+=delegate { if(showRequest.WaitOne(0))Open(); };
            openPoll.Start();
            if (!autoStart || !startMinimized || window.TopMost) Open();
            else
            {
                window.HideToTray();
            }
        }

        public void Open()        {
            if (window == null) return;
            window.WindowState = FormWindowState.Normal;
            window.ShowInTaskbar=false;
            window.PlaceNearClock();
            window.Show();
            window.BringToFront();
            window.Activate();
        }

        private void CloseAll()
        {
            if (openPoll != null) { openPoll.Stop(); openPoll.Dispose(); openPoll=null; }
            if (pulseTimer != null) { pulseTimer.Stop(); pulseTimer.Dispose(); pulseTimer=null; }
            if (trayIcon != null) { trayIcon.Visible = false; trayIcon.Dispose(); trayIcon = null; }
            if (window != null) { window.ExitNow(); window.Dispose(); window = null; }
            if (pulseIcon != null) { pulseIcon.Dispose(); pulseIcon=null; }
            if (mainIcon != null) { mainIcon.Dispose(); mainIcon = null; }
            ExitThread();
        }
    }


    public static class AppSettings
    {
        private static readonly string StartMinimizedPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"QuickPrompt","start-minimized.txt");
        public static bool GetStartMinimized()
        {
            try{
                if(!File.Exists(StartMinimizedPath))return true;
                return File.ReadAllText(StartMinimizedPath,Encoding.ASCII).Trim()=="1";
            }catch{return true;}
        }
        public static void SetStartMinimized(bool value)
        {
            string folder=Path.GetDirectoryName(StartMinimizedPath);
            Directory.CreateDirectory(folder);
            File.WriteAllText(StartMinimizedPath,value?"1":"0",Encoding.ASCII);
        }
    }

    public static class StartupRegistration
    {
        private const string RunPath=@"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName="Quick Text";
        public static bool IsEnabled()
        {
            try{
                using(RegistryKey key=Registry.CurrentUser.OpenSubKey(RunPath,false)){
                    return key!=null && (key.GetValue(ValueName)!=null || key.GetValue("QuickText")!=null);
                }
            }catch{return false;}
        }
        public static bool SetEnabled(bool enabled)
        {
            try{
                using(RegistryKey key=Registry.CurrentUser.CreateSubKey(RunPath)){
                    if(key==null)return false;
                    if(enabled){
                        string command="\""+Application.ExecutablePath+"\" --startup";
                        key.SetValue(ValueName,command,RegistryValueKind.String);
                        if(key.GetValue("QuickText")!=null)key.DeleteValue("QuickText",false);
                    }else{
                        if(key.GetValue(ValueName)!=null)key.DeleteValue(ValueName,false);
                        if(key.GetValue("QuickText")!=null)key.DeleteValue("QuickText",false);
                    }
                    return true;
                }
            }catch{return false;}
        }
    }
    public static class Program
    {
        private static Mutex guard;
        [STAThread]
        public static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 0 && args[0] == "--test-pins") { TestPins(); return; }
            if (args.Length > 0 && args[0] == "--test-numbering") { TestNumbering(); return; }
            if (args.Length > 0 && args[0] == "--test-links") { TestLinks(); return; }
            if (args.Length > 0 && args[0] == "--test-ui") { TestUi(); return; }
            EventWaitHandle openRequest=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\QuickText-Open");
            bool created;
            guard = new Mutex(true, "Local\\QuickText-SingleInstance", out created);
            if (!created) { openRequest.Set();openRequest.Dispose();guard.Dispose();return; }

            try
            {
                if (args.Length > 0 && args[0] == "--self-test") { SelfTest(); return; }
                if (args.Length > 0 && args[0] == "--test-pins") { TestPins(); return; }
                bool startup = args.Length > 0 && args[0] == "--startup";
                bool startMinimized = AppSettings.GetStartMinimized();
                Application.Run(new TrayContext(startup,openRequest,startMinimized));
            }
            catch (Exception ex)
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");
                try { File.WriteAllText(logPath, ex.ToString()); } catch {}
                MessageBox.Show(ex.Message, "QuickText - Error");
            }
            finally { openRequest.Dispose();guard.ReleaseMutex(); guard.Dispose(); }
        }

        private static void TestNumbering()
        {
            string output=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"number-test-result.txt");
            string temp=Path.Combine(Path.GetTempPath(),"quicktext-number-test-"+Guid.NewGuid().ToString("N")+".json");
            string result="FAIL";
            try{
                string legacy="[{\"Id\":\"A\",\"Title\":\"Alpha\",\"Content\":\"one\"},{\"Id\":\"B\",\"Title\":\"Beta\",\"Content\":\"two\"},{\"Id\":\"C\",\"Title\":\"Gamma\",\"Content\":\"three\"}]";
                File.WriteAllText(temp,legacy,new UTF8Encoding(false));
                PromptStore store=new PromptStore(temp);
                if(store.Items.Count!=3 || store.Items[0].Number!=1 || store.Items[1].Number!=2 || store.Items[2].Number!=3)
                    throw new Exception("Legacy migration did not assign sequential numbers.");
                string[] ids=store.Items.Select(p=>p.Id).ToArray();
                store.Items.Remove(store.Items.Single(p=>p.Id=="B"));
                store.Save();
                int next=PromptNumbering.NextAvailableNumber(store.Items);
                if(next!=2)throw new Exception("Deleted number was not reused.");
                store.Items.Add(new PromptItem{Id="D",Number=next,Title="New item",Content="four"});
                store.Save();
                PromptStore reopened=new PromptStore(temp);
                if(reopened.Items.Single(p=>p.Id=="D").Number!=2)throw new Exception("Number did not persist.");
                List<PromptItem> found=PromptLogic.Order(reopened.Items,"2");
                if(found.Count!=1 || found[0].Id!="D")throw new Exception("Numeric search did not return the exact item.");
                found=PromptLogic.Order(reopened.Items,"#2");
                if(found.Count!=1 || found[0].Id!="D")throw new Exception("Hash-number search failed.");
                List<PromptItem> duplicate=new List<PromptItem>{
                    new PromptItem{Id="X",Number=1},new PromptItem{Id="Y",Number=1},new PromptItem{Id="Z",Number=0}
                };
                if(!PromptNumbering.Normalize(duplicate) || duplicate[0].Number!=1 || duplicate[1].Number!=2 || duplicate[2].Number!=3)
                    throw new Exception("Duplicate/empty number repair failed.");
                if(!ids.SequenceEqual(new string[]{"A","B","C"}))throw new Exception("Stable item identifiers changed.");
                result="PASS: legacy migration, stable IDs, deleted-number reuse, manual number persistence, exact numeric search, duplicate repair";
            }catch(Exception ex){result="FAIL: "+ex.ToString();}
            finally{try{if(File.Exists(temp))File.Delete(temp);}catch{}File.WriteAllText(output,result,Encoding.UTF8);}
        }

        private static void TestLinks()
        {
            string output=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"link-test-result.txt");
            string result="FAIL";
            try{
                string text="برای دریافت فایل روی دانلود کلیک کنید";
                int start=text.IndexOf("دانلود",StringComparison.Ordinal);
                PromptLink link=new PromptLink{Start=start,Length=6,Text="دانلود",Url="https://example.com/download?id=1&source=quicktext"};
                string html=RichDocument.BuildHtmlWithLinks(text,new PromptLink[]{link});
                if(!html.Contains("href=\"https://example.com/download?id=1&amp;source=quicktext\""))throw new Exception("Hyperlink URL was not encoded in clipboard HTML.");
                if(!html.Contains(">دانلود</a>"))throw new Exception("Visible link label was not preserved in clipboard HTML.");
                if(!html.Contains("StartFragment:"))throw new Exception("CF_HTML clipboard header is missing.");
                string rtf;
                using(RichTextBox formatted=new RichTextBox()){formatted.Text=text;rtf=formatted.Rtf;}
                PromptItem item=new PromptItem{Content=text,ContentRtf=rtf,Links=new List<PromptLink>{link}};
                RichDocument.Copy(item);
                IDataObject clipboard=Clipboard.GetDataObject();
                if(clipboard==null || !clipboard.GetDataPresent(DataFormats.Html) || !clipboard.GetDataPresent(DataFormats.Rtf))
                    throw new Exception("Clipboard did not expose both HTML and RTF formats.");
                string clipboardHtml=clipboard.GetData(DataFormats.Html) as string;
                if(String.IsNullOrEmpty(clipboardHtml) || !clipboardHtml.Contains("href="))
                    throw new Exception("Clipboard HTML is missing the clickable link.");
                if(Clipboard.GetText(TextDataFormat.UnicodeText)!=text)
                    throw new Exception("Clipboard plain text does not preserve the link label.");
                result="PASS: Persian link label, target URL, CF_HTML, RTF and plain-text clipboard formats";
            }catch(Exception ex){result="FAIL: "+ex.ToString();}
            File.WriteAllText(output,result,Encoding.UTF8);
        }

        private static void TestUi()
        {
            string output=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ui-test-result.txt");
            string result="FAIL";
            try{
                using(EditorDialog editor=new EditorDialog("Test","Alpha Beta",null,null,null,null,null,new string[0],17,new int[]{1,2},
                    new PromptLink[]{new PromptLink{Start=6,Length=4,Text="Beta",Url="https://example.com"}})){
                    var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                    RichTextBox content=(RichTextBox)typeof(EditorDialog).GetField("contentBox",flags).GetValue(editor);IntPtr editorHandle=content.Handle;
                    NumericUpDown number=(NumericUpDown)typeof(EditorDialog).GetField("numberBox",flags).GetValue(editor);
                    ComboBox category=(ComboBox)typeof(EditorDialog).GetField("categoryBox",flags).GetValue(editor);
                    if(number.Value!=17)throw new Exception("Editor number box did not load the selected number.");
                    if(category.Text!="Category")throw new Exception("Category placeholder is not inside the category box.");
                    string lastForTest=content.Text;content.Text="Prefix Alpha Beta";
                    List<PromptLink> updated=editor.EntryLinks;
                    if(updated.Count!=1 || updated[0].Start!=13)throw new Exception("Link position was not preserved. Before=["+lastForTest+"] After=["+content.Text+"] Count="+updated.Count+" Start="+(updated.Count>0?updated[0].Start:-1));
                    content.SelectAll();
                    typeof(EditorDialog).GetMethod("AlignJustified",flags).Invoke(editor,null);
                    if(!content.Rtf.Contains("\\qj"))throw new Exception("Justify paragraph alignment was not applied.");
                    System.Reflection.MethodInfo palette=typeof(EditorDialog).GetMethod("SelectPaletteColor",flags);
                    content.SelectAll();
                    palette.Invoke(editor,new object[]{true,Color.Yellow});
                    if(content.SelectionBackColor.ToArgb()!=Color.Yellow.ToArgb())throw new Exception("Text highlight background color did not apply.");
                    palette.Invoke(editor,new object[]{false,Color.Blue});
                    if(content.SelectionColor.ToArgb()!=Color.Blue.ToArgb())throw new Exception("Text color did not apply.");
                }
                using(OptionsDialog options=new OptionsDialog()){
                    bool hasVersion=options.Controls.Cast<Control>().Any(c=>c.Text=="Version 4.7.1");
                    bool hasGithub=options.Controls.Cast<Control>().Any(c=>c.Text=="GitHub");
                    if(!hasVersion || !hasGithub)throw new Exception("About/version/GitHub section is missing.");
                    Bitmap visibleIcon=(Bitmap)typeof(OptionsDialog).GetField("appIconBitmap",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(options);
                    string logoPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"QuickText-4.7-logo.png");
                    if(options.Icon==null || !options.ShowIcon || visibleIcon==null || visibleIcon.Width!=64 || visibleIcon.Height!=64 || !File.Exists(logoPath))
                        throw new Exception("The supplied QuickText logo is missing from the Options title bar.");
                }
                using(EditorDialog editorMenuTest=new EditorDialog("Menu test","Editor body")){
                    var ef=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                    RichEditBox editTitle=(RichEditBox)typeof(EditorDialog).GetField("titleBox",ef).GetValue(editorMenuTest);
                    RichEditBox editBody=(RichEditBox)typeof(EditorDialog).GetField("contentBox",ef).GetValue(editorMenuTest);
                    ContextMenuStrip titleMenu=editTitle.ContextMenuStrip;
                    ContextMenuStrip bodyMenu=editBody.ContextMenuStrip;
                    string[] expectedMenu=new string[]{"Cut","Copy","Paste","Select All"};
                    if(titleMenu==null || bodyMenu==null ||
                       !titleMenu.Items.Cast<ToolStripItem>().Select(x=>x.Text).SequenceEqual(expectedMenu) ||
                       !bodyMenu.Items.Cast<ToolStripItem>().Select(x=>x.Text).SequenceEqual(expectedMenu))
                        throw new Exception("Editable title and content must both have Cut, Copy, Paste and Select All.");
                    editTitle.Text="Alpha Beta";
                    editTitle.Select(0,5);
                    titleMenu.Items[1].PerformClick();
                    if(!Clipboard.ContainsText() || Clipboard.GetText()!="Alpha" || ((Label)typeof(EditorDialog).GetField("hint",ef).GetValue(editorMenuTest)).Text!="Copied!")
                        throw new Exception("Editor Copy action or red confirmation message failed.");
                    titleMenu.Items[0].PerformClick();
                    if(editTitle.Text!=" Beta" || Clipboard.GetText()!="Alpha")
                        throw new Exception("Editor Cut action failed.");
                    Clipboard.SetText("New");
                    editTitle.Select(0,0);
                    titleMenu.Items[2].PerformClick();
                    if(editTitle.Text!="New Beta" || ((Label)typeof(EditorDialog).GetField("hint",ef).GetValue(editorMenuTest)).Text!="Pasted!")
                        throw new Exception("Editor Paste action or confirmation message failed.");
                    titleMenu.Items[3].PerformClick();
                    if(editTitle.SelectionLength!=editTitle.TextLength || ((Label)typeof(EditorDialog).GetField("hint",ef).GetValue(editorMenuTest)).Text!="Selected all!")
                        throw new Exception("Editor Select All action or confirmation message failed.");
                    editBody.Text="Body Text";
                    editBody.Select(0,4);
                    bodyMenu.Items[1].PerformClick();
                    if(!Clipboard.ContainsText() || Clipboard.GetText()!="Body")
                        throw new Exception("Content field Copy action failed.");
                    bodyMenu.Items[0].PerformClick();
                    if(editBody.Text!=" Text")
                        throw new Exception("Content field Cut action failed.");
                    Clipboard.SetText("New ");
                    editBody.Select(0,0);
                    bodyMenu.Items[2].PerformClick();
                    if(editBody.Text!="New  Text")
                        throw new Exception("Content field Paste action failed.");
                    bodyMenu.Items[3].PerformClick();
                    if(editBody.SelectionLength!=editBody.TextLength)
                        throw new Exception("Content field Select All action failed.");
                }
                string toastPath=Path.Combine(Path.GetTempPath(),"quicktext-toast-test-"+Guid.NewGuid().ToString("N")+".json");
                PromptStore toastStore=new PromptStore(toastPath);
                PromptItem toastItem=new PromptItem{Id="toast",Number=1,Title="Toast test",Content="Toast content"};
                toastStore.Items.Add(toastItem);
                using(PromptWindow main=new PromptWindow(toastStore)){
                    main.ShowInTaskbar=false;main.Opacity=0;main.Show();Application.DoEvents();
                    var mf=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                    Label toast=(Label)typeof(PromptWindow).GetField("copyStatusLabel",mf).GetValue(main);
                    System.Windows.Forms.Timer timer=(System.Windows.Forms.Timer)typeof(PromptWindow).GetField("copyStatusTimer",mf).GetValue(main);
                    main.Copy(toastItem);Application.DoEvents();
                    if(toast==null || toast.Text!="Copied!" || !toast.Visible || toast.ForeColor.ToArgb()!=Color.FromArgb(210,35,48).ToArgb() || !timer.Enabled)
                        throw new Exception("Main list copy must show a visible red Copied! notification.");
                    if(toast.Bounds.Top!=38 || toast.Bounds.Height!=17 || toast.Bounds.Bottom>57)
                        throw new Exception("Main copy notification is not placed in the clear header row above search.");
                    main.Hide();
                }
                using(ViewerDialog viewer=new ViewerDialog(new PromptItem{Number=23,Title="Viewer title",Content="Viewer body"})){
                    var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                    Label number=(Label)typeof(ViewerDialog).GetField("numberBadge",flags).GetValue(viewer);
                    RoundedFramePanel titleCard=(RoundedFramePanel)typeof(ViewerDialog).GetField("titleFrame",flags).GetValue(viewer);
                    RoundedFramePanel bodyCard=(RoundedFramePanel)typeof(ViewerDialog).GetField("bodyFrame",flags).GetValue(viewer);
                    RichTextBox viewTitle=(RichTextBox)typeof(ViewerDialog).GetField("title",flags).GetValue(viewer);
                    RichTextBox viewBody=(RichTextBox)typeof(ViewerDialog).GetField("body",flags).GetValue(viewer);
                    ContextMenuStrip textMenu=(ContextMenuStrip)typeof(ViewerDialog).GetField("textContextMenu",flags).GetValue(viewer);
                    Label status=(Label)typeof(ViewerDialog).GetField("statusLabel",flags).GetValue(viewer);
                    if(number==null || number.Text!="#23")throw new Exception("Viewer number badge is missing or incorrect.");
                    if(titleCard==null || bodyCard==null || titleCard.CornerRadius<10 || bodyCard.CornerRadius<10)
                        throw new Exception("Viewer title/content frames are not rounded.");
                    if(viewTitle==null || viewBody==null || !viewTitle.ReadOnly || !viewBody.ReadOnly ||
                       viewTitle.Cursor!=Cursors.Arrow || viewBody.Cursor!=Cursors.Arrow)
                        throw new Exception("Viewer text must stay read-only with a normal arrow cursor.");
                    string[] menuLabels=textMenu.Items.Cast<ToolStripItem>().Select(x=>x.Text).ToArray();
                    if(!menuLabels.SequenceEqual(new string[]{"Cut","Copy","Paste","Select All"}))
                        throw new Exception("Viewer right-click menu must contain Cut, Copy, Paste and Select All.");
                    if(textMenu.Items[0].Enabled || textMenu.Items[2].Enabled)
                        throw new Exception("Cut/Paste must remain disabled in the locked viewer.");
                    if(status==null || status.ForeColor.ToArgb()!=Color.FromArgb(210,35,48).ToArgb())
                        throw new Exception("Viewer action feedback must be red.");
                    typeof(ViewerDialog).GetMethod("ShowStatus",flags).Invoke(viewer,new object[]{"Copied!"});
                    if(status.Text!="Copied!")
                        throw new Exception("Viewer action feedback was not displayed.");
                    if(viewTitle.ContextMenuStrip!=textMenu || viewBody.ContextMenuStrip!=textMenu)
                        throw new Exception("Viewer context menu is not attached to both text fields.");
                    if(typeof(PromptWindow).GetField("flash",flags)!=null || typeof(PromptWindow).GetField("flashTimer",flags)!=null)
                        throw new Exception("The transient green header notification is still present.");
                    Size expectedMain=UiSizeMemory.Load("main",new Size(332,385),new Size(260,235));if(viewer.Width>Math.Max(280,expectedMain.Width) || viewer.Height>Math.Max(250,expectedMain.Height))throw new Exception("Viewer is larger than the main application layout. Viewer="+viewer.Size+" Main="+expectedMain);
                    if(viewer.Icon==null)throw new Exception("Viewer window icon is missing.");
                }
                result="PASS: main list red Copied! notification, editor title/content Cut-Copy-Paste-SelectAll, supplied Options logo, rounded locked viewer, read-only Cut/Paste lock, red viewer confirmations, compact layout, version 4.7.1 and GitHub";
            }catch(Exception ex){result="FAIL: "+ex.ToString();}
            File.WriteAllText(output,result,Encoding.UTF8);
        }

        private static void TestPins()
        {
            string file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pin-test-result.txt");
            string tempFile = Path.Combine(Path.GetTempPath(), "quickprompt-pintest-" + Guid.NewGuid().ToString("N") + ".json");
            string result = "FAIL";
            try
            {
                PromptStore store = new PromptStore(tempFile);
                PromptItem a = new PromptItem { Id = "A", Title = "الف", Content = "متن الف" };
                PromptItem b = new PromptItem { Id = "B", Title = "ب", Content = "متن ب" };
                PromptItem c = new PromptItem { Id = "C", Title = "ج", Content = "متن ج" };
                PromptItem d = new PromptItem { Id = "D", Title = "د", Content = "متن د" };
                store.Items.AddRange(new PromptItem[] { a, b, c, d });
                if (!PromptLogic.Move(store.Items, c, -1)) throw new Exception("Move up failed");
                if (store.Items[1].Id != "C" || store.Items[2].Id != "B") throw new Exception("Move order failed");
                b.IsPinned = true;
                d.IsPinned = true;
                if (PromptLogic.Order(store.Items, "").First().Id != "B") throw new Exception("Pinned first failed");
                if (!PromptLogic.Move(store.Items, d, -1)) throw new Exception("Move within pinned group failed");
                var ordered = PromptLogic.Order(store.Items, "");
                if (ordered[0].Id != "D" || ordered[1].Id != "B") throw new Exception("Pinned order failed");
                if (PromptLogic.Move(store.Items, d, -1)) throw new Exception("Boundary move allowed");
                if (PromptLogic.Order(store.Items, "متن ج").Count != 1) throw new Exception("Search failed");
                store.Save();
                PromptStore reopened = new PromptStore(tempFile);
                var again = PromptLogic.Order(reopened.Items, "");
                if (again.Count != 4 || !again[0].IsPinned || !again[1].IsPinned || again[0].Id != "D" || again[1].Id != "B")
                    throw new Exception("Pinned persistence failed");
                if (again.Single(p => p.Id == "A").Content != "متن الف") throw new Exception("Content damaged");
                again[0].IsPinned = false;
                reopened.Save();
                PromptStore recheck = new PromptStore(tempFile);
                if (PromptLogic.Order(recheck.Items, "").Count(p => p.IsPinned) != 1) throw new Exception("Unpin persistence failed");
                result = "PASS: pin, unpin, reorder, boundaries, search, persistence, Unicode";
            }
            catch (Exception ex) { result = "FAIL: " + ex.ToString(); }
            finally
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
                File.WriteAllText(file, result, Encoding.UTF8);
            }
        }

        private static void SelfTest()
        {
            string output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest.txt");
            string tempfile = Path.Combine(Path.GetTempPath(), "quickprompt-check-" + Guid.NewGuid().ToString("N") + ".json");
            string result = "";
            try
            {
                PromptStore temp = new PromptStore(tempfile);
                PromptItem sample = new PromptItem {
                    Id = "test",
                    Title = "قانون فونت آکادمی",
                    Content = "تیتر: ۳۶ پیکسل\r\nمتن: ۱۶ پیکسل\r\nفارسی ✓",
                    Modified = DateTime.UtcNow.ToString("o")
                };
                temp.Items.Add(sample);
                temp.Save();
                PromptStore verify = new PromptStore(tempfile);
                if (verify.Items.Count != 1 || verify.Items[0].Content != sample.Content || verify.Items[0].Title != sample.Title) throw new Exception("Persistence failed");
                verify.Items[0].Content = "متن ویرایش‌شده";
                verify.Save();
                verify = new PromptStore(tempfile);
                if (verify.Items[0].Content != "متن ویرایش‌شده") throw new Exception("Edit failed");
                verify.Items.Clear();
                verify.Save();
                verify = new PromptStore(tempfile);
                if (verify.Items.Count != 0) throw new Exception("Delete failed");

                string previous = Clipboard.ContainsText() ? Clipboard.GetText(TextDataFormat.UnicodeText) : null;
                // Avoid altering non-text clipboard data in the test.
                if (previous != null || !Clipboard.ContainsData(DataFormats.Bitmap))
                {
                    IDataObject oldData = Clipboard.GetDataObject();
                    try {
                        Clipboard.SetText(sample.Content, TextDataFormat.UnicodeText);
                        if (Clipboard.GetText(TextDataFormat.UnicodeText) != sample.Content) throw new Exception("Clipboard failed");
                    } finally {
                        if (oldData != null) Clipboard.SetDataObject(oldData, true);
                        else Clipboard.Clear();
                    }
                    result = "PASS: create, load, edit, delete, Unicode, clipboard";
                }
                else result = "PASS: create, load, edit, delete, Unicode; clipboard skipped to preserve existing content";
            }
            catch (Exception ex) { result = "FAIL: " + ex.ToString(); }
            finally
            {
                try { if (File.Exists(tempfile)) File.Delete(tempfile); } catch {}
                File.WriteAllText(output, result, Encoding.UTF8);
            }
        }
    }
}