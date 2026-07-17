namespace AAEmu.Game.Models.Game.Items.Templates;

public class BodyPartTemplate : ItemTemplate
{
    public override Type ClassType => typeof(BodyPart);

    public uint ModelId { get; set; }

    public int RightEyeY { get; set; }

    public int RightEyeX { get; set; }

    public int RightEyeWidth { get; set; }

    public int RightEyeHeight { get; set; }

    public bool OddEye { get; set; }

    public int LeftEyeY { get; set; }

    public int LeftEyeX { get; set; }

    public int LeftEyeWidth { get; set; }

    public int LeftEyeHeight { get; set; }

    public string HairBase { get; set; }

    public string FaceMask { get; set; }

    public uint CustomTextureId { get; set; }

    public uint CustomTexture4Id { get; set; }

    public uint CustomTexture3Id { get; set; }

    public uint CustomTexture2Id { get; set; }

    public uint CustomTexture1Id { get; set; }

    public uint AssetId { get; set; }

    public uint Asset4Id { get; set; }

    public uint Asset3Id { get; set; }

    public uint Asset2Id { get; set; }

    public uint Asset1Id { get; set; }
    public bool NpcOnly { get; set; }
    public bool BeautyShopOnly { get; set; }
    public uint ItemId { get; set; }
    public uint SlotTypeId { get; set; }
}
