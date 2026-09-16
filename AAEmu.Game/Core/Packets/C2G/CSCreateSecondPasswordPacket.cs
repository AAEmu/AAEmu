using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Sets the account's second password. The client sends the positions the player clicked rather than the
/// password, so it is read back through the key table it was drawn from before anything is stored.
/// </summary>
public class CSCreateSecondPasswordPacket() : GamePacket(CSOffsets.CSCreateSecondPasswordPacket, 1)
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
        var password = manager.Decode(connection.AccountId, (byte)TableIndex, Pass);
        if (password == null)
        {
            // No tables outstanding for this account, or a click that belongs to no table: refuse rather
            // than store something that cannot be read back.
            connection.SendPacket(new SCSecondPassCreatedPacket(false));
            return;
        }

        var created = manager.TryCreate(connection.AccountId, password);
        if (created)
            manager.Forget(connection.AccountId);

        connection.SendPacket(new SCSecondPassCreatedPacket(created));
    }
}