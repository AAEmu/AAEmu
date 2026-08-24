using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSLeaveInstantGamePacket() : GamePacket(CSOffsets.CSLeaveInstantGamePacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var isIndunMatch = stream.ReadByte() != 0;
        var character = Connection.ActiveChar;
        if (character == null)
            return;

        if (isIndunMatch)
        {
            IndunMatchmakingManager.Instance.TryLeaveIndunMatch(character);
            return;
        }

        Logger.Warn("LeaveInstantGame battlefield char={0}", character.Name);
        character.CurrentInstantGame?.LeaveInstantGame(character);
    }
}
