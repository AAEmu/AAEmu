using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C.UnitState;

/// <summary>Skills, heading, packed state groups, flags, and flag-gated optional blocks.</summary>
internal static class UnitStateGameplaySerializer
{
    private const ushort InvisibleFlag = 1 << 5;
    private const ushort GmModeBlockFlag = 1 << 6;
    private const ushort HighAbilityBlockFlag = 1 << 7;
    private const ushort FirstHitterBlockFlag = 1 << 8;
    private const ushort SlaveSpawnPortalFlag = 1 << 11;
    private const ushort IdleFlag = 1 << 13;

    public static void Write(PacketStream stream, UnitStateWireContext context)
    {
        var unit = context.Unit;
        var skillIds = context.Character?.Skills.Skills.Values
            .Select(skill => skill.Id).Take(byte.MaxValue).ToArray() ?? [];
        var passiveIds = context.Character?.Skills.PassiveBuffs.Values
            .Select(buff => buff.Id).Take(byte.MaxValue).ToArray() ?? [];

        stream.Write(unchecked((sbyte)unit.ActiveWeapon));
        stream.Write((byte)skillIds.Length);
        stream.Write((byte)passiveIds.Length);
        stream.Write(unit.UnitStateType);
        stream.Write(unit.AppellationStampId);
        stream.Write(unit.VehicleDyeing);
        stream.Write(unit.IsTempFaction);
        stream.WritePisc(skillIds);
        stream.WritePisc(passiveIds);

        WriteHeading(stream, context);
        stream.Write(context.Character?.RaceGender ?? context.Npc?.RaceGender ?? unit.RaceGender);
        WritePackedState(stream, context);

        // so the client skips firstHitter/highAbility/gmmode optional blocks.
        if (context.BaseUnitType == BaseUnitType.Npc)
        {
            stream.Write((ushort)0);
            stream.Write((byte)0);
            return;
        }

        var flags = BuildFlags(unit);
        stream.Write(flags);
        stream.Write(unit.AttackFactionFlags);
        WriteOptionalBlocks(stream, unit.UnitStateOptionalData, flags);
    }

    private static void WriteHeading(PacketStream stream, UnitStateWireContext context)
    {
        if (context.BaseUnitType == BaseUnitType.Housing)
        {
            stream.Write(context.Unit.Transform.Local.Rotation.Z);
            return;
        }

        var (roll, pitch, yaw) = context.Unit.Transform.Local.ToRollPitchYawSBytes();
        stream.Write(roll);
        stream.Write(pitch);
        stream.Write(yaw);
    }

    private static void WritePackedState(PacketStream stream, UnitStateWireContext context)
    {
        // (PacketLayouts/unit-stream.md). Faction is applied via SCUnitFactionChanged
        // (0→real), not through member 0x8E4. Writing faction here diverged from the
        if (context.BaseUnitType == BaseUnitType.Npc)
        {
            stream.WritePisc(0u, 0u, 0u, 0u);
            stream.WritePisc(0u, 0u, 0u);
            stream.WritePisc(0u, 0u, 0u, 0u);
            return;
        }

        var unit = context.Unit;
        var optional = unit.UnitStateOptionalData;
        var activeSkillTemplate = unit.ActivePlotState?.ActiveSkill?.Template;

        stream.WritePisc(
            optional?.PlotDebugLevel ?? 0,
            optional?.VersionReserved7Fb0 ?? 0,
            optional?.VersionReserved7Fb4 ?? 0,
            context.Character?.Appellations.ActiveAppellation ?? 0u);

        stream.WritePisc(
            activeSkillTemplate?.Plot?.Id ?? 0u,
            activeSkillTemplate?.KeepManaRegen == true ? 1 : 0,
            (uint)(unit.Faction?.Id ?? 0));

        stream.WritePisc(
            unit.IsEquipmentPublic ? 1 : 0,
            unit.IsOffline ? 1 : 0,
            unit.DuelStateObjectId,
            context.Character?.HonorGainedInCombat ?? 0u);
    }

    private static ushort BuildFlags(Unit unit)
    {
        ushort flags = 0;
        if (unit.Invisible)
            flags |= InvisibleFlag;
        if (unit.IdleStatus)
            flags |= IdleFlag;
        if (unit is Slave { PendingSpawnPortal: true })
            flags |= SlaveSpawnPortalFlag;

        var optional = unit.UnitStateOptionalData;
        if (optional is null)
            return flags;
        if (optional.GmModeValues.Any(value => value != 0))
            flags |= GmModeBlockFlag;
        if (optional.HighAbilityValues.Any(value => value != 0))
            flags |= HighAbilityBlockFlag;
        if (optional.FirstHitterObjectId != 0 || optional.FirstHitterTeamId != 0)
            flags |= FirstHitterBlockFlag;
        return flags;
    }

    private static void WriteOptionalBlocks(PacketStream stream, UnitStateOptionalData optional, ushort flags)
    {
        if ((flags & FirstHitterBlockFlag) != 0)
        {
            stream.WriteBc(optional!.FirstHitterObjectId);
            stream.Write(optional.FirstHitterTeamId);
        }
        if ((flags & HighAbilityBlockFlag) != 0)
        {
            foreach (var value in optional!.HighAbilityValues)
                stream.Write(value);
        }
        if ((flags & GmModeBlockFlag) != 0)
        {
            foreach (var value in optional!.GmModeValues)
                stream.Write(value);
        }
    }
}
