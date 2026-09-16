using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Models.Game.NPChar;
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
        var casterUnit = caster as Unit;

        if (casterUnit == null)
        {
            Logger.Warn("SpawnGimmickEffect {0}: caster {1} is not a Unit", Id, caster?.ObjId ?? 0);
            return;
        }

        if (casterUnit.ParentWorld == null)
        {
            Logger.Warn("SpawnGimmickEffect {0}: unit {1} has no world", Id, casterUnit.ObjId);
            return;
        }

        Logger.Trace($"SpawnGimmickEffect GimmickId={GimmickId}, scale={Scale}, skill={(castObj as CastSkill)?.SkillId}");

        var spawner = new GimmickSpawner(casterUnit.ParentWorld, this, caster);

        // The constructor is what creates the gimmick, adds it to the world and (for an Npc caster) hangs it
        // on the unit itself; publish what it made rather than calling Spawn(0), which would create a second
        // one from UnitId 0 and leave it in the world alongside the real gimmick.
        var gimmick = spawner.Created;
        if (gimmick == null)
        {
            Logger.Info("SpawnGimmickEffect {0}: gimmick template {1} could not be created", Id, GimmickId);
            return;
        }

        if (casterUnit is Npc casterNpc)
            casterNpc.Gimmick = gimmick;

        // The zone drives a gimmick's movement, so announce this one to it the same way a level spawner
        // does (SpawnManager, the Gimmicks loop). Without it the zone never learns the gimmick exists.
        if (WorldIntegration.ZoneAuthority)
            WorldIntegration.RelayGimmickCreatedToZone?.Invoke(
                gimmick.ToZoneWireSpawnData(), (int)gimmick.Transform.ZoneId);

        if (casterUnit.CurrentTarget is Character character)
        {
            gimmick.CurrentTarget = character;
            return;
        }

        foreach (var character2 in WorldManager.GetAround<Character>(casterUnit))
        {
            gimmick.CurrentTarget = character2;
            break;
        }
    }
}
