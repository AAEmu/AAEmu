using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Squad;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSCreateSquadPacket() : GamePacket(CSOffsets.CSCreateSquadPacket, 1)
{
    public SquadFieldType Field { get; private set; }
    public uint CatalogId => Field.InstanceId;
    public SquadOpenType OpenType { get; private set; }
    public bool PartyInvitation { get; private set; }
    public string Explanation { get; private set; } = "";
    public byte LimitLevel { get; private set; }
    public int LimitGearScore { get; private set; }

    public override void Read(PacketStream stream)
    {
        Field = SquadFieldTypeWire.Read(stream);
        OpenType = (SquadOpenType)stream.ReadInt32();
        PartyInvitation = stream.ReadBoolean();
        Explanation = stream.ReadString();
        LimitLevel = stream.ReadByte();
        LimitGearScore = stream.ReadInt32();
        var character = Connection.ActiveChar;
        if (character == null)
            return;
        Logger.Info(
            "CSCreateSquad char={0} kind={1} instance={2} value={3} openType={4} partyInv={5} limitLv={6} limitGs={7}",
            character.Name, Field.Kind, Field.InstanceId, Field.Value, OpenType, PartyInvitation,
            LimitLevel, LimitGearScore);
        SquadManager.Instance.Create(character, Field, OpenType, PartyInvitation,
            Explanation, LimitLevel, LimitGearScore);
    }
}
