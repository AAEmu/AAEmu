using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ItemAsset
{
    public long? Id { get; set; }

    public long? ModelId { get; set; }

    public string Path { get; set; }

    public long? Detail { get; set; }

    public double? AttachmentOffsetPosX { get; set; }

    public double? AttachmentOffsetPosY { get; set; }

    public double? AttachmentOffsetPosZ { get; set; }

    public double? AttachmentOffsetRotX { get; set; }

    public double? AttachmentOffsetRotY { get; set; }

    public double? AttachmentOffsetRotZ { get; set; }

    public double? HeelOffsetHeight { get; set; }

    public long? HingeIdx { get; set; }

    public double? HingeLimit { get; set; }

    public double? HingeDamping { get; set; }

    public byte[] AllowMirror { get; set; }

    public string DefaultAnim { get; set; }

    public long? MoreAssetId { get; set; }

    public double? NameTagOffsetHeight { get; set; }
}
