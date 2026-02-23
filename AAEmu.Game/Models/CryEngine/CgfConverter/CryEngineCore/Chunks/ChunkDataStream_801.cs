using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using CgfConverter.Models;
using Extensions;
using static Extensions.BinaryReaderExtensions;

namespace CgfConverter.CryEngineCore;

internal sealed class ChunkDataStream_801 : ChunkDataStream
{
    public override void Read(BinaryReader b)
    {
        base.Read(b);

        Flags2 = b.ReadUInt32(); // another filler
        uint datastreamType = b.ReadUInt32();
        DataStreamType = (DatastreamType)datastreamType;
        SkipBytes(b, 4);    // data stream index, for multiple streams (not used)
        
        NumElements = b.ReadUInt32(); // number of elements in this chunk
        BytesPerElement = b.ReadUInt32();

        SkipBytes(b, 8);

        switch (DataStreamType)
        {
            case DatastreamType.VERTICES:
                Vertices = new Vector3[NumElements];
                if (BytesPerElement == 12)
                {
                    for (int i = 0; i < NumElements; i++)
                    {
                        Vertices[i] = b.ReadVector3();
                    }
                }
                else
                if (BytesPerElement == 8)
                {
                    for (int i = 0; i < NumElements; i++)
                    {
                        Vertices[i] = b.ReadVector3(InputType.Half);
                        b.ReadUInt16();
                    }
                }

                break;

            case DatastreamType.INDICES:
                Indices = new uint[NumElements];

                if (BytesPerElement == 2)
                {
                    for (int i = 0; i < NumElements; i++)
                    {
                        Indices[i] = b.ReadUInt16();
                    }
                }
                if (BytesPerElement == 4)
                {
                    for (int i = 0; i < NumElements; i++)
                    {
                        Indices[i] = b.ReadUInt32();
                    }
                }
                break;

            case DatastreamType.NORMALS:
                Normals = new Vector3[NumElements];
                for (int i = 0; i < NumElements; i++)
                {
                    Normals[i] = b.ReadVector3();
                }
                break;

            case DatastreamType.UVS:
                UVs = new UV[NumElements];
                for (int i = 0; i < NumElements; i++)
                {
                    UVs[i].U = b.ReadSingle();
                    UVs[i].V = b.ReadSingle();
                }
                break;

            case DatastreamType.TANGENTS:
                Tangents = new Tangent[NumElements, 2];
                Normals = new Vector3[NumElements];
                for (int i = 0; i < NumElements; i++)
                {
                    switch (BytesPerElement)
                    {
                        case 0x10:
                            // These have to be divided by 32767 to be used properly (value between 0 and 1)
                            Tangents[i, 0].x = b.ReadInt16();
                            Tangents[i, 0].y = b.ReadInt16();
                            Tangents[i, 0].z = b.ReadInt16();
                            Tangents[i, 0].w = b.ReadInt16();

                            Tangents[i, 1].x = b.ReadInt16();
                            Tangents[i, 1].y = b.ReadInt16();
                            Tangents[i, 1].z = b.ReadInt16();
                            Tangents[i, 1].w = b.ReadInt16();

                            break;
                        case 0x08:
                            // These have to be divided by 127 to be used properly (value between 0 and 1)
                            // Tangent
                            Tangents[i, 0].w = b.ReadSByte() / 127f;
                            Tangents[i, 0].x = b.ReadSByte() / 127f;
                            Tangents[i, 0].y = b.ReadSByte() / 127f;
                            Tangents[i, 0].z = b.ReadSByte() / 127f;

                            // Binormal
                            Tangents[i, 1].w = b.ReadSByte() / 127f;
                            Tangents[i, 1].x = b.ReadSByte() / 127f;
                            Tangents[i, 1].y = b.ReadSByte() / 127f;
                            Tangents[i, 1].z = b.ReadSByte() / 127f;

                            // Calculate the normal based on the cross product of the tangents.
                            //this.Normals[i].x = (Tangents[i,0].y * Tangents[i,1].z - Tangents[i,0].z * Tangents[i,1].y);
                            //this.Normals[i].y = 0 - (Tangents[i,0].x * Tangents[i,1].z - Tangents[i,0].z * Tangents[i,1].x); 
                            //this.Normals[i].z = (Tangents[i,0].x * Tangents[i,1].y - Tangents[i,0].y * Tangents[i,1].x);
                            break;
                        default:
                            throw new Exception("Need to add new Tangent Size");
                    }
                }
                // Utils.Log(LogLevelEnum.Debug, "Offset is {0:X}", b.BaseStream.Position);
                break;

            case DatastreamType.COLORS:
                switch (BytesPerElement)
                {
                    case 3:
                        Colors = new IRGBA[NumElements];
                        for (int i = 0; i < NumElements; i++)
                        {
                            Colors[i].r = b.ReadByte();
                            Colors[i].g = b.ReadByte();
                            Colors[i].b = b.ReadByte();
                            Colors[i].a = 255;
                        }
                        break;

                    case 4:
                        Colors = new IRGBA[NumElements];
                        for (int i = 0; i < NumElements; i++)
                        {
                            Colors[i] = b.ReadColor();
                        }
                        break;
                    default:
                        Utilities.Log("Unknown Color Depth");
                        for (int i = 0; i < NumElements; i++)
                        {
                            SkipBytes(b, BytesPerElement);
                        }
                        break;
                }
                break;

            #region case DataStreamTypeEnum.VERTSUVS:

            case DatastreamType.VERTSUVS:  // 3 half floats for verts, 3 half floats for normals, 2 half floats for UVs
                // Utils.Log(LogLevelEnum.Debug, "In VertsUVs...");
                Vertices = new Vector3[NumElements];
                Normals = new Vector3[NumElements];
                Colors = new IRGBA[NumElements];
                UVs = new UV[NumElements];
                switch (BytesPerElement)  // new Star Citizen files
                {
                    case 20:  // Dymek wrote this.  Used in 2.6 skin files.  3 floats for vertex position, 4 bytes for normals, 2 halfs for UVs.  Normals are calculated from Tangents
                        for (int i = 0; i < NumElements; i++)
                        {
                            Vertices[i] = b.ReadVector3(); // For some reason, skins are an extra 1 meter in the z direction.

                            // Normals are stored in a signed byte, prob div by 127.
                            Normals[i].X = b.ReadSByte() / 127.0f;
                            Normals[i].Y = b.ReadSByte() / 127.0f;
                            Normals[i].Z = b.ReadSByte() / 127.0f;
                            b.ReadSByte();

                            UVs[i].U = (float)b.ReadHalf();
                            UVs[i].V = (float)b.ReadHalf();
                        }
                        break;
                    case 16:   // Dymek updated this.
                        for (int i = 0; i < NumElements; i++)
                        {
                            Vertices[i] = b.ReadVector3(InputType.CryHalf);
                            SkipBytes(b, 2);

                            // Next structure is Colors, not normals.  For 16 byte elements, normals are calculated from Tangent data.
                            //this.RGBColors[i].r = b.ReadByte();
                            //this.RGBColors[i].g = b.ReadByte();
                            //this.RGBColors[i].b = b.ReadByte();
                            //b.ReadByte();           // additional byte.

                            //this.Normals[i].x = (b.ReadByte() - 128.0f) / 127.5f;
                            //this.Normals[i].y = (b.ReadByte() - 128.0f) / 127.5f;
                            //this.Normals[i].z = (b.ReadByte() - 128.0f) / 127.5f;
                            //b.ReadByte();           // additional byte.

                            // Read a Quat, convert it to vector3
                            Vector4 quat = new Vector4();
                            quat.X = (b.ReadByte() - 128.0f) / 127.5f;
                            quat.Y = (b.ReadByte() - 128.0f) / 127.5f;
                            quat.Z = (b.ReadByte() - 128.0f) / 127.5f;
                            quat.W = (b.ReadByte() - 128.0f) / 127.5f;
                            Normals[i].X = (2f * (quat.X * quat.Z + quat.Y * quat.W));
                            Normals[i].Y = (2f * (quat.Y * quat.Z - quat.X * quat.W));
                            Normals[i].Z = (2f * (quat.Z * quat.Z + quat.W * quat.W)) - 1f;

                            UVs[i].U = (float)b.ReadHalf();
                            UVs[i].V = (float)b.ReadHalf();
                            #region Legacy version using Halfs
                            //Half xshort = new Half();
                            //xshort.bits = b.ReadUInt16();
                            //this.Vertices[i].x = xshort.ToSingle();

                            //Half yshort = new Half();
                            //yshort.bits = b.ReadUInt16();
                            //this.Vertices[i].y = yshort.ToSingle();

                            //Half zshort = new Half();
                            //zshort.bits = b.ReadUInt16();
                            //this.Vertices[i].z = zshort.ToSingle();

                            //Half xnorm = new Half();
                            //xnorm.bits = b.ReadUInt16();
                            //this.Normals[i].x = xnorm.ToSingle();

                            //Half ynorm = new Half();
                            //ynorm.bits = b.ReadUInt16();
                            //this.Normals[i].y = ynorm.ToSingle();

                            //Half znorm = new Half();
                            //znorm.bits = b.ReadUInt16();
                            //this.Normals[i].z = znorm.ToSingle();

                            //Half uvu = new Half();
                            //uvu.bits = b.ReadUInt16();
                            //this.UVs[i].U = uvu.ToSingle();

                            //Half uvv = new Half();
                            //uvv.bits = b.ReadUInt16();
                            //this.UVs[i].V = uvv.ToSingle();
                            #endregion
                        }
                        break;
                    default:
                        Utilities.Log("Unknown VertUV structure");
                        for (int i = 0; i < NumElements; i++)
                        {
                            SkipBytes(b, BytesPerElement);
                        }
                        break;
                }
                break;
            #endregion
            #region case DataStreamTypeEnum.BONEMAP:
            case DatastreamType.BONEMAP:
                SkinningInfo skin = GetSkinningInfo();
                skin.BoneMapping = new List<MeshBoneMapping>();

                // Bones should have 4 bone IDs (index) and 4 weights.
                for (int i = 0; i < NumElements; i++)
                {
                    MeshBoneMapping tmpMap = new MeshBoneMapping();
                    switch (BytesPerElement)
                    {
                        case 8:
                            tmpMap.BoneIndex = new int[4];
                            tmpMap.Weight = new int[4];

                            for (int j = 0; j < 4; j++)         // read the 4 bone indexes first
                            {
                                tmpMap.BoneIndex[j] = b.ReadByte();

                            }
                            for (int j = 0; j < 4; j++)           // read the weights.
                            {
                                tmpMap.Weight[j] = b.ReadByte();
                            }
                            skin.BoneMapping.Add(tmpMap);
                            break;
                        case 12:
                            tmpMap.BoneIndex = new int[4];
                            tmpMap.Weight = new int[4];

                            for (int j = 0; j < 4; j++)         // read the 4 bone indexes first
                            {
                                tmpMap.BoneIndex[j] = b.ReadUInt16();

                            }
                            for (int j = 0; j < 4; j++)           // read the weights.
                            {
                                tmpMap.Weight[j] = b.ReadByte();
                            }
                            skin.BoneMapping.Add(tmpMap);

                            break;
                        default:
                            Utilities.Log("Unknown BoneMapping structure");
                            break;
                    }
                }
                break;

            #endregion
            #region DataStreamTypeEnum.QTANGENTS
            case DatastreamType.QTANGENTS:
                Tangents = new Tangent[NumElements, 2];
                Normals = new Vector3[NumElements];
                for (int i = 0; i < NumElements; i++)
                {
                    Tangents[i, 0].w = b.ReadSByte() / 127f;
                    Tangents[i, 0].x = b.ReadSByte() / 127f;
                    Tangents[i, 0].y = b.ReadSByte() / 127f;
                    Tangents[i, 0].z = b.ReadSByte() / 127f;

                    // Binormal
                    Tangents[i, 1].w = b.ReadSByte() / 127f;
                    Tangents[i, 1].x = b.ReadSByte() / 127f;
                    Tangents[i, 1].y = b.ReadSByte() / 127f;
                    Tangents[i, 1].z = b.ReadSByte() / 127f;

                    // Calculate the normal based on the cross product of the tangents.
                    Normals[i].X = (Tangents[i, 0].y * Tangents[i, 1].z - Tangents[i, 0].z * Tangents[i, 1].y);
                    Normals[i].Y = 0 - (Tangents[i, 0].x * Tangents[i, 1].z - Tangents[i, 0].z * Tangents[i, 1].x);
                    Normals[i].Z = (Tangents[i, 0].x * Tangents[i, 1].y - Tangents[i, 0].y * Tangents[i, 1].x);
                }
                break;
            #endregion // Prey normals?

            default:
                Utilities.Log(LogLevelEnum.Debug, "***** Unknown DataStream Type *****");
                break;
        }
    }
}
