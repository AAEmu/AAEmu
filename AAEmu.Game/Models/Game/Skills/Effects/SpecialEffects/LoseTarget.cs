using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class LoseTarget : SpecialEffectAction
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
        if (target is not Unit affectedUnit)
            return;

        if (WorldIntegration.ZoneAuthority)
        {
            // A zero target bc is the native clear-target sentinel. Wait for the
            // Zone response before changing the World mirror or notifying clients.
            WorldIntegration.RelayTargetChangedToZone?.Invoke(
                affectedUnit.ObjId,
                0,
                true);
            return;
        }

        affectedUnit.CurrentTarget = null;
        var packet = new SCTargetChangedPacket(affectedUnit.ObjId, 0);
        if (affectedUnit is Character character)
            character.SendPacket(packet);
        else
            affectedUnit.BroadcastPacket(packet, true);
    }
}
