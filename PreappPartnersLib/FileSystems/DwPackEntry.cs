using PreappPartnersLib.Utils;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PreappPartnersLib.FileSystems
{
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 0x120)]
    public unsafe struct DwPackEntry
    {
        public const int PATH_LENGTH = 260;
        public const int SIZE = 0x120;

        public int Field00;
        public short Index;
        public short PackIndex;
        public fixed byte PathBytes[PATH_LENGTH];
        public int Field104;
        public int CompressedSize;
        public int UncompressedSize;
        public int Flags;
        public int DataOffset;

        public string Path
        {
            get
            {
                fixed (byte* pathBytes = PathBytes)
                    return EncodingCache.ShiftJIS.GetString(NativeStringHelper.AsSpan(pathBytes));
            }
            set
            {
                fixed (byte* pathBytes = PathBytes)
                {
                    Unsafe.InitBlock(pathBytes, 0, PATH_LENGTH);
                    EncodingCache.ShiftJIS.GetBytes(value.AsSpan(), new Span<byte>(pathBytes, PATH_LENGTH));
                }
            }
        }
    }
}
