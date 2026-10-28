using System.Runtime.InteropServices;
using System.Text;

namespace WinInstaller
{
    // OEM-кодировка консольных утилит (diskpart, dism). С инвариантной культурой CurrentCulture даёт 437, поэтому берём системную через GetOEMCP.
    static class OemEnc
    {
        [DllImport("kernel32.dll")] static extern uint GetOEMCP();

        public static Encoding Get()
        {
            try { return Encoding.GetEncoding((int)GetOEMCP()); }
            catch { return Encoding.UTF8; }
        }
    }
}
