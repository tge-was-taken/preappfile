using System;

namespace PreappPartnersLib.Utils
{
    public static unsafe class NativeStringHelper
    {
        public static int GetLength(byte* str)
        {
            int length = 0;
            while (*str++ != 0) ++length;
            return length;
        }

        public static Span<byte> AsSpan(byte* str)
        {
            return new Span<byte>((void*)str, GetLength(str));
        }
    }
}
