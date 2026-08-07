using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCICSMenuListPacket : GamePacket
{
    private const byte WireMainGroups = 9;
    private const byte WireSubBytes = 8;
    private const byte MaxMainTab = 8;
    private const byte MaxSubTab = 7;

    private readonly bool _enabled;
    private readonly Dictionary<byte, Dictionary<byte, bool>> _tabsEnabled;

    public SCICSMenuListPacket(bool enabled) : base(SCOffsets.SCICSMenuListPacket, 1)
    {
        _enabled = enabled;

        _tabsEnabled = [];
        for (byte mainTab = 1; mainTab <= MaxMainTab; mainTab++)
        {
            _tabsEnabled.Add(mainTab, []);
            for (byte subTab = 1; subTab <= MaxSubTab; subTab++)
                _tabsEnabled[mainTab].Add(subTab, false);
        }

        foreach (var item in CashShopManager.Instance.MenuItems)
        {
            if (_tabsEnabled.TryGetValue(item.MainTab, out var subs) && subs.ContainsKey(item.SubTab))
                subs[item.SubTab] = true;
            else
                Logger.Debug($"ICS menu row with out-of-range tab main={item.MainTab} sub={item.SubTab} " +
                            $"(client supports main 1..{MaxMainTab}, sub 1..{MaxSubTab}); it will not appear in any tab");
        }
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_enabled);

        if (!_enabled)
            return stream;

        for (byte mainTab = 0; mainTab < WireMainGroups; mainTab++)
        {
            _tabsEnabled.TryGetValue(mainTab, out var subs);
            stream.Write(subs != null && subs.Values.Any(v => v));

            for (byte subTab = 0; subTab < WireSubBytes; subTab++)
            {
                var enabled = subTab >= 1 && subs != null && subs.TryGetValue(subTab, out var v) && v;
                stream.Write((byte)(enabled ? subTab : 0));
            }
        }

        return stream;
    }
}
