using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Clears the account's second password once the player has given it correctly.
/// </summary>
public class CSClearSecondPasswordPacket() : GamePacket(CSOffsets.CSClearSecondPasswordPacket, 1)
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

        if (!manager.TryBeginAttempt(connection.AccountId, out var retryAfter))
        {
            Logger.Debug("Second password: account {0} is over its attempt window, {1:0}s to wait",
                connection.AccountId, retryAfter.TotalSeconds);
            connection.SendPacket(new SCSecondPassClearedPacket(false, 0));
            return;
        }

        var password = manager.Decode(connection.AccountId, (byte)TableIndex, Pass);
        var failedCount = 0;
        var success = password != null && manager.TryClear(connection.AccountId, password, out failedCount);

        if (success)
            manager.Forget(connection.AccountId);

        connection.SendPacket(new SCSecondPassClearedPacket(success, (sbyte)failedCount));
    }
}