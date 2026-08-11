using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
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
    /// <summary>When true, non-flying summons snap to terrain under the XY (drop-from-rift).</summary>
    public bool EnableRayCast { get; set; } = true;

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
                    spawner.Position.Z = ResolveSpawnZ(
                        positionRelativeToUnit,
                        xx,
                        yy,
                        positionRelativeToUnit.Transform.World.Position.Z,
                        canFly: false);

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
        // Crimson / tower stage plots store an Npc template id in SubType (e.g. 8834 궁수,
        // 8826 보병). Those ids are not always npc_spawners rows; when both exist (8826), the
        // spawner row is a different mob. Prefer a real Npc template, then fall back to spawner
        // member lookup (legacy World-local SpawnEffect path).
        var templateId = ResolveZoneSpawnNpcTemplateId();
        if (templateId == 0)
        {
            Logger.Info($"SpawnEffect: SubType={SubType} is neither an Npc template nor npc_spawners member.");
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
        var npc = world == null ? null : NpcManager.Instance.Create(world, 0, templateId);
        if (npc == null)
        {
            Logger.Warn($"SpawnEffect: NPC template {templateId} (SubType={SubType}) could not be created.");
            return;
        }

        var (x, y) = MathUtil.AddDistanceToFrontDeg(
            PosDistance,
            positionRelativeToUnit.Transform.World.Position.X,
            positionRelativeToUnit.Transform.World.Position.Y,
            PosAngle);
        var yaw = orientationRelativeToUnit.Transform.World.Rotation.Z + OriAngle.DegToRad();
        // Portal/rift casters sit high for the client ball drop. SpawnEffect rows for ground
        // army (enable_ray_cast) must land on terrain — otherwise units freeze at air Z.
        var z = ResolveSpawnZ(
            positionRelativeToUnit,
            x,
            y,
            positionRelativeToUnit.Transform.World.Position.Z,
            npc.CanFly);

        npc.Transform = positionRelativeToUnit.Transform.CloneDetached(npc);
        npc.Transform.Local.SetPosition(x, y, z, 0f, 0f, yaw);
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

    /// <summary>
    /// Floor Z for ground army (Crimson balls land then emerge). Aerial Z kept for fliers.
    /// </summary>
    private float ResolveSpawnZ(BaseUnit anchor, float x, float y, float rawZ, bool canFly)
    {
        if (canFly)
            return rawZ;

        // Retail enable_ray_cast: snap non-flyers unless disabled for this effect row.
        // Default true when the column was not loaded (older caches).
        if (!EnableRayCast && rawZ > 0f)
        {
            // Still drop when the position unit is a flying portal / synthetic anchor above terrain.
            var anchorNpc = anchor as Npc;
            if (anchorNpc is not { CanFly: true } && anchor?.ObjId != uint.MaxValue)
                return rawZ;
        }

        var zoneId = anchor?.Transform?.ZoneId ?? 0;
        if (zoneId == 0)
            return rawZ;

        var ground = WorldManager.Instance.GetReferenceHeight(null, x, y, rawZ, zoneId);
        if (ground <= 0f)
            ground = WorldManager.Instance.GetHeight(zoneId, x, y, rawZ);

        if (ground <= 0f)
            return rawZ;

        // Only correct obvious air spawns (rift is typically +40–60 m).
        if (rawZ > ground + 3f)
        {
            Logger.Debug(
                "SpawnEffect terrain snap SubType={0} rawZ={1:F1} → ground={2:F1} @({3:F1},{4:F1})",
                SubType, rawZ, ground, x, y);
            return ground;
        }

        return rawZ;
    }

    /// <summary>
    /// Npc template for ZoneAuthority SpawnEffect: template id first, then spawner-member id.
    /// </summary>
    private uint ResolveZoneSpawnNpcTemplateId()
    {
        if (SubType != 0 && NpcManager.Instance.GetTemplate(SubType) != null)
            return SubType;

        var member = NpcGameData.Instance.GetNpcSpawnerNpc(SubType);
        return member?.MemberId ?? 0;
    }
}
