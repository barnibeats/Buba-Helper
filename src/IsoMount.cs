// Монтирование ISO через virtdisk.dll.
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
    /// <summary>Монтирование ISO через virtdisk.dll (без PowerShell).</summary>
    class IsoMount
    {
        [StructLayout(LayoutKind.Sequential)]
        struct VStorageType { public uint DeviceId; public Guid VendorId; }

        [DllImport("virtdisk.dll", CharSet = CharSet.Unicode)]
        static extern int OpenVirtualDisk(ref VStorageType t, string path, int access, int flags, IntPtr prm, out IntPtr handle);
        [DllImport("virtdisk.dll")]
        static extern int AttachVirtualDisk(IntPtr h, IntPtr sd, int flags, int provFlags, IntPtr prm, IntPtr ov);
        [DllImport("virtdisk.dll")]
        static extern int DetachVirtualDisk(IntPtr h, int flags, int provFlags);
        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr h);

        IntPtr handle = IntPtr.Zero;
        public string Path;
        public string Letter;     // например "E:"

        static HashSet<char> Letters()
        {
            HashSet<char> s = new HashSet<char>();
            foreach (DriveInfo d in DriveInfo.GetDrives()) s.Add(char.ToUpper(d.Name[0]));
            return s;
        }

        public bool Mount(string iso, out string error)
        {
            error = null;
            Dismount();
            HashSet<char> before = Letters();
            VStorageType t = new VStorageType();
            t.DeviceId = 1; // ISO
            t.VendorId = new Guid("EC984AEC-A0F9-47e9-901F-71415A66345B");
            IntPtr h;
            int r;
            try { r = OpenVirtualDisk(ref t, iso, 0xD0000, 0, IntPtr.Zero, out h); }
            catch (Exception ex) { error = "virtdisk.dll недоступна: " + ex.Message; return false; }
            if (r != 0) { error = new Win32Exception(r).Message; return false; }
            r = AttachVirtualDisk(h, IntPtr.Zero, 1 /*READ_ONLY*/, 0, IntPtr.Zero, IntPtr.Zero);
            if (r != 0) { CloseHandle(h); error = new Win32Exception(r).Message; return false; }
            handle = h;
            Path = iso;

            for (int i = 0; i < 60; i++)
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    char c = char.ToUpper(d.Name[0]);
                    if (before.Contains(c)) continue;
                    try { if (d.IsReady) { Letter = c + ":"; return true; } } catch { }
                }
                Thread.Sleep(250);
            }
            error = "ISO подключён, но буква диска не появилась";
            Dismount();
            return false;
        }

        public void Dismount()
        {
            if (handle != IntPtr.Zero)
            {
                try { DetachVirtualDisk(handle, 0, 0); } catch { }
                CloseHandle(handle);
                handle = IntPtr.Zero;
            }
            Letter = null;
            Path = null;
        }
    }
}
