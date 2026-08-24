using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSCancelInstantGamePacket() : GamePacket(CSOffsets.CSCancelInstantGamePacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var character = Connection.ActiveChar;
        if (character == null)
            return;

        // Always ack cancel so the client clears IsApplyInstance even if World already
        // dropped the queue (close/reopen UI, duplicate cancel clicks).
        IndunMatchmakingManager.Instance.TryWithdraw(character);
        InstantGameManager.Instance.WithdrawFromBattlefield(character);
        character.SendPacket(G2C.SCCancelInstantGamePacket.ClearQueue());
    }
}
