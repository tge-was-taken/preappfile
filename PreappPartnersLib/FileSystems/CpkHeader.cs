using System.Runtime.InteropServices;

namespace PreappPartnersLib.FileSystems
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct CpkHeader
    {
        public const int SIZE = 16;
        public const ulong SIGNATURE = 0x4B4341505F5744;

        public ulong Signature;
        public int Field08;
        public int FileCount;
    }

}
