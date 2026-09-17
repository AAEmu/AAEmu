using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Flags the target for a bot trial: the suspicion is remembered on the character and the client is told
/// with the bot-trial packet, which is what draws the trial state.
/// </summary>
public class ApplyBotTrial : SpecialEffectAction
{
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
        if (target is not Character character)
        {
            Logger.Debug("Special effects: ApplyBotTrial on a target that is not a player");
            return;
        }

        var fresh = character.BotCheck.MarkOnTrial();
        Logger.Info("Special effects: ApplyBotTrial on {0} (new: {1})", character.Name, fresh);

        // The effect's own parameters are the packet's, passed through untouched: nothing here knows what
        // they mean, and the client is the one that renders them.
        character.SendPacket(new SCSuspectGoingBotTrialPacket((ulong)value1, (ulong)value2, value3 != 0));
    }
}
