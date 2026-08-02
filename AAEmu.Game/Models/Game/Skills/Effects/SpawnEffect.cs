using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Effects.Enums;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class SpawnEffect : EffectTemplate
{
    public BaseUnitType OwnerTypeId { get; set; }
    public uint SubType { get; set; }
    public uint PosDirId { get; set; }
    public float PosAngle { get; set; }
    public float PosDistance { get; set; }
    public uint OriDirId { get; set; }
    public float OriAngle { get; set; }
    public bool UseSummonerFaction { get; set; }
    public float LifeTime { get; set; }
    public bool DespawnOnCreatorDeath { get; set; }
    public bool UseSummonerAggroTarget { get; set; }
    public MateState MateStateId { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        Logger.Trace($"SpawnEffect: OwnerTypeId={OwnerTypeId}, SubType={SubType}, UseSummonerFaction={UseSummonerFaction}, LifeTime={LifeTime}");

        switch (OwnerTypeId)
        {
            case BaseUnitType.Npc:
                {
                    if (WorldIntegration.ZoneAuthority)
                    {
                        SpawnNpcInZone(caster, target, castObj);
                        break;
                    }

                    var spawner = caster?.ParentWorld.SpawnManager.GetNpcSpawner(SubType, target);
                    if (spawner == null)
                    {
                        Logger.Info($"SpawnEffect: SubType={SubType} not found in spawners.");
                        return;
                    }

                    // dir id 1 = relative to target/spawner.
                    // dir id 2 = relative to caster.
                    var positionRelativeToUnit = PosDirId switch
                    {
                        1 => target,
                        2 => caster,
                        _ => null
                    };
                    var orientationRelativeToUnit = OriDirId switch
                    {
                        1 => target,
                        2 => caster,
                        _ => null
                    };

                    if (positionRelativeToUnit == null || orientationRelativeToUnit == null)
                    {
                        Logger.Warn($"SpawnEffect: Unhandled PosDirId {PosDirId} or OriDirId {OriDirId}");
                        return;
                    }

                    var (xx, yy) = MathUtil.AddDistanceToFrontDeg(PosDistance, positionRelativeToUnit.Transform.World.Position.X, positionRelativeToUnit.Transform.World.Position.Y, PosAngle);

                    spawner.Position.X = xx;
                    spawner.Position.Y = yy;
                    spawner.Position.Z = positionRelativeToUnit.Transform.World.Position.Z;

                    spawner.Position.Yaw = orientationRelativeToUnit.Transform.World.Rotation.Z + OriAngle.DegToRad();

                    spawner.RespawnTime = 0; // don't respawn

                    spawner.DoSpawnEffect(spawner.Id, this, caster, target);
                    break;
                }
            case BaseUnitType.Slave:
                {
                    if (caster is Character player)
                    {
                        // TODO: Implement OriDirId, PosDirId and MateStateId
                        using var transform = player.Transform.CloneDetached();
                        if (PosDistance == 0) { PosDistance = 2; }
                        transform.World.AddDistanceToFront(PosDistance);
                        transform.World.Rotate(transform.World.Rotation with { Z = OriAngle.DegToRad() });

                        var slave = player.ParentWorld.SlaveManager.Create(SubType, true, transform);
                        if (slave is { Template: null })
                        {
                            Logger.Info($"SpawnEffect: SubType={SubType} not found...");
                            return;
                        }
                        player.ForceDismountAndDespawn(slave, 500000); // delete Slave after 8min 20s
                    }
                    break;
                }
            case BaseUnitType.Mate:
                {
                    break;
                }
        }
    }

    private void SpawnNpcInZone(BaseUnit caster, BaseUnit target, CastAction castAction)
    {
        // SubType is an npc_spawners row. Resolve its member through loaded game content rather
        // than treating the spawner id as an NPC template id.
        var member = NpcGameData.Instance.GetNpcSpawnerNpc(SubType);
        if (member == null)
        {
            Logger.Info($"SpawnEffect: SubType={SubType} not found in npc_spawner_npcs.");
            return;
        }

        var positionRelativeToUnit = PosDirId switch
        {
            1 => target,
            2 => caster,
            _ => null
        };
        var orientationRelativeToUnit = OriDirId switch
        {
            1 => target,
            2 => caster,
            _ => null
        };
        if (positionRelativeToUnit?.Transform == null || orientationRelativeToUnit?.Transform == null)
        {
            Logger.Warn($"SpawnEffect: unhandled PosDirId {PosDirId} or OriDirId {OriDirId}.");
            return;
        }

        var world = caster?.ParentWorld;
        var npc = world == null ? null : NpcManager.Instance.Create(world, 0, member.MemberId);
        if (npc == null)
        {
            Logger.Warn($"SpawnEffect: NPC template {member.MemberId} for spawner {SubType} could not be created.");
            return;
        }

        var (x, y) = MathUtil.AddDistanceToFrontDeg(
            PosDistance,
            positionRelativeToUnit.Transform.World.Position.X,
            positionRelativeToUnit.Transform.World.Position.Y,
            PosAngle);
        var yaw = orientationRelativeToUnit.Transform.World.Rotation.Z + OriAngle.DegToRad();

        npc.Transform = positionRelativeToUnit.Transform.CloneDetached(npc);
        npc.Transform.Local.SetPosition(
            x,
            y,
            positionRelativeToUnit.Transform.World.Position.Z,
            0f,
            0f,
            yaw);
        npc.OwnerId = caster switch
        {
            Character character => character.Id,
            Npc creatorNpc => creatorNpc.OwnerId,
            _ => default
        };
        if (UseSummonerFaction && caster is Unit summoner)
            npc.Faction = summoner.Faction;

        npc.IsZoneMirror = true;
        npc.Spawn();
        if (!WorldIntegration.PublishNpcSpawn(
                npc,
                LifeTime,
                DespawnOnCreatorDeath,
                UseSummonerAggroTarget,
                caster))
        {
            WorldIntegration.DeleteNpcMirror(npc, false);
            return;
        }

        if (UseSummonerAggroTarget && (target ?? caster) is Unit aggroTarget)
            WorldIntegration.PublishAggro(npc, aggroTarget, 1, castAction);
    }
}
