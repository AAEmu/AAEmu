using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Opens/refreshes a guild's prestige-shop buff view. <see cref="TypeValue"/> is the expedition id
/// (recruit-board "Buff Effect" and own-guild shop both send it). Must not require the viewer to
/// already be in a guild — unguilded applicants need the listed guild's grades.
/// </summary>
public class CSExpeditionBuffPacket() : GamePacket(CSOffsets.CSExpeditionBuffPacket, 1)
{
    public int TypeValue { get; private set; }
    public bool Enter { get; private set; }
    public bool ResponseOnly { get; private set; }

    public override void Read(PacketStream stream)
    {
        TypeValue = stream.ReadInt32();
        Enter = stream.ReadBoolean();
        ResponseOnly = stream.ReadBoolean();

        var character = Connection.ActiveChar;
        if (character == null)
            return;

        var expeditionId = ExpeditionBuffViewRules.ResolveExpeditionId(
            TypeValue, (uint)(character.Expedition?.Id ?? 0));
        if (expeditionId != 0)
            ExpeditionManager.Instance.SendExpeditionBuffs(character, expeditionId);
    }
}
