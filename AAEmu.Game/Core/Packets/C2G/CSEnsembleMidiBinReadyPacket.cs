using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// A member's part in the ensemble. The maestro collects them; once every member has sent one the
/// ensemble is told to play.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each field
/// name alongside the value.
/// </remarks>
public class CSEnsembleMidiBinReadyPacket() : GamePacket(CSOffsets.CSEnsembleMidiBinReadyPacket, 1)
{
    public uint Bc { get; private set; }
    public uint Bc2 { get; private set; }
    public uint Size { get; private set; }
    public string Data { get; private set; }

    public override void Read(PacketStream stream)
    {
        Bc = stream.ReadBc();
        Bc2 = stream.ReadBc();
        Size = stream.ReadUInt32();
        Data = stream.ReadString();
    }

    public override void Execute()
    {
        var character = Connection?.ActiveChar;
        if (character == null)
            return;

        if (!MusicManager.Instance.EnsemblePartReady(character, Data))
            Logger.Warn("Ensemble: {0} sent a part that belongs to no ensemble of theirs", character.Name);
    }
}
