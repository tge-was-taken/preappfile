using PreappPartnersLib.Compression;
using System;
using System.Buffers;
using System.Diagnostics;

namespace PreappPartnersLib.FileSystems
{
    public static class CpkUtil
    {
        public static unsafe IMemoryOwner<byte> DecompressCpk(ReadOnlySpan<byte> source, out int decompressedSize)
        {
            fixed (byte* pSrc = source)
            {
                var pHeader = (CompressedCpkHeader*)pSrc;
                var destination = MemoryPool<byte>.Shared.Rent(pHeader->UncompressedSize);
                decompressedSize = DecompressCpk(source, destination.Memory.Span.Slice(0, pHeader->UncompressedSize));
                return destination;
            }
        }

        public static unsafe int DecompressCpk(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            fixed (byte* pInput = source)
            {
                var pHeader = (CompressedCpkHeader*)pInput;
                HuffmanCodec.Decompress(
                    source.Slice(sizeof(CompressedCpkHeader), pHeader->CompressedSize),
                    destination.Slice(0, pHeader->UncompressedSize));

                return pHeader->UncompressedSize;
            }
        }

        public static unsafe IMemoryOwner<byte> CompressCpk(ReadOnlySpan<byte> source, out int compressedSize)
        {
            var destination = MemoryPool<byte>.Shared.Rent(source.Length);
            compressedSize = CompressCpk(source, destination.Memory.Span.Slice(0, source.Length));
            return destination;
        }

        public static unsafe int CompressCpk(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            fixed (byte* pDest = destination)
            {
                var pHeader = (CompressedCpkHeader*)pDest;
                pHeader->Field00 = 0;
                pHeader->CompressedSize = source.Length;
                pHeader->UncompressedSize = source.Length;
                pHeader->Flags = 1;
                pHeader->CompressedSize = HuffmanCodec.Compress(source, destination.Slice(sizeof(CompressedCpkHeader)), HuffmanCodec.DEFAULT_CHUNK_SIZE);
                Trace.Assert(pHeader->CompressedSize < pHeader->UncompressedSize);
                return pHeader->CompressedSize + sizeof(CompressedCpkHeader);
            }
        }
    }
}
