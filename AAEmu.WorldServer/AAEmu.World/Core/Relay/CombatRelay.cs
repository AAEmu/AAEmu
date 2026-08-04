using AAEmu.Commons.Network;
using AAEmu.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.World.Core.Packets.Zw;

using NLog;

namespace AAEmu.World.Core.Relay;

/// <summary>
/// Translate zone combat/skill ZW into SC for clients.
/// Zone owns cast/damage/AI; World only relays.
/// </summary>
public class CombatRelay
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private static readonly HashSet<ushort> CombatOpcodes =
    [
        0x0009, // ZWSkillControllerState
        0x000A, // ZWCreateBuff
        0x000B, // ZWRemoveBuff
        0x000C, // ZWClearCombat
        0x000D, // ZWMakeAggroTargetHostile
        0x0018, // ZWStartSkill
        0x0019, // ZWStopSkill
        0x001A, // ZWStopCasting
        0x002D, // ZWResponseCombatUnits
        0x0031, // ZWKillNpc
        ZwOpcodes.AiAggro,
        0x0041, // ZWAggroRemove
    ];

    public bool IsCombatOpcode(ushort opcode) => CombatOpcodes.Contains(opcode);

    public void OnZwOpcode(ushort opcode, byte[] body, int bodyLen)
    {
        if (Environment.GetEnvironmentVariable("AAEMU_DISABLE_ZONE_COMBAT_RELAY") == "1")
            return;

        if (!IsCombatOpcode(opcode))
        {
            Logger.Debug("ZW opcode=0x{0:X4} len={1}", opcode, bodyLen);
            return;
        }

        try
        {
            // Opt-out: AAEMU_ZW_SC_RELAY=0. Full flood: =1.
            // Default ON for NPC casts + buffs + aggro (2026-07-20: gated 0x18 left mobs chase
            // without attack SC; gated 0x0A dropped self/zone buffs; World stub HP never healed on leash).
            var fullRelay = Environment.GetEnvironmentVariable("AAEMU_ZW_SC_RELAY") == "1";
            var npcCastOff = Environment.GetEnvironmentVariable("AAEMU_ZW_SC_RELAY_NPC") == "0";
            var safeDefault = Environment.GetEnvironmentVariable("AAEMU_ZW_SC_RELAY") != "0";

            var shouldRelay = opcode switch
            {
                0x0018 or 0x0019 or 0x001A or 0x0009 => safeDefault && !npcCastOff,
                0x000A or 0x000B or 0x000D or 0x0031 or 0x0034 or 0x0041 or 0x000C or 0x002D => safeDefault,
                _ => fullRelay
            };

            if (!shouldRelay)
            {
                if (opcode == 0x000B && TryRelay(opcode, body ?? [], relayToClients: false))
                    Logger.Info("Combat ZW 0x{0:X4} len={1} → lifecycle processed (SC relay gated)", opcode, bodyLen);
                else
                    Logger.Info("Combat ZW 0x{0:X4} len={1} (SC relay gated — AAEMU_ZW_SC_RELAY=1 or safe opcode)", opcode, bodyLen);
                return;
            }

            if (TryRelay(opcode, body ?? []))
                Logger.Info("Combat ZW 0x{0:X4} len={1} → SC relayed", opcode, bodyLen);
            else
                Logger.Info("Combat ZW 0x{0:X4} len={1} → SC relay pending/unhandled", opcode, bodyLen);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "CombatRelay failed zw=0x{0:X4} len={1}", opcode, bodyLen);
        }
    }

    private bool TryRelay(ushort zwOpcode, byte[] body, bool relayToClients = true)
    {
        var stream = new PacketStream();
        if (body.Length > 0)
            stream.Insert(0, body);
        stream.Pos = 0;

        return zwOpcode switch
        {
            0x0018 => RelayStartSkill(stream),
            0x0019 => RelayStopSkill(stream),
            0x001A => RelayStopCasting(stream),
            0x0009 => RelaySkillControllerState(stream),
            0x000D => RelayAggroHostile(stream),
            ZwOpcodes.AiAggro => RelayAiAggro(stream),
            0x000A => RelayCreateBuff(stream),
            0x000B => RelayRemoveBuff(stream, relayToClients),
            0x0031 => RelayKillNpc(stream),
            0x0041 => RelayAggroRemove(stream),
            0x000C => RelayClearCombat(stream),
            0x002D => RelayResponseCombatUnits(stream),
            _ => false
        };
    }

    /// <summary>ZWResponseCombatUnits (0x02D) — Zone answers World combat-unit query; log + accept.</summary>
    private bool RelayResponseCombatUnits(PacketStream stream)
    {
        Logger.Info("ZWResponseCombatUnits len={0}", stream.Count);
        return true;
    }

    /// <summary>
    /// <c>count</c> entries of <c>bc npcId, bc hostileUnitId, i32 value[3], i8 topFlags</c>.
    /// </summary>
    private bool RelayAiAggro(PacketStream stream)
    {
        const uint maxZoneUpdates = 0x80;
        const int serializedUpdateSize = 3 + 3 + sizeof(int) * 3 + sizeof(byte);

        if (stream.LeftBytes < sizeof(uint))
            return false;

        var count = stream.ReadUInt32();
        if (count > maxZoneUpdates || stream.LeftBytes != count * serializedUpdateSize)
            return false;

        var entriesByNpc = new Dictionary<uint, List<AiAggroEntry>>();
        for (var index = 0u; index < count; index++)
        {
            var npcId = stream.ReadBc();
            var entry = new AiAggroEntry(
                stream.ReadBc(),
                stream.ReadInt32(),
                stream.ReadInt32(),
                stream.ReadInt32(),
                stream.ReadByte());

            if (!entriesByNpc.TryGetValue(npcId, out var entries))
            {
                entries = [];
                entriesByNpc.Add(npcId, entries);
            }

            entries.Add(entry);
        }

        foreach (var (npcId, entries) in entriesByNpc)
        {
            for (var offset = 0; offset < entries.Count; offset += SCAiAggroPacket.MaxEntries)
            {
                var packetEntries = entries
                    .Skip(offset)
                    .Take(SCAiAggroPacket.MaxEntries)
                    .ToArray();
                var viewerUnitIds = packetEntries
                    .Select(entry => entry.HostileUnitId)
                    .Prepend(npcId)
                    .Distinct()
                    .ToArray();

                WorldIntegration.BroadcastPacketToUnitViewers(
                    new SCAiAggroPacket(npcId, packetEntries),
                    viewerUnitIds);
            }
        }

        return stream.LeftBytes == 0;
    }

    /// <summary>
    /// ZWStartSkill: source Bc, skillId u32, SkillCastTarget, almighty bool.
    /// Zone AI requests the cast; World runs the normal skill pipeline, whose ZoneAuthority bridge
    /// acknowledges it with WZSkillStarted/Fired/Ended and applies the data-defined effects.
    /// </summary>
    private bool RelayStartSkill(PacketStream stream)
    {
        if (stream.Count < 8)
            return false;

        var casterId = stream.ReadBc();
        var skillId = stream.ReadUInt32();
        var targetType = (SkillCastTargetType)stream.ReadByte();
        var target = SkillCastTarget.GetByType(targetType);
        target.Read(stream);
        if (stream.LeftBytes != sizeof(byte))
            return false;
        var almighty = stream.ReadBoolean();

        var template = SkillManager.Instance.GetSkillTemplate(skillId);
        if (template == null)
        {
            Logger.Warn("ZWStartSkill unknown skillId={0} caster={1}", skillId, casterId);
            return false;
        }

        var casterUnit = ResolveUnit(casterId);
        if (casterUnit == null)
        {
            Logger.Warn("ZWStartSkill unresolved caster={0} skillId={1}", casterId, skillId);
            return false;
        }

        if (casterUnit is Character character &&
            HeirGameData.Instance.TryGetHeirSkillForSuccessor(skillId, out _, out _) &&
            !character.HeirSkills.IsActiveSuccessor(skillId))
        {
            Logger.Warn("ZWStartSkill rejected unselected Heir successor skillId={0} caster={1}", skillId, casterId);
            return false;
        }

        var skill = new Skill(template);
        var caster = new SkillCasterUnit(casterId);
        var skillObject = new SkillObject();
        // After World stopped rejecting those as TooFarRange, bypassGcd=true applied full damage
        // every tick. NPC casters keep World swing/GCD (SkillLastUsed 800ms for skills 2/3/4);
        // player casts from Zone still bypass the duplicate World GCD.
        var bypassGcd = casterUnit is not Npc;
        var result = skill.Use(casterUnit, caster, target, skillObject, bypassGcd, out _, out _);
        Logger.Debug(
            "ZWStartSkill caster={0} skill={1} targetType={2} almighty={3} result={4} tl={5}",
            casterId, skillId, targetType, almighty, result, skill.TlId);

        return true;
    }

    /// <summary>ZWStopSkill — typically unit Bc + skillId (catalog).</summary>
    private bool RelayStopSkill(PacketStream stream)
    {
        if (stream.Count < 7)
            return false;
        var unitId = stream.ReadBc();
        var skillId = stream.ReadUInt32();
        if (!WorldIntegration.IsStreamedUnitForAnyClient(unitId))
            return true;
        WorldIntegration.BroadcastPacketToUnitViewers(new SCSkillStoppedPacket(unitId, skillId), unitId);
        return true;
    }

    /// <summary>
    /// emits one non-zero timeline and one zero sentinel.
    /// </summary>
    private bool RelayStopCasting(PacketStream stream)
    {
        if (stream.Count < 7)
            return false;

        var unitId = stream.ReadBc();
        if (stream.Pos + 4 > stream.Count)
            return false;

        var primaryTl = stream.ReadUInt16();
        var secondaryTl = stream.ReadUInt16();
        var tl = primaryTl != 0 ? primaryTl : secondaryTl;
        if (tl == 0)
            return true;

        WorldIntegration.BroadcastPacketToUnitViewers(
            new SCCastingStoppedPacket(tl, 0), unitId);
        return true;
    }

    /// <summary>
    /// bc object + u8 type; type zero additionally carries f32 length + torn/cutout booleans.
    /// </summary>
    private bool RelaySkillControllerState(PacketStream stream)
    {
        if (stream.Count < 4)
            return false;

        var objId = stream.ReadBc();
        if (stream.Pos >= stream.Count)
            return false;
        var scType = stream.ReadByte();

        var length = 0f;
        var torn = false;
        var cutout = false;
        if (scType == 0)
        {
            if (stream.Pos + 6 > stream.Count)
            {
                Logger.Warn("ZWSkillControllerState truncated type=0 obj={0} pos={1} len={2}",
                    objId, stream.Pos, stream.Count);
                return false;
            }

            length = stream.ReadSingle();
            torn = stream.ReadBoolean();
            cutout = stream.ReadBoolean();
        }

        if (!WorldIntegration.IsStreamedUnitForAnyClient(objId))
            return true;

        WorldIntegration.BroadcastPacketToUnitViewers(
            new SCSkillControllerStatePacket(objId, scType, length, torn, cutout), objId);
        return true;
    }

    private bool RelayAggroHostile(PacketStream stream)
    {
        if (stream.Count < 6)
            return false;

        var npcId = stream.ReadBc();
        var targetId = stream.ReadBc();

        if (!WorldIntegration.IsStreamedUnitForAnyClient(npcId) &&
            !WorldIntegration.IsStreamedUnitForAnyClient(targetId))
            return true;

        WorldIntegration.BroadcastPacketToUnitViewers(new SCCombatEngagedPacket(npcId, targetId), npcId, targetId);
        return true;
    }

    /// <summary>ZWCreateBuff: unit Bc + buffType u32 → World Buffs.AddBuff → SCBuffCreated.</summary>
    private bool RelayCreateBuff(PacketStream stream)
    {
        if (stream.Count < 7)
            return false;
        var unitId = stream.ReadBc();
        var buffType = stream.ReadUInt32();
        if (!WorldIntegration.IsStreamedUnitForAnyClient(unitId))
            return true;

        var unit = ResolveUnit(unitId);
        var buffTemplate = SkillManager.Instance.GetBuffTemplate(buffType);
        if (unit == null || buffTemplate == null)
        {
            Logger.Warn("ZWCreateBuff unresolved unit={0} buffType={1}", unitId, buffType);
            return false;
        }

        // Zone already applied; mirror into World + SC. Caster unknown on wire — self-apply.
        unit.Buffs.AddBuff(new Buff(
            unit,
            unit,
            new SkillCasterUnit(unit.ObjId),
            buffTemplate,
            null,
            DateTime.UtcNow)
        {
            // ZWCreateBuff is a post-application notification. Do not reflect it to Zone.
            ZoneAuthored = true
        });
        return true;
    }

    /// <summary>ZWRemoveBuff: unit Bc + instance u32 + template u32 + reason u8.</summary>
    private bool RelayRemoveBuff(PacketStream stream, bool relayToClients)
    {
        const int serializedSize = 3 + sizeof(uint) + sizeof(uint) + sizeof(byte);
        if (stream.LeftBytes != serializedSize)
            return false;

        var unitId = stream.ReadBc();
        var buffIndex = stream.ReadUInt32();
        var buffTemplateId = stream.ReadUInt32();
        var reason = stream.ReadByte();
        if (relayToClients && WorldIntegration.IsStreamedUnitForAnyClient(unitId))
        {
            var unit = ResolveUnit(unitId);
            if (unit != null)
            {
                var mirroredBuff = buffIndex != 0 ? unit.Buffs.GetEffectByIndex(buffIndex) : null;
                if (mirroredBuff != null)
                    unit.Buffs.RemoveEffect(buffIndex, notifyZone: false);
                else if (buffTemplateId != 0)
                    unit.Buffs.RemoveBuff(buffTemplateId, notifyZone: false);
                else
                    Logger.Warn("ZWRemoveBuff unresolved selector unit={0} index={1}", unitId, buffIndex);
            }
            else if (buffIndex != 0)
                WorldIntegration.BroadcastPacketToUnitViewers(
                    new SCBuffRemovedPacket(unitId, buffIndex), unitId);
        }

        // Complete spawn-dependent handoffs only after the client-visible removal is queued.
        WorldIntegration.ObserveZoneBuffRemoved(unitId, buffTemplateId);
        Logger.Debug(
            "ZWRemoveBuff unit={0} index={1} template={2} reason={3}",
            unitId,
            buffIndex,
            buffTemplateId,
            reason);
        return stream.LeftBytes == 0;
    }

    private bool RelayKillNpc(PacketStream stream)
    {
        if (stream.Count < 3)
            return false;

        var npcId = stream.ReadBc();
        // Zone kill authority → full death path (loot, SCUnitDeath, quest kills).
        // Despawn-only uses OnZoneNpcRemove → MirrorZoneNpcRemove (no loot).
        if (WorldIntegration.OnZoneNpcKilled != null)
            WorldIntegration.OnZoneNpcKilled.Invoke(npcId);
        else
            WorldIntegration.OnZoneNpcRemove?.Invoke(npcId);
        return true;
    }

    /// <summary>
    /// separate ZWClearCombat transitions and must not be inferred from this packet.
    /// </summary>
    private bool RelayAggroRemove(PacketStream stream)
    {
        const int serializedSize = 3 + 3 + sizeof(sbyte);
        if (stream.LeftBytes != serializedSize)
            return false;

        var topAggroUnitId = stream.ReadBc();
        var npcUnitId = stream.ReadBc();
        var reason = stream.ReadSByte();

        if (ResolveUnit(npcUnitId) is Npc npc &&
            npc.AggroTable.TryGetValue(topAggroUnitId, out var mirrorAggro))
        {
            npc.ClearAggroOfUnit(mirrorAggro.Owner);
        }

        Logger.Debug(
            "ZWAggroRemove topAggroUnit={0} npc={1} reason={2}",
            topAggroUnitId,
            npcUnitId,
            reason);
        return stream.LeftBytes == 0;
    }

    /// <summary>ZWClearCombat — NPC leash heal; player clear is often a Zone desync mid-fight.</summary>
    private bool RelayClearCombat(PacketStream stream)
    {
        if (stream.Count < 3)
            return false;
        var unitId = stream.ReadBc();
        Logger.Info("ZWClearCombat unit={0}", unitId);
        ZoneAuthorityCombat.ResetMirrorNpcHp(unitId);

        // Zone clears the *player* while mobs still swing — don't push SCCombatCleared or the
        // client drops combat UI / full out-of-combat regen while still taking hits.
        var world = WorldManager.Instance.GetWorld(WorldManager.DefaultInstanceId);
        if (world?.GetCharacterByObjId(unitId) is { } ch)
        {
            if (ch.IsInBattle || (DateTime.UtcNow - ch.LastCombatActivity).TotalSeconds < 8.0)
            {
                Logger.Info("ZWClearCombat player={0} — suppress SCCombatCleared (still in combat)", unitId);
                return true;
            }
        }

        if (WorldIntegration.IsStreamedUnitForAnyClient(unitId))
            WorldIntegration.BroadcastPacketToUnitViewers(new SCCombatClearedPacket(unitId), unitId);
        return true;
    }

    private static Unit? ResolveUnit(uint objId)
    {
        var world = WorldManager.Instance.GetWorld(WorldManager.DefaultInstanceId);
        if (world == null)
            return null;
        if (world.GetUnit(objId) is Unit u)
            return u;
        return world.GetNpc(objId) ?? (Unit?)world.GetCharacterByObjId(objId);
    }
}
