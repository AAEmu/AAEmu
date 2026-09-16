using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Asks the server to refresh the bot-check counters the client is showing. The counters travel with the
/// character state, which is the only packet known to carry them, so that is what the answer re-sends.
/// </summary>
/// <remarks>
/// The packet has no body. Every parameterless C2S type folds onto that one function, so a shared
/// serializer here is identical-COMDAT folding, not a base-class fall-through.
/// </remarks>
public class CSRefreshBotCheckInfoPacket() : GamePacket(CSOffsets.CSRefreshBotCheckInfoPacket, 1)
{
    public override void Read(PacketStream stream)
    {
    }

    public override void Execute()
    {
        var character = Connection?.ActiveChar;
        if (character == null)
            return;

        character.SendPacket(new SCCharacterStatePacket(character));
        Logger.Info("Bot check: refreshed {0} (remaining {1}, failed {2}, on trial {3})",
            character.Name, character.BotCheck.RemainChecks, character.BotCheck.FailedAnswers, character.BotCheck.OnTrial);
    }
}
