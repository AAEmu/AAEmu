using System.Drawing;
using System.Numerics;

namespace AAEmu.Game.Models.CryEngine.Objects
{
    public class VoxelMeshProcessor(VoxelMeshReader reader)
    {
        private static readonly List<int> ChunkIdsToProcess = [3, 4, 5, 6, 7, 8];
        public VoxelMeshReader Reader => reader;

        internal bool Process()
        {
            foreach(var chunkId in ChunkIdsToProcess)
            {
                var chunkData = GetRawChunkDataById(chunkId);
                if (chunkData == null || chunkData.Length <= 0)
                {
                    continue;
                }
                var parsedChunk = ProcessType22Chunk(chunkData);
            }
            // TODO: Validate data?
            return true;
        }

        private byte[] GetRawChunkDataById(int chunkId)
        {
            foreach (var (id, chunk) in Reader.ChunkTable)
            {
                if (chunk.ChunkId == chunkId)
                {
                    return Reader.ChunksData[id];
                }
            }
            return [];
        }

        private bool ProcessType22Chunk(byte[] chunk)
        {
            var headerSize = 40;
            if (chunk.Length < headerSize)
            {
                // Data too small
                return false;
            }
            var currentOffset = 0;
            var h0 = BitConverter.ToUInt16(chunk, currentOffset); currentOffset += 2;
            var h1 = BitConverter.ToUInt16(chunk, currentOffset); currentOffset += 2;
            var i2 = BitConverter.ToInt32(chunk, currentOffset); currentOffset += 4;
            var i3 = BitConverter.ToInt32(chunk, currentOffset); currentOffset += 4;
            var chunkId = BitConverter.ToInt32(chunk, currentOffset); currentOffset += 4;
            var i5 = BitConverter.ToInt32(chunk, currentOffset); currentOffset += 4;
            var i6 = BitConverter.ToInt32(chunk, currentOffset); currentOffset += 4;
            var itemCount = BitConverter.ToInt32(chunk, currentOffset); currentOffset += 4;
            var bytesPerItem = BitConverter.ToInt32(chunk, currentOffset); currentOffset += 4;
            var i9 = BitConverter.ToInt32(chunk, currentOffset); currentOffset += 4;
            var i10 = BitConverter.ToInt32(chunk, currentOffset); currentOffset += 4;

            var payloadData = chunk.AsSpan(currentOffset, chunk.Length - currentOffset).ToArray();

            switch (chunkId)
            {
                case 3:
                    Reader.Vertices = ParseVectorPayload(payloadData, itemCount, bytesPerItem);
                    break;
                case 4: // Vertices or Normals (No change)
                    Reader.Normals = ParseVectorPayload(payloadData, itemCount, bytesPerItem);
                    break;
                case 8: // Indices (No change)
                    Reader.Indices = ParseUShortPayload(payloadData, itemCount, bytesPerItem);
                    break;
                case 5: // RGBA Color Data
                    Reader.ColorData = ParseARGBPayload(payloadData, itemCount, bytesPerItem);
                    break;
                case 6: // Surface ID Data
                    Reader.SurfaceIds = ParseSurfaceIdPayload(payloadData, itemCount, bytesPerItem);
                    break;
                case 7: // Material Data(Leave as raw bytes)
                    Reader.OtherChunkData.Add((chunkId, ParseOtherPayloadData(payloadData, itemCount, bytesPerItem)));
                    break;
                default:
                    // Unknown ?
                    Reader.OtherChunkData.Add((chunkId, ParseOtherPayloadData(payloadData, itemCount, bytesPerItem)));
                    break;
            }
            return true;
        }

        private List<byte[]> ParseOtherPayloadData(byte[] payloadData, int itemCount, int bytesPerItem)
        {
            if (itemCount * bytesPerItem < payloadData.Length)
            {
                // Mismatch in expected data size.
                return [];
            }
            var res = new List<byte[]>();
            for (int offset = 0; offset <= payloadData.Length - bytesPerItem; offset += bytesPerItem)
            {
                res.Add(payloadData.AsSpan(offset, bytesPerItem).ToArray());
            }
            return res;
        }

        private List<Vector3> ParseVectorPayload(byte[] payloadData, int itemCount, int bytesPerItem)
        {
            if (bytesPerItem != 12)
            {
                // Only 12 bytes per item (Vector3) is supported.
                return [];
            }
            if (itemCount * bytesPerItem != payloadData.Length)
            {
                // Mismatch in expected data size.
                return [];
            }
            var res = new List<Vector3>();
            for (int offset = 0; offset <= payloadData.Length - bytesPerItem; offset += bytesPerItem)
            {
                // Format as -X, Z, Y from source file
                var x = BitConverter.ToSingle(payloadData, offset);
                var y = BitConverter.ToSingle(payloadData, offset + 4);
                var z = BitConverter.ToSingle(payloadData, offset + 8);
                res.Add(new Vector3(x, y, z));
            }
            return res;
        }

        private List<ushort> ParseUShortPayload(byte[] payloadData, int itemCount, int bytesPerItem)
        {
            if (bytesPerItem != 2)
            {
                // Only 2 bytes per item (ushort) is supported.
                return [];
            }
            if (itemCount * bytesPerItem != payloadData.Length)
            {
                // Mismatch in expected data size.
                return [];
            }
            var res = new List<ushort>();
            for (int offset = 0; offset <= payloadData.Length - bytesPerItem; offset += bytesPerItem)
            {
                var x = BitConverter.ToUInt16(payloadData, offset);
                res.Add(x);
            }
            return res;
        }

        private List<Color> ParseARGBPayload(byte[] payloadData, int itemCount, int bytesPerItem)
        {
            if (bytesPerItem != 4)
            {
                // Only 4 bytes per item (ushort) is supported.
                return [];
            }
            if (itemCount * bytesPerItem != payloadData.Length)
            {
                // Mismatch in expected data size.
                return [];
            }
            var res = new List<Color>();
            for (int offset = 0; offset <= payloadData.Length - bytesPerItem; offset += bytesPerItem)
            {
                res.Add(Color.FromArgb(payloadData[offset + 3], payloadData[offset + 0], payloadData[offset + 1], payloadData[offset + 2]));
            }
            return res;
        }

        private List<ushort> ParseSurfaceIdPayload(byte[] payloadData, int itemCount, int bytesPerItem)
        {
            if (bytesPerItem != 4)
            {
                // Only 4 bytes per item (ushort, ushort) is supported.
                return [];
            }
            if (itemCount * bytesPerItem != payloadData.Length)
            {
                // Mismatch in expected data size.
                return [];
            }
            var res = new List<ushort>();
            for (int offset = 0; offset <= payloadData.Length - bytesPerItem; offset += bytesPerItem)
            {
                // var x = BitConverter.ToUInt16(payloadData, offset); // padding?
                var surfaceId = BitConverter.ToUInt16(payloadData, offset + 2);
                res.Add(surfaceId);
            }
            return res;
        }

    }
}
