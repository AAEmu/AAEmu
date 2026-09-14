using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Expeditions.Recruitment;

namespace AAEmu.Game.Core.Packets.G2C;

public sealed class SCExpeditionApplicantsGetPacket(
    IReadOnlyList<(ExpeditionRecruitmentApplication Application, ExpeditionJoinCandidate Character)> rows)
    : GamePacket(SCOffsets.SCExpeditionApplicantsGetPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((uint)rows.Count);
        stream.Write((uint)rows.Count);
        foreach (var (application, character) in rows)
        {
            stream.Write((ulong)character.CharacterId);
            stream.Write(character.Name);
            stream.Write(character.Level);
            stream.Write(character.HeirLevel);
            stream.Write((int)character.FactionId);
            stream.Write(application.Memo);
            stream.Write(application.RegisteredAt);
        }
        return stream;
    }
}
