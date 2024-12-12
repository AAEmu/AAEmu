using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCICSMenuListPacket : GamePacket
{
    private const byte MainTabCount = 6;
    private const byte SubTabCount = 7;
    private readonly bool _enabled;
    private Dictionary<byte, Dictionary<byte, bool>> _tabsEnabled;

    public SCICSMenuListPacket(bool enabled) : base(SCOffsets.SCICSMenuListPacket, 1)
    {
        _enabled = enabled;

        // Initialize tabs
        _tabsEnabled = [];
        for (byte mainTab = 1; mainTab <= MainTabCount; mainTab++)
        {
            _tabsEnabled.Add(mainTab, []);
            for (byte subTab = 1; subTab <= SubTabCount; subTab++)
                _tabsEnabled[mainTab].Add(subTab, false);
        }

        // Set tab state to true for used tabs
        foreach (var item in CashShopManager.Instance.MenuItems)
            _tabsEnabled[item.MainTab][item.SubTab] = true;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_enabled);

        for (byte mainTab = 1; mainTab <= MainTabCount; mainTab++)
        {
            stream.Write(mainTab);

            for (byte subTab = 1; subTab <= SubTabCount; subTab++)
                stream.Write((byte)(_tabsEnabled[mainTab][subTab] ? subTab : 0));
        }

        return stream;
    }
}
