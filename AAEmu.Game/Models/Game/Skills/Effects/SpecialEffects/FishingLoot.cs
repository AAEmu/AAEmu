using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class FishingLoot : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.FishingLoot;

    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int value1,
        int value2,
        int value3,
        int value4)
    {
        if (caster is not Character character)
            return;

        Logger.Debug("Special effects: FishingLoot value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4);

        if (target == null)
        {
            Logger.Warn("FishingLoot reached without a target for {0}", character.Name);
            character.SendErrorMessage(FishingLootReplyRules.Reply(false, false, false, false)!.Value);
            return;
        }

        var zone = ZoneManager.Instance.GetZoneByKey(target.Transform.ZoneId);
        var zoneGroup = zone == null ? null : ZoneManager.Instance.GetZoneGroupById(zone.GroupId);
        if (zoneGroup == null)
        {
            Logger.Warn("{0} seems to be trying to fish out of bounds.", character.Name);
            character.SendErrorMessage(FishingLootReplyRules.Reply(true, false, false, false)!.Value);
            return;
        }

        var lootTableId = target.Transform.World.Position.Z > 101 ? zoneGroup.FishingLandLootPackId : zoneGroup.FishingSeaLootPackId;
        var pack = LootGameData.Instance.GetPack(lootTableId);

        if (pack == null || pack.Loots.Count <= 0)
        {
            Logger.Error(
                "FishingLoot has no usable loot pack id={0} for zone={1} character={2}",
                lootTableId,
                target.Transform.ZoneId,
                character.Name);
            character.SendErrorMessage(FishingLootReplyRules.Reply(true, pack != null, pack?.Loots.Count > 0, false)!.Value);
            return;
        }

        var delivered = pack.GiveLootPack(character, ActabilityType.Fishing, ItemTaskType.SkillEffectGainItem);
        var reply = FishingLootReplyRules.Reply(true, true, true, delivered);
        if (reply.HasValue)
            character.SendErrorMessage(reply.Value);
    }
}
