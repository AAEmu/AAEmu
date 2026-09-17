using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Checks a second password the player typed, and counts the wrong answers.
/// </summary>
public class CSCheckSecondPasswordPacket() : GamePacket(CSOffsets.CSCheckSecondPasswordPacket, 1)
{
    public int Time { get; private set; }
    public sbyte TableIndex { get; private set; }
    public string Pass { get; private set; }

    public override void Read(PacketStream stream)
    {
        Time = stream.ReadInt32();
        TableIndex = stream.ReadSByte();
        Pass = stream.ReadString();

        var connection = Connection;
        if (connection?.ActiveChar == null)
            return;

        var manager = SecondPasswordManager.Instance;

        // An account gets so many attempts per window; over that it is answered without the password being
        // checked at all, which is what keeps a wrong password from costing a key derivation per packet.
        if (!manager.TryBeginAttempt(connection.AccountId, out var retryAfter))
        {
            Logger.Debug("Second password: account {0} is over its attempt window, {1:0}s to wait",
                connection.AccountId, retryAfter.TotalSeconds);
            connection.SendPacket(new SCSecondPassCheckedPacket(false, 0));
            return;
        }

        var password = manager.Decode(connection.AccountId, (byte)TableIndex, Pass);
        var failedCount = 0;
        var success = password != null && manager.Verify(connection.AccountId, password, out failedCount);

        if (success)
            manager.Forget(connection.AccountId);

        connection.SendPacket(new SCSecondPassCheckedPacket(success, (sbyte)failedCount));
    }
}