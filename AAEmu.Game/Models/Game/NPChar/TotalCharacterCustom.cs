namespace AAEmu.Game.Models.Game.NPChar
{
    public class TotalCharacterCustom
    {
        public uint Id { get; set; }
        public uint ModelId { get; set; }
        public string Name { get; set; }
        public bool NpcOnly { get; set; }
        public uint HairId { get; set; }
        public uint HornId { get; set; }
        public uint BodyNormalMapId { get; set; }
        public float BodyNormalMapWeight { get; set; }
        public uint DefaultHairColor { get; set; }
        public uint HairColorId { get; set; }
        public uint HornColorId { get; set; }
        public uint SkinColorId { get; set; }
        public float TwoToneFirstWidth { get; set; }
        public uint TwoToneHairColor { get; set; }
        public float TwoToneSecondWidth { get; set; }
        public uint FaceMovableDecalAssetId { get; set; }
        public float FaceMovableDecalScale { get; set; }
        public float FaceMovableDecalRotate { get; set; }
        public short FaceMovableDecalMoveX { get; set; }
        public short FaceMovableDecalMoveY { get; set; }
        public uint FaceFixedDecalAsset0Id { get; set; }
        public uint FaceFixedDecalAsset1Id { get; set; }
        public uint FaceFixedDecalAsset2Id { get; set; }
        public uint FaceFixedDecalAsset3Id { get; set; }
        public uint FaceFixedDecalAsset4Id { get; set; }
        public uint FaceFixedDecalAsset5Id { get; set; }
        public uint FaceDiffuseMapId { get; set; }
        public uint FaceNormalMapId { get; set; }
        public uint FaceEyelashMapId { get; set; }
        public uint LipColor { get; set; }
        public uint LeftPupilColor { get; set; }
        public uint RightPupilColor { get; set; }
        public uint EyebrowColor { get; set; }
        public byte[] Modifier { get; set; }
        public uint OwnerTypeId { get; set; }
        public float FaceMovableDecalWeight { get; set; }
        public float FaceFixedDecalAsset0Weight { get; set; }
        public float FaceFixedDecalAsset1Weight { get; set; }
        public float FaceFixedDecalAsset2Weight { get; set; }
        public float FaceFixedDecalAsset3Weight { get; set; }
        public float FaceFixedDecalAsset4Weight { get; set; }
        public float FaceFixedDecalAsset5Weight { get; set; }
        public float FaceNormalMapWeight { get; set; }
        public uint DecoColor { get; set; }

        public TotalCharacterCustom()
        {
            Modifier = new byte[0];
        }
    }
}
