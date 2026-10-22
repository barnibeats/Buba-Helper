// Тёмный MessageBox. Класс лежит в пространстве имён приложения и перекрывает System.Windows.Forms.MessageBox,
// поэтому все существующие вызовы MessageBox.Show(...) автоматически получают тёмное окно.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WinInstaller
{
    static class MessageBox
    {
        public static DialogResult Show(string text) { return Show(null, text, "Buba-Helper", MessageBoxButtons.OK, MessageBoxIcon.None); }
        public static DialogResult Show(string text, string caption) { return Show(null, text, caption, MessageBoxButtons.OK, MessageBoxIcon.None); }
        public static DialogResult Show(IWin32Window owner, string text) { return Show(owner, text, "Buba-Helper", MessageBoxButtons.OK, MessageBoxIcon.None); }
        public static DialogResult Show(IWin32Window owner, string text, string caption) { return Show(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.None); }
        public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons) { return Show(owner, text, caption, buttons, MessageBoxIcon.None); }

        static void Buttons(MessageBoxButtons b, List<string> caps, List<DialogResult> res)
        {
            switch (b)
            {
                case MessageBoxButtons.OKCancel: caps.Add("OK"); res.Add(DialogResult.OK); caps.Add("Отмена"); res.Add(DialogResult.Cancel); break;
                case MessageBoxButtons.YesNo: caps.Add("Да"); res.Add(DialogResult.Yes); caps.Add("Нет"); res.Add(DialogResult.No); break;
                case MessageBoxButtons.YesNoCancel: caps.Add("Да"); res.Add(DialogResult.Yes); caps.Add("Нет"); res.Add(DialogResult.No); caps.Add("Отмена"); res.Add(DialogResult.Cancel); break;
                case MessageBoxButtons.RetryCancel: caps.Add("Повторить"); res.Add(DialogResult.Retry); caps.Add("Отмена"); res.Add(DialogResult.Cancel); break;
                case MessageBoxButtons.AbortRetryIgnore: caps.Add("Прервать"); res.Add(DialogResult.Abort); caps.Add("Повторить"); res.Add(DialogResult.Retry); caps.Add("Пропустить"); res.Add(DialogResult.Ignore); break;
                default: caps.Add("OK"); res.Add(DialogResult.OK); break;
            }
        }

        public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            return Show(owner, text, caption, buttons, icon, MessageBoxDefaultButton.Button1);
        }

        public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton def)
        {
            List<string> caps = new List<string>();
            List<DialogResult> res = new List<DialogResult>();
            Buttons(buttons, caps, res);
            int defIdx = def == MessageBoxDefaultButton.Button2 ? 1 : def == MessageBoxDefaultButton.Button3 ? 2 : 0;
            if (defIdx >= caps.Count) defIdx = 0;

            using (Form f = new Form())
            {
                f.Text = caption;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = owner != null ? FormStartPosition.CenterParent : FormStartPosition.CenterScreen;
                f.MinimizeBox = false; f.MaximizeBox = false; f.ShowInTaskbar = false;
                f.BackColor = Theme.Bg; f.ForeColor = Theme.Text; f.Font = Theme.UI;
                f.HandleCreated += delegate { Dark.Title(f.Handle); };

                Color ic = Theme.Accent; string glyph = "i";
                bool hasIcon = icon != MessageBoxIcon.None;
                switch (icon)
                {
                    case MessageBoxIcon.Error: ic = Theme.Danger; glyph = "×"; break;
                    case MessageBoxIcon.Warning: ic = Theme.Warn; glyph = "!"; break;
                    case MessageBoxIcon.Question: ic = Theme.Accent; glyph = "?"; break;
                }

                int textW = 430, left = hasIcon ? 84 : 28;
                Size ts = TextRenderer.MeasureText(text ?? "", Theme.UI, new Size(textW, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                int textH = Math.Max(ts.Height + 4, 32);
                Control body;
                if (textH > 340)
                {
                    TextBox tb = new TextBox();
                    tb.Multiline = true; tb.ReadOnly = true; tb.ScrollBars = ScrollBars.Vertical; tb.BorderStyle = BorderStyle.None;
                    tb.BackColor = Theme.Bg; tb.ForeColor = Theme.Text; tb.Font = Theme.UI;
                    tb.Text = (text ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
                    tb.SetBounds(left, 26, textW + 20, 340);
                    Dark.Scroll(tb);
                    body = tb; textH = 340;
                }
                else
                {
                    Label l = new Label();
                    l.AutoSize = false; l.Text = text ?? ""; l.Font = Theme.UI; l.ForeColor = Theme.Text; l.UseMnemonic = false;
                    l.SetBounds(left, 26, textW, textH);
                    body = l;
                }
                f.Controls.Add(body);

                if (hasIcon)
                {
                    DPanel badge = new DPanel();
                    badge.SetBounds(26, 26, 40, 40);
                    badge.Paint += delegate(object s, PaintEventArgs e)
                    {
                        e.Graphics.Clear(Theme.Bg);
                        Theme.Smooth(e.Graphics);
                        using (SolidBrush b = new SolidBrush(Theme.Mix(Theme.Bg, ic, 0.2))) e.Graphics.FillEllipse(b, 0, 0, 39, 39);
                        using (Pen p = new Pen(ic, 1.5f)) e.Graphics.DrawEllipse(p, 0.75f, 0.75f, 37.5f, 37.5f);
                        TextRenderer.DrawText(e.Graphics, glyph, new Font("Segoe UI Semibold", 15f), new Rectangle(0, 0, 40, 38), ic,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    };
                    f.Controls.Add(badge);
                }

                int bodyH = Math.Max(textH, hasIcon ? 40 : 0);
                DPanel bar = new DPanel();
                bar.BackColor = Theme.Side;
                int barH = 64;
                int width = left + textW + 40;
                bar.SetBounds(0, 26 + bodyH + 26, width, barH);
                f.Controls.Add(bar);
                f.ClientSize = new Size(width, bar.Bottom);

                int bw = 104, x = width - 20;
                UiButton[] btns = new UiButton[caps.Count];
                for (int i = caps.Count - 1; i >= 0; i--)
                {
                    UiButton b = new UiButton();
                    b.Text = caps[i]; b.DialogResult = res[i];
                    b.Primary = i == defIdx;
                    b.SetBounds(x - bw, 15, bw, 34);
                    bar.Controls.Add(b); btns[i] = b;
                    x -= bw + 8;
                    if (i == defIdx) f.AcceptButton = b;
                }
                DialogResult esc = res.Contains(DialogResult.Cancel) ? DialogResult.Cancel : res.Contains(DialogResult.No) ? DialogResult.No : res[res.Count - 1];
                f.ActiveControl = btns[defIdx];
                f.CancelButton = btns[res.IndexOf(esc)];   // Esc закрывает окно с «отрицательным» ответом

                return owner != null ? f.ShowDialog(owner) : f.ShowDialog();
            }
        }
    }
}
