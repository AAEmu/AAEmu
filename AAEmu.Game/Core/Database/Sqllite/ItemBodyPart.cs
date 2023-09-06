using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ItemBodyPart
{
    public long? Id { get; set; }

    public long? ItemId { get; set; }

    public long? SlotTypeId { get; set; }

    public long? ModelId { get; set; }

    public long? AssetId { get; set; }

    public long? Asset1Id { get; set; }

    public long? Asset2Id { get; set; }

    public long? Asset3Id { get; set; }

    public long? Asset4Id { get; set; }

    public string FaceMask { get; set; }

    public string HairBase { get; set; }

    public byte[] NpcOnly { get; set; }

    public byte[] BeautyshopOnly { get; set; }

    public long? CustomTextureId { get; set; }

    public long? CustomTexture1Id { get; set; }

    public long? CustomTexture2Id { get; set; }

    public long? CustomTexture3Id { get; set; }

    public long? CustomTexture4Id { get; set; }
}
