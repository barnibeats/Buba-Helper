// Самопроверка редактора файла ответов («обезьяний» тест): случайно меняет поля на всех страницах и проверяет,
// что интерфейс никогда не записывает в состояние недопустимые значения и что XML всегда генерируется.
// Запуск: Buba-Helper.exe --selftest-ui отчёт.txt [число действий] [seed]
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;

namespace WinInstaller
{
    partial class GenEditor
    {
        static readonly string[] Pool = { "", " ", "a", "User", "bob", "it's", "x&y<z>", "C:\\Tools", "D:\\a b\\", "10.0.0.5", "256.1.1.1", "pool.ntp.org", "ABCDE-FGHIJ-KLMNO-PQRST-UVWXY",
            "Администратор", "line1\r\nline2", "HKLM\\SOFTWARE\\X", "HKCU\\Software\\Y", "echo hi", "Write-Host 1", "03:00", "Foo.Bar", ".iso", "E", "Z", "PC-01", "12345678" };

        static void Collect(Control root, List<Control> acc)
        {
            foreach (Control c in root.Controls)
            {
                acc.Add(c);
                Collect(c, acc);
            }
        }

        bool Interactive(Control c)
        {
            if (!c.Visible || !c.Enabled) return false;
            if (c is NumericUpDown || c is ComboBox) return true;
            TextBox tb = c as TextBox;
            if (tb != null) return !tb.ReadOnly;
            if (c is CheckBox) return true;
            AeroButton b = c as AeroButton;
            if (b != null)
            {
                string t = b.Text;
                return t.StartsWith("+ ") || t == "Удалить" || t == "Вверх" || t == "Вниз" || t == "Все" || t == "Сбросить" || t == "Выбрать все";
            }
            return false;
        }

        public string SelfTest(int ops, int seed)
        {
            Random rnd = new Random(seed);
            StringBuilder rep = new StringBuilder();
            int errors = 0, done = 0;
            for (int i = 0; i < ops && errors < 10; i++)
            {
                Go(rnd.Next(pages.Count));
                List<Control> all = new List<Control>();
                Collect(pages[Index], all);
                List<Control> cand = all.FindAll(Interactive);
                if (cand.Count == 0) continue;
                Control c = cand[rnd.Next(cand.Count)];
                string what = c.GetType().Name + " «" + (c is TextBox || c is NumericUpDown ? c.Name : c.Text) + "» на странице " + pages[Index].Id;
                try
                {
                    NumericUpDown nud = c as NumericUpDown;
                    ComboBox cb = c as ComboBox;
                    TextBox tb = c as TextBox;
                    CheckBox chk = c as CheckBox;
                    AeroButton btn = c as AeroButton;
                    if (nud != null)
                    {
                        int k = rnd.Next(4);
                        nud.Value = k == 0 ? nud.Minimum : k == 1 ? nud.Maximum : (decimal)rnd.Next((int)Math.Min(nud.Minimum, 1000), (int)Math.Min(nud.Maximum, 100000) + 1);
                        what += " = " + nud.Value;
                    }
                    else if (cb != null) { if (cb.Items.Count > 0) { cb.SelectedIndex = rnd.Next(cb.Items.Count); what += " = " + cb.SelectedItem; } }
                    else if (tb != null) { tb.Text = Pool[rnd.Next(Pool.Length)]; what += " = " + tb.Text; }
                    else if (chk != null) { chk.Checked = !chk.Checked; what += " = " + chk.Checked; }
                    else if (btn != null) btn.PerformClick();
                    Regenerate();
                    List<string> P = new List<string>();
                    GenState.Sanitize(St.Clone(), P);
                    P.RemoveAll(delegate(string s) { return s.Contains("время «"); });   // свободный ввод времени допускается, как в оригинале
                    if (P.Count > 0) { errors++; rep.AppendLine("Недопустимое состояние после: " + what + "\n  " + string.Join("\n  ", P.ToArray())); }
                    done++;
                }
                catch (Exception ex) { errors++; rep.AppendLine("ИСКЛЮЧЕНИЕ при: " + what + "\n" + ex); }
            }
            rep.Insert(0, "Действий: " + done + ", ошибок: " + errors + "\n");
            return rep.ToString();
        }
    }
}
