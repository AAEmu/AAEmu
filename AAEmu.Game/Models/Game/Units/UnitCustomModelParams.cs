using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Units
{
    public enum UnitCustomModelType
    {
        None = 0,
        Hair = 1,
        Skin = 2,
        Face = 3
    }

    public class FixedDecalAsset : PacketMarshaler
    {
        public uint AssetId { get; set; }
        public float AssetWeight { get; set; }

        public FixedDecalAsset(uint assetId = 0, float assetWeight = 0)
        {
            AssetId = assetId;
            AssetWeight = assetWeight;
        }

        public override void Read(PacketStream stream)
        {
            AssetId = stream.ReadUInt32();
            AssetWeight = stream.ReadSingle();
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(AssetId);
            stream.Write(AssetWeight);
            return stream;
        }
    }

    public class FaceModel : PacketMarshaler
    {
        public uint MovableDecalAssetId { get; set; }
        public float MovableDecalWeight { get; set; }
        public float MovableDecalScale { get; set; }
        public float MovableDecalRotate { get; set; }
        public short MovableDecalMoveX { get; set; }
        public short MovableDecalMoveY { get; set; }

        public FixedDecalAsset[] FixedDecalAsset { get; }

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
            FixedDecalAsset = new FixedDecalAsset[4];
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

            foreach (var asset in FixedDecalAsset)
                asset.Read(stream);

            DiffuseMapId = stream.ReadUInt32();
            NormalMapId = stream.ReadUInt32();
            EyelashMapId = stream.ReadUInt32();
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
            stream.Write(MovableDecalAssetId);
            stream.Write(MovableDecalWeight);
            stream.Write(MovableDecalScale);
            stream.Write(MovableDecalRotate);
            stream.Write(MovableDecalMoveX);
            stream.Write(MovableDecalMoveY);

            foreach (var asset in FixedDecalAsset)
                stream.Write(asset);

            stream.Write(DiffuseMapId);
            stream.Write(NormalMapId);
            stream.Write(EyelashMapId);
            stream.Write(NormalMapWeight);
            stream.Write(LipColor);
            stream.Write(LeftPupilColor);
            stream.Write(RightPupilColor);
            stream.Write(EyebrowColor);
            stream.Write(DecoColor);

            stream.Write(Modifier, true);
            return stream;
        }
    }

    public class UnitCustomModelParams : PacketMarshaler
    {
        private UnitCustomModelType _type;
        public uint HairColorId { get; private set; }
        public uint SkinColorId { get; private set; }
        public uint ModelId { get; private set; }
        public FaceModel Face { get; private set; }

        public UnitCustomModelParams(UnitCustomModelType type = UnitCustomModelType.None)
        {
            SetType(type);
        }

        public UnitCustomModelParams SetType(UnitCustomModelType type)
        {
            _type = type;
            if (_type == UnitCustomModelType.Face)
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
            SetType((UnitCustomModelType) stream.ReadByte()); // ext

            if (_type == UnitCustomModelType.None)
                return;

            HairColorId = stream.ReadUInt32();

            if (_type == UnitCustomModelType.Hair)
                return;

            SkinColorId = stream.ReadUInt32();
            ModelId = stream.ReadUInt32();

            if (_type == UnitCustomModelType.Skin)
                return;

            Face.Read(stream);
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte) _type); // ext
            if (_type == UnitCustomModelType.None)
                return stream;

            stream.Write(HairColorId);

            if (_type == UnitCustomModelType.Hair)
                return stream;

            stream.Write(SkinColorId);
            stream.Write(ModelId);

            if (_type == UnitCustomModelType.Skin)
                return stream;

            stream.Write(Face);

            return stream;
        }
    }
}
