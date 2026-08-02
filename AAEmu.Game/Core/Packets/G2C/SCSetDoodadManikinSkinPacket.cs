using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>Sets the costume appearance displayed by a private coffer mannequin.</summary>
/// <remarks>
/// <c>+0x10</c>, signed i32 item-template <c>type</c> at <c>+0x14</c>, and signed i32 packed
/// <c>type</c> as an item template and applies the color before refreshing the doodad model.
/// </remarks>
public class SCSetDoodadManikinSkinPacket : GamePacket
{
    private readonly uint _doodadObjId;
    private readonly int _itemTemplateId;
    private readonly int _dyeingColor;

    public SCSetDoodadManikinSkinPacket(DoodadCoffer coffer)
        : base(SCOffsets.SCSetDoodadManikinSkinPacket, 1)
    {
        _doodadObjId = coffer.ObjId;

        var item = coffer.ItemContainer?.GetItemBySlot(DoodadCoffer.ManikinDisplaySlot);
        if (item?.Template is not EquipItemTemplate equipTemplate ||
            (coffer.AllowedItemCategoryIds.Count > 0 &&
             !coffer.AllowedItemCategoryIds.Contains(item.Template.CategoryId)))
        {
            return;
        }

        _itemTemplateId = unchecked((int)item.TemplateId);
        _dyeingColor = unchecked((int)equipTemplate.DyeingColor);
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_doodadObjId);
        stream.Write(_itemTemplateId);
        stream.Write(_dyeingColor);
        return stream;
    }
}
