using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("screenless")]
[assembly: AssemblyProduct("screenless")]
[assembly: AssemblyDescription("Android mirroring without the terminal")]
[assembly: AssemblyCompany("Vighnesh2514")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace Screenless
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool hidden = false;
            foreach (string a in args)
            {
                if (a == "--tray") hidden = true;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; }
            catch { }
            Application.Run(new MainForm(hidden));
        }
    }

    class Device
    {
        public string Serial;
        public string State;
        public string Model;
        public bool Usb;

        public override string ToString()
        {
            string name = string.IsNullOrEmpty(Model) ? Serial : Model.Replace("_", " ");
            return name + "   ·   " + (Usb ? "USB" : "Wi-Fi") + "   ·   " + State;
        }
    }

    class MainForm : Form
    {
        static readonly Color Bg = Color.FromArgb(10, 10, 10);
        static readonly Color Card = Color.FromArgb(17, 17, 17);
        static readonly Color Line = Color.FromArgb(38, 38, 38);
        static readonly Color Fg = Color.FromArgb(245, 245, 245);
        static readonly Color Muted = Color.FromArgb(140, 140, 140);
        static readonly Color Red = Color.FromArgb(255, 42, 42);
        static readonly Color Green = Color.FromArgb(40, 205, 90);
        static readonly Color Amber = Color.FromArgb(240, 180, 40);

        string adb;
        string scrcpy;
        List<Device> devices = new List<Device>();
        Process mirror;
        bool refreshing;
        bool working;
        bool exiting;
        bool balloonShown;
        bool installStarted;
        bool startHidden;
        Icon appIcon;
        NotifyIcon tray;
        ToolStripMenuItem connectItem;

        Panel statusDot;
        Color statusColor = Muted;
        Label statusTitle;
        Label statusDetail;
        ComboBox deviceCombo;
        TextBox ipText;
        Button refreshBtn;
        Button connectBtn;
        Button wifiBtn;
        Button startBtn;
        Button stopBtn;
        ComboBox qualityCombo;
        CheckBox audioCheck;
        CheckBox startupCheck;
        TextBox logBox;
        Label toolsLabel;
        System.Windows.Forms.Timer timer;

        public MainForm(bool startHiddenForm)
        {
            startHidden = startHiddenForm;
            Text = "screenless";
            ClientSize = new Size(560, 700);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Bg;
            ForeColor = Fg;
            Font = new Font("Segoe UI", 9f);
            appIcon = LoadAppIcon();
            Icon = appIcon;

            BuildUi();
            BuildTray();
            LoadSettings();
            RefreshTools();
            RefreshDevices();

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 6000;
            timer.Tick += delegate
            {
                if (Visible && !working && !refreshing && mirror == null) RefreshDevices();
            };
            timer.Start();

            Shown += delegate
            {
                if (startHidden)
                {
                    Hide();
                    ShowInTaskbar = false;
                }
            };
        }

        /* ---------- look and feel ---------- */

        Label MakeLabel(string text, int x, int y, int w, int h, float size, Color color, bool bold, bool mono)
        {
            Label l = new Label();
            l.Text = text;
            l.Location = new Point(x, y);
            l.Size = new Size(w, h);
            l.ForeColor = color;
            l.BackColor = Color.Transparent;
            l.Font = new Font(mono ? "Consolas" : "Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular);
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.AutoSize = false;
            return l;
        }

        Button MakeButton(string text, int x, int y, int w, int h, bool primary)
        {
            Button b = new Button();
            b.Text = text;
            b.Location = new Point(x, y);
            b.Size = new Size(w, h);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = primary ? 0 : 1;
            b.FlatAppearance.BorderColor = Line;
            b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(220, 32, 32) : Color.FromArgb(30, 30, 30);
            b.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(190, 24, 24) : Color.FromArgb(40, 40, 40);
            b.BackColor = primary ? Red : Card;
            b.ForeColor = primary ? Color.Black : Fg;
            b.Font = new Font("Segoe UI", primary ? 11.5f : 9f, primary ? FontStyle.Bold : FontStyle.Regular);
            b.Cursor = Cursors.Hand;
            b.TabStop = false;
            b.UseVisualStyleBackColor = false;
            return b;
        }

        Panel MakeCard(int x, int y, int w, int h)
        {
            Panel p = new Panel();
            p.Location = new Point(x, y);
            p.Size = new Size(w, h);
            p.BackColor = Card;
            p.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Line))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
                }
            };
            return p;
        }

        Panel MakeDot(int x, int y, int size, Color color, Color background)
        {
            Panel p = new Panel();
            p.Location = new Point(x, y);
            p.Size = new Size(size, size);
            p.BackColor = background;
            p.Tag = color;
            p.Paint += delegate(object s, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                Color c = (Color)((Panel)s).Tag;
                using (Brush b = new SolidBrush(c))
                {
                    e.Graphics.FillEllipse(b, 0, 0, p.Width - 1, p.Height - 1);
                }
            };
            return p;
        }

        void BuildUi()
        {
            SuspendLayout();

            Panel logoDot = MakeDot(20, 24, 10, Red, Bg);
            Controls.Add(logoDot);
            Controls.Add(MakeLabel("screenless", 38, 8, 300, 30, 18f, Fg, true, false));
            Controls.Add(MakeLabel("Android on your laptop, without the terminal", 40, 46, 420, 20, 8.5f, Muted, false, false));

            Panel card = MakeCard(20, 74, 520, 84);
            statusDot = MakeDot(16, 20, 10, Muted, Card);
            card.Controls.Add(statusDot);
            statusTitle = MakeLabel("Checking your phone ...", 38, 10, 460, 26, 12f, Fg, true, false);
            statusDetail = MakeLabel("", 38, 38, 460, 36, 8.5f, Muted, false, false);
            card.Controls.Add(statusTitle);
            card.Controls.Add(statusDetail);
            Controls.Add(card);

            Controls.Add(MakeLabel("PHONE", 20, 176, 200, 16, 8f, Muted, true, true));

            deviceCombo = new ComboBox();
            deviceCombo.Location = new Point(20, 196);
            deviceCombo.Size = new Size(400, 28);
            deviceCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            deviceCombo.FlatStyle = FlatStyle.Flat;
            deviceCombo.BackColor = Card;
            deviceCombo.ForeColor = Fg;
            deviceCombo.Font = new Font("Segoe UI", 9.5f);
            deviceCombo.DrawMode = DrawMode.OwnerDrawFixed;
            deviceCombo.ItemHeight = 22;
            deviceCombo.DrawItem += delegate(object s, DrawItemEventArgs e)
            {
                bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
                using (Brush b = new SolidBrush(selected ? Color.FromArgb(36, 36, 36) : Card))
                {
                    e.Graphics.FillRectangle(b, e.Bounds);
                }
                if (e.Index >= 0)
                {
                    string text = deviceCombo.Items[e.Index].ToString();
                    TextRenderer.DrawText(e.Graphics, text, deviceCombo.Font, e.Bounds, Fg,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                }
            };
            Controls.Add(deviceCombo);

            refreshBtn = MakeButton("Refresh", 428, 196, 112, 28, false);
            refreshBtn.Click += delegate { RefreshDevices(); };
            Controls.Add(refreshBtn);

            Controls.Add(MakeLabel("WI-FI IP", 20, 240, 200, 16, 8f, Muted, true, true));

            ipText = new TextBox();
            ipText.Location = new Point(20, 260);
            ipText.Size = new Size(336, 28);
            ipText.BackColor = Card;
            ipText.ForeColor = Fg;
            ipText.BorderStyle = BorderStyle.FixedSingle;
            ipText.Font = new Font("Consolas", 10.5f);
            Controls.Add(ipText);

            connectBtn = MakeButton("Connect", 364, 260, 84, 26, false);
            connectBtn.Click += delegate { ConnectIp(); };
            Controls.Add(connectBtn);

            Controls.Add(MakeLabel("On the phone: Settings → Wi-Fi → your network → IP address", 20, 292, 520, 18, 8f, Muted, false, false));

            wifiBtn = MakeButton("Turn on Wi-Fi mode (plug in USB first)", 20, 318, 520, 36, false);
            wifiBtn.Click += delegate { TurnOnWifi(); };
            Controls.Add(wifiBtn);

            startBtn = MakeButton("Start mirror", 20, 364, 520, 50, true);
            startBtn.Click += delegate { StartMirror(); };
            Controls.Add(startBtn);

            stopBtn = MakeButton("Stop mirror", 20, 364, 520, 50, false);
            stopBtn.Visible = false;
            stopBtn.Click += delegate { StopMirror(); };
            Controls.Add(stopBtn);

            Controls.Add(MakeLabel("QUALITY", 20, 472, 200, 16, 8f, Muted, true, true));

            qualityCombo = new ComboBox();
            qualityCombo.Location = new Point(20, 490);
            qualityCombo.Size = new Size(190, 26);
            qualityCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            qualityCombo.FlatStyle = FlatStyle.Flat;
            qualityCombo.BackColor = Card;
            qualityCombo.ForeColor = Fg;
            qualityCombo.Font = new Font("Segoe UI", 9f);
            qualityCombo.Items.Add("Auto (recommended)");
            qualityCombo.Items.Add("Best quality");
            qualityCombo.Items.Add("Smooth (weak Wi-Fi)");
            qualityCombo.SelectedIndex = 0;
            qualityCombo.SelectedIndexChanged += delegate { SaveSetting("Quality", qualityCombo.SelectedIndex); };
            Controls.Add(qualityCombo);

            audioCheck = new CheckBox();
            audioCheck.Text = "Phone audio";
            audioCheck.Location = new Point(224, 492);
            audioCheck.Size = new Size(130, 24);
            audioCheck.ForeColor = Fg;
            audioCheck.BackColor = Bg;
            audioCheck.FlatStyle = FlatStyle.Flat;
            audioCheck.Font = new Font("Segoe UI", 9f);
            audioCheck.CheckedChanged += delegate { SaveSetting("Audio", audioCheck.Checked ? 1 : 0); };
            Controls.Add(audioCheck);

            startupCheck = new CheckBox();
            startupCheck.Text = "Start with Windows";
            startupCheck.Location = new Point(364, 492);
            startupCheck.Size = new Size(180, 24);
            startupCheck.ForeColor = Fg;
            startupCheck.BackColor = Bg;
            startupCheck.FlatStyle = FlatStyle.Flat;
            startupCheck.Font = new Font("Segoe UI", 9f);
            startupCheck.CheckedChanged += delegate
            {
                try { SetStartup(startupCheck.Checked); }
                catch (Exception ex) { Log("Could not change startup: " + ex.Message); }
            };
            Controls.Add(startupCheck);

            Controls.Add(MakeLabel("LOG", 20, 532, 200, 16, 8f, Muted, true, true));

            logBox = new TextBox();
            logBox.Location = new Point(20, 550);
            logBox.Size = new Size(520, 100);
            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.ScrollBars = ScrollBars.Vertical;
            logBox.BackColor = Color.FromArgb(7, 7, 7);
            logBox.ForeColor = Color.FromArgb(205, 205, 205);
            logBox.BorderStyle = BorderStyle.FixedSingle;
            logBox.Font = new Font("Consolas", 8.4f);
            Controls.Add(logBox);

            toolsLabel = MakeLabel("scrcpy + adb", 20, 664, 300, 20, 8f, Muted, false, true);
            Controls.Add(toolsLabel);

            LinkLabel github = new LinkLabel();
            github.Text = "GitHub";
            github.Location = new Point(410, 664);
            github.Size = new Size(60, 20);
            github.LinkColor = Red;
            github.ActiveLinkColor = Fg;
            github.VisitedLinkColor = Red;
            github.LinkBehavior = LinkBehavior.HoverUnderline;
            github.BackColor = Bg;
            github.Font = new Font("Consolas", 8.5f);
            github.LinkClicked += delegate { OpenUrl("https://github.com/Vighnesh2514/screenless-android"); };
            Controls.Add(github);

            LinkLabel guide = new LinkLabel();
            guide.Text = "Guide";
            guide.Location = new Point(478, 664);
            guide.Size = new Size(62, 20);
            guide.LinkColor = Red;
            guide.ActiveLinkColor = Fg;
            guide.VisitedLinkColor = Red;
            guide.LinkBehavior = LinkBehavior.HoverUnderline;
            guide.BackColor = Bg;
            guide.Font = new Font("Consolas", 8.5f);
            guide.LinkClicked += delegate { OpenUrl("https://github.com/Vighnesh2514/screenless-android/blob/main/docs/WINDOWS.md"); };
            Controls.Add(guide);

            ToolTip tip = new ToolTip();
            tip.SetToolTip(ipText, "On the phone: Settings → Wi-Fi → your network → IP address");
            tip.SetToolTip(wifiBtn, "Plug in USB first. Saves the phone's IP and turns wireless mode on.");
            tip.SetToolTip(startBtn, "Open the phone in a window on this laptop.");
            tip.SetToolTip(connectBtn, "Connect over Wi-Fi to the IP above.");
            tip.SetToolTip(qualityCombo, "Auto uses USB quality on cable and lighter quality on Wi-Fi.");

            ResumeLayout(false);
        }

        void BuildTray()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.BackColor = Card;
            menu.ForeColor = Fg;
            menu.Renderer = new ToolStripProfessionalRenderer();

            ToolStripMenuItem open = new ToolStripMenuItem("Open screenless");
            open.Font = new Font(open.Font, FontStyle.Bold);
            open.Click += delegate { ShowApp(); };
            menu.Items.Add(open);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem start = new ToolStripMenuItem("Start mirror");
            start.Click += delegate { StartMirror(); };
            menu.Items.Add(start);

            ToolStripMenuItem stop = new ToolStripMenuItem("Stop mirror");
            stop.Click += delegate { StopMirror(); };
            menu.Items.Add(stop);

            ToolStripMenuItem wifi = new ToolStripMenuItem("Turn on Wi-Fi mode");
            wifi.Click += delegate { TurnOnWifi(); };
            menu.Items.Add(wifi);

            connectItem = new ToolStripMenuItem("Connect to last IP");
            connectItem.Click += delegate { ConnectIp(); };
            menu.Items.Add(connectItem);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem startup = new ToolStripMenuItem("Start with Windows");
            startup.Click += delegate
            {
                try
                {
                    bool next = !IsStartupEnabled();
                    SetStartup(next);
                    if (startupCheck != null) startupCheck.Checked = next;
                    startup.Checked = next;
                }
                catch (Exception ex) { Log("Could not change startup: " + ex.Message); }
            };
            menu.Items.Add(startup);

            ToolStripMenuItem exit = new ToolStripMenuItem("Exit");
            exit.Click += delegate { ExitApp(); };
            menu.Items.Add(exit);

            menu.Opening += delegate
            {
                stop.Enabled = mirror != null;
                start.Enabled = mirror == null;
                startup.Checked = IsStartupEnabled();
                connectItem.Text = ipText.Text.Trim().Length > 0
                    ? "Connect to " + ipText.Text.Trim()
                    : "Connect to last IP";
            };

            tray = new NotifyIcon();
            tray.Icon = appIcon;
            tray.Text = "screenless";
            tray.Visible = true;
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { ShowApp(); };
        }

        /* ---------- helpers ---------- */

        void UI(MethodInvoker action)
        {
            if (IsDisposed) return;
            try
            {
                if (InvokeRequired) BeginInvoke(action);
                else action();
            }
            catch { }
        }

        void Log(string message)
        {
            UI(delegate
            {
                if (logBox == null) return;
                logBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "]  " + message + "\r\n");
                if (logBox.TextLength > 24000) logBox.Text = logBox.Text.Substring(logBox.TextLength - 12000);
                logBox.SelectionStart = logBox.TextLength;
                logBox.ScrollToCaret();
            });
        }

        void SetStatus(string title, string detail, Color color)
        {
            statusColor = color;
            UI(delegate
            {
                if (statusTitle == null) return;
                statusTitle.Text = title;
                statusDetail.Text = detail;
                if (statusDot != null)
                {
                    statusDot.Tag = color;
                    statusDot.Invalidate();
                }
                if (tray != null) tray.Text = "screenless — " + title;
            });
        }

        void ShowApp()
        {
            Show();
            ShowInTaskbar = true;
            WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        void ExitApp()
        {
            exiting = true;
            if (tray != null) tray.Visible = false;
            if (mirror != null)
            {
                try { mirror.Kill(); } catch { }
            }
            Close();
            Application.Exit();
        }

        void OpenUrl(string url)
        {
            try { Process.Start(url); }
            catch (Exception ex) { Log("Could not open the link: " + ex.Message); }
        }

        Icon LoadAppIcon()
        {
            try
            {
                Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("screenless.ico");
                if (s != null) return new Icon(s);
            }
            catch { }
            return SystemIcons.Application;
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int dark = 1;
                if (DwmSetWindowAttribute(Handle, 20, ref dark, 4) != 0)
                {
                    DwmSetWindowAttribute(Handle, 19, ref dark, 4);
                }
            }
            catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                ShowInTaskbar = false;
                if (!balloonShown && tray != null)
                {
                    tray.ShowBalloonTip(2500, "screenless is still running",
                        "Right-click the tray icon to open it or exit.", ToolTipIcon.Info);
                    balloonShown = true;
                }
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized) Hide();
        }

        /* ---------- settings ---------- */

        void LoadSettings()
        {
            try
            {
                RegistryKey k = Registry.CurrentUser.OpenSubKey("Software\\screenless");
                string ip = "";
                int quality = 0;
                int audio = 0;
                if (k != null)
                {
                    object v = k.GetValue("LastIp");
                    if (v != null) ip = v.ToString();
                    v = k.GetValue("Quality");
                    if (v != null) quality = Convert.ToInt32(v);
                    v = k.GetValue("Audio");
                    if (v != null) audio = Convert.ToInt32(v);
                    k.Close();
                }
                if (ip.Length == 0)
                {
                    string file = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "screenless"), "last-ip.txt");
                    if (File.Exists(file)) ip = File.ReadAllText(file).Trim();
                }
                ipText.Text = ip;
                if (quality < 0 || quality > 2) quality = 0;
                qualityCombo.SelectedIndex = quality;
                audioCheck.Checked = audio == 1;
                startupCheck.Checked = IsStartupEnabled();
            }
            catch { }
        }

        void SaveSetting(string name, object value)
        {
            try
            {
                RegistryKey k = Registry.CurrentUser.CreateSubKey("Software\\screenless");
                k.SetValue(name, value);
                k.Close();
            }
            catch { }
        }

        void SaveIp(string ip)
        {
            SaveSetting("LastIp", ip);
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "screenless");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "last-ip.txt"), ip);
            }
            catch { }
            UI(delegate { ipText.Text = ip; });
        }

        bool IsStartupEnabled()
        {
            try
            {
                RegistryKey rk = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
                if (rk == null) return false;
                bool on = rk.GetValue("screenless") != null;
                rk.Close();
                return on;
            }
            catch { return false; }
        }

        void SetStartup(bool on)
        {
            RegistryKey rk = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", true);
            if (rk == null) return;
            if (on) rk.SetValue("screenless", "\"" + Application.ExecutablePath + "\" --tray");
            else rk.DeleteValue("screenless", false);
            rk.Close();
        }

        /* ---------- tools ---------- */

        string FindOnPath(string file)
        {
            string path = Environment.GetEnvironmentVariable("PATH");
            if (path == null) return null;
            foreach (string dir in path.Split(';'))
            {
                string d = dir.Trim();
                if (d.Length == 0) continue;
                try
                {
                    string full = Path.Combine(d, file);
                    if (File.Exists(full)) return full;
                }
                catch { }
            }
            return null;
        }

        string FindScrcpy()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string onPath = FindOnPath("scrcpy.exe");
            if (onPath != null) return onPath;

            string[] candidates = new string[]
            {
                Path.Combine(Path.Combine(local, "Programs"), "scrcpy\\scrcpy.exe"),
                Path.Combine(Path.Combine(local, "Microsoft\\WinGet\\Links"), "scrcpy.exe")
            };
            foreach (string c in candidates)
            {
                if (File.Exists(c)) return c;
            }

            string packages = Path.Combine(local, "Microsoft\\WinGet\\Packages");
            if (Directory.Exists(packages))
            {
                try
                {
                    string[] hits = Directory.GetFiles(packages, "scrcpy.exe", SearchOption.AllDirectories);
                    if (hits.Length > 0) return hits[0];
                }
                catch { }
            }
            return null;
        }

        string FindAdb()
        {
            if (!string.IsNullOrEmpty(scrcpy))
            {
                string beside = Path.Combine(Path.GetDirectoryName(scrcpy), "adb.exe");
                if (File.Exists(beside)) return beside;
            }
            string onPath = FindOnPath("adb.exe");
            if (onPath != null) return onPath;

            string sdk = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android\\Sdk"), "platform-tools\\adb.exe");
            if (File.Exists(sdk)) return sdk;
            return null;
        }

        void RefreshTools()
        {
            scrcpy = FindScrcpy();
            adb = FindAdb();
            bool ok = scrcpy != null && adb != null;

            UI(delegate
            {
                if (toolsLabel != null)
                {
                    toolsLabel.Text = ok ? "scrcpy + adb ready" : "scrcpy not found — installing ...";
                    toolsLabel.ForeColor = ok ? Muted : Amber;
                }
                if (startBtn != null) startBtn.Enabled = ok;
                if (wifiBtn != null) wifiBtn.Enabled = ok;
                if (connectBtn != null) connectBtn.Enabled = ok;
            });

            if (!ok && !installStarted)
            {
                installStarted = true;
                InstallScrcpy();
            }
        }

        void InstallScrcpy()
        {
            SetStatus("Installing scrcpy", "One-time download of the official Windows build", Amber);
            Log("scrcpy was not found. Downloading the official build ...");
            Thread t = new Thread(delegate()
            {
                try
                {
                    WebClient wc = new WebClient();
                    wc.Headers.Add("User-Agent", "screenless-app");
                    string json = wc.DownloadString("https://api.github.com/repos/Genymobile/scrcpy/releases/latest");
                    Match m = Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]*scrcpy-win64-[^\"]*\\.zip)\"");
                    if (!m.Success) throw new Exception("No Windows build in the latest release.");
                    string url = m.Groups[1].Value;

                    wc.DownloadProgressChanged += delegate(object s, DownloadProgressChangedEventArgs e)
                    {
                        SetStatus("Installing scrcpy", "Downloading ... " + e.ProgressPercentage + "%", Amber);
                    };

                    string zip = Path.Combine(Path.GetTempPath(), "screenless-scrcpy.zip");
                    wc.DownloadFile(url, zip);

                    string temp = Path.Combine(Path.GetTempPath(), "screenless-scrcpy-" + Guid.NewGuid().ToString("N"));
                    ZipFile.ExtractToDirectory(zip, temp);
                    string[] dirs = Directory.GetDirectories(temp);
                    if (dirs.Length == 0) throw new Exception("Unexpected archive layout.");

                    string dest = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"), "scrcpy");
                    if (Directory.Exists(dest)) Directory.Delete(dest, true);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    Directory.Move(dirs[0], dest);
                    try { Directory.Delete(temp, true); } catch { }
                    try { File.Delete(zip); } catch { }

                    AddToUserPath(dest);
                    RefreshTools();
                    Log("scrcpy installed to " + dest);
                    SetStatus("Ready", "scrcpy installed", Green);
                    RefreshDevices();
                }
                catch (Exception ex)
                {
                    Log("Install failed: " + ex.Message);
                    SetStatus("scrcpy is missing", "Download it from github.com/Genymobile/scrcpy/releases and press Refresh", Red);
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        void AddToUserPath(string dir)
        {
            try
            {
                string user = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User);
                if (user == null) user = "";
                bool found = false;
                foreach (string p in user.Split(';'))
                {
                    if (string.Equals(p.Trim(), dir, StringComparison.OrdinalIgnoreCase)) { found = true; break; }
                }
                if (!found)
                {
                    Environment.SetEnvironmentVariable("PATH", user + ";" + dir, EnvironmentVariableTarget.User);
                }
                string current = Environment.GetEnvironmentVariable("PATH");
                if (current == null || current.IndexOf(dir, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    Environment.SetEnvironmentVariable("PATH", current + ";" + dir);
                }
            }
            catch { }
        }

        /* ---------- adb ---------- */

        string Run(string exe, string arguments, int timeoutMs)
        {
            if (string.IsNullOrEmpty(exe)) return "";
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, arguments);
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;

                Process p = new Process();
                p.StartInfo = psi;
                StringBuilder sb = new StringBuilder();
                object gate = new object();
                DataReceivedEventHandler handler = delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data != null)
                    {
                        lock (gate) sb.AppendLine(e.Data);
                    }
                };
                p.OutputDataReceived += handler;
                p.ErrorDataReceived += handler;
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                }
                else
                {
                    p.WaitForExit();
                }
                lock (gate) return sb.ToString().Trim();
            }
            catch (Exception ex)
            {
                return "ERROR: " + ex.Message;
            }
        }

        List<Device> ParseDevices(string output)
        {
            List<Device> list = new List<Device>();
            if (string.IsNullOrEmpty(output)) return list;
            foreach (string raw in output.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("List of devices")) continue;
                if (line.StartsWith("*")) continue;
                string[] parts = Regex.Split(line, "\\s+");
                if (parts.Length < 2) continue;

                Device d = new Device();
                d.Serial = parts[0];
                d.State = parts[1];
                d.Usb = d.Serial.IndexOf(':') < 0;
                Match m = Regex.Match(line, "model:(\\S+)");
                d.Model = m.Success ? m.Groups[1].Value : "";
                list.Add(d);
            }
            return list;
        }

        void RefreshDevices()
        {
            if (refreshing) return;
            if (string.IsNullOrEmpty(adb))
            {
                UpdateStatus();
                return;
            }
            refreshing = true;
            Thread t = new Thread(delegate()
            {
                string output = Run(adb, "devices -l", 8000);
                List<Device> found = ParseDevices(output);
                UI(delegate
                {
                    devices = found;
                    refreshing = false;
                    UpdateDeviceCombo();
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        void UpdateDeviceCombo()
        {
            string keep = null;
            if (deviceCombo.SelectedItem is Device) keep = ((Device)deviceCombo.SelectedItem).Serial;
            deviceCombo.Items.Clear();
            int select = 0;
            for (int i = 0; i < devices.Count; i++)
            {
                deviceCombo.Items.Add(devices[i]);
                if (devices[i].Serial == keep) select = i;
            }
            if (devices.Count > 0) deviceCombo.SelectedIndex = select;
            UpdateStatus();
            UpdateMirrorUi();
        }

        Device SelectedDevice()
        {
            if (deviceCombo.SelectedItem is Device) return (Device)deviceCombo.SelectedItem;
            return null;
        }

        Device FirstDevice()
        {
            Device usb = null;
            Device wifi = null;
            foreach (Device d in devices)
            {
                if (d.State != "device") continue;
                if (d.Usb && usb == null) usb = d;
                if (!d.Usb && wifi == null) wifi = d;
            }
            if (usb != null) return usb;
            return wifi;
        }

        Device FirstUsb()
        {
            foreach (Device d in devices)
            {
                if (d.Usb && d.State == "device") return d;
            }
            return null;
        }

        void UpdateStatus()
        {
            if (mirror != null)
            {
                SetStatus("Mirroring", "The phone is open in a window on this laptop", Green);
                return;
            }
            Device d = FirstDevice();
            if (d != null)
            {
                string name = string.IsNullOrEmpty(d.Model) ? d.Serial : d.Model.Replace("_", " ");
                SetStatus(name + " connected", (d.Usb ? "USB" : "Wi-Fi") + "  ·  " + d.Serial, Amber);
                return;
            }
            foreach (Device bad in devices)
            {
                if (bad.State == "unauthorized" || bad.State == "offline")
                {
                    SetStatus("Phone needs permission", "Unlock the phone and tap Allow, or replug the cable.", Red);
                    return;
                }
            }
            SetStatus("Phone not connected", "Plug in USB, or enter the Wi-Fi IP and press Connect.", Muted);
        }

        void UpdateMirrorUi()
        {
            bool running = mirror != null;
            bool ready = !working && !string.IsNullOrEmpty(scrcpy) && !string.IsNullOrEmpty(adb);
            if (startBtn != null) { startBtn.Visible = !running; startBtn.Enabled = ready; }
            if (stopBtn != null) { stopBtn.Visible = running; stopBtn.Enabled = running; }
            if (wifiBtn != null) { wifiBtn.Enabled = !working && adb != null; }
            if (connectBtn != null) { connectBtn.Enabled = !working && adb != null; }
            if (refreshBtn != null) { refreshBtn.Enabled = !working; }
            if (deviceCombo != null) { deviceCombo.Enabled = !working; }
        }

        void SetWorking(bool value)
        {
            working = value;
            UI(delegate { UpdateMirrorUi(); });
        }

        /* ---------- actions ---------- */

        void TurnOnWifi()
        {
            if (adb == null) { Log("adb is not ready yet."); return; }
            Device usb = FirstUsb();
            if (usb == null)
            {
                Log("Plug in the USB cable first.");
                SetStatus("USB needed", "Connect the cable once, then press this button again.", Red);
                return;
            }
            SetWorking(true);
            SetStatus("Turning on Wi-Fi mode", "Saving the phone's IP and switching wireless on ...", Amber);
            Log("USB phone: " + usb.Serial);

            Thread t = new Thread(delegate()
            {
                string ip = GetWlanIp(usb.Serial);
                if (ip != null)
                {
                    SaveIp(ip);
                    Log("Phone Wi-Fi IP: " + ip);
                }
                else
                {
                    Log("Could not read the phone's Wi-Fi IP. Is Wi-Fi on?");
                }

                string result = Run(adb, "-s " + usb.Serial + " tcpip 5555", 15000);
                if (result.Length > 0) Log(result);

                bool back = WaitForDevice(usb.Serial, 20000);
                Run(adb, "-s " + usb.Serial + " shell svc power stayon true", 6000);
                Run(adb, "-s " + usb.Serial + " shell input keyevent KEYCODE_WAKEUP", 6000);

                SetWorking(false);
                if (back)
                {
                    Log("Wi-Fi mode is on. Unplug the cable, then press Start mirror.");
                    SetStatus("Wi-Fi mode on", "Unplug the cable and press Start mirror", Green);
                }
                else
                {
                    Log("The phone did not come back. Unplug it, wait 5 seconds, plug it in again.");
                    SetStatus("Phone did not reconnect", "Unplug, wait 5 seconds, plug in again", Red);
                }
                RefreshDevices();
            });
            t.IsBackground = true;
            t.Start();
        }

        string GetWlanIp(string serial)
        {
            string output = Run(adb, "-s " + serial + " shell ip -f inet addr show wlan0", 10000);
            Match m = Regex.Match(output, "inet (\\d+\\.\\d+\\.\\d+\\.\\d+)");
            return m.Success ? m.Groups[1].Value : null;
        }

        bool WaitForDevice(string serial, int timeoutMs)
        {
            DateTime deadline = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < deadline)
            {
                string state = Run(adb, "-s " + serial + " get-state", 3000);
                if (state.Trim() == "device") return true;
                Thread.Sleep(500);
            }
            return false;
        }

        bool IsIp(string text)
        {
            if (text == null) return false;
            return Regex.IsMatch(text.Trim(), "^\\d{1,3}(\\.\\d{1,3}){3}$");
        }

        void ConnectIp()
        {
            string ip = ipText.Text.Trim();
            if (!IsIp(ip))
            {
                Log("Enter an IP like 192.168.1.42 (no port).");
                SetStatus("Check the IP", "Example: 192.168.1.42", Red);
                return;
            }
            if (adb == null) { Log("adb is not ready yet."); return; }

            SetWorking(true);
            SetStatus("Connecting over Wi-Fi", "Trying " + ip + " ...", Amber);
            Thread t = new Thread(delegate()
            {
                string target = ip + ":5555";
                string output = Run(adb, "connect " + target, 15000);
                Log(output);
                bool ok = WaitForDevice(target, 10000);
                if (ok) SaveIp(ip);
                SetWorking(false);
                if (ok)
                {
                    SetStatus("Connected over Wi-Fi", ip + ":5555", Green);
                    Log("Connected. Press Start mirror.");
                }
                else
                {
                    SetStatus("Could not connect", "Same Wi-Fi? Phone restarted? Plug in USB once.", Red);
                    Log("Could not connect. If the phone restarted, plug in USB and press the Wi-Fi button once.");
                }
                RefreshDevices();
            });
            t.IsBackground = true;
            t.Start();
        }

        string JoinArgs(List<string> args)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string a in args)
            {
                if (sb.Length > 0) sb.Append(' ');
                if (a.IndexOf(' ') >= 0)
                {
                    sb.Append('"').Append(a).Append('"');
                }
                else
                {
                    sb.Append(a);
                }
            }
            return sb.ToString();
        }

        void StartMirror()
        {
            if (mirror != null) return;
            if (string.IsNullOrEmpty(scrcpy) || string.IsNullOrEmpty(adb))
            {
                Log("scrcpy is not ready yet.");
                return;
            }

            Device target = SelectedDevice();
            if (target == null) target = FirstDevice();
            if (target == null)
            {
                if (IsIp(ipText.Text))
                {
                    Log("No phone yet. Connecting to " + ipText.Text.Trim() + " first ...");
                    ConnectIp();
                    return;
                }
                Log("No phone found. Plug in USB or connect by IP.");
                SetStatus("Phone not connected", "Plug in USB, or enter the Wi-Fi IP and press Connect.", Muted);
                return;
            }

            bool wifi = !target.Usb;
            Run(adb, "-s " + target.Serial + " shell input keyevent KEYCODE_WAKEUP", 5000);
            Run(adb, "-s " + target.Serial + " shell svc power stayon true", 5000);

            List<string> args = new List<string>();
            args.Add("-s");
            args.Add(target.Serial);
            args.Add("--video-codec=h264");
            args.Add("--max-fps=60");
            args.Add("--turn-screen-off");
            args.Add("--stay-awake");
            if (!audioCheck.Checked) args.Add("--no-audio");

            int quality = qualityCombo.SelectedIndex;
            if (quality == 1)
            {
                args.Add("--video-bit-rate=8M");
                if (wifi) args.Add("--max-size=1280");
            }
            else if (quality == 2)
            {
                args.Add("--video-bit-rate=2M");
                args.Add("--max-size=800");
            }
            else
            {
                if (wifi)
                {
                    args.Add("--video-bit-rate=3M");
                    args.Add("--max-size=1024");
                }
                else
                {
                    args.Add("--video-bit-rate=8M");
                }
            }

            ProcessStartInfo psi = new ProcessStartInfo(scrcpy, JoinArgs(args));
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;

            Process p = new Process();
            p.StartInfo = psi;
            p.EnableRaisingEvents = true;
            p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
            {
                if (e.Data != null && !IsNoise(e.Data)) Log(e.Data);
            };
            p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
            {
                if (e.Data != null && !IsNoise(e.Data)) Log(e.Data);
            };
            p.Exited += delegate
            {
                if (mirror == p) mirror = null;
                UI(delegate
                {
                    UpdateMirrorUi();
                    UpdateStatus();
                });
                Log("Mirror window closed.");
            };

            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            mirror = p;

            string name = string.IsNullOrEmpty(target.Model) ? target.Serial : target.Model.Replace("_", " ");
            SetStatus("Mirroring " + name, (wifi ? "Wi-Fi" : "USB") + "  ·  " + target.Serial, Green);
            Log("Opening " + name + " over " + (wifi ? "Wi-Fi" : "USB") + " ...");
            UpdateMirrorUi();
        }

        bool IsNoise(string line)
        {
            if (line.IndexOf("INFO:", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (line.IndexOf("github.com/Genymobile/scrcpy", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (line.StartsWith("scrcpy ")) return true;
            return false;
        }

        void StopMirror()
        {
            if (mirror == null) return;
            try { mirror.Kill(); } catch { }
            mirror = null;
            Log("Mirror stopped.");
            UpdateMirrorUi();
            UpdateStatus();
        }
    }
}
