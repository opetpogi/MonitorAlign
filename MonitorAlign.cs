// MonitorAlign: makes the mouse cross between monitors of different physical sizes
// at the same physical height (like LittleBigMouse / the Mac "perfect alignment" utilities).
// Build: build.bat (uses the C# compiler that ships with Windows).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MonitorAlign
{
    class Mon
    {
        public string Name;
        public string Id;            // hardware ID (model + port); \\.\DISPLAYn can change when a monitor reconnects
        public Rectangle B;         // pixel bounds (physical pixels, DPI-aware)
        public bool Primary;
        public double Inches = 24;   // diagonal
        public double LeftMm, TopMm; // physical position of the top-left corner

        public double Mmpp { get { return Inches * 25.4 / Math.Sqrt((double)B.Width * B.Width + (double)B.Height * B.Height); } }
        public double WidthMm { get { return B.Width * Mmpp; } }
        public double HeightMm { get { return B.Height * Mmpp; } }
        public int Number { get { int n; return int.TryParse(new string(Name.Where(char.IsDigit).ToArray()), out n) ? n : 0; } }
    }

    static class Native
    {
        public delegate IntPtr HookProc(int n, IntPtr w, IntPtr l);
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct MSLL { public POINT pt; public uint mouseData, flags, time; public IntPtr extra; }
        [DllImport("user32.dll")] public static extern IntPtr SetProcessDpiAwarenessContext(IntPtr v);
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int id, HookProc fn, IntPtr mod, uint tid);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr h, int n, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] public static extern IntPtr GetModuleHandle(string n);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool EnumDisplayDevices(string device, uint index, ref DISPLAY_DEVICE dd, uint flags);

        // The monitor's device path, e.g. \\?\DISPLAY#DELF069#...UID200195#{...}; falls back to the display name.
        public static string MonitorId(string displayName)
        {
            var dd = new DISPLAY_DEVICE();
            dd.cb = Marshal.SizeOf(dd);
            if (EnumDisplayDevices(displayName, 0, ref dd, 1) && !string.IsNullOrEmpty(dd.DeviceID)) return dd.DeviceID.Replace("|", "");
            return displayName;
        }
    }

    static class Config
    {
        public static List<Mon> Mons = new List<Mon>();
        public static bool Enabled = true, Block = false, Guides = false;
        public static string Wallpaper = "";   // source image for the physically aligned spanned wallpaper
        static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MonitorAlign");
        static readonly string FilePath = Path.Combine(Dir, "settings.txt");
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public static bool Existed;

        // Every monitor ever saved, keyed by "id|resolution". Monitors that are switched off stay in here,
        // so their alignment is still there when they come back.
        static Dictionary<string, string[]> saved = new Dictionary<string, string[]>();
        static string Key(Mon m) { return m.Id + "|" + m.B.Width + "x" + m.B.Height; }

        public static void Load()
        {
            saved = new Dictionary<string, string[]>();
            Existed = File.Exists(FilePath);
            if (Existed)
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    var p = line.Split('|');
                    if (p[0] == "enabled") Enabled = p[1] == "1";
                    else if (p[0] == "block") Block = p[1] == "1";
                    else if (p[0] == "wallpaper") Wallpaper = p[1];
                    else if (p[0] == "mon" && p.Length >= 6) saved[p[1] + "|" + p[2]] = p;
                }

            Mons = Screen.AllScreens.Select(s => new Mon { Name = s.DeviceName, Id = Native.MonitorId(s.DeviceName), B = s.Bounds, Primary = s.Primary })
                                    .OrderBy(m => m.Number).ToList();
            ResetPositions();
            foreach (var m in Mons)
            {
                string[] p;
                if (!saved.TryGetValue(Key(m), out p) &&
                    !saved.TryGetValue(m.Name + "|" + m.B.Width + "x" + m.B.Height, out p)) continue; // older files keyed by display name
                m.Inches = double.Parse(p[3], Inv);
                m.LeftMm = double.Parse(p[4], Inv);
                m.TopMm = double.Parse(p[5], Inv);
            }
        }

        // Places every monitor next to the primary along the axis they touch and centers it on the other axis.
        public static void ResetPositions()
        {
            var p = Mons.FirstOrDefault(m => m.Primary) ?? Mons[0];
            p.LeftMm = 0; p.TopMm = 0;
            foreach (var m in Mons.Where(m => m != p))
            {
                bool beside = m.B.Right <= p.B.Left || m.B.Left >= p.B.Right;
                if (beside)
                {
                    m.LeftMm = m.B.Left < p.B.Left ? (m.B.Right - p.B.Left) * p.Mmpp - m.WidthMm : (m.B.Left - p.B.Left) * p.Mmpp;
                    m.TopMm = (p.HeightMm - m.HeightMm) / 2; // centers level
                }
                else
                {
                    m.TopMm = m.B.Top < p.B.Top ? (m.B.Bottom - p.B.Top) * p.Mmpp - m.HeightMm : (m.B.Top - p.B.Top) * p.Mmpp;
                    m.LeftMm = (p.WidthMm - m.WidthMm) / 2;
                }
            }
        }

        public static void Save()
        {
            Directory.CreateDirectory(Dir);
            var lines = new List<string> { "enabled|" + (Enabled ? "1" : "0"), "block|" + (Block ? "1" : "0"), "wallpaper|" + Wallpaper };
            foreach (var m in Mons)
            {
                saved.Remove(m.Name + "|" + m.B.Width + "x" + m.B.Height); // drop the old name-keyed entry once migrated
                saved[Key(m)] = new[] { "mon", m.Id, m.B.Width + "x" + m.B.Height,
                    m.Inches.ToString("0.0", Inv), m.LeftMm.ToString("0.0", Inv), m.TopMm.ToString("0.0", Inv) };
            }
            lines.AddRange(saved.Values.Select(p => string.Join("|", p)));
            File.WriteAllLines(FilePath, lines);
        }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public static bool StartWithWindows
        {
            get { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("MonitorAlign") != null; }
            set
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                    if (value) k.SetValue("MonitorAlign", "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue("MonitorAlign", false);
            }
        }
    }

    // Low-level mouse hook: when the cursor leaves one monitor for another, re-map the
    // crossing coordinate through millimetres so it lands at the same physical spot.
    static class Mover
    {
        static IntPtr hook;
        static Native.HookProc proc = Hook; // keep the delegate alive
        const int EdgeSlack = 300;          // ignore jumps that don't start near the crossed edge
        public static bool AllowInjected;   // --test: treat SendInput moves as real ones

        public static void Start() { hook = Native.SetWindowsHookEx(14, proc, Native.GetModuleHandle(null), 0); }
        public static void Stop() { if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }

        static IntPtr Hook(int n, IntPtr w, IntPtr l)
        {
            if (n >= 0 && (int)w == 0x200 && Config.Enabled)
            {
                var ms = (Native.MSLL)Marshal.PtrToStructure(l, typeof(Native.MSLL));
                if (((ms.flags & 1) == 0 || AllowInjected) && Handle(ms.pt.X, ms.pt.Y)) return (IntPtr)1;
            }
            return Native.CallNextHookEx(hook, n, w, l);
        }

        static Mon At(int x, int y) { return Config.Mons.FirstOrDefault(m => m.B.Contains(x, y)); }
        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }

        // Returns true when the move was replaced by our own SetCursorPos (or blocked).
        static bool Handle(int x, int y)
        {
            Native.POINT c;
            Native.GetCursorPos(out c);
            var from = At(c.X, c.Y);
            if (from == null || from.B.Contains(x, y)) return false;

            var f = from.B;
            bool right = x >= f.Right, left = x < f.Left, down = y >= f.Bottom, up = y < f.Top;
            if ((right || left) && (up || down)) return false; // diagonal corner: leave to Windows

            if (right || left)
            {
                if (right ? f.Right - 1 - c.X > EdgeSlack : c.X - f.Left > EdgeSlack) return false;
                double mm = from.TopMm + (Clamp(y, f.Top, f.Bottom - 1) - f.Top + 0.5) * from.Mmpp;
                var cands = Config.Mons.Where(m => right ? m.B.Left == f.Right : m.B.Right == f.Left).ToList();
                var to = Pick(cands, mm, m => m.TopMm, m => m.HeightMm);
                if (to == null) return false;
                double py = (mm - to.TopMm) / to.Mmpp;
                if (py < 0 || py >= to.B.Height) { if (Config.Block) return true; }
                int ny = to.B.Top + Clamp((int)Math.Floor(py), 0, to.B.Height - 1);
                int nx = right ? to.B.Left + Math.Min(x - f.Right, 20) : to.B.Right - 1 - Math.Min(f.Left - 1 - x, 20);
                Native.SetCursorPos(Clamp(nx, to.B.Left, to.B.Right - 1), ny);
                return true;
            }
            else
            {
                if (down ? f.Bottom - 1 - c.Y > EdgeSlack : c.Y - f.Top > EdgeSlack) return false;
                double mm = from.LeftMm + (Clamp(x, f.Left, f.Right - 1) - f.Left + 0.5) * from.Mmpp;
                var cands = Config.Mons.Where(m => down ? m.B.Top == f.Bottom : m.B.Bottom == f.Top).ToList();
                var to = Pick(cands, mm, m => m.LeftMm, m => m.WidthMm);
                if (to == null) return false;
                double px = (mm - to.LeftMm) / to.Mmpp;
                if (px < 0 || px >= to.B.Width) { if (Config.Block) return true; }
                int nx = to.B.Left + Clamp((int)Math.Floor(px), 0, to.B.Width - 1);
                int ny = down ? to.B.Top + Math.Min(y - f.Bottom, 20) : to.B.Bottom - 1 - Math.Min(f.Top - 1 - y, 20);
                Native.SetCursorPos(nx, Clamp(ny, to.B.Top, to.B.Bottom - 1));
                return true;
            }
        }

        // The neighbour whose physical span contains mm, else the closest one.
        static Mon Pick(List<Mon> cands, double mm, Func<Mon, double> start, Func<Mon, double> size)
        {
            return cands.OrderBy(m => mm < start(m) ? start(m) - mm : mm > start(m) + size(m) ? mm - start(m) - size(m) : 0)
                        .FirstOrDefault();
        }
    }

    // Builds a spanned wallpaper where each monitor shows the part of the picture that is physically
    // behind it, so the image continues straight across monitors of different sizes.
    static class WallpaperMaker
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool SystemParametersInfo(int action, int param, string value, int flags);

        public static readonly string OutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MonitorAlign", "wallpaper.jpg");
        static string lastSig;
        public static void MarkCurrent() { lastSig = Signature(); }

        static string Signature()
        {
            return Config.Wallpaper + "|" + string.Join(";", Config.Mons.Select(m => m.Name + m.B + m.Inches + "," + m.LeftMm + "," + m.TopMm));
        }

        // Re-renders and sets the wallpaper when the source or the alignment changed since the last time.
        public static void Apply(bool force)
        {
            if (string.IsNullOrEmpty(Config.Wallpaper) || !File.Exists(Config.Wallpaper)) return;
            var sig = Signature();
            if (!force && sig == lastSig) return;
            Render(Config.Wallpaper, OutPath);
            using (var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true))
            {
                k.SetValue("WallpaperStyle", "22"); // Span
                k.SetValue("TileWallpaper", "0");
            }
            SystemParametersInfo(20, 0, OutPath, 3); // SPI_SETDESKWALLPAPER, update ini + broadcast
            lastSig = sig;
        }

        public static void Render(string source, string outPath)
        {
            var ms = Config.Mons;
            var vb = ms.Select(m => m.B).Aggregate(Rectangle.Union);
            double minX = ms.Min(m => m.LeftMm), minY = ms.Min(m => m.TopMm);
            double wMm = ms.Max(m => m.LeftMm + m.WidthMm) - minX, hMm = ms.Max(m => m.TopMm + m.HeightMm) - minY;
            using (var src = Image.FromFile(source))
            using (var bmp = new Bitmap(vb.Width, vb.Height))
            using (var g = Graphics.FromImage(bmp))
            using (var attr = new System.Drawing.Imaging.ImageAttributes())
            {
                g.Clear(Color.Black);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                attr.SetWrapMode(WrapMode.TileFlipXY); // no dark seams at the crop edges
                // Scale the picture to cover the physical area of all monitors, centred (like "Fill").
                double mmPerPx = Math.Max(wMm / src.Width, hMm / src.Height);
                double ox = minX - (src.Width * mmPerPx - wMm) / 2, oy = minY - (src.Height * mmPerPx - hMm) / 2;
                foreach (var m in ms)
                {
                    var dest = new Rectangle(m.B.X - vb.X, m.B.Y - vb.Y, m.B.Width, m.B.Height);
                    g.DrawImage(src, dest,
                        (float)((m.LeftMm - ox) / mmPerPx), (float)((m.TopMm - oy) / mmPerPx),
                        (float)(m.WidthMm / mmPerPx), (float)(m.HeightMm / mmPerPx), GraphicsUnit.Pixel, attr);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                var jpg = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders().First(c => c.MimeType == "image/jpeg");
                var ep = new System.Drawing.Imaging.EncoderParameters(1);
                ep.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 95L);
                bmp.Save(outPath, jpg, ep);
            }
        }
    }

    // Click-through overlay that draws lines every 20 mm in shared physical space.
    // When the alignment is right, the lines continue straight across the bezel.
    class GuideOverlay : Form
    {
        readonly Mon mon;
        public Mon Mon { get { return mon; } }
        public GuideOverlay(Mon m)
        {
            mon = m;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Bounds = m.B;
            TopMost = true;
            BackColor = TransparencyKey = Color.Magenta;
            DoubleBuffered = true;
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x8000000; return cp; }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var thin = new Pen(Color.Lime, 1))
            using (var thick = new Pen(Color.Red, 3))
            using (var font = new Font("Segoe UI", 10, FontStyle.Bold))
            {
                for (int k = (int)Math.Floor(mon.TopMm / 20); k * 20 <= mon.TopMm + mon.HeightMm; k++)
                {
                    float py = (float)((k * 20 - mon.TopMm) / mon.Mmpp);
                    bool major = k % 5 == 0;
                    g.DrawLine(major ? thick : thin, 0, py, Width, py);
                    if (major) g.DrawString((k * 20) + " mm", font, Brushes.Red, 8, py + 3);
                }
                for (int k = (int)Math.Floor(mon.LeftMm / 20); k * 20 <= mon.LeftMm + mon.WidthMm; k++)
                {
                    float px = (float)((k * 20 - mon.LeftMm) / mon.Mmpp);
                    g.DrawLine(k % 5 == 0 ? thick : thin, px, 0, px, Height);
                }
            }
        }
    }

    // Draws the monitors to scale; drag one (or use the arrow keys) to line them up.
    class Preview : Control
    {
        public Mon Selected;
        public event Action Changed;
        double scale, minX, minY;
        Point dragStart; double dragL, dragT; bool dragging;

        public Preview()
        {
            SetStyle(ControlStyles.Selectable | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            TabStop = true;
            BackColor = Color.FromArgb(32, 34, 38);
        }

        void Fit()
        {
            var ms = Config.Mons;
            minX = ms.Min(m => m.LeftMm); minY = ms.Min(m => m.TopMm);
            double w = ms.Max(m => m.LeftMm + m.WidthMm) - minX, h = ms.Max(m => m.TopMm + m.HeightMm) - minY;
            scale = Math.Min((Width - 40) / w, (Height - 40) / h);
            minX -= ((Width / scale) - w) / 2; minY -= ((Height / scale) - h) / 2;
        }

        RectangleF Rect(Mon m)
        {
            return new RectangleF((float)((m.LeftMm - minX) * scale), (float)((m.TopMm - minY) * scale), (float)(m.WidthMm * scale), (float)(m.HeightMm * scale));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (!dragging) Fit();
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var font = new Font("Segoe UI", 11, FontStyle.Bold))
            using (var small = new Font("Segoe UI", 9))
                foreach (var m in Config.Mons)
                {
                    var r = Rect(m);
                    using (var fill = new SolidBrush(m.Primary ? Color.FromArgb(52, 98, 160) : Color.FromArgb(70, 76, 88)))
                        g.FillRectangle(fill, r);
                    using (var pen = new Pen(m == Selected ? Color.Orange : Color.FromArgb(150, 160, 175), m == Selected ? 3 : 1))
                        g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(m.Number + (m.Primary ? " (main)" : ""), font, Brushes.White, new RectangleF(r.X, r.Y, r.Width, r.Height - 20), sf);
                    g.DrawString(m.Inches.ToString("0.#") + "\"  " + m.B.Width + "×" + m.B.Height, small, Brushes.Gainsboro, new RectangleF(r.X, r.Y + 20, r.Width, r.Height - 20), sf);
                }
            if (Focused && Selected != null)
                g.DrawString("Arrow keys: 1 mm  ·  Shift+arrow: 10 mm", Font, Brushes.Gray, 6, Height - 18);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            Selected = Config.Mons.LastOrDefault(m => Rect(m).Contains(e.Location));
            if (Selected != null) { dragging = true; dragStart = e.Location; dragL = Selected.LeftMm; dragT = Selected.TopMm; }
            Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!dragging) return;
            Selected.LeftMm = Math.Round(dragL + (e.X - dragStart.X) / scale);
            Selected.TopMm = Math.Round(dragT + (e.Y - dragStart.Y) / scale);
            Invalidate();
            if (Changed != null) Changed();
        }
        protected override void OnMouseUp(MouseEventArgs e) { dragging = false; Invalidate(); }
        protected override bool IsInputKey(Keys k) { return (k & Keys.KeyCode) >= Keys.Left && (k & Keys.KeyCode) <= Keys.Down || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (Selected == null) return;
            double s = e.Shift ? 10 : 1;
            if (e.KeyCode == Keys.Up) Selected.TopMm -= s;
            else if (e.KeyCode == Keys.Down) Selected.TopMm += s;
            else if (e.KeyCode == Keys.Left) Selected.LeftMm -= s;
            else if (e.KeyCode == Keys.Right) Selected.LeftMm += s;
            else return;
            Invalidate();
            if (Changed != null) Changed();
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    }

    class SettingsForm : Form
    {
        readonly Preview preview = new Preview();
        readonly FlowLayoutPanel sizes = new FlowLayoutPanel();
        public event Action Changed;

        public SettingsForm()
        {
            Text = "MonitorAlign";
            Font = new Font("Segoe UI", 9.5f);
            ClientSize = new Size(720, 560);
            MinimumSize = new Size(560, 480);
            StartPosition = FormStartPosition.CenterScreen;
            Icon = App.MakeIcon();

            var intro = new Label
            {
                Dock = DockStyle.Top, Height = 54, Padding = new Padding(10, 8, 10, 0),
                Text = "1. Enter each monitor's diagonal size.  2. Turn on the guide lines.  3. Drag a monitor (or select it and use the " +
                       "arrow keys) until the guide lines run straight across the gap between your screens."
            };
            preview.Dock = DockStyle.Fill;
            preview.Changed += OnChanged;

            sizes.Dock = DockStyle.Bottom; sizes.Height = 44; sizes.Padding = new Padding(8, 8, 8, 0);
            BuildSizes();

            var opts = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 108, Padding = new Padding(8, 4, 8, 0) };
            var en = new CheckBox { Text = "Align the mouse between monitors", Checked = Config.Enabled, AutoSize = true };
            en.CheckedChanged += (s, e) => { Config.Enabled = en.Checked; OnChanged(); };
            var gd = new CheckBox { Text = "Show guide lines", Checked = Config.Guides, AutoSize = true };
            gd.CheckedChanged += (s, e) => { Config.Guides = gd.Checked; OnChanged(); };
            var bl = new CheckBox { Text = "Stop the mouse where the screens don't physically overlap", Checked = Config.Block, AutoSize = true };
            bl.CheckedChanged += (s, e) => { Config.Block = bl.Checked; OnChanged(); };
            var st = new CheckBox { Text = "Start with Windows", Checked = Config.StartWithWindows, AutoSize = true };
            st.CheckedChanged += (s, e) => { Config.StartWithWindows = st.Checked; };
            var reset = new Button { Text = "Reset positions", AutoSize = true };
            reset.Click += (s, e) => { Config.ResetPositions(); OnChanged(); };
            var wp = new Button { Text = "Aligned wallpaper…", AutoSize = true };
            var wpOff = new Button { Text = "Stop aligning wallpaper", AutoSize = true, Enabled = Config.Wallpaper != "" };
            wp.Click += (s, e) =>
            {
                using (var dlg = new OpenFileDialog { Title = "Pick a picture to span across your monitors", Filter = "Pictures|*.jpg;*.jpeg;*.png;*.bmp" })
                {
                    if (Config.Wallpaper != "") dlg.FileName = Config.Wallpaper;
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    Config.Wallpaper = dlg.FileName;
                    Config.Save();
                    UseWaitCursor = true;
                    WallpaperMaker.Apply(true);
                    UseWaitCursor = false;
                    wpOff.Enabled = true;
                }
            };
            // Only forgets the picture; the current wallpaper stays until you change it in Windows.
            wpOff.Click += (s, e) => { Config.Wallpaper = ""; Config.Save(); wpOff.Enabled = false; };
            opts.Controls.AddRange(new Control[] { en, gd, st, bl, reset, wp, wpOff });

            Controls.Add(preview);
            Controls.Add(sizes);
            Controls.Add(opts);
            Controls.Add(intro);
        }

        public void BuildSizes()
        {
            sizes.Controls.Clear();
            foreach (var m in Config.Mons)
            {
                var mon = m;
                sizes.Controls.Add(new Label { Text = "Monitor " + m.Number + (m.Primary ? " (main)" : "") + ":", AutoSize = true, Margin = new Padding(0, 5, 2, 0) });
                var num = new NumericUpDown { Minimum = 10, Maximum = 100, DecimalPlaces = 1, Increment = 0.5m, Value = (decimal)m.Inches, Width = 64 };
                num.ValueChanged += (s, e) => { mon.Inches = (double)num.Value; OnChanged(); };
                sizes.Controls.Add(num);
                sizes.Controls.Add(new Label { Text = "inches", AutoSize = true, Margin = new Padding(2, 5, 24, 0) });
            }
            preview.Invalidate();
        }

        void OnChanged() { preview.Invalidate(); if (Changed != null) Changed(); }
    }

    class App : ApplicationContext
    {
        readonly NotifyIcon tray = new NotifyIcon();
        readonly List<GuideOverlay> overlays = new List<GuideOverlay>();
        SettingsForm settings;
        readonly ToolStripMenuItem enabledItem, guidesItem;

        public App()
        {
            Config.Load();
            enabledItem = new ToolStripMenuItem("Align mouse", null, (s, e) => { Config.Enabled = !Config.Enabled; Changed(); });
            guidesItem = new ToolStripMenuItem("Guide lines", null, (s, e) => { Config.Guides = !Config.Guides; Changed(); });
            var menu = new ContextMenuStrip();
            menu.Items.Add("Settings…", null, (s, e) => ShowSettings());
            menu.Items.Add(enabledItem);
            menu.Items.Add(guidesItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => ExitThread());
            menu.Opening += (s, e) => { enabledItem.Checked = Config.Enabled; guidesItem.Checked = Config.Guides; };
            tray.ContextMenuStrip = menu;
            tray.Icon = MakeIcon();
            tray.Text = "MonitorAlign";
            tray.Visible = true;
            tray.MouseDoubleClick += (s, e) => ShowSettings();

            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            Mover.Start();
            WallpaperMaker.MarkCurrent();
            if (!Config.Existed) ShowSettings();
        }

        void OnDisplayChanged(object s, EventArgs e)
        {
            Config.Save();
            Config.Load();
            if (settings != null) settings.BuildSizes();
            Changed();
            WallpaperMaker.Apply(false);
        }

        void ShowSettings()
        {
            if (settings == null || settings.IsDisposed)
            {
                settings = new SettingsForm();
                settings.Changed += Changed;
                settings.FormClosed += (s, e) => { Config.Guides = false; Changed(); WallpaperMaker.Apply(false); };
            }
            settings.Show();
            settings.Activate();
        }

        void Changed()
        {
            Config.Save();
            // Repaint existing overlays while dragging; recreating them every move made them flicker.
            bool same = Config.Guides && overlays.Select(o => o.Mon).SequenceEqual(Config.Mons);
            if (same) { foreach (var o in overlays) o.Invalidate(); return; }
            foreach (var o in overlays) o.Close();
            overlays.Clear();
            if (Config.Guides)
                foreach (var m in Config.Mons) { var o = new GuideOverlay(m); overlays.Add(o); o.Show(); }
        }

        protected override void ExitThreadCore()
        {
            Mover.Stop();
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            tray.Visible = false;
            base.ExitThreadCore();
        }

        public static Icon MakeIcon()
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.FillRectangle(new SolidBrush(Color.FromArgb(52, 120, 200)), 1, 6, 17, 13);
                    g.FillRectangle(new SolidBrush(Color.FromArgb(120, 130, 145)), 20, 9, 11, 9);
                    g.FillRectangle(Brushes.Orange, 0, 12, 32, 2);
                    g.FillRectangle(Brushes.Gray, 7, 21, 5, 4);
                    g.FillRectangle(Brushes.Gray, 4, 25, 11, 2);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        [STAThread]
        static void Main(string[] args)
        {
            Mover.AllowInjected = args.Contains("--test");
            if (args.Length == 3 && args[0] == "--render-wallpaper") // preview: render only, don't set it
            {
                try { Native.SetProcessDpiAwarenessContext((IntPtr)(-4)); } catch { }
                Config.Load();
                WallpaperMaker.Render(args[1], args[2]);
                return;
            }
            bool first;
            using (new Mutex(true, "MonitorAlign-single-instance", out first))
            {
                if (!first) return;
                try { Native.SetProcessDpiAwarenessContext((IntPtr)(-4)); } catch { } // per-monitor v2: real pixels everywhere
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new App());
            }
        }
    }
}
