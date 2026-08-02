using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Faction;

namespace AAEmu.World.Core.Packets.Wz;

/// <summary>
/// WZFactionList (0x013) — WZ_BRINGONLINE: [u32 total][u8 count≤20][FactionDesc×count].
/// Empty list is valid; prefer real system factions when Game has loaded them.
/// </summary>
public class WZFactionListPacket : ZonePacket
{
    private readonly IReadOnlyList<SystemFaction> _factions;

    public WZFactionListPacket() : this(null)
    {
    }

    public WZFactionListPacket(IReadOnlyList<SystemFaction> factions) : base(WzOpcodes.FactionList)
    {
        _factions = factions ?? Array.Empty<SystemFaction>();
    }

    /// <summary>Build from FactionManager when available (≤20 per packet).</summary>
    public static WZFactionListPacket FromGame()
    {
        try
        {
            var all = FactionManager.Instance.GetSystemFactions();
            if (all == null || all.Count == 0)
                return new WZFactionListPacket();
            // Single bring-online packet: count == total ≤ 20
            var take = all.Take(20).ToList();
            return new WZFactionListPacket(take);
        }
        catch
        {
            return new WZFactionListPacket();
        }
    }

    protected override void WriteBody(PacketStream stream)
    {
        var count = (byte)Math.Min(20, _factions.Count);
        stream.Write((uint)count); // total
        stream.Write(count);
        for (var i = 0; i < count; i++)
        {
            var f = _factions[i];
            stream.Write((uint)f.Id);
            stream.Write((uint)f.MotherId);
            stream.Write(Truncate(f.Name, 128));
            stream.Write((long)f.OwnerId);
            stream.Write(Truncate(f.OwnerName ?? "", 128));
            stream.Write((byte)f.UnitOwnerType);
            stream.Write(f.PoliticalSystem);
            stream.Write(0ul); // createdTime
            stream.Write(f.AggroLink);
            stream.Write(f.DiplomacyTarget); // dTarget
            stream.Write((byte)0); // allowChangeName
            stream.Write(0ul); // renameTime
            stream.Write(false); // integrationFaction
        }
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max]);
}
