using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Routes a family-notice request to the owner-authority and persistence path.
/// </summary>
/// <remarks>
/// which passes each field name alongside the value:
/// string notice
/// </remarks>
public class CSFamilyNoticeSetPacket() : GamePacket(CSOffsets.CSFamilyNoticeSetPacket, 1)
{
    public string Notice { get; private set; }

    public override void Read(PacketStream stream)
    {
        Notice = stream.ReadString();
        FamilyManager.Instance.SetNotice(Connection.ActiveChar, Notice);
    }
}
