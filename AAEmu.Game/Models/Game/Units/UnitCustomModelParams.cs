using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Models.Game.Units;

public enum UnitCustomModelType: byte
{
    None = 0,
    Hair = 1,
    Skin = 2,
    Face = 3
}

// One fixed-decal slot (asset id + blend weight). In 10.0.2.13 the ids and weights are serialized in
// separate runs (see FaceModel), so this is a plain data holder.
public class FixedDecalAsset(uint assetId = 0, float assetWeight = 0)
{
    public uint AssetId { get; set; } = assetId;
    public float AssetWeight { get; set; } = assetWeight;
}

// 10.0.2.13 face customization tier (LobbyChar_WriteAppearance T3 block).
// Wire order: movable face-decal transform, then a pish/pisc group of the 6
// fixed-decal asset ids, then a pish/pisc group of {diffuse, normal, eyelash} map ids, then the 6 fixed-decal
// weights, then the normal-map weight, then the 5 colors, then the 128-byte face-maker blob.
public class FaceModel : PacketMarshaler
{
    public uint MovableDecalAssetId { get; set; }
    public float MovableDecalWeight { get; set; }
    public float MovableDecalScale { get; set; }
    public float MovableDecalRotate { get; set; }
    public short MovableDecalMoveX { get; set; }
    public short MovableDecalMoveY { get; set; }

    private FixedDecalAsset[] FixedDecalAsset { get; }

    public uint DiffuseMapId { get; set; }
    public uint NormalMapId { get; set; }
    public uint EyelashMapId { get; set; }
    public float NormalMapWeight { get; set; }
    public uint LipColor { get; set; }
    public uint LeftPupilColor { get; set; }
    public uint RightPupilColor { get; set; }
    public uint EyebrowColor { get; set; }
    public uint DecoColor { get; set; }

    public byte[] Modifier { get; set; }

    public FaceModel()
    {
        FixedDecalAsset = new FixedDecalAsset[6]; // 10.0.2.13 widened the fixed-decal count from 4 to 6
        for (var i = 0; i < FixedDecalAsset.Length; i++)
            FixedDecalAsset[i] = new FixedDecalAsset();

        Modifier = new byte[128];
    }

    public bool SetFixedDecalAsset(byte index, uint id, float weight)
    {
        if (FixedDecalAsset.Length <= index)
            return false;

        FixedDecalAsset[index].AssetId = id;
        FixedDecalAsset[index].AssetWeight = weight;

        return true;
    }

    public override void Read(PacketStream stream)
    {
        MovableDecalAssetId = stream.ReadUInt32();
        MovableDecalWeight = stream.ReadSingle();
        MovableDecalScale = stream.ReadSingle();
        MovableDecalRotate = stream.ReadSingle();
        MovableDecalMoveX = stream.ReadInt16();
        MovableDecalMoveY = stream.ReadInt16();

        var decalIds = PishPiscCodec.Read(stream, 6);
        var mapIds = PishPiscCodec.Read(stream, 3);
        DiffuseMapId = mapIds[0];
        NormalMapId = mapIds[1];
        EyelashMapId = mapIds[2];

        for (var i = 0; i < 6; i++)
            FixedDecalAsset[i].AssetId = decalIds[i];
        for (var i = 0; i < 6; i++)
            FixedDecalAsset[i].AssetWeight = stream.ReadSingle();

        NormalMapWeight = stream.ReadSingle();
        LipColor = stream.ReadUInt32();
        LeftPupilColor = stream.ReadUInt32();
        RightPupilColor = stream.ReadUInt32();
        EyebrowColor = stream.ReadUInt32();
        DecoColor = stream.ReadUInt32();

        Modifier = stream.ReadBytes();
    }

    public override PacketStream Write(PacketStream stream)
    {
        // movable face-decal transform
        stream.Write(MovableDecalAssetId);
        stream.Write(MovableDecalWeight);
        stream.Write(MovableDecalScale);
        stream.Write(MovableDecalRotate);
        stream.Write(MovableDecalMoveX);
        stream.Write(MovableDecalMoveY);

        // 6 fixed-decal asset ids, then the 3 face-map ids — each as its own pish/pisc group
        var decalIds = new uint[6];
        for (var i = 0; i < 6; i++)
            decalIds[i] = FixedDecalAsset[i].AssetId;
        PishPiscCodec.Write(stream, decalIds);
        PishPiscCodec.Write(stream, [DiffuseMapId, NormalMapId, EyelashMapId]);

        // 6 fixed-decal weights, then the normal-map weight
        for (var i = 0; i < 6; i++)
            stream.Write(FixedDecalAsset[i].AssetWeight);
        stream.Write(NormalMapWeight);

        stream.Write(LipColor);
        stream.Write(LeftPupilColor);
        stream.Write(RightPupilColor);
        stream.Write(EyebrowColor);
        stream.Write(DecoColor);

        stream.Write(Modifier, true); // face-maker morph sliders (length-prefixed, max 128)
        return stream;
    }
}

// 10.0.2.13 appearance block (LobbyChar_WriteAppearance). A leading `ext` byte
// (UnitCustomModelType) is a cumulative LOD gate: 0 = nothing, 1 = +hair (T1), 2 = +body (T2), >=3 = +face (T3).
// Shared by SC_PACKET_UNIT_STATE, the character list, and the CSCreateCharacter body.
public class UnitCustomModelParams : PacketMarshaler
{
    private UnitCustomModelType _type;

    // T1 — base appearance (ext >= Hair)
    public byte Race { get; set; }
    public byte Gender { get; set; }
    public long VisualRaceExpiredTime { get; set; }
    public byte VisualRace { get; set; }
    public byte VisualGender { get; set; }
    public uint HairColor { get; set; }
    public uint HornColor { get; set; }
    public uint HairColorId { get; set; }        // wire `defaultHairColor`
    public uint TwoToneHairColor { get; set; }
    public float TwoToneFirstWidth { get; set; }
    public float TwoToneSecondWidth { get; set; }

    // T2 — body (ext >= Skin)
    public uint SkinColorId { get; set; }        // wire `skinColor`
    public uint BodyDiffuseMap { get; set; }
    public uint BodyNormalMap { get; set; }
    public float BodyWeight { get; set; }

    // Kept for callers; the unit's model id is sent separately (unit-state modelRef), not in this block.
    public uint ModelId { get; set; }

    // T3 — face (ext >= Face)
    public FaceModel Face { get; private set; }

    public UnitCustomModelParams(UnitCustomModelType type = UnitCustomModelType.None)
    {
        SetType(type);
    }

    public UnitCustomModelParams SetType(UnitCustomModelType type)
    {
        _type = type;
        if (_type >= UnitCustomModelType.Face)
            Face = new FaceModel();
        return this;
    }

    public UnitCustomModelParams SetModelId(uint modelId)
    {
        ModelId = modelId;
        return this;
    }

    public UnitCustomModelParams SetHairColorId(uint hairColorId)
    {
        HairColorId = hairColorId;
        return this;
    }

    public UnitCustomModelParams SetSkinColorId(uint skinColorId)
    {
        SkinColorId = skinColorId;
        return this;
    }

    public UnitCustomModelParams SetFace(FaceModel face)
    {
        Face = face;
        return this;
    }

    public override void Read(PacketStream stream)
    {
        SetType((UnitCustomModelType)stream.ReadByte()); // ext

        if (_type < UnitCustomModelType.Hair)
            return;

        // T1
        Race = stream.ReadByte();
        Gender = stream.ReadByte();
        VisualRaceExpiredTime = stream.ReadInt64();
        VisualRace = stream.ReadByte();
        VisualGender = stream.ReadByte();
        HairColor = stream.ReadUInt32();
        HornColor = stream.ReadUInt32();
        HairColorId = stream.ReadUInt32();
        TwoToneHairColor = stream.ReadUInt32();
        TwoToneFirstWidth = stream.ReadSingle();
        TwoToneSecondWidth = stream.ReadSingle();

        if (_type < UnitCustomModelType.Skin)
            return;

        // T2
        SkinColorId = stream.ReadUInt32();
        BodyDiffuseMap = stream.ReadUInt32();
        BodyNormalMap = stream.ReadUInt32();
        BodyWeight = stream.ReadSingle();

        if (_type < UnitCustomModelType.Face)
            return;

        // T3
        Face.Read(stream);
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)_type); // ext

        if (_type < UnitCustomModelType.Hair)
            return stream;

        // T1
        stream.Write(Race);
        stream.Write(Gender);
        stream.Write(VisualRaceExpiredTime);
        stream.Write(VisualRace);
        stream.Write(VisualGender);
        stream.Write(HairColor);
        stream.Write(HornColor);
        stream.Write(HairColorId);          // defaultHairColor
        stream.Write(TwoToneHairColor);
        stream.Write(TwoToneFirstWidth);
        stream.Write(TwoToneSecondWidth);

        if (_type < UnitCustomModelType.Skin)
            return stream;

        // T2
        stream.Write(SkinColorId);          // skinColor
        stream.Write(BodyDiffuseMap);
        stream.Write(BodyNormalMap);
        stream.Write(BodyWeight);

        if (_type < UnitCustomModelType.Face)
            return stream;

        // T3
        stream.Write(Face);

        return stream;
    }
}
