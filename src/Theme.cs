// Тёмная плоская тема в духе игровых лаунчеров: палитра, шрифты, значки и тёмный режим окон.
// Всё рисуется кодом (GDI+), без внешних файлов, поэтому выглядит одинаково и в WinPE, где системной темы нет.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WinInstaller
{
    static class Theme
    {
        public static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }

        // поверхности: окно < боковая панель < карточка < поле ввода < наведение
        public static readonly Color Bg = C(14, 16, 20);
        public static readonly Color Side = C(18, 21, 27);
        public static readonly Color Card = C(24, 28, 36);
        public static readonly Color Card2 = C(29, 34, 44);
        public static readonly Color Field = C(33, 38, 49);
        public static readonly Color Hover = C(41, 47, 61);
        public static readonly Color Line = C(45, 52, 66);
        public static readonly Color LineHi = C(70, 80, 102);
        // текст
        public static readonly Color Text = C(232, 235, 241);
        public static readonly Color Muted = C(141, 150, 168);
        public static readonly Color Dim = C(92, 100, 117);
        // акценты
        public static readonly Color Accent = C(91, 124, 255);
        public static readonly Color AccentHi = C(124, 152, 255);
        public static readonly Color AccentLo = C(66, 94, 226);
        public static readonly Color AccentSoft = C(31, 39, 70);
        public static readonly Color Ok = C(61, 214, 140);
        public static readonly Color Warn = C(242, 184, 75);
        public static readonly Color Danger = C(255, 107, 107);

        public static Color Ink { get { return Text; } }

        public static readonly Font UI = new Font("Segoe UI", 9.5f);
        public static readonly Font Small = new Font("Segoe UI", 8.5f);
        public static readonly Font Semi = new Font("Segoe UI Semibold", 9.5f);
        public static readonly Font Big = new Font("Segoe UI Semibold", 11f);
        public static readonly Font H1 = new Font("Segoe UI Semibold", 17f);
        public static readonly Font Mono = new Font("Consolas", 9.5f);

        public const TextFormatFlags TF = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;

        public static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = rad * 2;
            if (rad <= 0 || r.Width < d || r.Height < d) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }

        public static void FillRound(Graphics g, RectangleF r, float rad, Color fill)
        {
            using (GraphicsPath p = Round(r, rad))
            using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
        }

        public static void StrokeRound(Graphics g, RectangleF r, float rad, Color line, float w)
        {
            using (GraphicsPath p = Round(r, rad))
            using (Pen pen = new Pen(line, w)) g.DrawPath(pen, p);
        }

        public static void GradRound(Graphics g, RectangleF r, float rad, Color top, Color bottom)
        {
            if (r.Width < 1 || r.Height < 1) return;
            using (GraphicsPath p = Round(r, rad))
            using (LinearGradientBrush b = new LinearGradientBrush(r, top, bottom, 90f)) g.FillPath(b, p);
        }

        public static Color ParentBg(Control c)
        {
            return c.Parent != null ? c.Parent.BackColor : Bg;
        }
    }

    /// <summary>Тёмный режим окон Windows 10/11: заголовок окна и полосы прокрутки. Всё в try/catch: в WinPE и старых системах просто не сработает.</summary>
    static class Dark
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string sub, string ids);
        [DllImport("uxtheme.dll", EntryPoint = "#135")] static extern int AllowDarkModeForApp(int mode);
        [DllImport("uxtheme.dll", EntryPoint = "#133")] static extern bool AllowDarkModeForWindow(IntPtr h, bool allow);

        public static void Init()
        {
            try { AllowDarkModeForApp(1); } catch { }
        }

        public static void Title(IntPtr h)
        {
            try { int v = 1; DwmSetWindowAttribute(h, 20, ref v, 4); DwmSetWindowAttribute(h, 19, ref v, 4); } catch { }
        }

        /// <summary>Тёмные полосы прокрутки у окна/списка.</summary>
        public static void Scroll(IntPtr h)
        {
            try { AllowDarkModeForWindow(h, true); } catch { }
            try { SetWindowTheme(h, "DarkMode_Explorer", null); } catch { }
        }

        public static void Scroll(Control c)
        {
            if (c.IsHandleCreated) Scroll(c.Handle);
            c.HandleCreated += delegate { Scroll(c.Handle); };
        }
    }

    /// <summary>Плоские линейные значки (сетка 24x24), цвет задаётся при рисовании.</summary>
    static class Icons
    {
        static readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>();

        static PointF[] Pts(params float[] v)
        {
            PointF[] p = new PointF[v.Length / 2];
            for (int i = 0; i < p.Length; i++) p[i] = new PointF(v[i * 2], v[i * 2 + 1]);
            return p;
        }

        /// <summary>Значок из кэша: не освобождать.</summary>
        public static Bitmap Get(string name, int size, Color col)
        {
            string key = name + "|" + col.ToArgb() + "|" + size;
            Bitmap b;
            if (!cache.TryGetValue(key, out b)) { b = Make(name, size, col); cache[key] = b; }
            return b;
        }

        public static Bitmap Make(string name, int size) { return Make(name, size, Theme.Muted); }

        public static Bitmap Make(string name, int size, Color col)
        {
            Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                Theme.Smooth(g);
                if (name == "app") { DrawApp(g, size); return bmp; }
                g.ScaleTransform(size / 24f, size / 24f);
                using (Pen p = new Pen(col, 1.9f))
                using (SolidBrush fill = new SolidBrush(col))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                    Glyph(g, p, fill, name);
                }
            }
            return bmp;
        }

        static void Glyph(Graphics g, Pen p, Brush fill, string name)
        {
            switch (name)
            {
                case "disc":
                    g.DrawEllipse(p, 3f, 3f, 18f, 18f); g.DrawEllipse(p, 9f, 9f, 6f, 6f);
                    break;
                case "drive":
                    using (GraphicsPath gp = Theme.Round(new RectangleF(3, 7, 18, 10), 2.5f)) g.DrawPath(p, gp);
                    g.DrawLine(p, 6.5f, 12f, 11f, 12f); g.FillEllipse(fill, 15.8f, 10.8f, 2.4f, 2.4f);
                    break;
                case "layout":
                    using (GraphicsPath gp = Theme.Round(new RectangleF(3, 4, 18, 16), 2.5f)) g.DrawPath(p, gp);
                    g.DrawLine(p, 9.5f, 4f, 9.5f, 20f); g.DrawLine(p, 9.5f, 12f, 21f, 12f);
                    break;
                case "gear":
                    g.DrawEllipse(p, 7f, 7f, 10f, 10f); g.DrawEllipse(p, 10.2f, 10.2f, 3.6f, 3.6f);
                    for (int k = 0; k < 8; k++)
                    {
                        double a = k * Math.PI / 4;
                        g.DrawLine(p, (float)(12 + 6 * Math.Cos(a)), (float)(12 + 6 * Math.Sin(a)), (float)(12 + 9.2 * Math.Cos(a)), (float)(12 + 9.2 * Math.Sin(a)));
                    }
                    break;
                case "go":
                    g.FillPolygon(fill, Pts(8, 5, 19.5f, 12, 8, 19));
                    g.DrawPolygon(p, Pts(8, 5, 19.5f, 12, 8, 19));
                    break;
                case "camera":
                    using (GraphicsPath gp = Theme.Round(new RectangleF(3, 7.5f, 18, 12), 2.5f)) g.DrawPath(p, gp);
                    g.DrawLines(p, Pts(8, 7.5f, 9.8f, 4.5f, 14.2f, 4.5f, 16, 7.5f));
                    g.DrawEllipse(p, 8.6f, 10.4f, 6.8f, 6.8f);
                    break;
                case "doc":
                    g.DrawPolygon(p, Pts(6, 3, 14, 3, 19, 8, 19, 21, 6, 21));
                    g.DrawLines(p, Pts(14, 3, 14, 8, 19, 8));
                    g.DrawLine(p, 9.5f, 13f, 15.5f, 13f); g.DrawLine(p, 9.5f, 17f, 15.5f, 17f);
                    break;
                case "arrowR":
                    g.DrawLine(p, 4f, 12f, 19.5f, 12f); g.DrawLines(p, Pts(13, 5.5f, 19.5f, 12, 13, 18.5f));
                    break;
                case "arrowL":
                    g.DrawLine(p, 20f, 12f, 4.5f, 12f); g.DrawLines(p, Pts(11, 5.5f, 4.5f, 12, 11, 18.5f));
                    break;
                case "star":
                    {
                        PointF[] st = new PointF[10];
                        for (int k = 0; k < 10; k++)
                        {
                            double a = -Math.PI / 2 + k * Math.PI / 5, rr = k % 2 == 0 ? 9.5 : 4.2;
                            st[k] = new PointF((float)(12 + rr * Math.Cos(a)), (float)(12.8 + rr * Math.Sin(a)));
                        }
                        g.DrawPolygon(p, st);
                    }
                    break;
                case "globe":
                    g.DrawEllipse(p, 3f, 3f, 18f, 18f); g.DrawEllipse(p, 8f, 3f, 8f, 18f); g.DrawLine(p, 3f, 12f, 21f, 12f);
                    break;
                case "monitor":
                    using (GraphicsPath gp = Theme.Round(new RectangleF(3, 4, 18, 12), 2f)) g.DrawPath(p, gp);
                    g.DrawLine(p, 8f, 20f, 16f, 20f); g.DrawLine(p, 12f, 16f, 12f, 20f);
                    break;
                case "user":
                    g.DrawEllipse(p, 8f, 3.5f, 8f, 8f); g.DrawArc(p, 4.5f, 14f, 15f, 14f, 180, 180);
                    break;
                case "shield":
                    g.DrawPolygon(p, Pts(12, 3, 20, 6, 20, 12, 12, 21, 4, 12, 4, 6));
                    g.DrawLines(p, Pts(8.2f, 11.8f, 11f, 14.6f, 15.8f, 9));
                    break;
                case "net":
                    g.DrawLines(p, Pts(12, 6.5f, 5.5f, 17.5f, 18.5f, 17.5f, 12, 6.5f));
                    foreach (PointF c in Pts(12, 5.5f, 5, 18, 19, 18)) { g.FillEllipse(fill, c.X - 2.4f, c.Y - 2.4f, 4.8f, 4.8f); }
                    break;
                case "key":
                    g.DrawEllipse(p, 3f, 8f, 8f, 8f);
                    g.DrawLine(p, 11f, 12f, 21f, 12f); g.DrawLine(p, 17f, 12f, 17f, 16f); g.DrawLine(p, 20f, 12f, 20f, 15f);
                    break;
                case "trash":
                    g.DrawLine(p, 4f, 7f, 20f, 7f);
                    g.DrawPolygon(p, Pts(6, 7, 7, 21, 17, 21, 18, 7));
                    g.DrawLines(p, Pts(9, 7, 9, 4, 15, 4, 15, 7));
                    g.DrawLine(p, 10f, 11f, 10f, 17f); g.DrawLine(p, 14f, 11f, 14f, 17f);
                    break;
                case "box":
                    g.DrawPolygon(p, Pts(12, 3, 20, 7.5f, 20, 16.5f, 12, 21, 4, 16.5f, 4, 7.5f));
                    g.DrawLines(p, Pts(4, 7.5f, 12, 12, 20, 7.5f)); g.DrawLine(p, 12f, 12f, 12f, 21f);
                    break;
                case "script":
                    using (GraphicsPath gp = Theme.Round(new RectangleF(3, 4, 18, 16), 2.5f)) g.DrawPath(p, gp);
                    g.DrawLines(p, Pts(7, 9, 10.5f, 12, 7, 15)); g.DrawLine(p, 12.5f, 15f, 17f, 15f);
                    break;
                case "clock":
                    g.DrawEllipse(p, 3f, 3f, 18f, 18f); g.DrawLines(p, Pts(12, 7, 12, 12, 15.8f, 14.2f));
                    break;
                case "check":
                    g.DrawEllipse(p, 3f, 3f, 18f, 18f); g.DrawLines(p, Pts(7.8f, 12.4f, 10.7f, 15.3f, 16.4f, 9.2f));
                    break;
                case "tick":
                    g.DrawLines(p, Pts(5, 12.5f, 10, 17.5f, 19, 7));
                    break;
                case "folder":
                    g.DrawPolygon(p, Pts(3, 6.5f, 9.5f, 6.5f, 11.5f, 9, 21, 9, 21, 19, 3, 19));
                    break;
                case "search":
                    g.DrawEllipse(p, 4f, 4f, 11f, 11f); g.DrawLine(p, 13.2f, 13.2f, 20f, 20f);
                    break;
                case "refresh":
                    g.DrawArc(p, 5f, 5f, 14f, 14f, -50, 280);
                    g.DrawLines(p, Pts(18.5f, 3.5f, 18.5f, 8.5f, 13.5f, 8.5f));
                    break;
                case "log":
                    g.DrawLine(p, 4f, 6f, 20f, 6f); g.DrawLine(p, 4f, 12f, 20f, 12f); g.DrawLine(p, 4f, 18f, 14f, 18f);
                    break;
                case "save":
                    g.DrawPolygon(p, Pts(4, 4, 17, 4, 20, 7, 20, 20, 4, 20));
                    g.DrawRectangle(p, 8f, 4f, 7f, 5f); g.DrawRectangle(p, 7f, 13f, 10f, 7f);
                    break;
                default:
                    g.DrawEllipse(p, 4f, 4f, 16f, 16f);
                    break;
            }
        }

        /// <summary>Логотип приложения: скруглённый квадрат с градиентом и стрелкой в лоток.</summary>
        static void DrawApp(Graphics g, int s)
        {
            RectangleF r = new RectangleF(0.5f, 0.5f, s - 1, s - 1);
            using (GraphicsPath gp = Theme.Round(r, s * 0.26f))
            using (LinearGradientBrush br = new LinearGradientBrush(r, Theme.C(139, 108, 255), Theme.C(58, 132, 255), 45f))
                g.FillPath(br, gp);
            using (SolidBrush w = new SolidBrush(Color.White))
            {
                g.FillRectangle(w, s * 0.435f, s * 0.2f, s * 0.13f, s * 0.3f);
                g.FillPolygon(w, new PointF[] { new PointF(s * 0.27f, s * 0.47f), new PointF(s * 0.73f, s * 0.47f), new PointF(s * 0.5f, s * 0.72f) });
                using (GraphicsPath tray = Theme.Round(new RectangleF(s * 0.26f, s * 0.77f, s * 0.48f, s * 0.075f), s * 0.035f)) g.FillPath(w, tray);
            }
        }

        public static Icon AppIcon()
        {
            using (Bitmap b = Make("app", 32)) return Icon.FromHandle(b.GetHicon());
        }

        /// <summary>Записывает значок приложения (16/24/32/48 px) в .ico - для /win32icon при сборке exe.</summary>
        public static void WriteIco(string path)
        {
            int[] sizes = new int[] { 16, 24, 32, 48 };
            List<byte[]> frames = new List<byte[]>();
            foreach (int s in sizes)
            {
                byte[] px = new byte[s * s * 4];
                using (Bitmap b = Make("app", s))
                {
                    BitmapData d = b.LockBits(new Rectangle(0, 0, s, s), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    Marshal.Copy(d.Scan0, px, 0, px.Length);
                    b.UnlockBits(d);
                }
                int maskRow = ((s + 31) / 32) * 4;
                using (MemoryStream ms = new MemoryStream())
                using (BinaryWriter w = new BinaryWriter(ms))
                {
                    w.Write(40); w.Write(s); w.Write(s * 2); w.Write((short)1); w.Write((short)32);
                    w.Write(0); w.Write(px.Length + maskRow * s); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
                    for (int y = s - 1; y >= 0; y--) w.Write(px, y * s * 4, s * 4);   // BMP хранится снизу вверх
                    w.Write(new byte[maskRow * s]);
                    w.Flush();
                    frames.Add(ms.ToArray());
                }
            }
            using (FileStream fs = new FileStream(path, FileMode.Create))
            using (BinaryWriter w = new BinaryWriter(fs))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)sizes[i]); w.Write((byte)sizes[i]); w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32); w.Write(frames[i].Length); w.Write(offset);
                    offset += frames[i].Length;
                }
                foreach (byte[] f in frames) w.Write(f);
            }
        }
    }
}
