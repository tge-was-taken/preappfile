using System.Runtime.InteropServices;

namespace PreappPartnersLib.FileSystems
{
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 0x14)]
    public unsafe struct DwPackHeader
    {
        public const int SIZE = 0x14;
        public const ulong SIGNATURE = 0x4B4341505F5744;

        public ulong Signature; // DW_PACK\0
        public int Field08;
        public int FileCount;
        public int Index;
    }
}
