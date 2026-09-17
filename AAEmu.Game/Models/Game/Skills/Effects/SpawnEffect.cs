using System.Numerics;
using AAEmu.Game.Core.Managers;
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
    /// <summary>Upper end of the placement band. Equal to the minimum on a row that does not scatter.</summary>
    public float PosAngleMax { get; set; }
    /// <inheritdoc cref="PosAngleMax"/>
    public float PosDistanceMax { get; set; }
    public uint OriDirId { get; set; }
    public float OriAngle { get; set; }
    public bool UseSummonerFaction { get; set; }
    public float LifeTime { get; set; }
    public bool DespawnOnCreatorDeath { get; set; }
    public bool UseSummonerAggroTarget { get; set; }
    public MateState MateStateId { get; set; }
    /// <summary>When true, non-flying summons snap to terrain under the XY (drop-from-rift).</summary>
    public bool EnableRayCast { get; set; } = true;
    /// <summary>Height above the anchor's Z that the ray cast starts from.</summary>
    public float RayOffSet { get; set; }

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

    /// <summary>
    /// The angle this row places a spawn at, drawn from its own band.
    /// </summary>
    /// <remarks>
    /// 237 rows author a band that is not a point (mate effects 3438/3439 are 0-360), and the loader used to
    /// keep only <c>pos_angle_min</c>, so those rows all placed every summon on the same bearing. A row whose
    /// two ends match is exact and unchanged: <see cref="SpawnScatterRules.Scatter"/> returns the minimum.
    /// </remarks>
    internal static float ResolvePosAngle(float angleMin, float angleMax) =>
        SpawnScatterRules.Angle(angleMin, angleMax, Random.Shared.Next(0, 1001));

    /// <summary>
    /// The distance this row places a spawn at, drawn from its own band.
    /// </summary>
    /// <remarks>
    /// 248 rows author a band that is not a point (mate effect 3438 is 1-5, 2266 is 50-100). Feeding the old
    /// non-zero guard with this is what keeps "distance 0 means the row left it unset, use 2" working.
    /// </remarks>
    internal static float ResolvePosDistance(float distanceMin, float distanceMax) =>
        SpawnScatterRules.Distance(distanceMin, distanceMax, Random.Shared.Next(0, 1001));

    /// <summary>
    /// The Z a row's ray cast starts from: the anchor's Z raised by <c>ray_off_set</c>.
    /// </summary>
    /// <remarks>
    /// 548 rows carry a non-zero <c>ray_off_set</c> (50 on 183, 5 on 135, 10 on 74), and it is the last
    /// unread column in the table. See <see cref="SpawnScatterRules.RayCastOriginZ"/> for why it is read as
    /// head-room for the cast. A row with 0 — 2,255 of them — is exactly the anchor's own Z, unchanged.
    /// </remarks>
    internal static float ResolveRayCastOriginZ(float anchorZ, float rayOffSet) =>
        SpawnScatterRules.RayCastOriginZ(anchorZ, rayOffSet);

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

                    var posDistance = ResolvePosDistance(PosDistance, PosDistanceMax);
                    var posAngle = ResolvePosAngle(PosAngle, PosAngleMax);
                    var (xx, yy) = MathUtil.AddDistanceToFrontDeg(posDistance, positionRelativeToUnit.Transform.World.Position.X, positionRelativeToUnit.Transform.World.Position.Y, posAngle);

                    spawner.Position.X = xx;
                    spawner.Position.Y = yy;
                    // The same question the zone-authority branch asks: a model that holds its own
                    // altitude keeps the depth it was placed at, a grounded one is snapped to the
                    // terrain under it. Answering it with a hardcoded false drops a swimmer to the
                    // ocean surface here, and the later off-ground check can only preserve that.
                    // The member this spawner actually creates is the one DoSpawnEffect picks below
                    // (MemberId == spawner.UnitId), so that is the model the Z belongs to.
                    var standaloneCanFly =
                        NpcManager.Instance.GetTemplate(spawner.UnitId) is { } standaloneTemplate &&
                        ModelManager.Instance.IsFlyOrSwim(standaloneTemplate.ModelId);
                    spawner.Position.Z = ResolveSpawnZ(
                        positionRelativeToUnit,
                        xx,
                        yy,
                        ResolveRayCastOriginZ(positionRelativeToUnit.Transform.World.Position.Z, RayOffSet),
                        canFly: standaloneCanFly);

                    spawner.Position.Yaw = orientationRelativeToUnit.Transform.World.Rotation.Z + OriAngle.DegToRad();

                    spawner.RespawnTime = 0; // don't respawn

                    spawner.DoSpawnEffect(spawner.Id, this, caster, target);
                    break;
                }
            case BaseUnitType.Slave:
                {
                    if (caster is Character player)
                    {
                        // Where the summon lands is the row's, the same way it is for an Npc spawn: pos_dir
                        // picks the unit it is placed around, pos_distance/pos_angle (each drawn from its own
                        // band) the offset, and ori_dir/ori_angle the facing. The old code always anchored on
                        // the caster and turned it by OriAngle alone, so a row that authorises pos_dir 1
                        // placed its summon behind the caster instead of at the target.
                        var positionUnit = ResolvePositionUnit(PosDirId, caster, target) ?? player;
                        var orientationUnit = ResolveOrientationUnit(OriDirId, caster, target, positionUnit) ?? player;

                        var posDistance = ResolvePosDistance(PosDistance, PosDistanceMax);
                        if (posDistance == 0)
                            posDistance = 2;

                        using var transform = positionUnit.Transform.CloneDetached();
                        transform.World.AddDistanceToFront(posDistance);
                        transform.World.Rotate(transform.World.Rotation with
                        {
                            Z = orientationUnit.Transform.World.Rotation.Z + OriAngle.DegToRad()
                        });

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
                    // Left alone on purpose. Only 12 rows set owner_type_id 5, every one of them under
                    // sub_type 848 (mate effects 3122/3131 are the 드래곤/탈것 family), and the Mate family
                    // is already spawned by MateManager.AddActiveMateAndSpawn from the owner's saved mate.
                    // There is no server-side entry point that conjures a mate from a template id alone, so
                    // implementing this branch means inventing one and a second ownership path with it. The
                    // row's placement columns are loaded and resolved here either way, so whatever grows that
                    // entry point has them.
                    Logger.Debug("SpawnEffect {0}: Mate spawn (sub_type {1}) has no server-side entry point",
                        Id, SubType);
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
            npc.IsOffGround);

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
