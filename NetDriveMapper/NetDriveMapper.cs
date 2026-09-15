using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace NetDriveMapper
{
    static class Native
    {
        public const int VK_SHIFT = 0x10;
        public const int VK_CONTROL = 0x11;

        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vKey);

        public static bool IsKeyDown(int vKey)
        {
            return (GetAsyncKeyState(vKey) & 0x8000) != 0;
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Config old = Config.Load();
            bool editSetup = false;
            bool resetSetup = false;
            foreach (string a in args)
            {
                if (string.Equals(a, "/setup", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a, "-setup", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a, "/edit", StringComparison.OrdinalIgnoreCase))
                    editSetup = true;
                if (string.Equals(a, "/reset", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a, "-reset", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a, "/new", StringComparison.OrdinalIgnoreCase))
                    resetSetup = true;
            }
            if (Native.IsKeyDown(Native.VK_SHIFT))
                editSetup = true;
            if (Native.IsKeyDown(Native.VK_CONTROL))
                resetSetup = true;
            if (editSetup)
                resetSetup = false;

            Config cfg = old;
            bool needDialog = cfg == null || !cfg.IsValid() || editSetup || resetSetup;
            if (needDialog)
            {
                Config seed = resetSetup ? null : old;
                using (SetupForm f = new SetupForm(seed))
                {
                    if (f.ShowDialog() != DialogResult.OK)
                        return;
                    cfg = f.Result;
                }
                Mapper.UnmountAll(old);
                if (cfg == null || cfg.Filled().Count == 0)
                {
                    Config.Delete();
                    return;
                }
                cfg.Save();
            }

            try
            {
                Mapper.Remount(cfg);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Сетевые диски",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    sealed class ShareEntry
    {
        public string Path;
        public string Label;
        public string Letter;
    }

    sealed class Config
    {
        public const int MaxShares = 10;
        public List<ShareEntry> Shares = new List<ShareEntry>();

        static string Dir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "NetDriveMapper");
            }
        }

        public static string FilePath
        {
            get { return Path.Combine(Dir, "config.ini"); }
        }

        public bool IsValid()
        {
            List<ShareEntry> filled = Filled();
            if (filled.Count == 0) return false;
            HashSet<string> letters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ShareEntry e in filled)
            {
                if (!letters.Add(NormLetter(e.Letter))) return false;
                if (!labels.Add(e.Label)) return false;
            }
            return true;
        }

        public List<ShareEntry> Filled()
        {
            List<ShareEntry> list = new List<ShareEntry>();
            if (Shares == null) return list;
            foreach (ShareEntry e in Shares)
            {
                if (IsFilled(e)) list.Add(e);
            }
            return list;
        }

        public static bool IsFilled(ShareEntry e)
        {
            if (e == null) return false;
            return IsUnc(e.Path) && !string.IsNullOrEmpty(e.Label) && IsLetter(e.Letter);
        }

        public static string NormalizeUnc(string s)
        {
            if (s == null) return "";
            s = s.Trim();
            if (s.Length == 0) return "";
            s = s.Replace('/', '\\');
            s = s.Replace('\u2216', '\\');
            s = s.Replace('\uFF3C', '\\');
            while (s.IndexOf("\\\\", 1, StringComparison.Ordinal) >= 0)
            {
                int i = s.IndexOf("\\\\", 1, StringComparison.Ordinal);
                s = s.Remove(i, 1);
            }
            if (s.StartsWith("\\") && !s.StartsWith("\\\\"))
                s = "\\" + s;
            if (!s.StartsWith("\\\\"))
                s = "\\\\" + s;
            while (s.Length > 2 && s.EndsWith("\\"))
                s = s.Substring(0, s.Length - 1);
            return s;
        }

        public static bool IsUnc(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            s = NormalizeUnc(s);
            if (!s.StartsWith("\\\\")) return false;
            string rest = s.Substring(2);
            int slash = rest.IndexOf('\\');
            if (slash <= 0) return false;
            string server = rest.Substring(0, slash);
            string share = rest.Substring(slash + 1);
            if (share.Length == 0) return false;
            if (share.IndexOf('\\') >= 0)
                share = share.Substring(0, share.IndexOf('\\'));
            return server.Length > 0 && share.Length > 0;
        }

        public static bool IsLetter(string letter)
        {
            if (string.IsNullOrEmpty(letter)) return false;
            char c = char.ToUpperInvariant(letter.Trim()[0]);
            return c >= 'D' && c <= 'Z';
        }

        public static string NormLetter(string letter)
        {
            if (string.IsNullOrEmpty(letter)) return "";
            char c = char.ToUpperInvariant(letter.Trim()[0]);
            return c + ":";
        }

        public static Config Load()
        {
            if (!File.Exists(FilePath))
                return null;
            Config c = new Config();
            Dictionary<string, string> kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
            {
                string t = line.Trim();
                if (t.Length == 0 || t.StartsWith("#") || t.StartsWith(";"))
                    continue;
                int i = t.IndexOf('=');
                if (i <= 0) continue;
                kv[t.Substring(0, i).Trim()] = t.Substring(i + 1).Trim();
            }
            for (int n = 1; n <= MaxShares; n++)
            {
                string p, l, d;
                kv.TryGetValue("Path" + n, out p);
                kv.TryGetValue("Label" + n, out l);
                kv.TryGetValue("Letter" + n, out d);
                ShareEntry e = new ShareEntry();
                e.Path = string.IsNullOrEmpty(p) ? "" : NormalizeUnc(p);
                e.Label = l ?? "";
                e.Letter = string.IsNullOrEmpty(d) ? "" : NormLetter(d);
                if (IsFilled(e) || !string.IsNullOrEmpty(e.Path) || !string.IsNullOrEmpty(e.Label))
                    c.Shares.Add(e);
            }
            if (c.Shares.Count == 0)
                return null;
            return c;
        }

        public void Save()
        {
            Directory.CreateDirectory(Dir);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Count=" + Filled().Count);
            int n = 1;
            foreach (ShareEntry e in Filled())
            {
                sb.AppendLine("Path" + n + "=" + (e.Path ?? ""));
                sb.AppendLine("Label" + n + "=" + (e.Label ?? ""));
                sb.AppendLine("Letter" + n + "=" + (e.Letter ?? ""));
                n++;
            }
            for (; n <= MaxShares; n++)
            {
                sb.AppendLine("Path" + n + "=");
                sb.AppendLine("Label" + n + "=");
                sb.AppendLine("Letter" + n + "=");
            }
            File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
        }

        public static void Delete()
        {
            try
            {
                if (File.Exists(FilePath))
                    File.Delete(FilePath);
            }
            catch { }
        }
    }

    sealed class Row
    {
        public Panel Host;
        public TextBox Path;
        public TextBox Label;
        public ComboBox Letter;
    }

    sealed class SetupForm : Form
    {
        NumericUpDown nudCount;
        Panel rowsHost;
        Label emptyHint;
        List<Row> rows = new List<Row>();
        Button ok;
        Button cancel;
        public Config Result { get; private set; }

        public SetupForm(Config existing)
        {
            Text = "Сетевые диски  ·  CSL.by";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(560, 590);
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.FromArgb(249, 249, 255);

            Color orange = Color.FromArgb(247, 99, 27);
            Color dark = Color.FromArgb(40, 44, 52);

            Panel header = new Panel();
            header.SetBounds(0, 0, 560, 56);
            header.BackColor = dark;
            Label brand = new Label();
            brand.Text = "CSL.by  ·  Компьютерная безопасность";
            brand.ForeColor = Color.White;
            brand.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            brand.AutoSize = true;
            brand.Location = new Point(14, 8);
            Label sub = new Label();
            sub.Text = "Системное администрирование";
            sub.ForeColor = orange;
            sub.Font = new Font("Segoe UI", 8.5F);
            sub.AutoSize = true;
            sub.Location = new Point(14, 32);
            header.Controls.Add(brand);
            header.Controls.Add(sub);
            Controls.Add(header);

            Panel footer = new Panel();
            footer.SetBounds(0, 532, 560, 58);
            footer.BackColor = dark;
            LinkLabel web = new LinkLabel();
            web.Text = "www.csl.by";
            web.LinkColor = orange;
            web.ActiveLinkColor = Color.White;
            web.VisitedLinkColor = orange;
            web.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            web.AutoSize = true;
            web.Location = new Point(14, 10);
            web.LinkClicked += delegate
            {
                try { Process.Start("http://csl.by"); }
                catch { }
            };
            Label dev = new Label();
            dev.AutoSize = false;
            dev.SetBounds(110, 6, 440, 46);
            dev.ForeColor = Color.FromArgb(210, 210, 210);
            dev.Font = new Font("Segoe UI", 8F);
            dev.Text = "Разработчик: ООО «Компьютерная безопасность»\nМинск, ул. Фабрициуса, 9  ·  +375 29 337-14-12  ·  +375 29 671-38-22\ninfo@csl.by";
            footer.Controls.Add(web);
            footer.Controls.Add(dev);
            Controls.Add(footer);

            Label hint = new Label();
            hint.AutoSize = false;
            hint.SetBounds(12, 64, 536, 40);
            hint.ForeColor = Color.FromArgb(60, 64, 72);
            hint.Text = "Путь: \\\\IP_или_имя\\шара (можно без \\\\). Shift — править, Ctrl — заново.\nКоличество 0 — стереть сохранённые данные, отключить диски и убрать ярлыки.";

            Label lc = new Label();
            lc.Text = "Количество дисков:";
            lc.AutoSize = true;
            lc.Location = new Point(12, 112);
            lc.ForeColor = Color.FromArgb(40, 44, 52);

            nudCount = new NumericUpDown();
            nudCount.Minimum = 0;
            nudCount.Maximum = Config.MaxShares;
            nudCount.Value = 0;
            nudCount.SetBounds(160, 108, 50, 24);
            nudCount.ValueChanged += delegate { ApplyCount(); };

            rowsHost = new Panel();
            rowsHost.SetBounds(8, 140, 544, 318);
            rowsHost.BackColor = Color.White;
            rowsHost.AutoScroll = true;
            rowsHost.BorderStyle = BorderStyle.FixedSingle;

            emptyHint = new Label();
            emptyHint.AutoSize = false;
            emptyHint.SetBounds(8, 140, 544, 318);
            emptyHint.TextAlign = ContentAlignment.MiddleCenter;
            emptyHint.ForeColor = Color.FromArgb(120, 124, 132);
            emptyHint.BackColor = Color.White;
            emptyHint.Font = new Font("Segoe UI", 10F);
            emptyHint.Text = "Укажите количество дисков (1–10),\nчтобы появились поля для путей и ярлыков.\n\n0 — отключить всё и стереть настройки.";
            emptyHint.Visible = false;
            emptyHint.BorderStyle = BorderStyle.FixedSingle;

            for (int i = 0; i < Config.MaxShares; i++)
                rows.Add(BuildRow(i));

            ok = new Button();
            ok.Text = "Сохранить и подключить";
            ok.SetBounds(248, 468, 196, 32);
            ok.BackColor = Color.FromArgb(247, 99, 27);
            ok.ForeColor = Color.White;
            ok.FlatStyle = FlatStyle.Flat;
            ok.FlatAppearance.BorderSize = 0;
            ok.UseVisualStyleBackColor = false;
            ok.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            ok.Click += OnOk;

            cancel = new Button();
            cancel.Text = "Отмена";
            cancel.SetBounds(452, 468, 96, 32);
            cancel.FlatStyle = FlatStyle.Flat;
            cancel.UseVisualStyleBackColor = false;
            cancel.BackColor = Color.FromArgb(230, 232, 236);
            cancel.ForeColor = Color.FromArgb(40, 44, 52);
            cancel.DialogResult = DialogResult.Cancel;

            AcceptButton = ok;
            CancelButton = cancel;

            Controls.Add(hint);
            Controls.Add(lc);
            Controls.Add(nudCount);
            Controls.Add(rowsHost);
            Controls.Add(emptyHint);
            Controls.Add(ok);
            Controls.Add(cancel);

            int count = 0;
            if (existing != null && existing.Shares != null && existing.Shares.Count > 0)
            {
                count = existing.Shares.Count;
                if (count > Config.MaxShares) count = Config.MaxShares;
                for (int i = 0; i < count; i++)
                {
                    ShareEntry e = existing.Shares[i];
                    rows[i].Path.Text = e.Path ?? "";
                    rows[i].Label.Text = e.Label ?? "";
                    SelectLetter(rows[i].Letter, e.Letter);
                }
            }

            nudCount.Value = count;
            ApplyCount();
        }

        Row BuildRow(int index)
        {
            int y = 8 + index * 70;
            Row r = new Row();
            r.Host = new Panel();
            r.Host.SetBounds(8, y, 510, 66);

            Label lp = new Label();
            lp.Text = "Диск " + (index + 1) + ":";
            lp.AutoSize = true;
            lp.Location = new Point(0, 6);

            r.Path = new TextBox();
            r.Path.SetBounds(70, 2, 350, 24);

            Label ld = new Label();
            ld.Text = "Буква:";
            ld.AutoSize = true;
            ld.Location = new Point(428, 6);

            r.Letter = new ComboBox();
            r.Letter.DropDownStyle = ComboBoxStyle.DropDownList;
            r.Letter.SetBounds(470, 2, 38, 24);
            for (char c = 'D'; c <= 'Z'; c++)
                r.Letter.Items.Add(c + ":");

            Label ln = new Label();
            ln.Text = "Ярлык:";
            ln.AutoSize = true;
            ln.Location = new Point(0, 38);

            r.Label = new TextBox();
            r.Label.SetBounds(70, 34, 438, 24);

            r.Host.Controls.Add(lp);
            r.Host.Controls.Add(r.Path);
            r.Host.Controls.Add(ld);
            r.Host.Controls.Add(r.Letter);
            r.Host.Controls.Add(ln);
            r.Host.Controls.Add(r.Label);
            rowsHost.Controls.Add(r.Host);
            return r;
        }

        void ApplyCount()
        {
            int n = (int)nudCount.Value;
            bool none = n == 0;
            if (emptyHint != null)
            {
                emptyHint.Visible = none;
                if (none) emptyHint.BringToFront();
            }
            if (rowsHost != null)
            {
                rowsHost.Visible = !none;
                if (!none) rowsHost.BringToFront();
            }
            if (ok != null)
                ok.Text = none ? "Стереть и отключить" : "Сохранить и подключить";
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].Host.Visible = i < n;
                if (i < n && rows[i].Letter.SelectedItem == null)
                    rows[i].Letter.SelectedItem = DefaultLetter(i);
            }
        }

        static string DefaultLetter(int index)
        {
            char c = (char)('Z' - index);
            if (c < 'D') c = 'D';
            return c + ":";
        }

        static void SelectLetter(ComboBox cb, string letter)
        {
            if (string.IsNullOrEmpty(letter)) return;
            string n = Config.NormLetter(letter);
            int i = cb.Items.IndexOf(n);
            if (i >= 0) cb.SelectedIndex = i;
        }

        void OnOk(object sender, EventArgs e)
        {
            int n = (int)nudCount.Value;
            if (n == 0)
            {
                Result = new Config();
                Result.Shares = new List<ShareEntry>();
                DialogResult = DialogResult.OK;
                return;
            }
            List<ShareEntry> list = new List<ShareEntry>();
            HashSet<string> letters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < n; i++)
            {
                string pathRaw = (rows[i].Path.Text ?? "").Trim();
                string label = (rows[i].Label.Text ?? "").Trim();
                string letRaw = rows[i].Letter.SelectedItem == null ? "" : rows[i].Letter.SelectedItem.ToString();
                int num = i + 1;

                if (pathRaw.Length == 0 && label.Length == 0)
                {
                    MessageBox.Show("Диск " + num + ": заполните путь, букву и ярлык (или уменьшите количество).",
                        "Проверка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string path = Config.NormalizeUnc(pathRaw);
                if (!Config.IsUnc(path))
                {
                    MessageBox.Show("Диск " + num + ": путь как \\\\IP_или_имя\\шара\nНапример: \\\\192.168.0.10\\docs",
                        "Проверка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (label.Length == 0)
                {
                    MessageBox.Show("Диск " + num + ": укажите название ярлыка.",
                        "Проверка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (!Config.IsLetter(letRaw))
                {
                    MessageBox.Show("Диск " + num + ": выберите букву диска.",
                        "Проверка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                string letter = Config.NormLetter(letRaw);
                string nice = SanitizeFileName(label);
                if (!letters.Add(letter))
                {
                    MessageBox.Show("Буква " + letter + " указана дважды.",
                        "Проверка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (!labels.Add(nice))
                {
                    MessageBox.Show("Ярлык «" + nice + "» указан дважды.",
                        "Проверка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                ShareEntry ent = new ShareEntry();
                ent.Path = path;
                ent.Label = nice;
                ent.Letter = letter;
                list.Add(ent);
            }

            Result = new Config();
            Result.Shares = list;
            DialogResult = DialogResult.OK;
        }

        static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }

    static class Mapper
    {
        const int CONNECT_TEMPORARY = 0x00000004;
        const int RESOURCETYPE_DISK = 0x00000001;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct NETRESOURCE
        {
            public int dwScope;
            public int dwType;
            public int dwDisplayType;
            public int dwUsage;
            public string lpLocalName;
            public string lpRemoteName;
            public string lpComment;
            public string lpProvider;
        }

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        static extern int WNetAddConnection2(ref NETRESOURCE netResource, string password, string username, int flags);

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        static extern int WNetCancelConnection2(string lpName, int dwFlags, bool fForce);

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        static extern int WNetGetConnection(string localName, StringBuilder remoteName, ref int length);

        public static void UnmountAll(Config cfg)
        {
            if (cfg == null) return;
            foreach (ShareEntry e in cfg.Filled())
            {
                DisconnectOne(e);
                DeleteShortcut(e.Label);
            }
        }

        public static void Remount(Config cfg)
        {
            List<ShareEntry> list = cfg.Filled();
            foreach (ShareEntry e in list)
            {
                e.Path = Config.NormalizeUnc(e.Path);
                e.Letter = Config.NormLetter(e.Letter);
                DisconnectOne(e);
                DeleteShortcut(e.Label);
            }
            foreach (ShareEntry e in list)
            {
                ConnectOne(e);
                CreateShortcut(e);
            }
        }

        static void DisconnectOne(ShareEntry e)
        {
            if (!string.IsNullOrEmpty(e.Letter))
                WNetCancelConnection2(e.Letter, 0, true);
            try
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    if (!d.IsReady) continue;
                    try
                    {
                        if (string.Equals(UncOf(d.Name), e.Path, StringComparison.OrdinalIgnoreCase))
                            WNetCancelConnection2(d.Name.TrimEnd('\\'), 0, true);
                    }
                    catch { }
                }
            }
            catch { }
            System.Threading.Thread.Sleep(200);
        }

        static string UncOf(string drive)
        {
            StringBuilder sb = new StringBuilder(512);
            int len = sb.Capacity;
            int rc = WNetGetConnection(drive.TrimEnd('\\'), sb, ref len);
            if (rc == 0) return sb.ToString();
            return null;
        }

        static void ConnectOne(ShareEntry e)
        {
            NETRESOURCE nr = new NETRESOURCE();
            nr.dwType = RESOURCETYPE_DISK;
            nr.lpLocalName = e.Letter;
            nr.lpRemoteName = e.Path;
            int rc = WNetAddConnection2(ref nr, null, null, CONNECT_TEMPORARY);
            if (rc != 0)
            {
                throw new InvalidOperationException(
                    "Не удалось подключить " + e.Path + " как " + e.Letter +
                    " (код " + rc + "). Проверьте путь и доступ.");
            }
        }

        static string Desktop
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); }
        }

        static void DeleteShortcut(string label)
        {
            if (string.IsNullOrEmpty(label)) return;
            string lnk = Path.Combine(Desktop, label + ".lnk");
            try
            {
                if (File.Exists(lnk))
                    File.Delete(lnk);
            }
            catch { }
        }

        [DllImport("shell32.dll")]
        static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        const uint SHCNE_ASSOCCHANGED = 0x08000000;
        const uint SHCNE_UPDATEITEM = 0x00002000;
        const uint SHCNF_IDLIST = 0x0000;
        const uint SHCNF_PATHW = 0x0005;
        const uint SHCNF_FLUSH = 0x1000;

        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        class ShellLink { }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, int fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
            void Resolve(IntPtr hwnd, int fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("0000010b-0000-0000-C000-000000000046")]
        interface IPersistFile
        {
            void GetClassID(out Guid pClassID);
            [PreserveSig]
            int IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
        }

        public static void OpenDrive(string letter)
        {
            letter = Config.NormLetter(letter);
            Config cfg = Config.Load();
            if (cfg != null)
            {
                foreach (ShareEntry e in cfg.Filled())
                {
                    if (string.Equals(Config.NormLetter(e.Letter), letter, StringComparison.OrdinalIgnoreCase))
                    {
                        try { ConnectOne(e); }
                        catch { }
                        break;
                    }
                }
            }
            Process.Start("explorer.exe", letter + "\\");
        }

        static void CreateShortcut(ShareEntry e)
        {
            string lnk = Path.Combine(Desktop, e.Label + ".lnk");
            string root = e.Letter.TrimEnd('\\') + "\\";
            string exe = Application.ExecutablePath;
            string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            if (!File.Exists(explorer))
                explorer = Path.Combine(Environment.SystemDirectory, "explorer.exe");

            Type t = Type.GetTypeFromProgID("WScript.Shell");
            if (t == null)
                throw new InvalidOperationException("WScript.Shell недоступен.");
            object wsh = Activator.CreateInstance(t);
            object sc = t.InvokeMember("CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod, null, wsh, new object[] { lnk });
            Type scType = sc.GetType();
            System.Reflection.BindingFlags set = System.Reflection.BindingFlags.SetProperty;
            scType.InvokeMember("TargetPath", set, null, sc, new object[] { explorer });
            scType.InvokeMember("Arguments", set, null, sc, new object[] { root });
            scType.InvokeMember("WorkingDirectory", set, null, sc, new object[] { root });
            scType.InvokeMember("WindowStyle", set, null, sc, new object[] { 1 });
            scType.InvokeMember("Description", set, null, sc, new object[] { e.Path ?? "" });
            scType.InvokeMember("IconLocation", set, null, sc, new object[] { exe + ",0" });
            scType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, sc, null);

            IntPtr p = Marshal.StringToHGlobalUni(lnk);
            try
            {
                SHChangeNotify(SHCNE_UPDATEITEM, SHCNF_PATHW | SHCNF_FLUSH, p, IntPtr.Zero);
            }
            finally
            {
                Marshal.FreeHGlobal(p);
            }
        }
    }
}
