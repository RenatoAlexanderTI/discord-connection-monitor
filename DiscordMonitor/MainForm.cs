#nullable disable
using System;
using System.Diagnostics;
using System.Drawing;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Speech.Synthesis;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DiscordMonitor
{
    public partial class MainForm : Form
    {
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("Gdi32.dll")]
        private static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;

        private bool _wasInCall = false;
        private bool _connected = true;
        private DateTime _lastAlert = DateTime.MinValue;
        private const int ALERT_COOLDOWN_SECONDS = 8;

        private System.Windows.Forms.Timer _timer;
        private NotifyIcon _trayIcon;
        private SpeechSynthesizer _tts;

        private Panel _headerPanel;
        private Panel _bodyPanel;
        private Panel _alertPanel;
        private Label _lblTitle;
        private Label _lblDiscordStatus;
        private Label _lblConnectionStatus;
        private Label _lblPing;
        private Label _lblPacketLoss;
        private Label _lblLastAlert;
        private Label _lblAlertMsg;
        private PictureBox _pbDiscordDot;
        private PictureBox _pbConnDot;
        private CheckBox _chkAlwaysTop;

        private readonly Color BG_DARK   = Color.FromArgb(18, 18, 30);
        private readonly Color BG_HEADER = Color.FromArgb(22, 33, 62);
        private readonly Color BG_BODY   = Color.FromArgb(26, 26, 46);
        private readonly Color COL_GREEN  = Color.FromArgb(77, 222, 138);
        private readonly Color COL_RED    = Color.FromArgb(222, 77, 77);
        private readonly Color COL_YELLOW = Color.FromArgb(222, 187, 77);
        private readonly Color COL_GRAY   = Color.FromArgb(136, 153, 170);
        private readonly Color COL_MUTED  = Color.FromArgb(100, 110, 130);

        public MainForm()
        {
            InitializeComponent();
            BuildUI();
            SetupTray();
            SetupTTS();
            SetupTimer();
            PositionOnSecondaryMonitor();
            SetAlwaysOnTop(true);
        }

        private void BuildUI()
        {
            this.Text            = "Discord Monitor";
            this.Size            = new Size(240, 230);
            this.MinimumSize     = new Size(240, 230);
            this.MaximumSize     = new Size(240, 230);
            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor       = BG_DARK;
            this.StartPosition   = FormStartPosition.Manual;
            this.ShowInTaskbar   = false;
            this.Opacity         = 0.93;
            this.MouseDown      += Form_MouseDown;
            this.MouseMove      += Form_MouseMove;

            _headerPanel = new Panel { Dock = DockStyle.Top, Height = 32, BackColor = BG_HEADER };
            _headerPanel.MouseDown += Form_MouseDown;
            _headerPanel.MouseMove += Form_MouseMove;

            _lblTitle = new Label
            {
                Text      = "   Discord Monitor",
                ForeColor = COL_GRAY,
                Font      = new Font("Segoe UI", 9f),
                AutoSize  = false,
                Bounds    = new Rectangle(0, 7, 190, 18)
            };
            _lblTitle.MouseDown += Form_MouseDown;
            _lblTitle.MouseMove += Form_MouseMove;

            var btnClose = new Button
            {
                Text      = "x",
                ForeColor = COL_MUTED,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Bounds    = new Rectangle(212, 4, 24, 24),
                Font      = new Font("Segoe UI", 10f),
                Cursor    = Cursors.Hand
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s, e) => { _trayIcon.Visible = true; this.Hide(); };

            _headerPanel.Controls.Add(_lblTitle);
            _headerPanel.Controls.Add(btnClose);

            _bodyPanel = new Panel { Location = new Point(0, 32), Size = new Size(240, 198), BackColor = BG_BODY };
            _bodyPanel.MouseDown += Form_MouseDown;
            _bodyPanel.MouseMove += Form_MouseMove;

            _pbDiscordDot     = MakeDot(12, 14);
            var lblDiscordLbl = MakeLabel("Discord", 28, 14, COL_MUTED, 9f);
            _lblDiscordStatus = MakeLabel("Detectando...", 100, 14, COL_GRAY, 9f, true);

            var sep1 = new Panel { BackColor = Color.FromArgb(40, 255, 255, 255), Bounds = new Rectangle(12, 36, 216, 1) };

            _alertPanel  = new Panel { Bounds = new Rectangle(12, 44, 216, 30), BackColor = Color.FromArgb(60, 222, 77, 77), Visible = false };
            _lblAlertMsg = new Label { Text = "  Sin conexion durante llamada!", ForeColor = COL_RED, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), AutoSize = false, Bounds = new Rectangle(4, 6, 210, 18) };
            _alertPanel.Controls.Add(_lblAlertMsg);

            _pbConnDot           = MakeDot(12, 60);
            var lblConnLbl       = MakeLabel("Conexion", 28, 60, COL_MUTED, 9f);
            _lblConnectionStatus = MakeLabel("--", 100, 60, COL_GRAY, 9f, true);

            var lblPingLbl = MakeLabel("Ping", 28, 84, COL_MUTED, 9f);
            _lblPing       = MakeLabel("-- ms", 100, 84, COL_GRAY, 9f, true);

            var lblLossLbl = MakeLabel("Perdida pkts", 28, 104, COL_MUTED, 9f);
            _lblPacketLoss = MakeLabel("--", 100, 104, COL_GRAY, 9f, true);

            var sep2 = new Panel { BackColor = Color.FromArgb(40, 255, 255, 255), Bounds = new Rectangle(12, 126, 216, 1) };

            var lblLastLbl = MakeLabel("Ultimo aviso", 12, 134, COL_MUTED, 8.5f);
            _lblLastAlert  = MakeLabel("--", 110, 134, COL_MUTED, 8.5f);

            _chkAlwaysTop = new CheckBox
            {
                Text      = "Siempre encima",
                ForeColor = COL_MUTED,
                Font      = new Font("Segoe UI", 8.5f),
                Checked   = true,
                Bounds    = new Rectangle(12, 155, 150, 18),
                BackColor = Color.Transparent
            };
            _chkAlwaysTop.CheckedChanged += (s, e) => SetAlwaysOnTop(_chkAlwaysTop.Checked);

            _bodyPanel.Controls.Add(_pbDiscordDot);
            _bodyPanel.Controls.Add(lblDiscordLbl);
            _bodyPanel.Controls.Add(_lblDiscordStatus);
            _bodyPanel.Controls.Add(sep1);
            _bodyPanel.Controls.Add(_alertPanel);
            _bodyPanel.Controls.Add(_pbConnDot);
            _bodyPanel.Controls.Add(lblConnLbl);
            _bodyPanel.Controls.Add(_lblConnectionStatus);
            _bodyPanel.Controls.Add(lblPingLbl);
            _bodyPanel.Controls.Add(_lblPing);
            _bodyPanel.Controls.Add(lblLossLbl);
            _bodyPanel.Controls.Add(_lblPacketLoss);
            _bodyPanel.Controls.Add(sep2);
            _bodyPanel.Controls.Add(lblLastLbl);
            _bodyPanel.Controls.Add(_lblLastAlert);
            _bodyPanel.Controls.Add(_chkAlwaysTop);

            this.Controls.Add(_headerPanel);
            this.Controls.Add(_bodyPanel);
            this.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, 10, 10));
        }

        private Label MakeLabel(string text, int x, int y, Color color, float size, bool bold = false)
        {
            return new Label
            {
                Text      = text,
                ForeColor = color,
                Font      = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
                AutoSize  = true,
                Location  = new Point(x, y),
                BackColor = Color.Transparent
            };
        }

        private PictureBox MakeDot(int x, int y)
        {
            var pb = new PictureBox { Size = new Size(8, 8), Location = new Point(x, y), BackColor = COL_GRAY };
            pb.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, 8, 8, 8, 8));
            return pb;
        }

        private Point _dragStart;
        private bool _dragging;

        private void Form_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { _dragging = true; _dragStart = e.Location; }
        }

        private void Form_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            var pt = PointToScreen(e.Location);
            this.Location = new Point(pt.X - _dragStart.X, pt.Y - _dragStart.Y);
        }

        protected override void OnMouseUp(MouseEventArgs e) { _dragging = false; base.OnMouseUp(e); }

        private void SetupTray()
        {
            _trayIcon = new NotifyIcon { Text = "Discord Monitor", Icon = SystemIcons.Application, Visible = false };
            var menu = new ContextMenuStrip();
            menu.Items.Add("Mostrar", null, (s, e) => { this.Show(); this.BringToFront(); _trayIcon.Visible = false; });
            menu.Items.Add("-");
            var autostartItem = new ToolStripMenuItem("Iniciar con Windows") { Checked = IsAutostartEnabled() };
            autostartItem.Click += (s, e) => {
                if (IsAutostartEnabled()) { RemoveAutostart(); autostartItem.Checked = false; }
                else                      { AddAutostart();    autostartItem.Checked = true; }
            };
            menu.Items.Add(autostartItem);
            menu.Items.Add("-");
            menu.Items.Add("Salir", null, (s, e) => { _trayIcon.Visible = false; Application.Exit(); });
            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += (s, e) => { this.Show(); this.BringToFront(); _trayIcon.Visible = false; };
        }

        private const string REG_RUN  = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string APP_NAME = "DiscordMonitor";

        public static void AddAutostart()    { try { using var k = Registry.CurrentUser.OpenSubKey(REG_RUN, true);  k?.SetValue(APP_NAME, $"\"{Application.ExecutablePath}\""); } catch { } }
        public static void RemoveAutostart() { try { using var k = Registry.CurrentUser.OpenSubKey(REG_RUN, true);  k?.DeleteValue(APP_NAME, false); } catch { } }
        public static bool IsAutostartEnabled() { try { using var k = Registry.CurrentUser.OpenSubKey(REG_RUN, false); return k?.GetValue(APP_NAME) != null; } catch { return false; } }

        private void SetupTTS()
        {
            try { _tts = new SpeechSynthesizer(); _tts.Volume = 100; _tts.Rate = 0; } catch { }
        }

        private void FireAlerts()
        {
            Task.Run(() => {
                try { System.Media.SystemSounds.Exclamation.Play(); } catch { }
                System.Threading.Thread.Sleep(500);
                try { System.Media.SystemSounds.Exclamation.Play(); } catch { }
            });
            Task.Run(() => {
                try { _tts?.SpeakAsync("Atencion. Se cayo la conexion. Estas en llamada de Discord."); } catch { }
            });
            try {
                _trayIcon.Visible = true;
                _trayIcon.ShowBalloonTip(4000, "Discord Monitor", "Se cayo la conexion!\nEstas en llamada de Discord.", ToolTipIcon.Warning);
            } catch { }
        }

        private void SetupTimer()
        {
            _timer = new System.Windows.Forms.Timer { Interval = 2000 };
            _timer.Tick += OnTick;
            _timer.Start();
        }

        private void OnTick(object sender, EventArgs e)
        {
            bool discordNow = IsDiscordRunning();
            bool connNow    = CheckConnection(out int ping);

            bool shouldAlert = discordNow && !connNow && _wasInCall
                               && (DateTime.Now - _lastAlert).TotalSeconds > ALERT_COOLDOWN_SECONDS;

            if (shouldAlert) { _lastAlert = DateTime.Now; FireAlerts(); }

            _wasInCall = discordNow;
            _connected = connNow;

            UpdateUI(discordNow, connNow, ping);
        }

        private bool IsDiscordRunning()
        {
            try { return Process.GetProcessesByName("Discord").Length > 0; } catch { return false; }
        }

        private bool CheckConnection(out int pingMs)
        {
            pingMs = 0;
            try {
                using var p = new Ping();
                var r = p.Send("8.8.8.8", 1500);
                if (r.Status == IPStatus.Success) { pingMs = (int)r.RoundtripTime; return true; }
                r = p.Send("1.1.1.1", 1500);
                if (r.Status == IPStatus.Success) { pingMs = (int)r.RoundtripTime; return true; }
                return false;
            } catch { return false; }
        }

        private void UpdateUI(bool discord, bool conn, int ping)
        {
            if (InvokeRequired) { Invoke(new Action(() => UpdateUI(discord, conn, ping))); return; }

            _pbDiscordDot.BackColor     = discord ? COL_GREEN : COL_GRAY;
            _pbDiscordDot.Region        = Region.FromHrgn(CreateRoundRectRgn(0, 0, 8, 8, 8, 8));
            _lblDiscordStatus.Text      = discord ? "Activo" : "No detectado";
            _lblDiscordStatus.ForeColor = discord ? COL_GREEN : COL_GRAY;

            _pbConnDot.BackColor           = conn ? COL_GREEN : COL_RED;
            _pbConnDot.Region              = Region.FromHrgn(CreateRoundRectRgn(0, 0, 8, 8, 8, 8));
            _lblConnectionStatus.Text      = conn ? "Estable" : "Sin conexion";
            _lblConnectionStatus.ForeColor = conn ? COL_GREEN : COL_RED;

            _lblPing.Text       = conn ? $"{ping} ms" : "-- ms";
            _lblPing.ForeColor  = ping < 80 ? COL_GREEN : ping < 200 ? COL_YELLOW : COL_RED;
            _lblPacketLoss.Text      = conn ? "0%" : "100%";
            _lblPacketLoss.ForeColor = conn ? COL_GREEN : COL_RED;

            _alertPanel.Visible = discord && !conn;

            if (_lastAlert != DateTime.MinValue)
                _lblLastAlert.Text = _lastAlert.ToString("HH:mm:ss");
        }

        private void PositionOnSecondaryMonitor()
        {
            Screen target = Screen.PrimaryScreen;
            foreach (Screen s in Screen.AllScreens)
                if (!s.Primary) { target = s; break; }
            this.Location = new Point(target.WorkingArea.Right - this.Width - 16, target.WorkingArea.Top + 16);
        }

        private void SetAlwaysOnTop(bool onTop)
        {
            this.TopMost = onTop;
            if (onTop) SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                _trayIcon.Visible = true;
                this.Hide();
                _trayIcon.ShowBalloonTip(2000, "Discord Monitor", "Sigue activo en la bandeja del sistema.", ToolTipIcon.Info);
                return;
            }
            _timer?.Stop();
            _tts?.Dispose();
            _trayIcon?.Dispose();
            base.OnFormClosing(e);
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ResumeLayout(false);
        }
    }
}
