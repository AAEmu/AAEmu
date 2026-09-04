using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Squad;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSApplySquadMatchingPacket() : GamePacket(CSOffsets.CSApplySquadMatchingPacket, 1)
{
    public SquadFieldType Field { get; private set; }

    public uint CatalogId => Field.InstanceId;

    public override void Read(PacketStream stream)
    {
        Field = SquadFieldTypeWire.Read(stream);
        var character = Connection.ActiveChar;
        if (character == null)
            return;
        Logger.Info("CSApplySquadMatching char={0} kind={1} instance={2} value={3}",
            character.Name, Field.Kind, Field.InstanceId, Field.Value);
        SquadManager.Instance.ApplyMatching(character, CatalogId);
    }
}
