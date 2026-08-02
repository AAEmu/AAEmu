using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

using WorldIntegration = AAEmu.Game.WorldIntegration;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class KnockBack : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.KnockBack;

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
        if (caster is Character)
            Logger.Debug("Special effects: KnockBack value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4);

        if (target is not Unit trg || trg.Hp <= 0)
            return;

        // value1 = distance (meters when small; mm when large — same heuristic as leap/teleport SE).
        var distance = value1 > 100 ? value1 / 1000f : value1;
        if (distance <= 0f)
            distance = 1f;

        var from = caster.Transform.World.Position;
        var to = trg.Transform.World.Position;
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 0.001f)
        {
            // Degenerate: push along caster facing.
            var (px, py) = MathUtil.AddDistanceToFront(distance, to.X, to.Y, caster.Transform.World.Rotation.Z);
            dx = px - to.X;
            dy = py - to.Y;
            len = MathF.Sqrt(dx * dx + dy * dy);
        }

        var nx = dx / len;
        var ny = dy / len;
        var zLift = value3 > 100 ? value3 / 1000f : value3;
        var destX = to.X + nx * distance;
        var destY = to.Y + ny * distance;
        var destZ = to.Z + MathF.Max(0f, zLift);

        trg.BroadcastPacket(new SCKnockBackUnitPacket(trg.ObjId, destX, destY, destZ), true);

        // Players are World-owned for movement; NPCs under ZoneAuthority get position from Zone.
        if (trg is Character || !WorldIntegration.ZoneAuthority)
        {
            trg.SetPosition(destX, destY, destZ,
                trg.Transform.World.Rotation.X,
                trg.Transform.World.Rotation.Y,
                trg.Transform.World.Rotation.Z);
        }

        if (WorldIntegration.ZoneAuthority)
            WorldIntegration.RelayKnockBackToZone?.Invoke(trg.ObjId, destX, destY, destZ);
    }
}
