using System;

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

        var zoneGroupId = ZoneManager.Instance.GetZoneByKey(target.Transform.ZoneId).GroupId;
        var zoneGroup = ZoneManager.Instance.GetZoneGroupById(zoneGroupId);
        if (zoneGroup == null)
        {
            Logger.Warn($"{character.Name} seems to be trying to fish out of bounds.");
            return;
        }

        var lootTableId = target.Transform.World.Position.Z > 101 ? zoneGroup.FishingLandLootPackId : zoneGroup.FishingSeaLootPackId;

        Logger.Debug($"FishingLoot: lootTableId={lootTableId}");

        var pack = LootGameData.Instance.GetPack(lootTableId);

        if (pack == null || pack.Loots.Count <= 0)
        {
            Logger.Warn($"FishingLoot: {character.Name} в таблицах добычи отсутствует требуемый LootPackId={lootTableId}");
            return;
        }

        if (!pack.GiveLootPack(character, ItemTaskType.SkillEffectGainItem))
        {
            character.SendErrorMessage(ErrorMessageType.BagFull);
        }
    }
}
