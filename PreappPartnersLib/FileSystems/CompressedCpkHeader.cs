using System.Runtime.InteropServices;

namespace PreappPartnersLib.FileSystems
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct CompressedCpkHeader
    {
        public int Field00;
        public int CompressedSize;
        public int UncompressedSize;
        public int Flags;
    }

}
