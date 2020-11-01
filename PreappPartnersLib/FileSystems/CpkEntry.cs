using PreappPartnersLib.Utils;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PreappPartnersLib.FileSystems
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct CpkEntry
    {
        public const int SIZE = 264;
        public const int PATH_LENGTH = 260;

        public fixed byte PathBytes[260];
        public short FileIndex;
        public short PacIndex;

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
