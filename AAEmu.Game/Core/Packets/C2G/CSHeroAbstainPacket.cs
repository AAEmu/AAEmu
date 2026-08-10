using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// A candidate declining to stand, from the withdrawal dialog in the Hero Election window.
/// </summary>
/// <remarks>
/// One u64, which the client's writer (.text 0xaacea0) calls "type". X2Hero:RequestAbstain takes no
/// argument (election.lua:277), so the client fills it in itself; it is read and ignored, and the
/// withdrawing character is taken from the connection. Trusting an id off the wire here would let a
/// crafted packet withdraw somebody else's candidacy.
/// </remarks>
public class CSHeroAbstainPacket() : GamePacket(CSOffsets.CSHeroAbstainPacket, 1)
{
    public ulong TypeValue { get; private set; }

    public override void Read(PacketStream stream)
    {
        TypeValue = stream.ReadUInt64();

        var character = Connection?.ActiveChar;
        if (character == null)
            return;

        var result = HeroElectionManager.Instance.Abstain(character);
        if (result != HeroElectionManager.AbstainResult.Ok)
        {
            character.SendMessage(HeroElectionManager.Explain(result));
            Logger.Debug("HeroAbstain from {0} refused: {1}", character.Name, result);
            return;
        }

        // The window stays open on the dialog's OK, so refresh it in place rather than reopening: the
        // withdrawing candidate has just removed themselves from the list they are looking at.
        HeroElectionManager.Instance.SendBallot(character, openWindow: false);
    }
}
