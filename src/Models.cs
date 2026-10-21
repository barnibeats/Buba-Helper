// Модели данных: диск, образ, план установки.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace WinInstaller
{
    // ---------------------------------------------------------------- модели

    class DiskInfo
    {
        public int Number;
        public long Size;
        public string Model = "";
        public string Bus = "";
        public string Style = "?";
        public bool Removable;
        public List<string> Volumes = new List<string>();
        public List<char> Letters = new List<char>();
        public bool Blocked;   // на этом диске лежит источник
    }

    class ImageInfo
    {
        public int Index;
        public string Name = "";
        public long Bytes;
        public override string ToString()
        {
            string s = Index + ". " + Name;
            if (Bytes > 0) s += "  (" + Util.FmtSize(Bytes) + ")";
            return s;
        }
    }

    class Plan
    {
        public string ImageFile;
        public int ImageIndex;
        public DiskInfo Disk;
        public bool Gpt;
        public long WinSizeMB;      // 0 = всё свободное место
        public bool DataPart;
        public string DriversDir;
        public bool Reboot;
        public bool DryRun;
        public bool Keep;           // форматировать только раздел Windows, остальное не трогать
        public char WinLetter;      // (Keep) буква раздела Windows
        public int WinPart;         // (Keep) номер раздела Windows на диске
        public int EspPart;         // (Keep, GPT) номер EFI-раздела
        public string Xml;          // unattend.xml, копируется в Windows\Panther\Unattend\Unattend.xml и Windows\Panther\unattend.xml
        public string SourcePath;   // выбранный файл (ISO/WIM) - для проверки .sha256
        public bool VerifyHash;
    }
}
