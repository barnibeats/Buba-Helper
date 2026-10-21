// Главная форма: переключение режимов (установка / захват / файл ответов) и связь с генератором XML.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace WinInstaller
{
    partial class MainForm
    {
        void SwitchMode()
        {
            bool cap = rbModeCapture.Checked, xml = rbModeXml.Checked;
            if (xml) EnsureGen();
            tabCapture.Visible = cap;
            tabInstall.Visible = !cap && !xml;
            if (genEditor != null) genEditor.Visible = xml;
            progress.Visible = lblStatus.Visible = txtLog.Visible = !xml;
            LayoutFooter(xml);
            btnInstall.Text = cap ? "ЗАХВАТИТЬ ОБРАЗ" : "УСТАНОВИТЬ WINDOWS";
            if (cap) RefreshVolumes();
            UpdateNav();
        }

        /// <summary>Кнопки «Назад/Далее» под вкладками установки и внизу окна в режиме генератора (там нет журнала).</summary>
        void LayoutFooter(bool xml)
        {
            int fy = xml ? ClientSize.Height - 52 : 358;
            AnchorStyles v = xml ? AnchorStyles.Bottom : AnchorStyles.Top;
            btnBack.Anchor = v | AnchorStyles.Left;
            btnNext.Anchor = v | AnchorStyles.Right;
            btnInstall.Anchor = v | AnchorStyles.Right;
            lblNavHint.Anchor = v | AnchorStyles.Left | AnchorStyles.Right;
            btnBack.Top = btnNext.Top = btnInstall.Top = lblNavHint.Top = fy;
            if (genEditor != null) genEditor.SetBounds(0, 48, ClientSize.Width, fy - 56);
        }

        void EnsureGen()
        {
            if (genEditor != null) return;
            UseWaitCursor = true;
            try
            {
                genEditor = new GenEditor();
                genEditor.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
                genEditor.StatePath = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Buba-Helper"), "generator-settings.json");
                genEditor.LoadFromFile(genEditor.StatePath);
                genEditor.IndexChanged += delegate { UpdateNav(); };
                genEditor.Regenerated += delegate { UpdateNav(); };
                genEditor.UseXml += OnUseXml;
                Controls.Add(genEditor);
                genEditor.SetBounds(0, 48, ClientSize.Width, ClientSize.Height - 52 - 56);
                genEditor.Visible = false;
                genEditor.Go(0);
            }
            finally { UseWaitCursor = false; }
        }

        /// <summary>Файл ответов из генератора: подставляется в поле XML установки или захвата образа.</summary>
        void OnUseXml(string path, bool forCapture)
        {
            if (forCapture)
            {
                txtXml.Text = path;
                rbModeCapture.Checked = true;
                Log("Файл ответов из генератора выбран для захвата образа: " + path);
                MessageBox.Show(this, "Файл ответов выбран для захвата образа (шаг «3. XML и запуск»).", "Файл ответов");
            }
            else
            {
                txtInstXml.Text = path;
                rbModeInstall.Checked = true;
                Log("Файл ответов из генератора выбран для установки: " + path);
                MessageBox.Show(this, "Файл ответов выбран для установки (шаг «4. Настройки», поле «XML ответов»).\n\nПри установке этой программой образ применяется через DISM, поэтому из файла используются этапы specialize и oobeSystem (пользователи, пропуск OOBE, твики). Диск и редакцию вы выбираете на вкладках установки.", "Файл ответов");
            }
        }
    }
}
