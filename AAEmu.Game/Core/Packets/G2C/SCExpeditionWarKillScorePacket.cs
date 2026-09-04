using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Guild War kill scoreboard. Opcode 0x1a. Answers CSExpeditionWarKillScorePacket (client poll while
/// the scoreboard is open + periodic) and is pushed on every war kill.
/// </summary>
/// <remarks>
/// Wire layout from the real client Unpack FUN_39a9a470, cross-checked against the scoreboard Lua
/// (expedition_war.lua FillResult / GetExpeditionWarKillScore):
///   remainTime      (i64, +0xe8)  - ms remaining (Lua divides by 1000)
///   ourTotalKills   (u32)         - Lua ourTotalKill  -> gauge left score
///   enemyTotalKills (u32)         - Lua enemyTotalKill -> gauge right score
///   10x { memberId (u64), kills (u16) }   - our side, client resolves name+ability from id;
///                                           only members with >=1 kill, rest zero-padded
///   enemyCount (u8, 0..10)
///   enemyCount x { memberId (u64), name (string), kills (u16), ability0..2 (u8) }
/// Per-row "kills" comes from these arrays (Lua FillScoreInfo -> data["kills"]); retail only lists
/// members who have scored, so both sides are filtered to WarKillsByMember > 0.
/// </remarks>
public class SCExpeditionWarKillScorePacket : GamePacket
{
    private readonly long _remainMs;
    private readonly uint _ourTotal;
    private readonly uint _enemyTotal;
    private readonly (ulong Id, ushort Kills)[] _ourSlots;     // exactly 10, zero-padded
    private readonly (ulong Id, string Name, ushort Kills, byte[] Ab)[] _enemyScorers; // <= 10

    public SCExpeditionWarKillScorePacket(Expedition ours, Expedition enemy) : base(SCOffsets.SCExpeditionWarKillScorePacket, 1)
    {
        var end = ours?.WarEndsAt ?? enemy?.WarEndsAt;
        _remainMs = end.HasValue ? Math.Max(0L, (long)(end.Value - DateTime.UtcNow).TotalMilliseconds) : 0L;
        _ourTotal = ours?.WarKillScore ?? 0;
        _enemyTotal = enemy?.WarKillScore ?? 0;

        _ourSlots = new (ulong, ushort)[10];
        var ourScorers = ScorersOf(ours);
        for (var i = 0; i < ourScorers.Count && i < 10; i++)
            _ourSlots[i] = (ourScorers[i].Id, ourScorers[i].Kills);

        _enemyScorers = ScorersOf(enemy)
            .Select(s =>
            {
                var m = enemy?.Members?.FirstOrDefault(x => x.CharacterId == (uint)s.Id);
                return (s.Id, m?.Name ?? "", s.Kills, m?.Abilities ?? [0, 0, 0]);
            })
            .Take(10)
            .ToArray();
    }

    private static List<(ulong Id, ushort Kills)> ScorersOf(Expedition e)
    {
        if (e == null)
            return [];
        return e.WarKillsByMember
            .Where(kv => kv.Value > 0)
            .OrderByDescending(kv => kv.Value)
            .Select(kv => ((ulong)kv.Key, (ushort)Math.Min(kv.Value, ushort.MaxValue)))
            .ToList();
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_remainMs);
        stream.Write(_ourTotal);
        stream.Write(_enemyTotal);

        for (var i = 0; i < 10; i++)
        {
            stream.Write((ulong)_ourSlots[i].Id);
            stream.Write(_ourSlots[i].Kills);
        }

        stream.Write((byte)_enemyScorers.Length);
        foreach (var (id, name, kills, ab) in _enemyScorers)
        {
            stream.Write((ulong)id);
            stream.Write(name ?? "");
            stream.Write(kills);
            stream.Write((byte)(ab.Length > 0 ? ab[0] : 0));
            stream.Write((byte)(ab.Length > 1 ? ab[1] : 0));
            stream.Write((byte)(ab.Length > 2 ? ab[2] : 0));
        }

        return stream;
    }
}
