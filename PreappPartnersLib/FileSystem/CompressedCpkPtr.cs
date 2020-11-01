using PreappPartnersLib.Compression;
using System;
using System.Runtime.InteropServices;

namespace PreappPartnersLib.FileSystems
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct CompressedCpkPtr : IDisposable
    {
        public void* Ptr;

        public CompressedCpkHeader* Header => (CompressedCpkHeader*)Ptr;
        public CompressedDataHeader* Data => (CompressedDataHeader*)(Header + 1);
        public CompressedChunkHeader* Chunks => (CompressedChunkHeader*)(Data + 1);

        public CompressedCpkPtr(void* ptr)
        {
            Ptr = ptr;
        }

        public void Dispose()
        {
            Marshal.FreeHGlobal((IntPtr)Ptr);
        }
    }

}
