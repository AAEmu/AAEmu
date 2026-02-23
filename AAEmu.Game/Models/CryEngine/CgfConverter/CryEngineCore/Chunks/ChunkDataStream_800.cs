using CgfConverter.Models;
using Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using static Extensions.BinaryReaderExtensions;

namespace CgfConverter.CryEngineCore;

internal sealed class ChunkDataStream_800 : ChunkDataStream
{
    private short starCitizenFlag = 0;

    public override void Read(BinaryReader b)
    {
        base.Read(b);

        Flags2 = b.ReadUInt32(); // another filler
        uint dataStreamType = b.ReadUInt32();
        DataStreamType = (DatastreamType)dataStreamType;
        NumElements = b.ReadUInt32(); // number of elements in this chunk

        if (_model.FileVersion == FileVersion.CryTek3 || _model.FileVersion == FileVersion.CryTek1And2)
            BytesPerElement = b.ReadUInt32();
        
        if (_model.FileVersion == FileVersion.CryTek_3_6)
        {
            BytesPerElement = (uint)b.ReadInt16();  // Star Citizen 2.0 is using an int16 here now.
            starCitizenFlag = b.ReadInt16();        // For Star Citizen files, this is 257.  Other known games (Hunt, Evolve, Prey) are 0.
        }

        SkipBytes(b, 8);

        // Now do loops to read for each of the different Data Stream Types.  If vertices, need to populate Vector3s for example.
        switch (DataStreamType)
        {
            #region case DataStreamTypeEnum.VERTICES:

            case DatastreamType.VERTICES:  // Ref is 0x00000000
                Vertices = new Vector3[NumElements];

                switch (BytesPerElement)
                {
                    case 12:
                        for (int i = 0; i < NumElements; i++)
                        {
                            Vertices[i].X = b.ReadSingle();
                            Vertices[i].Y = b.ReadSingle();
                            Vertices[i].Z = b.ReadSingle();
                        }
                        break;
                    case 8:  // Prey files, and old Star Citizen files, Evolve
                        for (int i = 0; i < NumElements; i++)
                        {
                            Vertices[i] = b.ReadVector3(InputType.Half);
                            b.ReadUInt16();
                        }
                        break;
                    case 16:
                        for (int i = 0; i < NumElements; i++)
                        {
                            Vertices[i] = b.ReadVector3();
                            SkipBytes(b, 4); // TODO:  Sometimes there's a W to these structures.  Will investigate.
                        }
                        break;
                }
                break;

            #endregion
            #region case DataStreamTypeEnum.INDICES:

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

            #endregion
            #region case DataStreamTypeEnum.NORMALS:

            case DatastreamType.NORMALS:
                Normals = new Vector3[NumElements];
                if (BytesPerElement == 4)
                {
                    for (int i = 0; i < NumElements; i++)
                    {
                        SkipBytes(b, 4);
                        Normals[i] = new Vector3();
                    }
                }
                else
                {
                    for (int i = 0; i < NumElements; i++)
                    {
                        Normals[i] = b.ReadVector3();
                    }
                }

                break;

            #endregion
            #region case DataStreamTypeEnum.UVS:

            case DatastreamType.UVS:
                UVs = new UV[NumElements];
                for (int i = 0; i < NumElements; i++)
                {
                    UVs[i].U = b.ReadSingle();
                    UVs[i].V = b.ReadSingle();
                }
                //Utils.Log(LogLevelEnum.Debug, "Offset is {0:X}", b.BaseStream.Position);
                break;

            #endregion
            #region case DataStreamTypeEnum.TANGENTS:

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
                            Normals[i].X = (Tangents[i,0].y * Tangents[i,1].z - Tangents[i,0].z * Tangents[i,1].y);
                            Normals[i].Y = 0 - (Tangents[i,0].x * Tangents[i,1].z - Tangents[i,0].z * Tangents[i,1].x); 
                            Normals[i].Z = (Tangents[i,0].x * Tangents[i,1].y - Tangents[i,0].y * Tangents[i,1].x);
                            break;
                        default:
                            throw new Exception("Need to add new Tangent Size");
                    }
                }
                // Utils.Log(LogLevelEnum.Debug, "Offset is {0:X}", b.BaseStream.Position);
                break;

            #endregion
            #region case DataStreamTypeEnum.COLORS:

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

            #endregion
            #region case DataStreamTypeEnum.VERTSUVS:

            case DatastreamType.VERTSUVS:
                Vertices = new Vector3[NumElements];
                Colors = new IRGBA[NumElements];
                UVs = new UV[NumElements];
                switch (BytesPerElement)  // new Star Citizen files
                {
                    case 20:  // Dymek's code.  3 floats for vertex position, 4 bytes for normals, 2 halfs for UVs.  Normals are calculated from Tangents
                        Normals = new Vector3[NumElements];
                        for (int i = 0; i < NumElements; i++)
                        {
                            Vertices[i] = b.ReadVector3(); // For some reason, skins are an extra 1 meter in the z direction.

                            // Probably not normals
                            Normals[i].X = b.ReadSByte() / 127f;
                            Normals[i].Y = b.ReadSByte() / 127f;
                            Normals[i].Z = b.ReadSByte() / 127f;
                            b.ReadSByte(); // Should be FF.

                            UVs[i].U = (float)b.ReadHalf();
                            UVs[i].V = (float)b.ReadHalf();
                        }
                        break;
                    case 16:   // Dymek updated
                        if (starCitizenFlag == 257)
                        {
                            for (int i = 0; i < NumElements; i++)
                            {
                                Vertices[i].X = b.ReadDymekHalf();
                                Vertices[i].Y = b.ReadDymekHalf();
                                Vertices[i].Z = b.ReadDymekHalf();
                                SkipBytes(b, 2);

                                Colors[i] = b.ReadColorBGRA();

                                // Inelegant hack for Blender, as it's Collada importer doesn't support Alpha channels,
                                // and some materials need the alpha channel more than the green channel.
                                // This is complicated, as some materials need the green channel more.
                                byte alpha = Colors[i].a;
                                byte green = Colors[i].g;
                                Colors[i].a = green;
                                Colors[i].g = alpha;

                                // UVs ABSOLUTELY should use the Half structures.
                                UVs[i].U = (float)b.ReadHalf();
                                UVs[i].V = (float)b.ReadHalf();
                            }
                        }
                        else
                        {
                            Normals = new Vector3[NumElements];
                            // Legacy version using Halfs (Also Hunt models)
                            for (int i = 0; i < NumElements; i++)
                            {
                                Vertices[i] = b.ReadVector3(InputType.Half);
                                Normals[i] = b.ReadVector3(InputType.Half);  // prob not normals
                                UVs[i].U = (float)b.ReadHalf();
                                UVs[i].V = (float)b.ReadHalf();
                            }
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

                switch (BytesPerElement)
                {
                    case 8:
                        for (int i = 0; i < NumElements; i++)
                        {
                            MeshBoneMapping tmpMap = new();
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
                        }
                        break;
                    case 12:
                        for (int i = 0; i < NumElements; i++)
                        {
                            MeshBoneMapping tmpMap = new MeshBoneMapping();
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
                        }
                        break;

                    default:
                        Utilities.Log("Unknown BoneMapping structure");
                        break;
                }

                break;

            #endregion
            #region case DataStreamTypeEnum.QTangents
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
            #region default:

            default:
                Utilities.Log(LogLevelEnum.Debug, "***** Unknown DataStream Type *****");
                break;

                #endregion
        }
    }
}
