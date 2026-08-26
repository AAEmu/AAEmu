using System;
using System.Linq;
using System.Numerics;

using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Physics.Debug;
using AAEmu.Game.Physics.Forces;

using Jitter2;
using Jitter2.Dynamics;
using Jitter2.LinearMath;

namespace AAEmu.Game.Weather;

/// <summary>
/// Placeholder for ship-in-storm ("cloud") behavior.
/// Enabled only when <see cref="SeaWeatherModelType.Realistic"/> is selected.
/// </summary>
public sealed class StormShipLogic(World world, Func<WorldInstance> getWorld) : ForceGenerator(world)
{
    // Storm cloud doodad and its clout buff (configured in client data / compact sqlite).
    private const uint StormBuffId = 1917;
    // In ArcheAge data, regular cannons are VehicleModelId 10 with base model id 117.
    // In our `slaves.model_id` / `SlaveTemplate.ModelId` this shows up as the base model id (117).
    private const uint RegularCannonModelId = 117;

    /// <summary>Client-visible time-of-day while storm buff is active (in-game hours).</summary>
    public const float StormClientTimeOfDayHours = 2f;

    /// <summary>
    /// If storm rules should override the client's displayed time-of-day, returns the forced hours; otherwise null.
    /// </summary>
    public static float? ResolveClientTimeOfDayHours(Character character)
    {
        var seaWeatherModel = AppConfiguration.Instance.World?.SeaWeatherModel ?? SeaWeatherModelType.Official;
        if (seaWeatherModel != SeaWeatherModelType.Realistic)
            return null;

        if (!(character?.Buffs.CheckBuff(StormBuffId) ?? false))
            return null;

        return StormClientTimeOfDayHours;
    }

    private static bool IsRegularCannonSlave(Slave slave) =>
        slave?.Template?.ModelId == RegularCannonModelId;

    /// <summary>
    /// Storm skill gate. If blocked, returns true and provides the fail result so the caller can handle packet sending.
    /// </summary>
    public static bool TryBlockMountOrSlaveSkill(
        WorldInstance world,
        uint skillId,
        Slave casterSlave,
        out SkillResult result,
        out uint errorValue)
    {
        result = SkillResult.Success;
        errorValue = 0u;

        var seaWeatherModel = AppConfiguration.Instance.World?.SeaWeatherModel ?? SeaWeatherModelType.Official;
        if (seaWeatherModel != SeaWeatherModelType.Realistic)
            return false;

        // Harpoon is also a ship-mounted weapon/slot; storm rules shouldn't block it.
        if (HarpoonMechanicsDebug.IsShipHarpoonSkill(skillId))
            return false;

        // Do not infer "cannon-ness" from the slot/attach point: future versions can place trade packs
        // in cannon slots (e.g. Merchant Schooner). Prefer checking the slave's model instead.
        if (!IsRegularCannonSlave(casterSlave))
            return false;

        // Cannon skills are cast by the cannon slave; its parent ship is stored as owner obj id.
        if (casterSlave.OwnerType != BaseUnitType.Slave || casterSlave.OwnerObjId == 0)
            return false;

        if (world.GetUnit(casterSlave.OwnerObjId) is not Slave parentShip)
            return false;

        if (!parentShip.Buffs.CheckBuff(StormBuffId))
            return false;

        result = SkillResult.NoPerm;
        errorValue = 0u;
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
            if (slave?.RigidBody is not { } body || body.MotionType == MotionType.Static || !body.IsActive)
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
            if (doodad.AttachPoint is not (AttachPointKind.LampFront or AttachPointKind.LampRear))
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
            .Where(g => g.GroupKindId == DoodadFuncGroups.DoodadFuncGroupKind.Normal)
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

        // If we can't infer on/off from the model string, fall back to the highest-id normal group
        // (often the last authored phase is the "active" visual for toggles).
        return groups.Max(g => g.Id);
    }
}

