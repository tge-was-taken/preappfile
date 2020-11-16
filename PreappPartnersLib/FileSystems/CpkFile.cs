using PreappPartnersLib.Compression;
using PreappPartnersLib.FileSystems;
using PreappPartnersLib.Utils;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PreappPartnersLib.FileSystems
{
    public enum CompressionState
    {
        Detect,
        Compressed,
        Uncompressed
    }

    public class CpkFile
    {
        public List<CpkFileEntry> Entries { get; }

        public CpkFile()
        {
            Entries = new List<CpkFileEntry>();
        }

        public CpkFile(string path)
        {
            Entries = new List<CpkFileEntry>();
            using var stream = File.OpenRead(path);
            Read(stream);
        }

        public CpkFile(Stream stream)
        {
            Entries = new List<CpkFileEntry>();
            Read(stream);
        }

        public void Read( Stream stream, CompressionState compressionState = CompressionState.Detect )
        {
            Entries.Clear();

            var bufferMemory = MemoryPool<byte>.Shared.Rent((int)stream.Length);
            var buffer = bufferMemory.Memory.Span.Slice(0, (int)stream.Length);
            try
            {
                stream.Read(buffer);
                Read( buffer );
            }
            finally
            {
                bufferMemory.Dispose();
            }
        }

        public void Read( Span<byte> buffer, CompressionState compressionState = CompressionState.Detect )
        {
            if ( compressionState == CompressionState.Compressed || 
               ( compressionState == CompressionState.Detect && Encoding.ASCII.GetString( buffer.Slice( 0, 8 ) ) != "DW_PACK\0" ) )
            {
                using var decompressed = CpkUtil.DecompressCpk( buffer, out var decompressedSize );
                ReadUncompressed( decompressed.Memory.Span.Slice( 0, decompressedSize ) );
            }
            else
            {
                ReadUncompressed( buffer );
            }
        }

        private void ReadUncompressed( Span<byte> buffer )
        {
            ref var header = ref MemoryMarshal.AsRef<CpkHeader>( buffer );
            var entryOff = CpkHeader.SIZE + ( CpkLookupTableEntry.SIZE * CpkLookupTableEntry.ENTRY_COUNT );
            var entryCount = ( buffer.Length - entryOff ) / CpkEntry.SIZE;
            for ( int i = 0; i < entryCount; i++ )
            {
                ref var entry = ref MemoryMarshal.AsRef<CpkEntry>( buffer.Slice( entryOff ) );
                if ( entry.Path.Length == 0 )
                    continue;

                Entries.Add( new CpkFileEntry() { Path = entry.Path, FileIndex = entry.FileIndex, PacIndex = entry.PacIndex } );
                entryOff += CpkEntry.SIZE;
            }
        }

        public void Write( Stream stream, bool compress = true )
        {
            var bufferSize = CpkHeader.SIZE + (CpkLookupTableEntry.ENTRY_COUNT * CpkLookupTableEntry.SIZE) +
                (Entries.Count * CpkEntry.SIZE);
            var bufferMemory = MemoryPool<byte>.Shared.Rent(bufferSize);
            var buffer = bufferMemory.Memory.Span.Slice(0, bufferSize);

            try
            {
                Write( buffer );

                if ( compress )
                {
                    var comBuffer = CpkUtil.CompressCpk( buffer, out var comSize );
                    bufferMemory.Dispose();
                    bufferMemory = comBuffer;
                    buffer = bufferMemory.Memory.Span.Slice( 0, comSize );
                }

                stream.Write(buffer);
            }
            finally
            {
                bufferMemory.Dispose();
            }
        }

        public void Write( Span<byte> buffer )
        {
            // Write header
            ref var header = ref MemoryMarshal.AsRef<CpkHeader>( buffer );
            header.Signature = CpkHeader.SIGNATURE;
            header.Field08 = 0;
            header.FileCount = Entries.Count;

            // Build & write lookup table
            var hashLookup = new Dictionary<string, ushort>();
            for ( int i = 0; i < Entries.Count; i++ )
                hashLookup[ Entries[ i ].Path ] = HashUtil.ComputeHash( Entries[ i ].Path );

            var lookupTable = new CpkLookupTableEntry[ CpkLookupTableEntry.ENTRY_COUNT ];
            for ( int i = 0; i < lookupTable.Length; i++ )
                lookupTable[ i ].Index = -1;

            Entries.Sort( ( x, y ) => hashLookup[ x.Path ].CompareTo( hashLookup[ y.Path ] ) );

            for ( int i = 0; i < Entries.Count; i++ )
            {
                var hash = hashLookup[ Entries[ i ].Path ];
                lookupTable[ hash ].Count++;
                if ( lookupTable[ hash ].Index < 0 )
                    lookupTable[ hash ].Index = i;
            }

            var bufferOffset = CpkHeader.SIZE;
            for ( int i = 0; i < lookupTable.Length; i++ )
            {
                ref var entry = ref MemoryMarshal.AsRef<CpkLookupTableEntry>( buffer.Slice( bufferOffset ) );
                entry = lookupTable[ i ];
                bufferOffset += CpkLookupTableEntry.SIZE;
            }

            // Write entries
            for ( int i = 0; i < Entries.Count; i++ )
            {
                ref var entry = ref MemoryMarshal.AsRef<CpkEntry>( buffer.Slice( bufferOffset ) );
                entry.Path = Entries[ i ].Path;
                entry.FileIndex = Entries[ i ].FileIndex;
                entry.PacIndex = Entries[ i ].PacIndex;
                bufferOffset += CpkEntry.SIZE;
            }
        }

        public void Unpack(IList<DwPackFile> packs, string directoryPath, Func<CpkFileEntry, bool> callback)
        {
            var baseStreamLock = new object();
            Parallel.ForEach(Entries, (entry =>
            {
                if ( callback != null && !callback( entry ) ) return;
                var unpackPath = Path.Combine(directoryPath, entry.Path);
                var unpackDir = Path.GetDirectoryName(unpackPath);
                Directory.CreateDirectory(unpackDir);

                using var fileStream = File.Create(unpackPath);
                var pacEntry = packs[entry.PacIndex].Entries[entry.FileIndex];
                if (pacEntry.IsCompressed)
                {
                    // Copy compressed data from basestream so we can decompress in parallel
                    using var compressedBufferMem = MemoryPool<byte>.Shared.Rent(pacEntry.CompressedSize);
                    var compressedBuffer = compressedBufferMem.Memory.Span.Slice(0, pacEntry.CompressedSize);
                    lock (baseStreamLock)
                        pacEntry.CopyTo(compressedBuffer, decompress: false);

                    // Decompress
                    using var outBufferMem = MemoryPool<byte>.Shared.Rent(pacEntry.UncompressedSize);
                    var outBuffer = outBufferMem.Memory.Span.Slice(0, pacEntry.UncompressedSize);
                    HuffmanCodec.Decompress(compressedBuffer, outBuffer);
                    fileStream.Write(outBuffer);
                }
                else
                {
                    lock (baseStreamLock)
                        pacEntry.CopyTo(fileStream, decompress: false);
                }
            }));
        }

        public static CpkFile Pack(string directoryPath, bool compress, Func<string, bool> fileCallback,
            Action<DwPackFile> packCreatedCallback)
        {
            var cpk = new CpkFile();
            var pack = new DwPackFile();
            var pacIndex = 0;
            var fileIndex = 0;
            long curPacSize = DwPackHeader.SIZE;
            var syncLock = new object();

            Parallel.ForEach(Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories), (path =>
            {
                if ( fileCallback != null && !fileCallback( path ) ) return;
                var relativePath = path.Substring(path.IndexOf(directoryPath) + directoryPath.Length + 1);
                var entry = new DwPackFileEntry(relativePath, File.OpenRead(path), compress);

                lock ( syncLock )
                {
                    if ( curPacSize + entry.CompressedSize >= 1678174829 )
                    {
                        packCreatedCallback(pack);
                        pack = new DwPackFile() { Index = ++pacIndex };
                        fileIndex = 0;
                        curPacSize = 0;
                    }

                    fileIndex++;
                    cpk.Entries.Add(new CpkFileEntry(relativePath, (short)fileIndex, (short)pacIndex));
                    pack.Entries.Add(entry);
                    curPacSize += DwPackEntry.SIZE + entry.CompressedSize;
                }
            }));

            packCreatedCallback(pack);
            return cpk;
        }
    }

    public class CpkFilePackResult
    {
        public CpkFile Cpk { get; }
        public IReadOnlyList<DwPackFile> Packs { get; }

        public CpkFilePackResult( CpkFile cpk, IList<DwPackFile> packs )
        {
            Cpk = cpk;
            Packs = (IReadOnlyList<DwPackFile>)packs;
        }
    }

    public class CpkFileEntry
    {
        public string Path { get; set; }
        public short FileIndex { get; set; }
        public short PacIndex { get; set; }

        public CpkFileEntry()
        {

        }

        public CpkFileEntry( string path, short fileIndex, short pacIndex )
        {
            Path = path;
            FileIndex = fileIndex;
            PacIndex = pacIndex;
        }
    }
}
