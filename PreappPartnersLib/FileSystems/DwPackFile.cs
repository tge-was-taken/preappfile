using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Amicitia.IO.Binary;
using Amicitia.IO.Streams;
using PreappPartnersLib.Compression;

namespace PreappPartnersLib.FileSystems
{
    public class DwPackFile
    {
        private Stream mBaseStream;

        public int Field08 { get; set; }
        public int Index { get; set; }
        public List<DwPackFileEntry> Entries { get; }

        public DwPackFile()
        {
            Entries = new List<DwPackFileEntry>();
        }

        public DwPackFile(string path)
        {
            Entries = new List<DwPackFileEntry>();
            mBaseStream = File.OpenRead(path);
            Read(mBaseStream);
        }

        public DwPackFile(Stream stream)
        {
            Entries = new List<DwPackFileEntry>();
            mBaseStream = stream;
            Read(stream);
        }

        public void Read(Stream stream)
        {
            Entries.Clear();

            using var reader = new BinaryValueReader(stream, StreamOwnership.Retain, Endianness.Little);
            var header = reader.Read<DwPackHeader>();
            Field08 = header.Field08;
            Index = header.Index;

            var dataStartOffset = DwPackHeader.SIZE + (header.FileCount * DwPackEntry.SIZE);
            for (int i = 0; i < header.FileCount; i++)
            {
                var entry = reader.Read<DwPackEntry>();
                Entries.Add(new DwPackFileEntry(stream, dataStartOffset, ref entry));
            }
        }

        public void Write(Stream stream, bool compress, Action<DwPackFileEntry> callback)
        {
            using var writer = new BinaryValueWriter(stream, StreamOwnership.Retain, Endianness.Little);

            DwPackHeader header;
            header.Signature = DwPackHeader.SIGNATURE;
            header.Field08 = Field08;
            header.FileCount = Entries.Count;
            header.Index = Index;
            writer.Write(header);

            var dataOffset = 0;
            for (int i = 0; i < Entries.Count; i++)
            {
                var e = Entries[i];
                if (compress)
                    e.Compress();

                var entry = new DwPackEntry();
                entry.Field00 = e.Field00;
                entry.Index = (short)i;
                entry.PackIndex = (short)header.Index;
                entry.Path = e.Path;
                entry.Field104 = e.Field104;
                entry.CompressedSize = e.CompressedSize;
                entry.UncompressedSize = e.UncompressedSize;
                entry.Flags = e.IsCompressed ? 1 : 0;
                entry.DataOffset = dataOffset;
                writer.Write(entry);
                dataOffset += entry.CompressedSize;     
            }

            for (int i = 0; i < Entries.Count; i++)
            {
                callback?.Invoke(Entries[i]);
                Entries[i].CopyTo(writer.GetBaseStream(), decompress: false);
            }
        }

        public void Unpack(string directoryPath, Action<DwPackFileEntry> callback)
        {
            var baseStreamLock = new object();
            Parallel.ForEach(Entries, (entry =>
            {
                callback?.Invoke(entry);
                var unpackPath = Path.Combine(directoryPath, entry.Path);
                var unpackDir = Path.GetDirectoryName(unpackPath);
                Directory.CreateDirectory(unpackDir);

                using var fileStream = File.Create(unpackPath);
                if (entry.IsCompressed)
                {
                    // Copy compressed data from basestream so we can decompress in parallel
                    var compressedBufferMem = MemoryPool<byte>.Shared.Rent(entry.CompressedSize);
                    var compressedBuffer = compressedBufferMem.Memory.Span.Slice(0, entry.CompressedSize);
                    lock ( baseStreamLock )
                        entry.CopyTo(compressedBuffer, decompress: false);

                    // Decompress
                    using var outBufferMem = MemoryPool<byte>.Shared.Rent(entry.UncompressedSize);
                    var outBuffer = outBufferMem.Memory.Span.Slice(0, entry.UncompressedSize);
                    HuffmanCodec.Decompress(compressedBuffer, outBuffer);
                    fileStream.Write(outBuffer);
                }
                else
                {
                    lock (baseStreamLock)
                        entry.CopyTo(fileStream, decompress: false);
                }
            }));
        }

        public static DwPackFile Pack( string directoryPath, bool compress, Action<string> callback )
        {
            var pack = new DwPackFile();
            Parallel.ForEach(Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories), (path =>
            {
                callback?.Invoke(path);
                var relativePath = path.Substring(path.IndexOf(directoryPath) + directoryPath.Length + 1);
                pack.Entries.Add(new DwPackFileEntry(relativePath, File.OpenRead(path), compress));
            }));

            return pack;
        }
    }

    public class DwPackFileEntry
    {
        private Stream mBaseStream;
        private long mDataOffset;
        private Stream mDataStream;

        public int Field00 { get; set; }
        public string Path { get; set; }
        public int Field104 { get; set; }
        public int CompressedSize { get; private set; }
        public int UncompressedSize { get; private set; }
        public bool IsCompressed { get; private set; }

        public DwPackFileEntry(string path, Stream stream, bool compress)
        {
            Field00 = 0;
            Path = path;
            mDataStream = stream;
            CompressedSize = UncompressedSize = (int)stream.Length;
            IsCompressed = false;

            if (compress)
                Compress();
        }

        public DwPackFileEntry(Stream baseStream, long dataStartOffset, ref DwPackEntry entry)
        {
            mBaseStream = baseStream;
            mDataOffset = dataStartOffset + entry.DataOffset;
            Field00 = entry.Field00;
            Path = entry.Path;
            Field104 = entry.Field104;
            CompressedSize = entry.CompressedSize;
            UncompressedSize = entry.UncompressedSize;
            IsCompressed = entry.Flags > 0;
        }

        public Stream Open( bool decompress )
        {
            if (mDataStream == null)
            {
                mDataStream = mBaseStream.Slice(mDataOffset, CompressedSize);
                if ( decompress )
                    Decompress();
            }

            return mDataStream.Slice(0, mDataStream.Length);
        }

        public void Decompress()
        {
            if ( IsCompressed )
            {
                mDataStream = new MemoryStream(UncompressedSize);
                CopyTo(mDataStream, decompress: true);
                IsCompressed = false;
                CompressedSize = UncompressedSize;
            }
        }

        public void CopyTo( Stream destination, bool decompress )
        {
            if ( IsCompressed && decompress )
            {
                using var inBufferMem = MemoryPool<byte>.Shared.Rent(CompressedSize);
                var inBuffer = inBufferMem.Memory.Span.Slice(0, CompressedSize);
                Open( false ).Read(inBufferMem.Memory.Span.Slice(0, CompressedSize));

                using var outBufferMem = MemoryPool<byte>.Shared.Rent(UncompressedSize);
                var outBuffer = outBufferMem.Memory.Span.Slice(0, UncompressedSize);
                HuffmanCodec.Decompress(inBuffer, outBuffer);
                destination.Write(outBuffer);
            }
            else
            {
                Open(false).CopyTo(destination);
            }
        }

        public void CopyTo(Span<byte> destination, bool decompress)
        {
            if (IsCompressed && decompress)
            {
                using var inBufferMem = MemoryPool<byte>.Shared.Rent(CompressedSize);
                var inBuffer = inBufferMem.Memory.Span.Slice(0, CompressedSize);
                Open(false).Read(inBufferMem.Memory.Span.Slice(0, CompressedSize));
                HuffmanCodec.Decompress(inBuffer, destination);
            }
            else
            {
                Open(false).Read(destination);
            }
        }

        public void Compress()
        {
            if ( !IsCompressed )
            {
                using var inBufferMem = MemoryPool<byte>.Shared.Rent(CompressedSize);
                var inBuffer = inBufferMem.Memory.Span.Slice(0, CompressedSize);
                mDataStream.Position = 0;
                mDataStream.Read(inBufferMem.Memory.Span.Slice(0, CompressedSize));

                using var outBufferMem = MemoryPool<byte>.Shared.Rent(UncompressedSize);
                var outBuffer = outBufferMem.Memory.Span.Slice(0, UncompressedSize);
                int compressedSize = 0;
                if (UncompressedSize > 0)
                    compressedSize = HuffmanCodec.Compress(inBuffer, outBuffer);

                mDataStream = new MemoryStream(UncompressedSize);
                mDataStream.Write(outBuffer.Slice(0, compressedSize));

                IsCompressed = true;
                CompressedSize = compressedSize;
            }
        }
    }
}
