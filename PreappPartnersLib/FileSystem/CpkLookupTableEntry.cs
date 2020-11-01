using System.Runtime.InteropServices;

namespace PreappPartnersLib.FileSystems
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CpkLookupTableEntry
    {
        public const int SIZE = 8;
        public const int ENTRY_COUNT = 65536;

        public int Index;
        public int Count;
    }

}
