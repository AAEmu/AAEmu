using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncSpawn : DoodadFuncTemplate
{
    public BaseUnitType OwnerTypeId { get; set; }
    public uint SubType { get; set; }
    public uint PosDirId { get; set; }
    public float PosAngleMin { get; set; }
    public float PosAngleMax { get; set; }
    public float PosDistanceMin { get; set; }
    public float PosDistanceMax { get; set; }
    public uint OriDirId { get; set; }
    public float OriAngle { get; set; }
    public bool UseSummonerFaction { get; set; }
    public float LifeTime { get; set; }
    public bool DespawnOnCreatorDeath { get; set; }
    public bool UseSummonerAggroTarget { get; set; }
    public uint MateStateId { get; set; }

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        if (caster == null || owner?.Transform == null || OwnerTypeId != BaseUnitType.Npc)
        {
            Logger.Warn("DoodadFuncSpawn {0}: unsupported owner type {1}", Id, OwnerTypeId);
            return;
        }

        if (!WorldIntegration.ZoneAuthority)
        {
            Logger.Warn("DoodadFuncSpawn {0}: direct NPC handoff requires Zone authority", Id);
            return;
        }

        var world = owner.ParentWorld ?? caster.ParentWorld;
        var npc = world == null ? null : NpcManager.Instance.Create(world, 0, SubType);
        if (npc == null)
        {
            Logger.Warn("DoodadFuncSpawn {0}: NPC template {1} could not be created", Id, SubType);
            return;
        }

        var posAngle = DoodadSpawnPlacement.ResolveExact(PosAngleMin, PosAngleMax);
        var posDistance = DoodadSpawnPlacement.ResolveExact(PosDistanceMin, PosDistanceMax);
        if (posAngle == null || posDistance == null)
        {
            Logger.Warn("DoodadFuncSpawn {0}: unverified placement range angle={1}-{2} distance={3}-{4}",
                Id, PosAngleMin, PosAngleMax, PosDistanceMin, PosDistanceMax);
            WorldIntegration.DeleteNpcMirror(npc, false);
            return;
        }
        var sourcePosition = owner.Transform.World.Position;
        var sourceYaw = owner.Transform.World.Rotation.Z;
        var placement = DoodadSpawnPlacement.Resolve(
            PosDirId, OriDirId, sourcePosition.X, sourcePosition.Y, sourcePosition.Z,
            sourceYaw, posAngle.Value, posDistance.Value, OriAngle);
        if (placement == null)
        {
            Logger.Warn("DoodadFuncSpawn {0}: unsupported posDir={1} oriDir={2}", Id, PosDirId, OriDirId);
            WorldIntegration.DeleteNpcMirror(npc, false);
            return;
        }

        npc.Transform = owner.Transform.CloneDetached(npc);
        npc.Transform.Local.SetPosition(
            placement.Value.X, placement.Value.Y, placement.Value.Z, 0f, 0f, placement.Value.Yaw);
        npc.OwnerId = caster switch
        {
            Character character => character.Id,
            Npc creatorNpc => creatorNpc.OwnerId,
            _ => 0
        };
        if (UseSummonerFaction && caster is Unit summoner)
            npc.Faction = summoner.Faction;

        npc.IsZoneMirror = true;
        npc.Spawn();
        if (!WorldIntegration.PublishNpcSpawn(
                npc, LifeTime, DespawnOnCreatorDeath, UseSummonerAggroTarget, caster))
        {
            WorldIntegration.DeleteNpcMirror(npc, false);
            return;
        }

        owner.ToNextPhase = true;
    }
}

internal readonly record struct DoodadSpawnPosition(float X, float Y, float Z, float Yaw);

internal static class DoodadSpawnPlacement
{
    // Every referenced 10.0.2 r575 row is exact. Do not invent a distribution for unused bands.
    internal static float? ResolveExact(float min, float max) => min == max ? min : null;

    internal static DoodadSpawnPosition? Resolve(
        uint posDirId, uint oriDirId, float sourceX, float sourceY, float sourceZ,
        float sourceYaw, float posAngle, float posDistance, float oriAngle)
    {
        if (posDirId is not (1 or 2))
            return null;

        var bearing = posDirId == 2 ? sourceYaw + posAngle.DegToRad() : posAngle.DegToRad();
        var (x, y) = MathUtil.AddDistanceToFront(posDistance, sourceX, sourceY, bearing);
        var yaw = oriDirId switch
        {
            1 => oriAngle.DegToRad(),
            2 => sourceYaw + oriAngle.DegToRad(),
            3 => MathF.Atan2(y - sourceY, x - sourceX) + oriAngle.DegToRad(),
            _ => float.NaN
        };
        return float.IsNaN(yaw) ? null : new DoodadSpawnPosition(x, y, sourceZ, yaw);
    }
}
