using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace PreappPartnersLib.Compression
{
    public static class HuffmanCodec
    {
        public const int DEFAULT_CHUNK_SIZE = 0x20000;

        public static unsafe int Decompress(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            ref readonly var dataHeader = ref MemoryMarshal.AsRef<CompressedDataHeader>(source);
            var srcOff = sizeof(CompressedDataHeader);

            Trace.Assert(dataHeader.Magic == 0x1234);
            var dstOffset = 0;

            if (destination.Length == 0)
                return dstOffset;

            ref readonly var chunkHeader = ref MemoryMarshal.AsRef<CompressedChunkHeader>(source.Slice(srcOff));
            for (int i = 0; i < dataHeader.ChunkCount; i++)
            {
                var srcChunk = source.Slice(dataHeader.HeaderSize + chunkHeader.DataOffset, chunkHeader.CompressedSize);
                var dstChunk = destination.Slice(dstOffset, chunkHeader.UncompressedSize);

                DecompressChunk(srcChunk, dstChunk);

                dstOffset += chunkHeader.UncompressedSize;
                srcOff += sizeof(CompressedChunkHeader);
                chunkHeader = ref MemoryMarshal.AsRef<CompressedChunkHeader>(source.Slice(srcOff));
            }

            return dstOffset;
        }

        public static unsafe int Compress(ReadOnlySpan<byte> source, Span<byte> destination, int chunkSize = DEFAULT_CHUNK_SIZE)
        {
            ref var dataHeader = ref MemoryMarshal.AsRef<CompressedDataHeader>(destination);
            dataHeader.Magic = 0x1234;
            dataHeader.ChunkSize = chunkSize;
            dataHeader.ChunkCount = (int)Math.Ceiling((float)source.Length / (float)dataHeader.ChunkSize);
            dataHeader.HeaderSize = sizeof(CompressedDataHeader) + (sizeof(CompressedChunkHeader) * dataHeader.ChunkCount);

            var dstOff = sizeof(CompressedDataHeader);
            var srcOff = 0;
            var dataOff = 0;
            for (int i = 0; i < dataHeader.ChunkCount; i++)
            {
                ref var chunkHeader = ref MemoryMarshal.AsRef<CompressedChunkHeader>(destination.Slice(dstOff));
                var uncompressedSize = Math.Min(dataHeader.ChunkSize, source.Length - srcOff);
                var dataOffAbs = dataHeader.HeaderSize + dataOff;
                var compressedSize = CompressChunk(source.Slice(srcOff, uncompressedSize), destination.Slice(dataOffAbs));
                Debug.Assert(compressedSize < uncompressedSize);

                chunkHeader.UncompressedSize = uncompressedSize;
                chunkHeader.CompressedSize = compressedSize;
                chunkHeader.DataOffset = dataOff;

                dstOff += sizeof(CompressedChunkHeader);
                srcOff += uncompressedSize;
                dataOff += compressedSize;
            }

            return dataHeader.HeaderSize + dataOff;
        }

        public static unsafe void DecompressChunk(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            var srcOffset = 0;
            var bits = source[srcOffset++];
            var bitIndex = 0;

            bool ReadBit(ReadOnlySpan<byte> source)
            {
                // Bits are read from MSB to LSB (left-to-right)
                if (bitIndex > 7)
                {
                    bitIndex = 0;
                    bits = source[srcOffset++];
                }

                var val = (bits & (1 << (7 - bitIndex++))) != 0;

                return val;
            }

            // Read Huffman tree
            var root = new HuffmanNode();
            var workNode = root;
            while (workNode != null)
            {
                // Read number of expansions for current node
                // Each 1 indicates an expansion that adds 2 non-leaf children to the active node, and selects the next leftmost node
                while (ReadBit(source))
                {
                    workNode.Left = new HuffmanNode();
                    workNode.Right = new HuffmanNode();
                    workNode = workNode.Left;
                }

                // Expansions are done, turn the current node into a leaf
                // Value of the leaf is stored in the following 8 bits
                byte value = 0;
                for (int i = 0; i < 8; i++)
                {
                    if (ReadBit(source))
                        value |= (byte)(1 << (7 - i));
                }

                workNode.Value = value;

                // Find next active node (leftmost node that is not a leaf and has no children)
                workNode = root.FindFirstInvalidNode();
            }

            // Decompress the data using the tree
            var destOffset = 0;
            while (destOffset < destination.Length)
            {
                workNode = root;

                while (workNode.Value == null)
                {
                    if (!ReadBit(source))
                    {
                        workNode = workNode.Left;
                    }
                    else
                    {
                        workNode = workNode.Right;
                    }

                }

                destination[destOffset++] = workNode.Value.Value;
            }
        }

        public static unsafe int CompressChunk(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            var root = BuildHuffmanTree(source);
            return WriteHuffmanData(source, destination, root);
        }

        private struct HuffmanNodeInfo
        {
            public int Uses;
            public HuffmanNode Node;
        }

        private struct ValueStatistics
        {
            public byte Value;
            public int Uses;
        }

        private static void BubbleSort<T>(T[] array, Func<T, T, int> comparer)
        {
            T temp;
            for (int j = 0; j <= array.Length - 2; j++)
            {
                for (int i = 0; i <= array.Length - 2; i++)
                {
                    if (comparer(array[i], array[i + 1]) > 0)
                    {
                        temp = array[i + 1];
                        array[i + 1] = array[i];
                        array[i] = temp;
                    }
                }
            }
        }

        private static HuffmanNode BuildHuffmanTree(ReadOnlySpan<byte> source)
        {
            // Get the number of occurences of each byte value
            var values = ArrayPool<ValueStatistics>.Shared.Rent(256);
            var nodes = ArrayPool<HuffmanNodeInfo>.Shared.Rent(256 + 1);

            try
            {
                for (int i = 0; i < values.Length; i++)
                {
                    values[i].Value = (byte)i;
                    values[i].Uses = 0;
                }

                for (int i = 0; i < source.Length; i++)
                    values[source[i]].Uses++;

                // Sort the number of occurences in ascending order
                BubbleSort(values, (x, y) => x.Uses.CompareTo(y.Uses));

                // Create nodes for all values
                for (int i = 0; i < nodes.Length; i++)
                {
                    nodes[i].Uses = int.MaxValue;
                    nodes[i].Node = null;
                }

                var usedNodes = 0;
                for (int i = 0; i < values.Length; i++)
                {
                    if (values[i].Uses > 0)
                    {
                        nodes[usedNodes].Uses = values[i].Uses;
                        nodes[usedNodes].Node = new HuffmanNode() { Value = values[i].Value };
                        usedNodes++;
                    }
                }

                while (usedNodes > 1)
                {
                    // Pick 2 smallest values and create a node for them
                    var left = nodes[0];
                    var right = nodes[1];

                    // Create new node & overwrite first node with it
                    var node = new HuffmanNode();
                    node.Left = left.Node;
                    node.Right = right.Node;
                    var uses = left.Uses + right.Uses;
                    usedNodes--;

                    // Move array back
                    Array.Copy(nodes, 2, nodes, 0, nodes.Length - 2);

                    // Insert node before next largest value
                    var insertionIndex = nodes.Length - 1;
                    for (int i = 0; i < nodes.Length; i++)
                    {
                        if (nodes[i].Uses > uses)
                        {
                            insertionIndex = i;
                            break;
                        }
                    }

                    Array.Copy(nodes, insertionIndex, nodes, insertionIndex + 1, nodes.Length - (insertionIndex + 1));
                    nodes[insertionIndex].Node = node;
                    nodes[insertionIndex].Uses = uses;
                }

                // Only the root remains
                var root = nodes[0];
                return root.Node;
            }
            finally
            {
                ArrayPool<ValueStatistics>.Shared.Return(values);
                ArrayPool<HuffmanNodeInfo>.Shared.Return(nodes);
            }
        }

        private struct HuffmanCode
        {
            public long Bits;
            public int Depth;
        }

        private static HuffmanCode? GetHuffmanCode(HuffmanNode node, byte val)
        {
            HuffmanCode? GetHuffmanCode(HuffmanNode node, byte val, int code, int depth)
            {
                Trace.Assert(depth < 64);

                if (node == null) return null;

                if (node.Value == val)
                    return new HuffmanCode() { Bits = code, Depth = depth };

                var found = GetHuffmanCode(node.Left, val, code | (0 << depth), depth + 1);
                if (found != null) return found;

                return GetHuffmanCode(node.Right, val, code | (1 << depth), depth + 1);
            }

            return GetHuffmanCode(node, val, 0, 0);
        }

        private static int WriteHuffmanData(ReadOnlySpan<byte> source, Span<byte> destination, HuffmanNode root)
        {
            var srcOffset = 0;
            var dstOffset = 0;
            var bits = 0;
            var bitIndex = 0;

            void WriteBit(bool value, Span<byte> destination)
            {
                if (bitIndex > 7)
                    Flush(destination);

                // Bits are written from MSB to LSB (left-to-right)
                bits |= ((value ? 1 : 0) << (7 - bitIndex++));
            }

            void Flush(Span<byte> destination)
            {
                if (bitIndex > 0)
                {
                    destination[dstOffset++] = (byte)bits;
                    bitIndex = 0;
                    bits = 0;
                }
            }


            var workNode = root;
            workNode.Invalidate();

            while (workNode != null)
            {
                // Expand nodes until we have a leaf node
                while (workNode.Value == null)
                {
                    WriteBit(true, destination);
                    workNode.IsInvalid = false;
                    workNode = workNode.Left;
                }

                // Bit that indicates there's no more expansions possible
                WriteBit(false, destination);

                // Write leaf value
                workNode.IsInvalid = false;
                for (int i = 0; i < 8; i++)
                    WriteBit((workNode.Value.Value & (1 << (7 - i))) != 0, destination);

                // Find next active node (leftmost node that is not a leaf and has no children)
                workNode = root.FindFirstInvalidNode();
            }

            // Write huffman codes
            var huffmanCodeDict = new Dictionary<byte, HuffmanCode>();
            for (int i = 0; i < source.Length; i++)
            {
                var val = source[i];
                HuffmanCode code;

                if (!huffmanCodeDict.ContainsKey(val))
                {
                    code = GetHuffmanCode(root, val).Value;
                    huffmanCodeDict[val] = code;
                }
                else
                {
                    code = huffmanCodeDict[val];
                }

                for (int j = 0; j < code.Depth; j++)
                    WriteBit((code.Bits & (1 << j)) != 0, destination);
            }

            Flush(destination);
            return dstOffset;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct CompressedDataHeader
    {
        public int Magic;
        public int ChunkCount;
        public int ChunkSize;
        public int HeaderSize;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct CompressedChunkHeader
    {
        public int UncompressedSize;
        public int CompressedSize;
        public int DataOffset;
    }
}
