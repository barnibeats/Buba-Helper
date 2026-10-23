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
            bool cap = rbModeCapture.Checked, rep = rbModeRepair.Checked, xml = rbModeXml.Checked;
            if (xml) EnsureGen();
            tabCapture.Visible = cap;
            tabRepair.Visible = rep;
            tabInstall.Visible = !cap && !rep && !xml;
            navSteps.Visible = !xml;
            if (genEditor != null) genEditor.Visible = xml;
            progress.Visible = lblStatus.Visible = btnLog.Visible = !xml;
            pnlLog.Visible = logOpen && !xml;
            LayoutFooter(xml);
            btnInstall.Text = cap ? "ЗАХВАТИТЬ ОБРАЗ" : rep ? "ИСПРАВИТЬ ЗАГРУЗЧИК" : "УСТАНОВИТЬ WINDOWS";
            if (!xml) RebuildNav();
            if (cap) RefreshVolumes();
            if (rep) RefreshSystems();
            UpdateNav();
        }

        /// <summary>Нижняя полоса: в режиме генератора нет прогресса и статуса, поэтому она ниже, а кнопки выше.</summary>
        void LayoutFooter(bool xml)
        {
            footer.Height = xml ? 72 : 108;
            int by = xml ? 16 : 58;
            btnBack.Top = btnNext.Top = btnInstall.Top = lblNavHint.Top = by;
        }

        void EnsureGen()
        {
            if (genEditor != null) return;
            UseWaitCursor = true;
            try
            {
                genEditor = new GenEditor();
                genEditor.Dock = DockStyle.Fill;
                genEditor.StatePath = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Buba-Helper"), "generator-settings.json");
                genEditor.LoadFromFile(genEditor.StatePath);
                genEditor.IndexChanged += delegate { UpdateNav(); };
                genEditor.Regenerated += delegate { UpdateNav(); };
                genEditor.UseXml += OnUseXml;
                genEditor.Visible = false;
                body.Controls.Add(genEditor);
                genEditor.BringToFront();   // заполняющий контрол «докуется» последним, после бокового меню
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
                MessageBox.Show(this, "Файл ответов выбран для захвата образа (шаг «XML и запуск»).", "Файл ответов");
            }
            else
            {
                txtInstXml.Text = path;
                rbModeInstall.Checked = true;
                Log("Файл ответов из генератора выбран для установки: " + path);
                MessageBox.Show(this, "Файл ответов выбран для установки (шаг «Настройки», поле «XML файл ответов»).\n\nПри установке этой программой образ применяется через DISM, поэтому из файла используются этапы specialize и oobeSystem (пользователи, пропуск OOBE, твики). Диск и редакцию вы выбираете на вкладках установки.", "Файл ответов");
            }
        }
    }
}
