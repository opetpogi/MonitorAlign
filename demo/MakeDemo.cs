// Renders the MonitorAlign demo video: draws each frame with System.Drawing and pipes it to ffmpeg.
// Build + run: demo\make-demo.bat  ->  dist\MonitorAlign-demo.mp4
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

class MakeDemo
{
    const int W = 1280, H = 720, Fps = 30;

    // Physical model (mm), both 1920x1080. 27" right (main), 23" left, centres level.
    static readonly double M27 = 27 * 25.4 / Math.Sqrt(1920.0 * 1920 + 1080 * 1080);
    static readonly double M23 = 23 * 25.4 / Math.Sqrt(1920.0 * 1920 + 1080 * 1080);
    static readonly double H27 = 1080 * M27, W27 = 1920 * M27, H23 = 1080 * M23, W23 = 1920 * M23;
    static readonly double Top23 = (H27 - H23) / 2;

    // Screen placement in video pixels (1 px = 1 mm).
    const float X23 = 76, Y0 = 178, Bezel = 10, Gap = 22;
    static readonly float X27 = X23 + (float)W23 + Bezel * 2 + Gap;

    static readonly Font Title = new Font("Segoe UI Semibold", 30);
    static readonly Font Sub = new Font("Segoe UI", 17);
    static readonly Font Small = new Font("Segoe UI", 13);
    static readonly Font Big = new Font("Segoe UI Semibold", 46);

    // Scene boundaries (seconds).
    const double SIntro = 3, SWithout = 7.5, SWith = 7, SBlock = 7, SGuides = 7, SOutro = 3.5;

    static void Main(string[] args)
    {
        string outFile = args.Length > 0 ? args[0] : "MonitorAlign-demo.mp4";
        double total = SIntro + SWithout + SWith + SBlock + SGuides + SOutro;
        int frames = (int)(total * Fps);
        var psi = new ProcessStartInfo("ffmpeg",
            "-y -loglevel error -f rawvideo -pix_fmt bgra -s " + W + "x" + H + " -r " + Fps +
            " -i - -c:v libx264 -preset slow -crf 18 -pix_fmt yuv420p -movflags +faststart \"" + outFile + "\"")
        { UseShellExecute = false, RedirectStandardInput = true };
        Console.InputEncoding = new System.Text.UTF8Encoding(false); // no BOM on ffmpeg's stdin, or every frame shifts 3 bytes
        var ff = Process.Start(psi);
        var stdin = ff.StandardInput.BaseStream;
        var buf = new byte[W * H * 4];
        using (var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb))
        {
            for (int i = 0; i < frames; i++)
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    Render(g, i / (double)Fps);
                }
                if (args.Length > 1 && i % Fps == 0) bmp.Save(args[1] + "\\f" + (i / Fps).ToString("00") + ".png");
                var d = bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                Marshal.Copy(d.Scan0, buf, 0, buf.Length);
                bmp.UnlockBits(d);
                stdin.Write(buf, 0, buf.Length);
            }
        }
        stdin.Close();
        ff.WaitForExit();
        Console.WriteLine("wrote " + outFile + " (" + frames + " frames, exit " + ff.ExitCode + ")");
    }

    // ---------- helpers ----------
    static double Clamp01(double v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
    static double Ease(double v) { v = Clamp01(v); return v * v * (3 - 2 * v); }
    static double Fade(double t, double len) { return Clamp01(t / 0.4) * Clamp01((len - t) / 0.4); }
    static Color A(Color c, double a) { return Color.FromArgb((int)(Clamp01(a) * c.A), c); }

    static PointF P27(double px, double py) { return new PointF(X27 + (float)(px * M27), Y0 + (float)(py * M27)); }
    static PointF P23(double px, double py) { return new PointF(X23 + (float)(px * M23), Y0 + (float)(Top23 + py * M23)); }

    static void Text(Graphics g, string s, Font f, Color c, float y, double a)
    {
        using (var b = new SolidBrush(A(c, a)))
        {
            var sz = g.MeasureString(s, f);
            g.DrawString(s, f, b, (W - sz.Width) / 2, y);
        }
    }

    static void Label(Graphics g, string s, PointF at, Color c, double a)
    {
        var sz = g.MeasureString(s, Small);
        var r = new RectangleF(at.X - sz.Width / 2 - 8, at.Y, sz.Width + 16, sz.Height + 6);
        using (var bg = new SolidBrush(A(Color.FromArgb(230, 20, 22, 28), a)))
        using (var pen = new Pen(A(c, a), 1.5f))
        using (var tb = new SolidBrush(A(c, a)))
        {
            g.FillRectangle(bg, r);
            g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            g.DrawString(s, Small, tb, r.X + 8, r.Y + 3);
        }
    }

    static void Background(Graphics g)
    {
        using (var b = new LinearGradientBrush(new Rectangle(0, 0, W, H), Color.FromArgb(24, 27, 34), Color.FromArgb(12, 13, 17), 90f))
            g.FillRectangle(b, 0, 0, W, H);
        using (var desk = new LinearGradientBrush(new Rectangle(0, 610, W, 110), Color.FromArgb(58, 46, 36), Color.FromArgb(34, 27, 21), 90f))
            g.FillRectangle(desk, 0, 610, W, 110);
    }

    static void Monitor(Graphics g, float x, float y, float w, float h, string name, double guideOffsetMm, bool guides, double guideAlpha, double topMm)
    {
        // stand
        using (var sb = new SolidBrush(Color.FromArgb(48, 50, 56)))
        {
            float cx = x + w / 2;
            g.FillRectangle(sb, cx - 14, y + h + Bezel, 28, 610 - (y + h + Bezel));
            g.FillRectangle(sb, cx - 70, 604, 140, 8);
        }
        using (var frame = new SolidBrush(Color.FromArgb(18, 18, 20)))
            g.FillRectangle(frame, x - Bezel, y - Bezel, w + Bezel * 2, h + Bezel * 2 + 4);
        using (var scr = new LinearGradientBrush(new RectangleF(x, y, w, h), Color.FromArgb(40, 64, 104), Color.FromArgb(24, 36, 62), 60f))
            g.FillRectangle(scr, x, y, w, h);
        using (var nb = new SolidBrush(Color.FromArgb(70, 255, 255, 255)))
        using (var nf = new Font("Segoe UI Semibold", 40))
        {
            var sz = g.MeasureString(name, nf);
            g.DrawString(name, nf, nb, x + (w - sz.Width) / 2, y + (h - sz.Height) / 2);
        }
        if (guides && guideAlpha > 0)
        {
            var clip = g.Clip;
            g.SetClip(new RectangleF(x, y, w, h));
            using (var thin = new Pen(A(Color.Lime, guideAlpha * 0.8), 1))
            using (var thick = new Pen(A(Color.FromArgb(255, 70, 70), guideAlpha), 3))
                for (int k = -2; k < 30; k++)
                {
                    float ly = y + (float)(k * 20 - topMm + guideOffsetMm);
                    if (ly < y - 4 || ly > y + h + 4) continue;
                    g.DrawLine(k % 5 == 0 ? thick : thin, x, ly, x + w, ly);
                }
            g.Clip = clip;
        }
    }

    static void Desk(Graphics g, bool guides = false, double guideAlpha = 0, double guideOffset23 = 0)
    {
        Monitor(g, X23, Y0 + (float)Top23, (float)W23, (float)H23, "23\"", guideOffset23, guides, guideAlpha, Top23);
        Monitor(g, X27, Y0, (float)W27, (float)H27, "27\"", 0, guides, guideAlpha, 0);
    }

    // Arrow cursor: 32 px tall on the monitor it is on, so it is physically smaller on the 23".
    static void Cursor(Graphics g, PointF p, double mmpp, double a)
    {
        if (a <= 0) return;
        float k = (float)(mmpp * 32 / 20 * 3.2);
        var pts = new[] { new PointF(0, 0), new PointF(0, 17), new PointF(4, 13.2f), new PointF(7, 20), new PointF(9.5f, 19), new PointF(6.6f, 12.2f), new PointF(12, 12.2f) };
        for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(p.X + pts[i].X * k, p.Y + pts[i].Y * k);
        using (var fill = new SolidBrush(A(Color.White, a)))
        using (var pen = new Pen(A(Color.Black, a), 1.4f) { LineJoin = LineJoin.Round })
        {
            g.FillPolygon(fill, pts);
            g.DrawPolygon(pen, pts);
        }
    }

    static void DashedLevel(Graphics g, float y, Color c, double a)
    {
        using (var pen = new Pen(A(c, a), 2) { DashStyle = DashStyle.Dash })
            g.DrawLine(pen, X23 - 30, y, X27 + (float)W27 + 30, y);
    }

    // A horizontal swipe from the 27" (pixel x 1200 -> 0) onto the 23" (pixel x 1919 -> 1250).
    // Returns the cursor position for progress u in [0,1]; on23 tells which screen it is on.
    static PointF Swipe(double u, double row27, double row23, out bool on23)
    {
        double total = 1200 + 670;
        double d = Ease(u) * total;
        if (d <= 1200) { on23 = false; return P27(1200 - d, row27); }
        on23 = true;
        return P23(1919 - (d - 1200), row23);
    }

    // ---------- scenes ----------
    static void Render(Graphics g, double t)
    {
        Background(g);
        if (t < SIntro) { Intro(g, t); return; }
        t -= SIntro;
        if (t < SWithout) { SwipeScene(g, t, SWithout, false); return; }
        t -= SWithout;
        if (t < SWith) { SwipeScene(g, t, SWith, true); return; }
        t -= SWith;
        if (t < SBlock) { BlockScene(g, t); return; }
        t -= SBlock;
        if (t < SGuides) { GuidesScene(g, t); return; }
        t -= SGuides;
        Outro(g, t);
    }

    static void Intro(Graphics g, double t)
    {
        double a = Fade(t, SIntro);
        Desk(g);
        using (var dim = new SolidBrush(Color.FromArgb((int)(225 * a), 10, 11, 14)))
            g.FillRectangle(dim, 0, 0, W, H);
        Text(g, "MonitorAlign", Big, Color.White, 250, a);
        Text(g, "Smooth mouse movement between monitors of different sizes", Sub, Color.FromArgb(200, 210, 225), 330, a);
        Text(g, "27\" + 23\", both 1920 \u00d7 1080", Small, Color.FromArgb(150, 160, 175), 372, a);
    }

    static void SwipeScene(Graphics g, double t, double len, bool fixedMode)
    {
        double a = Fade(t, len);
        Desk(g);
        const double row27 = 130;
        double yMm = row27 * M27;
        double row23 = fixedMode ? (yMm - Top23) / M23 : row27;   // Windows keeps the pixel row; MonitorAlign keeps the height
        Text(g, fixedMode ? "With MonitorAlign" : "Without MonitorAlign", Title, fixedMode ? Color.FromArgb(110, 220, 140) : Color.FromArgb(255, 120, 110), 40, a);
        Text(g, fixedMode ? "The pointer crosses at exactly the same height" : "Windows keeps the pixel row, so the pointer jumps down on the smaller screen",
             Sub, Color.FromArgb(200, 210, 225), 98, a);

        double u = Clamp01((t - 0.6) / 3.6);
        bool on23;
        var p = Swipe(u, row27, row23, out on23);
        DashedLevel(g, Y0 + (float)yMm, Color.FromArgb(255, 210, 90), a * 0.8);
        Label(g, "eye level of the pointer", new PointF(X27 + (float)W27 - 110, Y0 + (float)yMm - 34), Color.FromArgb(255, 210, 90), a);

        // trail
        for (int i = 12; i >= 1; i--)
        {
            bool o; var q = Swipe(Clamp01((t - 0.6 - i * 0.035) / 3.6), row27, row23, out o);
            using (var b = new SolidBrush(A(Color.FromArgb(120, 190, 255), a * (1 - i / 13.0) * 0.5)))
                g.FillEllipse(b, q.X - 4, q.Y - 4, 8, 8);
        }
        Cursor(g, p, on23 ? M23 : M27, a);

        if (on23 && t > 3.2)
        {
            double b = a * Clamp01((t - 3.2) / 0.4);
            float yHere = p.Y;
            if (!fixedMode)
            {
                float y0 = Y0 + (float)yMm;
                using (var pen = new Pen(A(Color.FromArgb(255, 110, 100), b), 3) { EndCap = LineCap.ArrowAnchor })
                    g.DrawLine(pen, X23 + (float)W23 - 60, y0, X23 + (float)W23 - 60, yHere + 2);
                double cm = (yHere - y0) / 10.0;
                Label(g, "lands " + cm.ToString("0.0") + " cm lower", new PointF(X23 + (float)W23 - 170, yHere + 26), Color.FromArgb(255, 120, 110), b);
            }
            else
                Label(g, "same height  \u2713", new PointF(X23 + (float)W23 - 170, yHere + 26), Color.FromArgb(110, 220, 140), b);
        }
    }

    static void BlockScene(Graphics g, double t)
    {
        double a = Fade(t, SBlock);
        Desk(g);
        Text(g, "Optional: stop where there is no screen beside it", Title, Color.FromArgb(140, 190, 255), 40, a);
        Text(g, "The top and bottom strips of the 27\" have nothing next to them", Sub, Color.FromArgb(200, 210, 225), 98, a);

        // shade the strips of the 27" edge with no 23" beside them
        using (var hb = new HatchBrush(HatchStyle.BackwardDiagonal, A(Color.FromArgb(255, 110, 100), a * 0.7), Color.Transparent))
        {
            g.FillRectangle(hb, X27, Y0, 40, (float)Top23);
            g.FillRectangle(hb, X27, Y0 + (float)(Top23 + H23), 40, (float)(H27 - Top23 - H23));
        }

        const double rowTop = 25;  // above the 23"
        const double rowIn = 190;  // beside the 23"
        PointF p; double mmpp = M27;
        if (t < 2.2) p = P27(900 * (1 - Ease((t - 0.4) / 1.6)), rowTop);                     // move left, hit the edge
        else if (t < 3.4)
        {
            double shake = Math.Sin((t - 2.2) * 40) * 3 * (1 - Clamp01((t - 2.2) / 1.2));
            p = new PointF(P27(0, rowTop).X + (float)Math.Abs(shake), P27(0, rowTop).Y);         // bumps the edge
        }
        else if (t < 4.6) p = P27(0, rowTop + (rowIn - rowTop) * Ease((t - 3.4) / 1.1));      // slide down the edge
        else
        {
            double row23 = (rowIn * M27 - Top23) / M23;
            double u = Ease((t - 4.6) / 1.4);
            p = P23(1919 - 700 * u, row23); mmpp = M23;                                          // now it crosses, level
        }
        Cursor(g, p, mmpp, a);

        if (t > 2.2 && t < 4.4)
            Label(g, "stops at the edge", new PointF(X27 + 110, Y0 + (float)(rowTop * M27) + 30), Color.FromArgb(255, 120, 110), a * Clamp01((t - 2.2) / 0.3));
        if (t > 4.8)
            Label(g, "crosses once the 23\" is beside it", new PointF(X23 + (float)W23 - 200, p.Y + 30), Color.FromArgb(110, 220, 140), a * Clamp01((t - 4.8) / 0.3));
    }

    static void GuidesScene(Graphics g, double t)
    {
        double a = Fade(t, SGuides);
        double off = 38 * (1 - Ease((t - 1.6) / 2.6));     // 23" guide lines start 3.8 cm off, then line up
        Desk(g, true, a * Clamp01((t - 0.3) / 0.5), off);
        Text(g, "Line it up in a minute", Title, Color.White, 40, a);
        Text(g, t < 4.4 ? "Turn on guide lines and drag the 23\" in Settings\u2026" : "\u2026until the lines run straight across both screens",
             Sub, Color.FromArgb(200, 210, 225), 98, a);

        // mini settings preview, bottom centre
        float px = W / 2 - 150, py = 632, sc = 0.13f;
        using (var bg = new SolidBrush(A(Color.FromArgb(240, 32, 34, 38), a)))
        using (var border = new Pen(A(Color.FromArgb(120, 130, 145), a)))
        {
            g.FillRectangle(bg, px, py, 300, 80);
            g.DrawRectangle(border, px, py, 300, 80);
        }
        float bx = px + 150 - (float)(W23 + W27 + 20) * sc / 2, by = py + 14;
        float y23 = by + (float)((Top23 - off) * sc);
        using (var b27 = new SolidBrush(A(Color.FromArgb(52, 98, 160), a)))
        using (var b23 = new SolidBrush(A(Color.FromArgb(70, 76, 88), a)))
        using (var sel = new Pen(A(Color.Orange, a), 2))
        {
            g.FillRectangle(b23, bx, y23, (float)W23 * sc, (float)H23 * sc);
            g.DrawRectangle(sel, bx, y23, (float)W23 * sc, (float)H23 * sc);
            g.FillRectangle(b27, bx + (float)(W23 + 20) * sc, by, (float)W27 * sc, (float)H27 * sc);
        }
        Cursor(g, new PointF(bx + (float)W23 * sc / 2, y23 + (float)H23 * sc / 2), M27 * 0.5, a);

        if (t > 4.6) Label(g, "aligned  \u2713", new PointF(X23 + (float)W23 + Gap / 2 + Bezel, Y0 + 205), Color.FromArgb(110, 220, 140), a * Clamp01((t - 4.6) / 0.3));
    }

    static void Outro(Graphics g, double t)
    {
        double a = Fade(t, SOutro);
        Desk(g);
        using (var dim = new SolidBrush(Color.FromArgb((int)(190 * Clamp01(t / 0.4)), 10, 11, 14)))
            g.FillRectangle(dim, 0, 0, W, H);
        Text(g, "MonitorAlign", Big, Color.White, 220, a);
        Text(g, "Windows 10 / 11  \u00b7  tiny tray app  \u00b7  no admin needed", Sub, Color.FromArgb(200, 210, 225), 300, a);
        Text(g, "Unzip  \u2192  double-click Install.bat  \u2192  set your monitor sizes", Sub, Color.FromArgb(110, 220, 140), 345, a);
    }
}
