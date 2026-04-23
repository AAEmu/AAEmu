using System.Numerics;

using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

using Jitter2;
using Jitter2.Dynamics;
using Jitter2.LinearMath;

namespace AAEmu.Game.Physics.Forces;

/// <summary>
/// Placeholder for ship-in-storm ("cloud") behavior.
/// Enabled only when <see cref="WorldConfig.SeaWeatherModelType.Realistic"/> is selected.
/// </summary>
public sealed class StormShipLogic(World world, Func<WorldInstance> getWorld) : ForceGenerator(world)
{
    // Storm cloud doodad and its clout buff (configured in client data / compact sqlite).
    private const uint StormDoodadTemplateId = 3085;
    private const uint StormBuffId = 1917;

    private static bool IsCannonAttachPoint(AttachPointKind ap) =>
        ap is >= AttachPointKind.Cannon0 and <= AttachPointKind.Cannon8
            or >= AttachPointKind.Cannon9 and <= AttachPointKind.Cannon19;

    private static bool IsCannonAttachPointId(sbyte apId) =>
        apId is >= (sbyte)AttachPointKind.Cannon0 and <= (sbyte)AttachPointKind.Cannon8
            or >= (sbyte)AttachPointKind.Cannon9 and <= (sbyte)AttachPointKind.Cannon19;

    /// <summary>
    /// Storm skill gate + response sender. If blocked, sends a <see cref="SCSkillStartedPacket"/> with the error result
    /// and returns true so the caller can exit early.
    /// </summary>
    public static bool TryBlockMountOrSlaveSkillAndSend(
        GameConnection connection,
        WorldInstance world,
        uint skillId,
        SkillCaster skillCaster,
        SkillCastTarget skillCastTarget,
        Skill skill,
        SkillObject skillObject,
        Models.Game.Char.Character operatorCharacter,
        Slave casterSlave)
    {
        var seaWeatherModel = AppConfiguration.Instance.World?.SeaWeatherModel ?? WorldConfig.SeaWeatherModelType.Official;
        if (seaWeatherModel != WorldConfig.SeaWeatherModelType.Realistic)
            return false;

        // Cannon skills are typically cast by the cannon slave; prefer its own attach-point id,
        // and only use operator attach-point as a fallback.
        if (!IsCannonAttachPointId(casterSlave.AttachPointId))
        {
            if (operatorCharacter?.AttachedPoint is not { } ap || !IsCannonAttachPoint(ap))
                return false;
        }

        // Cannon skills are cast by the cannon slave; its parent ship is stored as owner obj id.
        if (casterSlave.OwnerType != BaseUnitType.Slave || casterSlave.OwnerObjId == 0)
            return false;

        if (world.GetUnit(casterSlave.OwnerObjId) is not Slave parentShip)
            return false;

        if (!parentShip.Buffs.CheckBuff(StormBuffId))
            return false;

        var res = SkillResult.NoPerm;
        const uint err = 0u;

        var scSkillStartedPacket = new SCSkillStartedPacket(skillId, 0, skillCaster, skillCastTarget, skill, skillObject)
        {
            RealCastTimeDiv10 = 0,
            BaseCastTimeDiv10 = 0
        };
        scSkillStartedPacket.SetSkillResult(res);
        scSkillStartedPacket.SetResultUInt(err);
        connection.SendPacket(scSkillStartedPacket);
        // Otherwise the skill press can feel like "nothing happens" client-side.
        operatorCharacter?.SendErrorMessage(ErrorMessageType.NotReady, 0, true);
        return true;
    }

    private sealed class StormState
    {
        public bool LampsIgnited;
        public DateTime NextSailDropUtc;
    }

    private readonly Dictionary<uint, StormState> _stateBySlaveObjId = new();

    private static TimeSpan SailDropInterval => GetSailDropInterval();
    private static TimeSpan GetSailDropInterval() => TimeSpan.FromSeconds(10);

    public override void PreStep(float timeStep)
    {
        var gameWorld = getWorld();
        if (gameWorld == null)
            return;

        var now = DateTime.UtcNow;

        // Cleanup: remove states for ships that no longer exist or no longer have the buff.
        if (_stateBySlaveObjId.Count > 0)
        {
            foreach (var key in _stateBySlaveObjId.Keys.ToList())
            {
                var unit = gameWorld.GetUnit(key);
                if (unit is not Slave s || !s.Buffs.CheckBuff(StormBuffId))
                    _stateBySlaveObjId.Remove(key);
            }
        }

        // Process ships currently in storm.
        foreach (var slave in gameWorld.GetAllSlaves())
        {
            if (slave?.RigidBody is not { } body || body.IsStatic || !body.IsActive)
                continue;

            if (!slave.Buffs.CheckBuff(StormBuffId))
                continue;

            if (!_stateBySlaveObjId.TryGetValue(slave.ObjId, out var st))
            {
                st = new StormState { LampsIgnited = false, NextSailDropUtc = now + SailDropInterval };
                _stateBySlaveObjId[slave.ObjId] = st;
            }

            if (!st.LampsIgnited)
            {
                IgniteShipLamps(slave);
                st.LampsIgnited = true;
            }

            if (now >= st.NextSailDropUtc)
            {
                DropRandomSail(slave);
                st.NextSailDropUtc = now + SailDropInterval;
            }

            // Blocking cannon fire is handled in CSStartSkillPacket (skill start gate) while storm buff is active.
            _ = body; // keep local for future physics effects
        }
    }

    private static void IgniteShipLamps(Slave ship)
    {
        foreach (var doodad in ship.AttachedDoodads)
        {
            if (doodad == null)
                continue;
            if (doodad.AttachPoint is not (AttachPointKind.LampFront or AttachPointKind.LampRear))
                continue;
            TrySwitchDoodadToNonStartPhase(doodad);
        }
    }

    private static void DropRandomSail(Slave ship)
    {
        var sails = ship.AttachedDoodads
            .Where(d => d is { AttachPoint: AttachPointKind.Sail0 or AttachPointKind.Sail1 or AttachPointKind.Sail2 })
            .ToList();

        if (sails.Count == 0)
            return;

        var sail = sails[Random.Shared.Next(sails.Count)];
        if (sail != null)
            TrySwitchDoodadToEndOrNormalPhase(sail);
    }

    private static void TrySwitchDoodadToNonStartPhase(Doodad doodad)
    {
        if (doodad?.Template?.FuncGroups == null || doodad.Template.FuncGroups.Count == 0)
            return;

        var target = doodad.Template.FuncGroups
            .Where(g => g.GroupKindId is DoodadFuncGroups.DoodadFuncGroupKind.Normal or DoodadFuncGroups.DoodadFuncGroupKind.End)
            .Select(g => g.Id)
            .FirstOrDefault();

        if (target > 0 && doodad.FuncGroupId != target)
            doodad.DoChangePhase(null, (int)target);
    }

    private static void TrySwitchDoodadToEndOrNormalPhase(Doodad doodad)
    {
        if (doodad?.Template?.FuncGroups == null || doodad.Template.FuncGroups.Count == 0)
            return;

        var target = doodad.Template.FuncGroups
            .Where(g => g.GroupKindId == DoodadFuncGroups.DoodadFuncGroupKind.End)
            .Select(g => g.Id)
            .FirstOrDefault();

        if (target == 0)
        {
            target = doodad.Template.FuncGroups
                .Where(g => g.GroupKindId == DoodadFuncGroups.DoodadFuncGroupKind.Normal)
                .Select(g => g.Id)
                .FirstOrDefault();
        }

        if (target > 0 && doodad.FuncGroupId != target)
            doodad.DoChangePhase(null, (int)target);
    }
}

