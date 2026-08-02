using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;

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
        if (count > 32)
            count = 32;

        var selected = new List<uint>((int)count);
        for (var i = 0; i < count; i++)
            selected.Add(stream.ReadUInt32());

        var character = Connection.ActiveChar;
        if (character == null)
            return;

        Logger.Info(
            "CSInvokeItemSelectiveItemEffect {0}: slot={1}/{2} try={3} picks=[{4}]",
            character.Name, slotType, slot, tryCount, string.Join(",", selected));

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

        var maxPicks = effect.SelectCount > 0 ? effect.SelectCount : 1;
        if (selected.Count == 0 || selected.Count > maxPicks)
        {
            Logger.Warn(
                "CSInvokeItemSelectiveItemEffect: pick count {0} invalid (max {1}, multi={2})",
                selected.Count, maxPicks, effect.IsMulti);
            return;
        }

        if (!effect.IsMulti && selected.Count != 1 && selected.Count != maxPicks)
        {
            Logger.Warn(
                "CSInvokeItemSelectiveItemEffect: non-multi effect expects 1 pick, got {0}",
                selected.Count);
            return;
        }

        // 1-based indices into elems ordered by id (client UI order).
        var grants = new List<SelectiveItemEffectElem>(selected.Count);
        var seen = new HashSet<uint>();
        foreach (var idx in selected)
        {
            if (idx == 0 || idx > effect.Elems.Count)
            {
                Logger.Warn(
                    "CSInvokeItemSelectiveItemEffect: option index {0} out of range 1..{1}",
                    idx, effect.Elems.Count);
                return;
            }

            if (!seen.Add(idx))
            {
                Logger.Warn("CSInvokeItemSelectiveItemEffect: duplicate option index {0}", idx);
                return;
            }

            grants.Add(effect.Elems[(int)idx - 1]);
        }

        var burn = effect.ConsumeItemCount > 0 ? effect.ConsumeItemCount : 1;
        if (source.Count < burn)
        {
            Logger.Warn(
                "CSInvokeItemSelectiveItemEffect: need {0}x tpl {1}, have {2}",
                burn, source.TemplateId, source.Count);
            return;
        }

        var burned = character.Inventory.Bag.ConsumeItem(
            ItemTaskType.ConsumeSkillSource, source.TemplateId, burn, source);
        if (burned < burn)
        {
            Logger.Warn(
                "CSInvokeItemSelectiveItemEffect: consume failed {0}/{1} tpl {2}",
                burned, burn, source.TemplateId);
            return;
        }

        foreach (var elem in grants)
        {
            var amount = elem.Count > 0 ? elem.Count : 1;
            if (!character.Inventory.Bag.AcquireDefaultItem(
                    ItemTaskType.SkillEffectGainItem, elem.ItemId, amount, elem.GradeId))
            {
                Logger.Warn(
                    "CSInvokeItemSelectiveItemEffect: failed to grant item {0} x{1} to {2} (mail?)",
                    elem.ItemId, amount, character.Name);
            }
            else
            {
                Logger.Info(
                    "CSInvokeItemSelectiveItemEffect: {0} received item {1} x{2} grade {3}",
                    character.Name, elem.ItemId, amount, elem.GradeId);
            }
        }
    }
}
