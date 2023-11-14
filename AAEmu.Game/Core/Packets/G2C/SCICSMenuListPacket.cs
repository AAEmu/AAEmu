using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.CashShop;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCICSMenuListPacket : GamePacket
{
    private static byte MainTabCount = 6;
    private static byte SubTabCount = 7;
    private readonly bool _enabled;
    private List<CashShopItem> _allItems;
    private Dictionary<byte, Dictionary<byte, bool>> _tabsEnabled;

    public SCICSMenuListPacket(bool enabled) : base(SCOffsets.SCICSMenuListPacket, 1)
    {
        _enabled = enabled;
        _allItems = CashShopManager.Instance.GetCashShopItems();

        // Initialize tabs
        _tabsEnabled = new();
        for (byte mainTab = 1; mainTab <= MainTabCount; mainTab++)
        {
            _tabsEnabled.Add(mainTab, new Dictionary<byte, bool>());
            for (byte subTab = 1; subTab <= SubTabCount; subTab++)
                _tabsEnabled[mainTab].Add(subTab, false);
        }

        // Set tab state to true for used tabs
        foreach (var item in _allItems)
            _tabsEnabled[item.MainTab][item.SubTab] = true;

        /*
        _tabs = new (byte mainTab, List<byte> subTabs)[]
        {
            (1, new List<byte> { 1,2,3,4,5,6,7 }),
            (2, new List<byte> { 1,2,3,4,5,6,7 }),
            (3, new List<byte> { 1,2,3,4,5,6,7 }),
            (4, new List<byte> { 1,2,3,4,5,6,7 }),
            (5, new List<byte> { 1,2,3,4,5,6,7 }),
            (6, new List<byte> { 1,2,3,4,5,6,7 })
        };
        */
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_enabled);

        for (byte mainTab = 1; mainTab <= MainTabCount; mainTab++)
        {
            stream.Write(mainTab);
            /*
            if (!_tabsEnabled.TryGetValue(mainTab, out var subTabs))
            {
                stream.Write((byte)0);
                continue;
            }
            */

            for (byte subTab = 1; subTab <= SubTabCount; subTab++)
                stream.Write((byte)(_tabsEnabled[mainTab][subTab] ? subTab : 0));
        }
        /*
        for (var i = 0; i < 6; i++)
        {
            stream.Write(_tabs[i].mainTab); // mainTab
            for (byte j = 1; j <= 7; j++)
            {
                if (_tabs[i].subTabs.IndexOf(j) > -1)
                    stream.Write(j); // subTab
                else
                    stream.Write((byte)0);
            }
        }
        */

        return stream;
    }
}
