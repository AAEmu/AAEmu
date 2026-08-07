using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCICSMenuListPacket : GamePacket
{
    private const byte MainTabCount = 9;
    private const byte SubTabCount = 8;
    private readonly bool _enabled;
    private readonly Dictionary<byte, Dictionary<byte, bool>> _tabsEnabled;

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

        // Set tab state to true for used tabs (guarded: an out-of-range tab in the data must not crash the send)
        foreach (var item in CashShopManager.Instance.MenuItems)
        {
            if (_tabsEnabled.TryGetValue(item.MainTab, out var subs) && subs.ContainsKey(item.SubTab))
                subs[item.SubTab] = true;
        }
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_enabled);

        if (!_enabled)
            return stream;

        for (byte mainTab = 0; mainTab < MainTabCount; mainTab++)
        {
            _tabsEnabled.TryGetValue(mainTab, out var subs); // group 0 has no entry -> sentinel
            stream.Write(subs != null && subs.Values.Any(v => v));

            for (byte subTab = 0; subTab < SubTabCount; subTab++)
            {
                var enabled = subTab >= 1 && subs != null && subs.TryGetValue(subTab, out var v) && v;
                stream.Write((byte)(enabled ? subTab : 0));
            }
        }

        return stream;
    }
}
