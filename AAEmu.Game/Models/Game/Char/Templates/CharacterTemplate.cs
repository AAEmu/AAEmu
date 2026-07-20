using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Char.Templates;

public class CharacterTemplate
{
    public Race Race { get; set; }

public uint WildPreviewClothPackId { get; set; }

public uint PreviewClothPackId { get; set; }

public uint MagicPreviewWeaponPackId { get; set; }

public uint MagicPreviewClothPackId { get; set; }

public uint LovePreviewWeaponPackId { get; set; }

public uint LovePreviewClothPackId { get; set; }

public uint FightPreviewWeaponPackId { get; set; }

public uint FightPreviewClothPackId { get; set; }

public uint FaceItemId { get; set; }

public uint DefaultSystemVoiceSoundPackId { get; set; }

public uint DefaultFxVoiceSoundPackId { get; set; }

public uint DefaultCustomId { get; set; }

public bool Creatable { get; set; }
    public Gender Gender { get; set; }
    public uint ModelId { get; set; }
    public uint ZoneId { get; set; }
    public FactionsEnum FactionId { get; set; }
    public uint ReturnDistrictId { get; set; }
    public uint ResurrectionDistrictId { get; set; }
    public WorldSpawnPosition SpawnPosition { get; set; } = new();
    public uint[] Items { get; set; } = new uint[7];
    public List<uint> Buffs { get; set; } = [];
    public byte NumInventorySlot { get; set; }
    public short NumBankSlot { get; set; }
}
