using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Replaces the account's second password. Both the old and the new password arrive as the positions the
/// player clicked, so each is read back through the table its own window was drawn from.
/// </summary>
/// <remarks>
/// Field order and widths come from the 10.0.2.13 client's serializer, which passes each value's name
/// alongside the value: time, oldPassTableIndex, newPassTableIndex, oldPass, newPass.
/// </remarks>
public class CSChangeSecondPasswordPacket() : GamePacket(CSOffsets.CSChangeSecondPasswordPacket, 1)
{
    public int Time { get; private set; }
    public sbyte OldPassTableIndex { get; private set; }
    public sbyte NewPassTableIndex { get; private set; }
    public string OldPass { get; private set; }
    public string NewPass { get; private set; }

    public override void Read(PacketStream stream)
    {
        Time = stream.ReadInt32();
        OldPassTableIndex = stream.ReadSByte();
        NewPassTableIndex = stream.ReadSByte();
        OldPass = stream.ReadString();
        NewPass = stream.ReadString();

        var connection = Connection;
        if (connection?.ActiveChar == null)
            return;

        var manager = SecondPasswordManager.Instance;

        if (!manager.TryBeginAttempt(connection.AccountId, out var retryAfter))
        {
            Logger.Debug("Second password: account {0} is over its attempt window, {1:0}s to wait",
                connection.AccountId, retryAfter.TotalSeconds);
            connection.SendPacket(new SCSecondPassChangedPacket(0, 0, 0));
            return;
        }

        var oldPassword = manager.Decode(connection.AccountId, (byte)OldPassTableIndex, OldPass);
        var newPassword = manager.Decode(connection.AccountId, (byte)NewPassTableIndex, NewPass);

        // Both windows have to have been drawn from tables this account still holds, otherwise neither
        // password can be read and nothing should be changed.
        var failedCount = 0;
        var changed = oldPassword != null && newPassword != null &&
                      manager.TryChange(connection.AccountId, oldPassword, newPassword, out failedCount);

        if (changed)
            manager.Forget(connection.AccountId);

        connection.SendPacket(new SCSecondPassChangedPacket(0, changed ? (sbyte)1 : (sbyte)0, (sbyte)failedCount));
    }
}
