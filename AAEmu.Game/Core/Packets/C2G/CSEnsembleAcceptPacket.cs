using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Music;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Takes up an ensemble invitation. The body names the player who suggested it, so an answer can only
/// land on that ensemble.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each field
/// name alongside the value: bc (3 bytes).
/// </remarks>
public class CSEnsembleAcceptPacket() : GamePacket(CSOffsets.CSEnsembleAcceptPacket, 1)
{
    public uint Bc { get; private set; }

    public override void Read(PacketStream stream)
    {
        Bc = stream.ReadBc();
    }

    public override void Execute()
    {
        var character = Connection?.ActiveChar;
        if (character == null)
            return;

        var session = MusicManager.Instance.FindEnsemble(character.ObjId);
        if (session == null)
        {
            Logger.Warn("Ensemble: {0} accepted {1}, who is not suggesting anything", character.Name, Bc);
            return;
        }

        // An answer names the suggester: refuse to fold it onto a different ensemble.
        if (session.MaestroBc != Bc)
        {
            Logger.Warn("Ensemble: {0} accepted {1} but their ensemble is led by {2}",
                character.Name, Bc, session.MaestroBc);
            return;
        }

        var result = MusicManager.Instance.AcceptEnsemble(character);
        Logger.Info("Ensemble: {0} accepted {1}: {2}", character.Name, Bc, result);
    }
}
