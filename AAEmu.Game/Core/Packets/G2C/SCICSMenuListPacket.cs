using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>Publishes Marketplace tab availability and page counts.</summary>
public class SCICSMenuListPacket : GamePacket
{
    /// <summary>9 slots (0 unused + main 1..8). Client ser loop is 9.</summary>
    private const int MainTabSlots = 9;
    /// <summary>Eight sub-tab slots, with slot zero unused.</summary>
    private const int SubTabSlots = 8;

    private readonly bool _enabled;
    // Indexed by 1-based main/sub ids (slot 0 left zero).
    private readonly bool[] _mainPresent = new bool[MainTabSlots];
    private readonly byte[,] _subPages = new byte[MainTabSlots, SubTabSlots];

    public SCICSMenuListPacket(bool enabled) : base(SCOffsets.SCICSMenuListPacket, 1)
    {
        _enabled = enabled;
        if (!enabled)
            return;

        // DB tabs match client tab ids (featured = 1,1). Do NOT convert to 0-based.
        for (byte main = 1; main < MainTabSlots; main++)
        {
            for (byte sub = 1; sub < SubTabSlots; sub++)
            {
                var n = CashShopManager.Instance.MenuItems.Count(t => t.MainTab == main && t.SubTab == sub);
                if (n == 0)
                    continue;
                // Client: main 1,sub 1 gets 4 slots/page; other tabs get 8 (matches SendICSPage).
                var itemsPerPage = main == 1 && sub == 1 ? 4 : 8;
                var pages = (int)Math.Ceiling(n / (float)itemsPerPage);
                _subPages[main, sub] = (byte)Math.Clamp(pages, 1, 255);
                _mainPresent[main] = true;
            }
        }
    }

    public override PacketStream Write(PacketStream stream)
    {
        // enable: client +248 (bool)
        stream.Write(_enabled);
        if (!_enabled)
            return stream;

        // Write each main-tab flag followed by its sub-tab page counts.
        for (var main = 0; main < MainTabSlots; main++)
        {
            stream.Write(_mainPresent[main]);
            for (var sub = 0; sub < SubTabSlots; sub++)
                stream.Write(_subPages[main, sub]);
        }

        return stream;
    }
}
