using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Shared body for SCBuffCreated (0x0EB) and WZBuffCreated (0x03E).
/// Dedicate: SkillCaster + i64 castId + bc target + u32 buffIndex + BuffData.
/// </summary>
public static class BuffCreatedWire
{
    /// <summary>Validates unit identifiers before relaying a buff to the zone.</summary>
    /// <remarks>
    /// Only the owner is mandatory. A missing or out-of-range caster does not disqualify the buff —
    /// <see cref="ZoneSafeCaster"/> substitutes the owner instead, because an item-granted, passive or
    /// system buff legitimately has no casting unit and the zone still has to know the buff exists.
    /// Dropping those left whole families of effects (sail speed among them) applied in World and
    /// absent from the simulation that actually computes movement.
    /// </remarks>
    public static bool IsZoneSafe(Buff buff, out string reason)
    {
        if (!ObjectIdManager.IsZoneUnitId(buff?.Owner?.ObjId ?? 0))
        {
            reason = $"owner ObjId {buff?.Owner?.ObjId ?? 0} is not a zone unit id";
            return false;
        }

        reason = null;
        return true;
    }

    /// <summary>
    /// The caster to put on the wire: the real one when it names a unit the zone can resolve, and the
    /// buff's own owner otherwise.
    /// </summary>
    /// <remarks>
    /// The zone resolves the unit behind a Unit, Item or Mount caster and does not check the result, so
    /// an id it cannot resolve is an access violation in the zone process rather than a rejected packet.
    /// Naming the owner keeps the reference valid and is what an item caster means anyway — the wielder.
    /// </remarks>
    public static SkillCaster ZoneSafeCaster(Buff buff)
    {
        var caster = buff.SkillCaster;
        var namesUnit = caster?.Type is SkillCasterType.Unit or SkillCasterType.Item or SkillCasterType.Mount;
        if (caster != null && (!namesUnit || ObjectIdManager.IsZoneUnitId(caster.ObjId)))
            return caster;

        return new SkillCasterUnit(buff.Owner.ObjId);
    }

    /// <summary>
    /// Only remove buffs whose Create was actually sent. A Remove for a unit/index
    /// Zone never Created is not a no-op — it can take the Zone process down.
    /// </summary>
    public static bool ShouldRelayRemoved(Buff buff, out string reason)
    {
        if (buff == null)
        {
            reason = "buff is null";
            return false;
        }

        if (buff.ZoneAuthored)
        {
            reason = "zone-authored";
            return false;
        }

        if (!buff.RelayedToZone)
        {
            reason = "create was not relayed";
            return false;
        }

        if (!ObjectIdManager.IsZoneUnitId(buff.Owner?.ObjId ?? 0))
        {
            reason = $"owner ObjId {buff.Owner?.ObjId ?? 0} is not a zone unit id";
            return false;
        }

        reason = null;
        return true;
    }

    /// <summary>
    /// Bytes a <see cref="SkillCaster"/> of the given type occupies on the wire, as
    /// <see cref="SkillCaster.Write"/> emits it: the type byte plus a 3-byte bc for every type, then
    /// whatever that subclass adds.
    /// </summary>
    private static int CasterWireSize(byte casterType) => casterType switch
    {
        // Item: item id (u64), template (u32), type1 (u8), type2 (u64).
        (byte)SkillCasterType.Item => 4 + 8 + 4 + 1 + 8,
        // Mount: mount skill template (u32).
        (byte)SkillCasterType.Mount => 4 + 4,
        // Unit, Unk1 and Doodad add nothing.
        _ => 4,
    };

    /// <summary>
    /// Recovers the buff instance index from an encoded SCBuffCreated / WZBuffCreated body so
    /// relays can track which zones accepted which buff. Body layout mirrors <see cref="Write"/>:
    /// SkillCaster union, u64 caster id, bc target, u32 index.
    /// </summary>
    /// <remarks>
    /// The offset has to be derived from the caster type rather than assumed. It was previously a fixed
    /// 16 or 17 bytes, which is wrong for every caster type — a unit caster puts the index at 15 and an
    /// item caster at 36 — so the index recorded when a Create was relayed never matched the one a later
    /// Update carried. The registry lookup that guards Update and Remove therefore always missed, and
    /// the zone silently kept the state it was given at Create for the life of the buff.
    /// </remarks>
    public static bool TryGetBuffIndex(byte[] body, out uint buffIndex)
    {
        buffIndex = 0;
        if (body == null || body.Length == 0)
            return false;

        // Caster, then the u64 cast id, then the 3-byte target bc.
        var offset = CasterWireSize(body[0]) + 8 + 3;
        if (body.Length < offset + 4)
            return false;

        buffIndex = BitConverter.ToUInt32(body, offset);
        return true;
    }

    /// <summary>
    /// Recovers the stack count written after the BuffData header so a Create rebuild can be
    /// compared with the count last accepted by the zone.
    /// </summary>
    public static bool TryGetStack(byte[] body, out uint stack)
    {
        stack = 0;
        if (body == null || body.Length == 0)
            return false;

        // After the index: buffId u32, level u8, abLevel i16, skillId u32, then stack u32.
        var offset = CasterWireSize(body[0]) + 8 + 3 + 4 + 4 + 1 + 2 + 4;
        if (body.Length < offset + 4)
            return false;

        stack = BitConverter.ToUInt32(body, offset);
        return true;
    }

    /// <param name="forZone">
    /// True for the zone-bound copy, which substitutes an unresolvable caster (see
    /// <see cref="ZoneSafeCaster"/>). The client copy keeps the caster verbatim.
    /// </param>
    public static void Write(PacketStream stream, Buff buff, bool forZone = false)
    {
        stream.Write(forZone ? ZoneSafeCaster(buff) : buff.SkillCaster);
        stream.Write((ulong)(buff.Caster?.Id ?? 0));
        stream.WriteBc(buff.Owner.ObjId);
        stream.Write(buff.Index);
        stream.Write(buff.Template.BuffId);
        stream.Write((byte)(buff.Caster?.Level ?? 1));
        stream.Write((short)buff.AbLevel);
        if (buff.Skill is not null && buff.Skill.Template.ToggleBuffId.Equals(buff.Template.Id))
            stream.Write(buff.Skill.Template.Id);
        else
            stream.Write(0);
        stream.Write((uint)Math.Max(1, buff.Stack));
        buff.WriteData(stream);
    }
}
