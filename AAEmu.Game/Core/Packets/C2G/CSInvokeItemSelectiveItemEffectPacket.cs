using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Wire: u8 slotType, u8 slotIndex, u32 tryCount, u32 count, u32[count] option indices (1-based).
/// </summary>
public class CSInvokeItemSelectiveItemEffectPacket() : GamePacket(CSOffsets.CSInvokeItemSelectiveItemEffectPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var slotType = (SlotType)stream.ReadByte();
        var slot = stream.ReadByte();
        var tryCount = stream.ReadUInt32();
        var count = stream.ReadUInt32();
        if (count > 32 || stream.LeftBytes != count * sizeof(uint))
            return;

        var selected = new List<uint>((int)count);
        for (var i = 0; i < count; i++)
            selected.Add(stream.ReadUInt32());

        var character = Connection.ActiveChar;
        if (character == null)
            return;

        Logger.Info(
            "CSInvokeItemSelectiveItemEffect {0}: slot={1}/{2} try={3} picks=[{4}]",
            character.Name, slotType, slot, tryCount, string.Join(",", selected));

        if (slotType != SlotType.Inventory)
            return;

        var source = character.Inventory.GetItem(slotType, slot);
        if (source == null)
        {
            Logger.Warn("CSInvokeItemSelectiveItemEffect: no item at {0}/{1} for {2}", slotType, slot, character.Name);
            return;
        }

        var skillId = source.Template?.UseSkillId ?? 0;
        if (skillId == 0)
        {
            Logger.Warn(
                "CSInvokeItemSelectiveItemEffect: item tpl {0} has no use_skill_id",
                source.TemplateId);
            return;
        }

        var effect = SelectiveItemEffectGameData.Instance.GetBySkillId(skillId);
        if (effect == null || effect.Elems.Count == 0)
        {
            Logger.Warn(
                "CSInvokeItemSelectiveItemEffect: no selective_item_effects for skill {0} (item tpl {1})",
                skillId, source.TemplateId);
            return;
        }

        var error = SelectiveItemOpening.Open(character, source.Id, effect, tryCount, selected,
            AAEmu.Game.Core.Managers.ItemManager.Instance);
        if (error.HasValue)
            character.SendErrorMessage(error.Value);
    }
}
