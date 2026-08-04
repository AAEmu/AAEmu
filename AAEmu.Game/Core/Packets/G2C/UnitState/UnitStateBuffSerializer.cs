using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C.UnitState;

/// <summary>
/// </summary>
public static class UnitStateBuffSerializer
{
    private const int MaxGoodBuffs = 32;
    private const int MaxBadBuffs = 20;
    private const int MaxHiddenBuffs = 28;

    public static void Write(PacketStream stream, Unit unit)
    {
        var goodBuffs = new List<Buff>();
        var badBuffs = new List<Buff>();
        var hiddenBuffs = new List<Buff>();
        unit.Buffs.GetAllBuffs(goodBuffs, badBuffs, hiddenBuffs, false);

        WriteList(stream, goodBuffs, MaxGoodBuffs);
        WriteList(stream, badBuffs, MaxBadBuffs);
        WriteList(stream, hiddenBuffs, MaxHiddenBuffs);
    }

    private static void WriteList(PacketStream stream, List<Buff> buffs, int maximum)
    {
        var count = Math.Min(buffs.Count, maximum);
        stream.Write((byte)count);
        foreach (var effect in buffs.Take(count))
            WriteRecord(stream, effect);
    }

    private static void WriteRecord(PacketStream stream, Buff effect)
    {
        var stack = effect.Owner?.Buffs is null
            ? 1u
            : (uint)Math.Max(1, effect.Owner.Buffs.GetBuffCountById(effect.Template.BuffId));
        var sourceSkillId = effect.Skill is not null &&
                            effect.Skill.Template.ToggleBuffId == effect.Template.Id
            ? effect.Skill.Template.Id
            : 0u;

        stream.Write(effect.Index);                    // instance id, i32
        stream.Write(effect.SkillCaster);              // SkillCaster union
        stream.Write((ulong)(effect.Caster?.Id ?? 0)); // caster character/unit id, u64
        stream.Write((byte)(effect.Caster?.Level ?? 1));
        stream.Write((ushort)effect.AbLevel);
        stream.WritePisc(
            (uint)effect.Duration,
            (uint)effect.GetTimeElapsed(),
            (uint)effect.Tick,
            effect.TickIndex);
        stream.WritePisc(
            effect.Template.BuffId,
            stack,
            (uint)effect.Charge,
            sourceSkillId);
    }
}
