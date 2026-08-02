using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSCharDetailPacket() : GamePacket(CSOffsets.CSCharDetailPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var name = stream.ReadString();
        Logger.Debug("CharDetail, Name: {0}", name);

        var characterId = string.IsNullOrWhiteSpace(name)
            ? 0u
            : NameManager.Instance.GetCharacterId(name.NormalizeName());
        if (characterId == 0)
        {
            Connection.SendPacket(new SCCharDetailPacket());
            return;
        }

        var character = WorldManager.Instance.GetCharacterById(characterId);
        character ??= Character.Load(characterId);

        Connection.SendPacket(character == null
            ? new SCCharDetailPacket()
            : new SCCharDetailPacket(character));
    }
}
