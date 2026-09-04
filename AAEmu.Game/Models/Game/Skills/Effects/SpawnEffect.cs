using System.Numerics;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Effects.Enums;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
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

    /// <summary>
    /// spawn_effects.pos_dir: 1 = target, 2 = caster. 0/3 have no separate unit — use target then caster.
    /// </summary>
    public static BaseUnit ResolvePositionUnit(uint posDirId, BaseUnit caster, BaseUnit target) =>
        posDirId switch
        {
            1 => target,
            2 => caster,
            0 or 3 => target ?? caster,
            _ => null
        };

    /// <summary>
    /// spawn_effects.ori_dir: 1 = target, 2 = caster. 0/3 = plot facing (keep the position unit).
    /// Lusca army rows use ori_dir 3 with pos_dir 1 and zero offset.
    /// </summary>
    public static BaseUnit ResolveOrientationUnit(
        uint oriDirId, BaseUnit caster, BaseUnit target, BaseUnit positionUnit) =>
        oriDirId switch
        {
            1 => target,
            2 => caster,
            0 or 3 => positionUnit,
            _ => null
        };

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

                    var positionRelativeToUnit = ResolvePositionUnit(PosDirId, caster, target);
                    var orientationRelativeToUnit = ResolveOrientationUnit(
                        OriDirId, caster, target, positionRelativeToUnit);

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

        var positionRelativeToUnit = ResolvePositionUnit(PosDirId, caster, target);
        var orientationRelativeToUnit = ResolveOrientationUnit(
            OriDirId, caster, target, positionRelativeToUnit);
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

        Logger.Info(
            "SpawnEffect npc={0} world=({1:F1},{2:F1},{3:F1}) posDir={4} oriDir={5}",
            templateId, x, y, z, PosDirId, OriDirId);
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
    /// Delegates to <see cref="TerrainFloor"/> — heightmap sample + snap caps, never GeoData.
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

        var world = anchor?.ParentWorld;
        var ground = TerrainFloor.SampleHeightmap(world, x, y);
        if (ground <= 0f)
            ground = TerrainFloor.SampleHeightmap(zoneId, x, y);

        var probe = new Vector3(x, y, rawZ);
        var overWater = TerrainFloor.TryWaterSurface(world, probe, out var waterZ);

        return TerrainFloor.ChooseUnitFloorZ(rawZ, ground, overWater, waterZ, SubType);
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
