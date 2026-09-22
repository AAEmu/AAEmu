using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// X2Nation:ResponseDiplomacy(ok): the asked hero's answer. The dialog's own 60 s timer sends false
/// (faction_relations.lua TimeOutPoc), but it starts when the packet arrives, so that false lands at
/// or after content_configs 288 (faction_diplomacy_dialog_timeout, 60 s) and the manager ignores it
/// in silence rather than counting a denial.
/// </summary>
/// <remarks>Body serializer x2game-dev.dll 0x39c550a0: one bool named "ok" (send site 0x391c4bb0).</remarks>
public class CSFactionRelationResponsePacket() : GamePacket(CSOffsets.CSFactionRelationResponsePacket, 1)
{
    public bool Ok { get; private set; }

    public override void Read(PacketStream stream)
    {
        Ok = stream.ReadBoolean();

        var character = Connection?.ActiveChar;
        if (character == null)
            return;

        FactionDiplomacyManager.Instance.Respond(character, Ok);
    }
}
