using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class TotalCharacterCustom
{
    public long? Id { get; set; }

    public long? ModelId { get; set; }

    public string Name { get; set; }

    public byte[] NpcOnly { get; set; }

    public long? HairId { get; set; }

    public long? HairColorId { get; set; }

    public long? SkinColorId { get; set; }

    public long? FaceMovableDecalAssetId { get; set; }

    public double? FaceMovableDecalScale { get; set; }

    public double? FaceMovableDecalRotate { get; set; }

    public long? FaceMovableDecalMoveX { get; set; }

    public long? FaceMovableDecalMoveY { get; set; }

    public long? FaceFixedDecalAsset0Id { get; set; }

    public long? FaceFixedDecalAsset1Id { get; set; }

    public long? FaceFixedDecalAsset2Id { get; set; }

    public long? FaceFixedDecalAsset3Id { get; set; }

    public long? FaceDiffuseMapId { get; set; }

    public long? FaceNormalMapId { get; set; }

    public long? FaceEyelashMapId { get; set; }

    public long? LipColor { get; set; }

    public long? LeftPupilColor { get; set; }

    public long? RightPupilColor { get; set; }

    public long? EyebrowColor { get; set; }

    public byte[] Modifier { get; set; }

    public long? OwnerTypeId { get; set; }

    public double? FaceMovableDecalWeight { get; set; }

    public double? FaceFixedDecalAsset0Weight { get; set; }

    public double? FaceFixedDecalAsset1Weight { get; set; }

    public double? FaceFixedDecalAsset2Weight { get; set; }

    public double? FaceFixedDecalAsset3Weight { get; set; }

    public double? FaceNormalMapWeight { get; set; }

    public long? DecoColor { get; set; }
}
