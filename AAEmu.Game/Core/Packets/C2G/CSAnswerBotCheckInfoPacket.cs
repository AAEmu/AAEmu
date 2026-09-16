using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// The player's answer to a bot check. It spends one of the pending checks; whether the answer was right
/// stays undecided, because the packet that carries the question is not identified yet — so nothing counts
/// failures until it is.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each field
/// name alongside the value.
/// </remarks>
public class CSAnswerBotCheckInfoPacket() : GamePacket(CSOffsets.CSAnswerBotCheckInfoPacket, 1)
{
    public string Answer { get; private set; }

    public override void Read(PacketStream stream)
    {
        Answer = stream.ReadString();
    }

    public override void Execute()
    {
        var character = Connection?.ActiveChar;
        if (character == null)
            return;

        if (!character.BotCheck.RecordAnswer())
        {
            Logger.Warn("Bot check: {0} answered '{1}' with no check pending", character.Name, Answer);
            return;
        }

        Logger.Info("Bot check: {0} answered '{1}', {2} check(s) left",
            character.Name, Answer, character.BotCheck.RemainChecks);
    }
}
