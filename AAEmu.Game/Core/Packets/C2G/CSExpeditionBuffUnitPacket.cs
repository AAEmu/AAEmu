using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// The client sends this repeatedly while the prestige-shop buff window is open. Answered with the
/// requesting character's own unit id plus the guild's current buff-grade state.
/// </summary>
public class CSExpeditionBuffUnitPacket() : GamePacket(CSOffsets.CSExpeditionBuffUnitPacket, 1)
{
    public uint Bc { get; private set; }

    public override void Read(PacketStream stream)
    {
        Bc = stream.ReadBc();

        var character = Connection.ActiveChar;
        if (character?.Expedition == null)
            return;

        character.SendPacket(new SCExpeditionBuffUnitPacket(Bc, character.Expedition.PurchasedBuffGrades));
    }
}
