using System;
using System.Linq;
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
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Physics.Debug;

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

    // "Naval Lantern" doodad templates (localized name in client data).
    // Ships don't consistently use AttachPointKind.LampFront/LampRear for these, so we also match by template id.
    private static bool IsNavalLanternTemplateId(uint templateId) =>
        templateId is 2612 or 6587;

    /// <summary>Client-visible time-of-day while storm buff is active (in-game hours).</summary>
    public const float StormClientTimeOfDayHours = 2f;

    /// <summary>
    /// If storm rules should override the client's displayed time-of-day, returns the forced hours; otherwise null.
    /// </summary>
    public static float? ResolveClientTimeOfDayHours(Character character)
    {
        var seaWeatherModel = AppConfiguration.Instance.World?.SeaWeatherModel ?? WorldConfig.SeaWeatherModelType.Official;
        if (seaWeatherModel != WorldConfig.SeaWeatherModelType.Realistic)
            return null;

        if (!character.Buffs.CheckBuff(StormBuffId))
            return null;

        return StormClientTimeOfDayHours;
    }

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

        // Harpoon is also a ship-mounted weapon/slot; storm rules shouldn't block it.
        if (HarpoonMechanicsDebug.IsShipHarpoonSkill(skillId))
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
    }

    private readonly Dictionary<uint, StormState> _stateBySlaveObjId = new();

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
                st = new StormState { LampsIgnited = false };
                _stateBySlaveObjId[slave.ObjId] = st;
            }

            if (!st.LampsIgnited)
            {
                IgniteShipLamps(slave);
                st.LampsIgnited = true;
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
            var isLampSlot = doodad.AttachPoint is AttachPointKind.LampFront or AttachPointKind.LampRear;
            if (!isLampSlot && !IsNavalLanternTemplateId(doodad.TemplateId))
                continue;
            TrySwitchDoodadToNonStartPhase(doodad);
        }
    }

    private static void TrySwitchDoodadToNonStartPhase(Doodad doodad)
    {
        if (doodad?.Template?.FuncGroups == null || doodad.Template.FuncGroups.Count == 0)
            return;

        var target = PickStormLitFuncGroupId(doodad);

        if (target > 0 && doodad.FuncGroupId != target)
            doodad.DoChangePhase(null, (int)target);
    }

    /// <summary>
    /// Many ship lamps have multiple <see cref="DoodadFuncGroups.DoodadFuncGroupKind.Normal"/> phases (off/on variants).
    /// Pick the best "lit" candidate instead of blindly taking <c>FirstOrDefault()</c>.
    /// </summary>
    private static uint PickStormLitFuncGroupId(Doodad doodad)
    {
        var groups = doodad.Template.FuncGroups
            .Where(g => g.GroupKindId is DoodadFuncGroups.DoodadFuncGroupKind.Normal or DoodadFuncGroups.DoodadFuncGroupKind.End)
            .ToList();

        if (groups.Count == 0)
            return 0;

        static bool LooksLit(string model) =>
            !string.IsNullOrEmpty(model)
            && model.Contains("_on", StringComparison.OrdinalIgnoreCase);

        static bool LooksUnlit(string model) =>
            !string.IsNullOrEmpty(model)
            && model.Contains("_off", StringComparison.OrdinalIgnoreCase);

        // Prefer explicit "on" prefab/model markers.
        foreach (var g in groups.OrderByDescending(g => g.Id))
        {
            if (LooksLit(g.Model) && !LooksUnlit(g.Model))
                return g.Id;
        }

        // If we can't infer on/off from the model string, fall back to the highest-id normal/end group
        // (often the last authored phase is the "active" visual for toggles).
        return groups.Max(g => g.Id);
    }
}

