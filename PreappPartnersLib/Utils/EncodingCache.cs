using System.Collections.Generic;
using System.Text;

namespace PreappPartnersLib.Utils
{
    public static class EncodingCache
    {
        public static readonly Encoding ShiftJIS;

        static EncodingCache()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            ShiftJIS = Encoding.GetEncoding(932);
        }
    }
}
