using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Music;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Turns an ensemble invitation down. The packet carries nothing: the player answering is the one who
/// was asked, so their ensemble is the one to answer.
/// </summary>
/// <remarks>
/// The packet has no body. Every parameterless C2S type folds onto that one function, so the shared
/// address is identical-COMDAT folding, not a base-class fall-through.
/// </remarks>
public class CSEnsembleRejectPacket() : GamePacket(CSOffsets.CSEnsembleRejectPacket, 1)
{
    public override void Read(PacketStream stream)
    {
    }

    public override void Execute()
    {
        var character = Connection?.ActiveChar;
        if (character == null)
            return;

        var result = MusicManager.Instance.RejectEnsemble(character);
        Logger.Info("Ensemble: {0} rejected their invitation: {1}", character.Name, result);
    }
}
