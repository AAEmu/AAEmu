using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class SpawnGimmickEffect : EffectTemplate
{
    public uint GimmickId { get; set; } // here we mean TemplateId
    public bool OffsetFromSource { get; set; }
    public uint OffsetCoordinateId { get; set; }
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public float OffsetZ { get; set; }
    public float Scale { get; set; }
    public uint VelocityCoordinateId { get; set; }
    public float VelocityX { get; set; }
    public float VelocityY { get; set; }
    public float VelocityZ { get; set; }
    public uint AngVelCoordinateId { get; set; }
    public float AngVelX { get; set; }
    public float AngVelY { get; set; }
    public float AngVelZ { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        var casterUnit = (Unit)caster;

        if (casterUnit == null)
            return;

        Logger.Debug($"SpawnGimmickEffect GimmickId={GimmickId}, scale={Scale}, skill={(castObj as CastSkill)?.SkillId}, " +
            $"caster={caster.ObjId} pos=({caster.Transform.World.Position.X:F1},{caster.Transform.World.Position.Y:F1},{caster.Transform.World.Position.Z:F1}), " +
            $"target={target?.ObjId} pos=({target?.Transform?.World.Position.X:F1},{target?.Transform?.World.Position.Y:F1},{target?.Transform?.World.Position.Z:F1})");

        // Use target as the position source when available (e.g. plot random area targeting),
        // so gimmicks spawn at the targeted position rather than at the caster.
        var positionSource = (target != null && target != caster) ? target : null;
        var spawner = new GimmickSpawner(caster.ParentWorld, this, caster, positionSource);

        if (casterUnit.Gimmick == null)
            return;

        if (casterUnit is { CurrentTarget: Character character })
        {
            casterUnit.Gimmick.CurrentTarget = character;
            return;
        }

        foreach (var character2 in WorldManager.GetAround<Character>(casterUnit))
        {
            casterUnit.Gimmick.CurrentTarget = character2;
            break;
        }
    }
}
