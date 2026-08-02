using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class ChangeTarget : SpecialEffectAction
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
        if (caster is not Unit newTarget || target is not Unit affectedUnit)
            return;

        if (WorldIntegration.ZoneAuthority)
        {
            // Zone owns the authoritative target mutation. Its ZW response updates
            // the World mirror and produces the client notification in wire order.
            WorldIntegration.RelayTargetChangedToZone?.Invoke(
                affectedUnit.ObjId,
                newTarget.ObjId,
                true);
            return;
        }

        affectedUnit.CurrentTarget = newTarget;
        var packet = new SCTargetChangedPacket(affectedUnit.ObjId, newTarget.ObjId);
        if (affectedUnit is Character character)
            character.SendPacket(packet);
        else
            affectedUnit.BroadcastPacket(packet, true);
    }
}
