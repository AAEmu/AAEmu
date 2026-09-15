using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// The defendant cuts their final statement short, so the bench can vote right away instead of
/// waiting out the statement clock.
/// </summary>
public class CSSkipFinalStatementPacket() : GamePacket(CSOffsets.CSSkipFinalStatementPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        // The body is a u64 trial id, like every other trial request the client sends.
        var trial = stream.ReadUInt64();

        TrialManager.Instance.OnSkipFinalStatement(Connection.ActiveChar, trial);
    }
}
