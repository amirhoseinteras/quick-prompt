using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Threading;
using Microsoft.Win32;

using System.Reflection;
[assembly: AssemblyTitle("Quick Prompt")]
[assembly: AssemblyDescription("Fast offline clipboard prompt manager for Windows")]
[assembly: AssemblyProduct("Quick Prompt")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]


namespace QuickPromptApp
{
    public class PromptItem
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
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
        }

        public void Save()
        {
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

    public static class PromptLogic
    {
        public static List<PromptItem> Order(List<PromptItem> items, string query)
        {
            string q = (query ?? "").Trim();
            return items
                .Where(p => q.Length == 0 || (p.Title ?? "").IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                                           (p.Content ?? "").IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0)
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

    public class EditorDialog : Form
    {
        private TextBox titleBox;
        private TextBox contentBox;
        public string EntryTitle { get { return titleBox.Text.Trim(); } }
        public string EntryContent { get { return contentBox.Text; } }

        public EditorDialog(string title, string content)
        {
            Text = String.IsNullOrEmpty(title) ? "پرامپت جدید" : "ویرایش پرامپت";
            Font = new Font("Tahoma", 9.5F);
            RightToLeft = RightToLeft.Yes;
            RightToLeftLayout = true;
            BackColor = Color.White;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(476, 406);

            Label titleLabel = new Label();
            titleLabel.Text = "عنوان";
            titleLabel.SetBounds(24, 18, 428, 25);
            Controls.Add(titleLabel);

            titleBox = new TextBox();
            titleBox.SetBounds(24, 48, 428, 33);
            titleBox.Font = new Font("Tahoma", 10F);
            titleBox.Text = title ?? "";
            Controls.Add(titleBox);

            Label bodyLabel = new Label();
            bodyLabel.Text = "متن کامل پرامپت";
            bodyLabel.SetBounds(24, 99, 428, 25);
            Controls.Add(bodyLabel);

            contentBox = new TextBox();
            contentBox.SetBounds(24, 130, 428, 211);
            contentBox.Multiline = true;
            contentBox.ScrollBars = ScrollBars.Vertical;
            contentBox.AcceptsReturn = true;
            contentBox.AcceptsTab = true;
            contentBox.Font = new Font("Tahoma", 9.5F);
            contentBox.Text = content ?? "";
            Controls.Add(contentBox);

            Button save = MakeButton("ذخیره", Color.FromArgb(215, 43, 56), Color.White);
            save.SetBounds(24, 357, 124, 35);
            save.Click += delegate {
                if (EntryTitle.Length == 0 || String.IsNullOrWhiteSpace(EntryContent)) {
                    MessageBox.Show(this, "عنوان و متن را وارد کنید.", "Quick Prompt", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(save);

            Button cancel = MakeButton("انصراف", Color.FromArgb(236, 238, 241), Color.FromArgb(36, 40, 47));
            cancel.SetBounds(159, 357, 100, 35);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);
            CancelButton = cancel;
        }

        public static Button MakeButton(string text, Color background, Color fore)
        {
            Button b = new Button();
            b.Text = text;
            b.BackColor = background;
            b.ForeColor = fore;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.Cursor = Cursors.Hand;
            b.Font = new Font("Tahoma", 9F, FontStyle.Bold);
            b.UseVisualStyleBackColor = false;
            return b;
        }
    }

    public class PromptWindow : Form
    {
        private PromptStore store;
        private FlowLayoutPanel itemsPanel;
        private TextBox searchBox;
        private Label statusLabel;
        private System.Windows.Forms.Timer statusTimer;
        private ToolTip tooltips;
        private Button alwaysTopButton;
        private readonly string pinSettingPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickPrompt", "keep-on-top.txt");
        private bool allowExit = false;

        public PromptWindow(PromptStore store)
        {
            this.store = store;
            Text = "Quick Prompt";
            Font = new Font("Tahoma", 9F);
            RightToLeft = RightToLeft.Yes;
            RightToLeftLayout = true;
            BackColor = Color.FromArgb(249, 250, 252);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = true;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(460, 514);
            try { TopMost = File.Exists(pinSettingPath) && File.ReadAllText(pinSettingPath).Trim() == "1"; } catch { TopMost = false; }
            tooltips = new ToolTip();
            try { Icon = new Icon(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "QuickPrompt.ico")); } catch {}

            Panel head = new Panel();
            head.Dock = DockStyle.Top;
            head.Height = 123;
            head.BackColor = Color.White;

            Label heading = new Label();
            heading.Text = "Quick Prompt";
            heading.Font = new Font("Segoe UI", 16F, FontStyle.Bold);
            heading.ForeColor = Color.FromArgb(23, 26, 34);
            heading.TextAlign = ContentAlignment.MiddleRight;
            heading.SetBounds(212, 9, 222, 36);
            head.Controls.Add(heading);

            Label desc = new Label();
            desc.Text = "کلیک روی عنوان: کپی | پین: اولویت | ▲▼: ترتیب";
            desc.Font = new Font("Tahoma", 8F);
            desc.ForeColor = Color.FromArgb(100, 108, 118);
            desc.TextAlign = ContentAlignment.MiddleRight;
            desc.SetBounds(110, 46, 324, 22);
            head.Controls.Add(desc);

            Button add = EditorDialog.MakeButton("+ پرامپت جدید", Color.FromArgb(218, 43, 57), Color.White);
            add.SetBounds(15, 14, 125, 34);
            add.Click += delegate { AddNew(); };
            head.Controls.Add(add);

            alwaysTopButton = EditorDialog.MakeButton("📌", Color.FromArgb(237, 239, 243), Color.FromArgb(76, 82, 94));
            alwaysTopButton.Font = new Font("Segoe UI Emoji", 12F);
            alwaysTopButton.SetBounds(151, 14, 41, 34);
            alwaysTopButton.AccessibleName = "پین پنجره - همیشه روی سایر برنامه‌ها";
            alwaysTopButton.Click += delegate { ToggleAlwaysOnTop(); };
            head.Controls.Add(alwaysTopButton);
            UpdateWindowPin();

            searchBox = new TextBox();
            searchBox.SetBounds(15, 84, 419, 29);
            searchBox.Font = new Font("Tahoma", 9.5F);
            searchBox.TextChanged += delegate { Rebuild(); };
            head.Controls.Add(searchBox);
            Label searchHint = new Label();
            searchHint.Text = "جستجو:";
            searchHint.SetBounds(371, 67, 62, 18);
            searchHint.Font = new Font("Tahoma", 7.5F);
            searchHint.ForeColor = Color.FromArgb(105, 112, 124);
            head.Controls.Add(searchHint);

            Panel footer = new Panel();
            footer.Height = 34;
            footer.Dock = DockStyle.Bottom;
            footer.BackColor = Color.White;
            statusLabel = new Label();
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.ForeColor = Color.FromArgb(44, 122, 85);
            statusLabel.TextAlign = ContentAlignment.MiddleCenter;
            statusLabel.Text = "همه متن‌ها به‌صورت خودکار ذخیره می‌شوند";
            footer.Controls.Add(statusLabel);
            statusTimer = new System.Windows.Forms.Timer();
            statusTimer.Interval = 2000;
            statusTimer.Tick += delegate {
                statusTimer.Stop();
                statusLabel.Text = "همه متن‌ها به‌صورت خودکار ذخیره می‌شوند";
            };

            itemsPanel = new FlowLayoutPanel();
            itemsPanel.Dock = DockStyle.Fill;
            itemsPanel.FlowDirection = FlowDirection.TopDown;
            itemsPanel.WrapContents = false;
            itemsPanel.AutoScroll = true;
            itemsPanel.Padding = new Padding(9, 10, 9, 10);
            itemsPanel.BackColor = Color.FromArgb(249, 250, 252);
            itemsPanel.Resize += delegate { ResizeCards(); };

            Controls.Add(itemsPanel);
            Controls.Add(footer);
            Controls.Add(head);
            Rebuild();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!allowExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                WindowState = FormWindowState.Minimized;
                return;
            }
            base.OnFormClosing(e);
        }

        public void ExitNow() { allowExit = true; Close(); }

        private void UpdateWindowPin()
        {
            alwaysTopButton.BackColor = TopMost ? Color.FromArgb(218, 43, 57) : Color.FromArgb(237, 239, 243);
            alwaysTopButton.ForeColor = TopMost ? Color.White : Color.FromArgb(76, 82, 94);
            alwaysTopButton.FlatAppearance.BorderSize = TopMost ? 1 : 0;
            alwaysTopButton.FlatAppearance.BorderColor = Color.FromArgb(161, 30, 41);
            tooltips.SetToolTip(alwaysTopButton, TopMost ? "پین پنجره فعال؛ برای غیرفعال‌کردن کلیک کن" : "همیشه روی بقیه پنجره‌ها؛ برای فعال‌کردن کلیک کن");
        }

        private void ToggleAlwaysOnTop()
        {
            bool next = !TopMost;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(pinSettingPath));
                File.WriteAllText(pinSettingPath, next ? "1" : "0", Encoding.ASCII);
                TopMost = next;
                UpdateWindowPin();
                Inform(next ? "پین پنجره فعال شد ✓" : "پین پنجره خاموش شد");
            }
            catch (Exception ex) { Error(ex); }
        }

        public void PlaceNearClock()
        {
            Rectangle w = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(w.Right - Width - 12, w.Bottom - Height - 12);
        }

        public void AddNew()
        {
            using (EditorDialog dlg = new EditorDialog("", ""))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                PromptItem p = new PromptItem();
                p.Id = Guid.NewGuid().ToString("N");
                p.Title = dlg.EntryTitle;
                p.Content = dlg.EntryContent;
                p.Modified = DateTime.UtcNow.ToString("o");
                store.Items.Insert(0, p);
                try { store.Save(); }
                catch (Exception ex) { store.Items.Remove(p); Error(ex); return; }
                searchBox.Text = "";
                Rebuild();
                Inform("پرامپت ذخیره شد ✓");
            }
        }

        private void TogglePromptPin(PromptItem item)
        {
            item.IsPinned = !item.IsPinned;
            try { store.Save(); }
            catch (Exception ex) { item.IsPinned = !item.IsPinned; Error(ex); return; }
            Rebuild();
            Inform(item.IsPinned ? "«" + item.Title + "» بالای فهرست پین شد ✓" : "پین پرامپت برداشته شد");
        }

        private void MovePrompt(PromptItem item, int direction)
        {
            if (searchBox.Text.Trim().Length != 0)
            {
                Inform("برای تغییر ترتیب، ابتدا جستجو را پاک کن");
                return;
            }
            if (!PromptLogic.Move(store.Items, item, direction)) return;
            try { store.Save(); }
            catch (Exception ex)
            {
                PromptLogic.Move(store.Items, item, -direction);
                Error(ex);
                return;
            }
            Rebuild();
            Inform("ترتیب پرامپت‌ها ذخیره شد ✓");
        }

        private void Edit(PromptItem item)
        {
            using (EditorDialog dlg = new EditorDialog(item.Title, item.Content))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string oldTitle = item.Title, oldContent = item.Content, oldModified = item.Modified;
                item.Title = dlg.EntryTitle;
                item.Content = dlg.EntryContent;
                item.Modified = DateTime.UtcNow.ToString("o");
                try { store.Save(); }
                catch (Exception ex) { item.Title = oldTitle; item.Content = oldContent; item.Modified = oldModified; Error(ex); return; }
                Rebuild();
                Inform("تغییرات ذخیره شد ✓");
            }
        }

        private void Delete(PromptItem item)
        {
            if (MessageBox.Show(this, "پرامپت «" + item.Title + "» حذف شود؟", "تأیید حذف", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            int index = store.Items.IndexOf(item);
            store.Items.Remove(item);
            try { store.Save(); }
            catch (Exception ex) { store.Items.Insert(index, item); Error(ex); return; }
            Rebuild();
            Inform("پرامپت حذف شد");
        }

        public void Copy(PromptItem item)
        {
            try
            {
                Clipboard.SetText(item.Content, TextDataFormat.UnicodeText);
                Inform("«" + item.Title + "» کپی شد ✓");
            }
            catch (Exception ex) { Error(ex); }
        }

        private void Inform(string message)
        {
            statusLabel.Text = message;
            statusTimer.Stop();
            statusTimer.Start();
        }

        private void Error(Exception ex)
        {
            MessageBox.Show(this, "خطا: " + ex.Message, "Quick Prompt", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void ResizeCards()
        {
            int width = itemsPanel.ClientSize.Width - itemsPanel.Padding.Left - itemsPanel.Padding.Right - 15;
            if (width < 260) return;
            foreach (Control p in itemsPanel.Controls) { if (p is Panel) p.Width = width; }
        }

        private void Rebuild()
        {
            if (itemsPanel == null) return;
            string q = searchBox == null ? "" : searchBox.Text.Trim();
            var filtered = PromptLogic.Order(store.Items, q);
            itemsPanel.SuspendLayout();
            itemsPanel.Controls.Clear();
            if (filtered.Count == 0)
            {
                Label empty = new Label();
                empty.Text = q.Length > 0 ? "موردی پیدا نشد." : "هنوز پرامپتی نداری.\r\nروی «+ پرامپت جدید» کلیک کن.";
                empty.ForeColor = Color.FromArgb(113, 119, 128);
                empty.Font = new Font("Tahoma", 10F);
                empty.TextAlign = ContentAlignment.MiddleCenter;
                empty.Width = 410;
                empty.Height = 100;
                itemsPanel.Controls.Add(empty);
            }
            foreach (PromptItem item in filtered)
            {
                Panel card = new Panel();
                card.Height = 79;
                card.Width = 410;
                card.Margin = new Padding(2, 3, 2, 6);
                card.BackColor = Color.White;
                card.BorderStyle = BorderStyle.FixedSingle;
                card.Cursor = Cursors.Hand;
                card.Click += delegate { Copy(item); };

                Label name = new Label();
                name.SetBounds(122, 9, 234, 26);
                name.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                name.Text = item.Title;
                name.Font = new Font("Tahoma", 9.4F, FontStyle.Bold);
                name.ForeColor = Color.FromArgb(28, 30, 38);
                name.TextAlign = ContentAlignment.MiddleRight;
                name.AutoEllipsis = true;
                name.Cursor = Cursors.Hand;
                name.Click += delegate { Copy(item); };
                card.Controls.Add(name);

                Label preview = new Label();
                preview.SetBounds(122, 39, 271, 23);
                preview.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                preview.Text = (item.Content ?? "").Replace("\r\n", " ").Replace("\n", " ");
                preview.ForeColor = Color.FromArgb(117, 120, 128);
                preview.AutoEllipsis = true;
                preview.TextAlign = ContentAlignment.MiddleRight;
                preview.Cursor = Cursors.Hand;
                preview.Click += delegate { Copy(item); };
                card.Controls.Add(preview);

                Button edit = EditorDialog.MakeButton("ویرایش", Color.FromArgb(237, 239, 243), Color.FromArgb(49, 52, 59));
                edit.Font = new Font("Tahoma", 7.3F);
                edit.SetBounds(7, 8, 73, 25);
                edit.Click += delegate { Edit(item); };
                card.Controls.Add(edit);

                Button del = EditorDialog.MakeButton("حذف", Color.FromArgb(253, 238, 239), Color.FromArgb(187, 44, 56));
                del.Font = new Font("Tahoma", 7.3F);
                del.SetBounds(7, 40, 73, 24);
                del.Click += delegate { Delete(item); };
                card.Controls.Add(del);

                Button pin = EditorDialog.MakeButton("📌", item.IsPinned ? Color.FromArgb(218, 43, 57) : Color.FromArgb(240, 241, 244),
                    item.IsPinned ? Color.White : Color.FromArgb(109, 116, 126));
                pin.Font = new Font("Segoe UI Emoji", 11F);
                pin.SetBounds(362, 8, 36, 29);
                pin.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                pin.AccessibleName = item.IsPinned ? "برداشتن پین: " + item.Title : "پین‌کردن: " + item.Title;
                pin.Click += delegate { TogglePromptPin(item); };
                card.Controls.Add(pin);

                var group = store.Items.Where(p => p.IsPinned == item.IsPinned).ToList();
                int position = group.IndexOf(item);
                bool canReorder = q.Length == 0;
                Button up = EditorDialog.MakeButton("▲", Color.FromArgb(237, 239, 243), Color.FromArgb(62, 70, 81));
                up.Font = new Font("Segoe UI Symbol", 9F);
                up.SetBounds(85, 8, 30, 25);
                up.AccessibleName = "انتقال به بالا: " + item.Title;
                up.Enabled = canReorder && position > 0;
                up.Click += delegate { MovePrompt(item, -1); };
                card.Controls.Add(up);

                Button down = EditorDialog.MakeButton("▼", Color.FromArgb(237, 239, 243), Color.FromArgb(62, 70, 81));
                down.Font = new Font("Segoe UI Symbol", 9F);
                down.SetBounds(85, 40, 30, 25);
                down.AccessibleName = "انتقال به پایین: " + item.Title;
                down.Enabled = canReorder && position < group.Count - 1;
                down.Click += delegate { MovePrompt(item, 1); };
                card.Controls.Add(down);
                tooltips.SetToolTip(pin, item.IsPinned ? "پرامپت پین شده؛ برای برداشتن پین کلیک کن" : "پرامپت مهم را بالای فهرست پین کن");
                tooltips.SetToolTip(up, "جابه‌جایی یک ردیف به بالا");
                tooltips.SetToolTip(down, "جابه‌جایی یک ردیف به پایین");

                tooltips.SetToolTip(card, "کپی کامل متن");
                tooltips.SetToolTip(name, "برای کپی کلیک کنید");
                itemsPanel.Controls.Add(card);
            }
            itemsPanel.ResumeLayout();
            ResizeCards();
        }
    }

    public class TrayContext : ApplicationContext
    {
        private PromptWindow window;
        private NotifyIcon trayIcon;
        private Icon mainIcon;

        public TrayContext(bool autoStart)
        {
            PromptStore store;
            try { store = new PromptStore(PromptStore.DataPath); }
            catch (Exception ex)
            {
                MessageBox.Show("فایل پرامپت‌ها خوانده نشد. برای جلوگیری از آسیب به داده‌ها، برنامه اجرا نمی‌شود.\r\n" + ex.Message, "Quick Prompt");
                ExitThread();
                return;
            }
            window = new PromptWindow(store);
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "QuickPrompt.ico");
            mainIcon = new Icon(iconPath);
            trayIcon = new NotifyIcon();
            trayIcon.Icon = mainIcon;
            trayIcon.Text = "Quick Prompt";
            trayIcon.Visible = true;
            trayIcon.MouseClick += delegate(object sender, MouseEventArgs e) {
                if (e.Button == MouseButtons.Left) Open();
            };
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.RightToLeft = RightToLeft.Yes;
            menu.Items.Add("بازکردن Quick Prompt", null, delegate { Open(); });
            menu.Items.Add("پرامپت جدید", null, delegate { Open(); window.AddNew(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("خروج کامل", null, delegate { CloseAll(); });
            trayIcon.ContextMenuStrip = menu;

            if (!autoStart) Open();
            else
            {
                window.PlaceNearClock();
                window.WindowState = FormWindowState.Minimized;
                window.Show();
            }
        }

        public void Open()
        {
            if (window == null) return;
            window.PlaceNearClock();
            window.Show();
            window.WindowState = FormWindowState.Normal;
            window.BringToFront();
            window.Activate();
        }

        private void CloseAll()
        {
            if (trayIcon != null) { trayIcon.Visible = false; trayIcon.Dispose(); trayIcon = null; }
            if (window != null) { window.ExitNow(); window.Dispose(); window = null; }
            if (mainIcon != null) { mainIcon.Dispose(); mainIcon = null; }
            ExitThread();
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
            bool created;
            guard = new Mutex(true, "Local\\QuickPrompt-SingleInstance", out created);
            if (!created) { MessageBox.Show("Quick Prompt از قبل کنار ساعت ویندوز در حال اجراست.", "Quick Prompt"); return; }

            try
            {
                if (args.Length > 0 && args[0] == "--self-test") { SelfTest(); return; }
                if (args.Length > 0 && args[0] == "--test-pins") { TestPins(); return; }
                bool startup = args.Length > 0 && args[0] == "--startup";
                Application.Run(new TrayContext(startup));
            }
            catch (Exception ex)
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");
                try { File.WriteAllText(logPath, ex.ToString()); } catch {}
                MessageBox.Show(ex.Message, "Quick Prompt - Error");
            }
            finally { guard.ReleaseMutex(); guard.Dispose(); }
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
