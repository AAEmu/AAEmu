using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// The client sends this repeatedly (observed 6x back-to-back) whenever the prestige-shop buff
/// window is open. Wire-verified 2026-08-28 (see SCExpeditionBuffUnitPacket.cs) that the expected
/// response shares the exact same "buffs" vector format as SCExpeditionBuffsPacket - answered with
/// that, scoped to the requesting character's own unit id. Previously "nothing acts on it yet".
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
