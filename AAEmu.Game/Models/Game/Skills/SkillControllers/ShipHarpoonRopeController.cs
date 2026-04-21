#nullable enable

using System;
using System.Collections.Concurrent;
using System.Numerics;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Physics;
using AAEmu.Game.Physics.Debug;
using NLog;

namespace AAEmu.Game.Models.Game.Skills.SkillControllers;

/// <summary>Server-side harpoon rope lifecycle (Launch 13749, Cut 13750, CSSkillControllerState).</summary>
public static class ShipHarpoonRopeController
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Rope tear threshold (meters) based on steady tension: tear when \(stretch = chord - paid\) reaches this value.
    /// Lower = easier to tear under sustained pull.
    /// </summary>
    private const float TearStretchMeters = 3f;

    /// <summary>
    /// Rope tear threshold (m/s) based on sudden tension spike: tear when \(jerk = Δstretch / dt\) reaches this value.
    /// Lower = easier to tear on quick yanks.
    /// </summary>
    private const float TearJerkMetersPerSec = 9f;

    /// <summary>
    /// Minimum stretch (meters) required to allow jerk-based tearing. Prevents tearing from noise while the rope is mostly slack.
    /// Lower = jerk-tear can trigger earlier (even with little tension).
    /// </summary>
    private const float TearJerkMinStretchMeters = 1f;

    /// <summary>
    /// Recoil scale: delta-V added to the shooter's hull (m/s) per meter of stretch at the moment of tear.
    /// Higher = stronger kickback on tear.
    /// </summary>
    private const float RecoilDvPerStretch = 0.12f;

    /// <summary>
    /// Recoil scale: delta-V added to the shooter's hull (m/s) per (m/s) of jerk above <see cref="TearJerkMetersPerSec"/>.
    /// Higher = extra kickback for very sharp yanks.
    /// </summary>
    private const float RecoilDvPerJerk = 0.02f;

    /// <summary>
    /// Maximum recoil delta-V applied to the shooter's hull (m/s) when the rope tears.
    /// </summary>
    private const float RecoilMaxDeltaV = 1.6f;

    private struct RopeTensionHistory
    {
        public float Stretch;
    }

    private static readonly ConcurrentDictionary<uint, RopeTensionHistory> _tensionHistoryByHarpoonObjId = new();

    public static void OnLaunchSucceeded(Slave harpoonSlave, SkillCastTarget target, Character? operatorChar)
    {
        if (!TryResolveHookFromSkillTarget(target, harpoonSlave, out var hookWorld, out var hookBasisObjId, out var hookLocal))
            return;

        if (harpoonSlave.HarpoonRope.IsEngaged)
            BreakRopeForClients(harpoonSlave, cutouted: false);
        else
            harpoonSlave.HarpoonRope.Clear();

        var launchTemplate = SkillManager.Instance.GetSkillTemplate(HarpoonMechanicsDebug.ShipLaunchHarpoonSkillId);
        var maxRange = launchTemplate != null ? Math.Max(0f, launchTemplate.MaxRange) : 0f;

        var origin = harpoonSlave.Transform.World.Position;
        var initialLen = Vector3.Distance(origin, hookWorld);

        harpoonSlave.HarpoonRope.IsEngaged = true;
        harpoonSlave.HarpoonRope.HookWorld = hookWorld;
        harpoonSlave.HarpoonRope.HookBasisObjId = hookBasisObjId;
        harpoonSlave.HarpoonRope.HookLocalInBasis = hookLocal;
        harpoonSlave.HarpoonRope.RopeLength = initialLen;
        harpoonSlave.HarpoonRope.MaxLaunchRange = maxRange;
        harpoonSlave.HarpoonRope.LastTeared = false;
        harpoonSlave.HarpoonRope.LastCutout = false;
        var pw = harpoonSlave.ParentWorld;
        // Deck / hull hits often fail IsWater() like dry land; that must not enable terrain tow when the basis is another boat
        // (otherwise only the shooter hull is pulled — same as an anchor). Ship–ship uses HookBasisObjId + pair impulses only.
        var hookBasisIsOtherBoat = hookBasisObjId != 0 && pw != null
            && pw.GetBaseUnit(hookBasisObjId) is Slave basisForHook
            && basisForHook.Template.IsABoat();
        harpoonSlave.HarpoonRope.HookAttachedToTerrain = pw != null && !pw.IsWater(hookWorld) && !hookBasisIsOtherBoat;
        harpoonSlave.HarpoonRope.ControllerExpireAtUtc = ResolveRopeControllerExpireUtc(launchTemplate);

        if (HarpoonMechanicsDebug.EnableVerboseHarpoonMechanicsLogging)
            Log.Debug("Harpoon rope engaged: slaveObjId={0} hook=({1:F1},{2:F1},{3:F1}) initialLen={4:F2} maxRange={5:F1} terrainHook={6} controllerExpireUtc={7}",
                harpoonSlave.ObjId, hookWorld.X, hookWorld.Y, hookWorld.Z, initialLen, maxRange, harpoonSlave.HarpoonRope.HookAttachedToTerrain,
                harpoonSlave.HarpoonRope.ControllerExpireAtUtc?.ToString("u") ?? "(none)");

        BroadcastSkillControllerRopeState(harpoonSlave, initialLen, teared: false, cutouted: false, except: operatorChar);
    }

    public static void OnCutRope(Slave harpoonSlave, Character? operatorChar)
    {
        BreakRopeForClients(harpoonSlave, cutouted: true);
    }

    public static void TryApplySkillControllerState(Character character, uint objId, float len, bool teared, bool cutouted)
    {
        if (character?.ParentWorld == null)
            return;

        if (character.ParentWorld.GetUnit(objId) is not Slave slave)
            return;

        if (!IsCharacterAttachedToSlave(character, slave))
            return;

        if (!slave.HarpoonRope.IsEngaged)
            return;

        var clampedLen = ClampClientReportedRopeLength(slave, len);
        slave.HarpoonRope.RopeLength = clampedLen;
        // Client-reported "teared" must not drive server break — server computes tear from tension/jerk.
        slave.HarpoonRope.LastTeared = false;
        slave.HarpoonRope.LastCutout = cutouted;

        if (TryBreakRopeIfHookOutOfRange(slave))
            return;

        if (cutouted)
        {
            BreakRopeForClients(slave, cutouted);
            return;
        }

        BroadcastSkillControllerRopeState(slave, clampedLen, teared: false, cutouted: false, except: character);
    }

    /// <summary>When the operator leaves this slave seat (harpoon station), drop the line per game design.</summary>
    public static void OnOperatorLeftSlave(Slave slave, Character? leavingOperator)
    {
        BreakRopeForClients(slave, cutouted: false);
    }

    /// <summary>
    /// Per physics tick on the parent hull: if a child harpoon stayed <see cref="ShipHarpoonRopeState.IsEngaged"/> past
    /// <see cref="ShipHarpoonRopeState.ControllerExpireAtUtc"/>, break server-side so tow does not continue after the client drops the rope.
    /// Walks the whole <see cref="Slave.AttachedSlaves"/> tree (harpoon may be nested under another mount).
    /// </summary>
    public static void TickHarpoonRopeControllerLifetime(Slave hull)
    {
        TryExpireHarpoonRopeInSubtree(hull, DateTime.UtcNow);
    }

    private static void TryExpireHarpoonRopeInSubtree(Slave node, DateTime now)
    {
        var st = node.HarpoonRope;
        if (st.IsEngaged && st.ControllerExpireAtUtc is { } until && now >= until)
        {
            if (HarpoonMechanicsDebug.EnableVerboseHarpoonMechanicsLogging)
                Log.Debug("Harpoon rope auto-break (skill_controllers Rope lifetime): slaveObjId={0}", node.ObjId);
            BreakRopeForClients(node, cutouted: false);
        }

        foreach (var child in node.AttachedSlaves)
            TryExpireHarpoonRopeInSubtree(child, now);
    }

    /// <summary>Matches <c>SkillControllerKind.Rope</c> (5) in compact DB; value1/value2 are ms in ship harpoon rows.</summary>
    private const uint SkillControllerKindRope = 5;

    /// <summary>
    /// Uses the minimum positive of <c>value1</c> and <c>value2</c> from the Launch skill's skill_controller row
    /// (e.g. 3961 → 100000 vs 180000 → 100 s) so the server clears before or with the first client timer.
    /// </summary>
    private static DateTime? ResolveRopeControllerExpireUtc(SkillTemplate? launchSkill)
    {
        if (launchSkill == null || launchSkill.SkillControllerId == 0)
            return null;
        var scId = launchSkill.SkillControllerId;
        if (SkillManager.Instance.GetEffectTemplate(scId, "SkillController") is not SkillControllerTemplate sc)
            return null;
        if (sc.KindId != SkillControllerKindRope)
            return null;

        var ms = int.MaxValue;
        if (sc.Value[0] > 0)
            ms = Math.Min(ms, sc.Value[0]);
        if (sc.Value[1] > 0)
            ms = Math.Min(ms, sc.Value[1]);
        if (ms == int.MaxValue)
            return null;

        return DateTime.UtcNow.AddMilliseconds(ms);
    }

    /// <summary>Clears server rope state and mirrors break to clients (skill controller UI). Sends only via the harpoon slave's broadcast (neighborhood).</summary>
    public static void BreakRopeForClients(Slave slave, bool cutouted)
    {
        if (slave is null || !slave.HarpoonRope.IsEngaged)
            return;

        var len = slave.HarpoonRope.RopeLength;
        var objId = slave.ObjId;
        slave.HarpoonRope.Clear();

        var pkt = new SCSkillControllerStatePacket(objId, 0, len, teared: true, cutouted);
        slave.BroadcastPacket(pkt, false);

        if (HarpoonMechanicsDebug.EnableVerboseHarpoonMechanicsLogging)
            Log.Debug("Harpoon rope server break + SCSkillControllerState: slaveObjId={0} len={1:F2} cutouted={2}",
                objId, len, cutouted);
    }

    /// <summary>
    /// Per physics tick on a ship hull: breaks engaged harpoon ropes when tension exceeds threshold or stretch spikes (jerk).
    /// Returns a recoil delta-V to apply to the shooter hull (horizontal plane, world X/Y).
    /// </summary>
    public static Vector3 TickTensionTearAndGetHullRecoilDeltaV(Slave hull, float dtSec)
    {
        if (hull.AttachedSlaves.Count == 0 || dtSec <= 0f)
            return default;

        var recoilSum = Vector3.Zero;
        foreach (var child in ShipHarpoonTowPhysics.EnumerateAttachedSlaveDescendants(hull))
        {
            var st = child.HarpoonRope;
            if (!st.IsEngaged)
            {
                _tensionHistoryByHarpoonObjId.TryRemove(child.ObjId, out _);
                continue;
            }

            var cannonPos = child.Transform.World.Position;
            var hook = GetHookWorldPosition(child);
            var dist = Vector3.Distance(cannonPos, hook);
            var paid = st.RopeLength + ShipHarpoonTowPhysics.ServerRopePaidLengthAdditiveMeters;
            var stretch = dist - paid;
            if (stretch <= 0f)
            {
                _tensionHistoryByHarpoonObjId[child.ObjId] = new RopeTensionHistory { Stretch = 0f };
                continue;
            }

            var hadHistory = _tensionHistoryByHarpoonObjId.TryGetValue(child.ObjId, out var hist);
            var jerk = hadHistory ? (stretch - hist.Stretch) / dtSec : 0f;
            _tensionHistoryByHarpoonObjId[child.ObjId] = new RopeTensionHistory { Stretch = stretch };

            var tearByStretch = stretch >= TearStretchMeters;
            var tearByJerk = jerk >= TearJerkMetersPerSec && stretch >= TearJerkMinStretchMeters;
            if (!tearByStretch && !tearByJerk)
                continue;

            if (HarpoonMechanicsDebug.EnableVerboseHarpoonMechanicsLogging)
                Log.Debug("Harpoon rope auto-tear (tension/jerk): hullObjId={0} harpoonObjId={1} dist={2:F2} paid={3:F2} stretch={4:F2} jerk={5:F2}",
                    hull.ObjId, child.ObjId, dist, paid, stretch, jerk);

            // Break first (clears IsEngaged and broadcasts SCSkillControllerState teared=true).
            BreakRopeForClients(child, cutouted: false);

            // Recoil: opposite the tension direction, horizontal (world X/Y) only.
            var dx = hook.X - cannonPos.X;
            var dy = hook.Y - cannonPos.Y;
            var d2 = dx * dx + dy * dy;
            if (d2 < 1e-6f)
                continue;

            var invLen = 1f / MathF.Sqrt(d2);
            var ux = dx * invLen;
            var uy = dy * invLen;
            var jerkExcess = MathF.Max(0f, jerk - TearJerkMetersPerSec);
            var dvMag = MathF.Min(RecoilMaxDeltaV, stretch * RecoilDvPerStretch + jerkExcess * RecoilDvPerJerk);
            recoilSum.X += -ux * dvMag;
            recoilSum.Y += -uy * dvMag;
        }

        return recoilSum;
    }

    /// <summary>
    /// Syncs rope / skill-controller visuals to characters near the harpoon slave (same AOI as <see cref="GameObject.BroadcastPacket"/>).
    /// Uses an explicit <c>GetAround</c> loop instead of <c>BroadcastPacket</c> so <paramref name="except"/> (operator) is skipped —
    /// their client already applied state from CS and must not receive a duplicate SC.
    /// Uses the same <paramref name="len"/> as server state — do not inflate vs chord (that skewed third-party slack vs operator).
    /// </summary>
    private static void BroadcastSkillControllerRopeState(Slave harpoonSlave, float len, bool teared, bool cutouted, Character? except = null)
    {
        if (harpoonSlave.ParentWorld == null)
            return;

        var pkt = new SCSkillControllerStatePacket(harpoonSlave.ObjId, 0, len, teared, cutouted);
        foreach (var chr in WorldManager.GetAround<Character>(harpoonSlave))
        {
            if (except != null && chr.ObjId == except.ObjId)
                continue;
            chr.SendPacket(pkt);
        }
    }

    /// <summary>Current world hook; recomputes when the hit uses a moving basis unit (<see cref="ShipHarpoonRopeState.HookBasisObjId"/>).</summary>
    public static Vector3 GetHookWorldPosition(Slave harpoonSlave)
    {
        var st = harpoonSlave.HarpoonRope;
        if (!st.IsEngaged)
            return default;
        if (st.HookBasisObjId == 0)
            return st.HookWorld;

        var worldInst = harpoonSlave.ParentWorld ?? WorldManager.Instance.GetWorld(harpoonSlave.Transform.InstanceId);
        if (worldInst?.GetBaseUnit(st.HookBasisObjId) is not BaseUnit basis)
            return st.HookWorld;

        var basisRot = basis.Transform.World.ToQuaternion();
        var basisScale = basis.Scale;
        return Vector3.Transform(st.HookLocalInBasis * basisScale, basisRot) + basis.Transform.World.Position;
    }

    /// <summary>
    /// <see cref="AAEmu.Game.Core.Packets.C2G.CSSkillControllerStatePacket"/> supplies <paramref name="len"/> from the client; clamp before physics
    /// taut/slack so spoofed values cannot force or suppress tow (see PR review / Greptile P1).
    /// </summary>
    private static float ClampClientReportedRopeLength(Slave slave, float len)
    {
        if (!float.IsFinite(len))
            return slave.HarpoonRope.RopeLength;

        len = MathF.Max(0f, len);
        var additive = ShipHarpoonTowPhysics.ServerRopePaidLengthAdditiveMeters;
        if (slave.HarpoonRope.MaxLaunchRange > 0f)
            return MathF.Min(len, slave.HarpoonRope.MaxLaunchRange + additive);

        var chord = Vector3.Distance(slave.Transform.World.Position, GetHookWorldPosition(slave));
        var generousCap = chord + additive + ShipHarpoonTowPhysics.SlackMarginMeters + 40f;
        return MathF.Min(len, generousCap);
    }

    private static bool TryBreakRopeIfHookOutOfRange(Slave slave)
    {
        if (!slave.HarpoonRope.IsEngaged || slave.HarpoonRope.MaxLaunchRange <= 0f)
            return false;

        var dist = Vector3.Distance(slave.Transform.World.Position, GetHookWorldPosition(slave));
        const float margin = 1.5f;
        if (dist <= slave.HarpoonRope.MaxLaunchRange + margin)
            return false;

        var maxSaved = slave.HarpoonRope.MaxLaunchRange;
        if (HarpoonMechanicsDebug.EnableVerboseHarpoonMechanicsLogging)
            Log.Debug("Harpoon rope auto-break (hook beyond range): slaveObjId={0} dist={1:F2} max={2:F2}",
                slave.ObjId, dist, maxSaved);
        BreakRopeForClients(slave, cutouted: false);
        return true;
    }

    private static bool IsCharacterAttachedToSlave(Character character, Slave slave)
    {
        foreach (var c in slave.AttachedCharacters.Values)
        {
            if (c != null && c.ObjId == character.ObjId)
                return true;
        }

        return false;
    }

    /// <summary>Same basis-frame rules as <see cref="Skill"/> <c>SetInitialTarget</c> for position casts (hull hit = <c>ObjId1</c> + local).</summary>
    private static bool TryResolveHookFromSkillTarget(SkillCastTarget target, BaseUnit caster, out Vector3 hookWorld, out uint hookBasisObjId, out Vector3 hookLocal)
    {
        hookBasisObjId = 0;
        hookLocal = default;
        hookWorld = default;
        switch (target)
        {
            case SkillCastPositionTarget p:
                if (p.ObjId1 != 0)
                {
                    var worldInst = caster.ParentWorld ?? WorldManager.Instance.GetWorld(caster.Transform.InstanceId);
                    if (worldInst?.GetBaseUnit(p.ObjId1) is BaseUnit basisUnit)
                    {
                        hookBasisObjId = p.ObjId1;
                        hookLocal = new Vector3(p.PosX, p.PosY, p.PosZ);
                        var basisRot = basisUnit.Transform.World.ToQuaternion();
                        hookWorld = Vector3.Transform(hookLocal * basisUnit.Scale, basisRot) + basisUnit.Transform.World.Position;
                        return true;
                    }

                    return false;
                }

                hookWorld = new Vector3(p.PosX, p.PosY, p.PosZ);
                return true;
            case SkillCastPosition2Target p2:
                hookWorld = new Vector3(p2.PosX, p2.PosY, p2.PosZ);
                return true;
            case SkillCastPosition3Target p3:
                hookWorld = new Vector3(p3.PosX, p3.PosY, p3.PosZ);
                return true;
            default:
                return false;
        }
    }
}
