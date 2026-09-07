using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Stream;
using AAEmu.Game.Core.Packets.S2C;

namespace AAEmu.Game.Core.Packets.C2S;

public class CTUccCharacterNamePacket() : StreamPacket(CTOffsets.CTUccCharacterNamePacket)
{
    public override void Read(PacketStream stream)
    {
        if (stream.LeftBytes != sizeof(ulong))
        {
            Logger.Warn("UccCharacterName request has invalid size: {0}", stream.LeftBytes);
            return;
        }

        var id = stream.ReadUInt64();
        if (id > uint.MaxValue)
        {
            Logger.Warn("UccCharacterName request has invalid character Id: {0}", id);
            return;
        }

        var name = NameManager.Instance.GetCharacterName((uint)id);
        if (name != null)
            Connection.SendPacket(new TCUccCharNamePacket(id, name));

        Logger.Debug("UccCharacterName, Id: {0}, Name: {1}", id, name);
    }
}
