using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class ReportBot : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.ReportBot;
    
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
        if (caster is Character casterPlayer)
        {
            Logger.Debug($"Special effects: ReportBot value1 {value1}, value2 {value2}, value3 {value3}, value4 {value4}");
        }
        else
        {
            // Only players can report other players
            return;
        }

        // TODO: Where does it save the reported message?

        if (targetObj?.Type == SkillCastTargetType.Unit)
        {
            var targetPlayer = caster.ParentWorld.GetCharacterByObjId(targetObj.ObjId);
            if (targetPlayer == null)
            {
                Logger.Debug($"Special effects: ReportBot target is not a player, ObjId: {targetObj.ObjId}");
                return;
            }

            var msg = (skillObject as SkillObjectText)?.Msg ?? string.Empty;

            if (!CrimeManager.Instance.ReportBot(targetPlayer, casterPlayer, msg))
            {
                Logger.Warn($"Special effects: ReportBot target {targetPlayer.Name} failed to get reported by {casterPlayer.Name} (possible multiple reports)");
                return;
            }
            // Broadcast result
            casterPlayer.BroadcastPacket(new SCBotSuspectReportedPacket(casterPlayer.Name, targetPlayer.Name), true);
        }
        else
        {
            Logger.Debug($"Special effects: ReportBot target is not a Unit, ObjId: {targetObj?.ObjId ?? 0}");
        }
    }
}
