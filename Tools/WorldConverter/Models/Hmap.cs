using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace AAEmu.WorldConverter.Models
{
    public class Hmap
    {
        public byte Version { get; set; }
        public byte Dummy { get; set; }
        public byte Flags { get; set; }
        public byte Flags2 { get; set; }

        public int ChunkSize { get; set; }
        public int HeightMapSizeInUnits { get; set; }
        public int UnitSizeInMeters { get; set; }
        public int SectorSizeInMeters { get; set; }
        public int SectorsTableSizeInSectors { get; set; }
        public float HeightmapZRatio { get; set; }
        public float OceanWaterLevel { get; set; }

        public List<NodeCell> Nodes { get; set; } = new List<NodeCell>();

        public void Read(BinaryReader br, bool disabledReCalc)
        {
            Version = br.ReadByte();
            Dummy = br.ReadByte();
            Flags = br.ReadByte();
            Flags2 = br.ReadByte();

            // TODO: spawn endian, flags & 1 ? eBigEndian : eLittleEndian

            ChunkSize = br.ReadInt32();
            HeightMapSizeInUnits = br.ReadInt32();
            UnitSizeInMeters = br.ReadInt32();
            SectorSizeInMeters = br.ReadInt32();
            SectorsTableSizeInSectors = br.ReadInt32();
            HeightmapZRatio = br.ReadSingle();
            OceanWaterLevel = br.ReadSingle();

            if (Version >= 24)
            {
                for (var n = 0; n < 64; n++)
                {
                    var f = br.ReadUInt16(); // unk?
                }
            }

            while (br.BaseStream.Position != ChunkSize)
            {
                // Console.WriteLine($"Hmap.Read() - Reading {br.BaseStream.Position}/{ChunkSize}");
                var node = new NodeCell();
                node.Read(br, disabledReCalc);
                Nodes.Add(node);                
            }
        }
    }

    public class NodeCell
    {
        const int Inv5Cm = 20;
        const uint Mask12Bit = (1 << 12) - 1;

        public byte Version { get; set; }
        public byte Dummy { get; set; }
        public byte Flags { get; set; }
        public byte Flags2 { get; set; }
        public AABB BoxHeightmap { get; set; } = new AABB();
        public byte bHasHoles { get; set; }
        public Single fOffset { get; set; }
        public Single fRange { get; set; }
        public int nSize { get; set; }
        public ushort[] pHMData { get; set; }

        private int iOffset;
        private int iRange;
        private int iStep;
        private Single fMin;
        private Single fMax;

        public void Read(BinaryReader br, bool disabledReCalc = false)
        {
            Version = br.ReadByte();
            Dummy = br.ReadByte();
            Flags = br.ReadByte();
            Flags2 = br.ReadByte();

            BoxHeightmap.Min.X = br.ReadSingle();
            BoxHeightmap.Min.Y = br.ReadSingle();
            BoxHeightmap.Min.Z = br.ReadSingle();
            BoxHeightmap.Max.X = br.ReadSingle();
            BoxHeightmap.Max.Y = br.ReadSingle();
            BoxHeightmap.Max.Z = br.ReadSingle();

            //Console.WriteLine($"NodeCell.Read() Area: {BoxHeightmap.Min} => {BoxHeightmap.Max}");

            bHasHoles = br.ReadByte();
            fOffset = br.ReadSingle();

            fRange = br.ReadSingle();
            nSize = br.ReadInt32();
            pHMData = new ushort[nSize * nSize];

            var unkCount = br.ReadInt32();

            for (var i = 0; i < pHMData.Length; i++)
                pHMData[i] = br.ReadUInt16();

            var i1 = br.ReadInt32();
            var f1 = br.ReadSingle();
            var f2 = br.ReadSingle();
            var f3 = br.ReadSingle();
            var f4 = br.ReadSingle();

            if (unkCount >= 1)
            {
                var unknownData = br.ReadBytes(unkCount);
            }

            var bytes36 = br.ReadBytes(36);


            Init();
            if ((disabledReCalc == false) && (Version < 7))
                RescaleToInt();
            UpScale();
        }

        public float RawDataToHeight(uint data)
        {
            return 0.05f * iOffset + (data >> 4) * iStep * 0.05f;
        }

        public ushort RawDataByIndex(uint i)
        {
            return pHMData[i];
        }

        public ushort RawDataByIndex(ushort nX, ushort nY)
        {
            if (nSize > 0)
            {
                var index = nX * nSize + nY;
                if (index >= pHMData.Length)
                    return 0;

                return pHMData[index];
            }

            return 0;
        }

        public float GetHeightByIndex(uint i)
        {
            return RawDataToHeight(pHMData[i]);
        }

        public float GetHeight(ushort nX, ushort nY)
        {
            if (nSize > 0)
            {
                var index = nX * nSize + nY;
                return GetHeightByIndex((uint) index);
            }

            return 0f;
        }

        private void Init()
        {
            fMin = fOffset;
            fMax = fMin + 0xFFF0 * fRange;

            iOffset = (int) (fMin * Inv5Cm);
            iRange = (int) ((fMax - fMin) * Inv5Cm);
            iStep = (int) (iRange > 0 ? (iRange + Mask12Bit - 1) / Mask12Bit : 1);
        }

        private void RescaleToInt()
        {
            for (var i = 0; i < pHMData.Length; i++)
            {
                var hraw = pHMData[i];

                var height = fMin + (0xFFF0 & hraw) * fRange;
                var hdec = (ushort) ((int) ((height - fMin) * Inv5Cm) / iStep);

                var res = (hraw & 0xF) | (hdec << 4);
                pHMData[i] = (ushort) res;
            }
        }

        private static float Lerp(float s, float e, float t)
        {
            return s + (e - s) * t;
        }

        private static float Blerp(float cX0Y0, float cX1Y0, float cX0Y1, float cX1Y1, float tx, float ty)
        {
            return Lerp(Lerp(cX0Y0, cX1Y0, tx), Lerp(cX0Y1, cX1Y1, tx), ty);
        }

        private System.Drawing.Rectangle FindNearestSignificantPoints(int x, int y)
        {
            return new System.Drawing.Rectangle((int)MathF.Floor(x / 33f * nSize), (int)MathF.Floor(y / 33f * nSize), 1, 1);
        }

        private ushort GetRawHeight(int x, int y)
        {
            return RawDataByIndex((ushort)x, (ushort)y);
        }

        private void UpScale()
        {
            /*
            if (nSize > 0 && nSize < 33)
            {
                var result = new ushort[33 * 33];

                for (ushort targetX = 0; targetX <= 32; targetX++)
                    for (ushort targetY = 0; targetY <= 32; targetY++)
                    {
                        var targetIndex = (targetX * 33) + targetY;
                        
                        if ((targetX < nSize) && (targetY < nSize))
                            result[targetIndex] = 0xFFFF;
                        // result[targetIndex] = pHMData[targetX + (nSize * targetY)];
                        else
                            result[targetIndex] = 0x0000;
                    }

                pHMData = result;
                return;
            }
            */

            if (nSize > 0 && nSize < 33)
            {
                var sourceScale = (float)nSize / 33f;
                var result = new ushort[33 * 33];

                for (var targetX = 0; targetX <= 32; targetX++)
                    for (var targetY = 0; targetY <= 32; targetY++)
                    {
                        var targetIndex = (targetX * 33) + targetY ;

                        var sourceXF = (float)targetX / 33f * (float)nSize ;
                        var sourceYF = (float)targetY / 33f * (float)nSize ;
                        var sourceX = (ushort)Math.Clamp((ushort)Math.Floor(sourceXF),0,nSize-1);
                        var sourceY = (ushort)Math.Clamp((ushort)Math.Floor(sourceYF),0,nSize-1);

                        // var nearestRawPoints = FindNearestSignificantPoints(sourceX, sourceY);
                        // var nearestRawPoints = new System.Drawing.Rectangle(sourceX, sourceY,1,1);

                        // Get heights for these points
                        var rawHeightTL = (float)RawDataByIndex(sourceX, sourceY);
                        var rawHeightTR = (float)RawDataByIndex((ushort)(sourceX+1), sourceY);
                        var rawHeightBL = (float)RawDataByIndex(sourceX, (ushort)(sourceY+1));
                        var rawHeightBR = (float)RawDataByIndex((ushort)(sourceX + 1), (ushort)(sourceY + 1));
                        
                        // Calculate offset within points
                        var offX = (sourceXF - sourceX);
                        var offY = (sourceYF - sourceY);
                        var height = Blerp(rawHeightTL, rawHeightTR, rawHeightBL, rawHeightBR, offX, offY);

                        result[targetIndex] = (ushort)Math.Round(height);
                    }

                pHMData = result;
            }
        }
    }
}
