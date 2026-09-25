using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Music;

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
    public byte[] Data { get; private set; } = [];

    public override void Read(PacketStream stream)
    {
        Bc = stream.ReadBc();
        Bc2 = stream.ReadBc();
        Size = stream.ReadUInt32();

        // The part is a standard MIDI file, not text: the unsigned size is followed by the
        // serializer's length-prefixed blob, which puts a u16 block length in front of the bytes. The
        // block length must agree with the declared size, otherwise the two disagree about where the
        // payload ends. Decode nothing and do not trim NULs; a malformed body stays empty so Execute
        // rejects it without mutating the session.
        var headerSize = sizeof(uint) + sizeof(ushort);
        if (stream.Overran || Size == 0 || Size > EnsembleSession.MaximumPartBytes ||
            stream.LeftBytes < (int)headerSize)
        {
            Data = [];
            return;
        }

        var blockSize = stream.ReadUInt16();
        if (stream.Overran || blockSize == 0 || blockSize != (ushort)Size)
        {
            Data = [];
            return;
        }

        if (stream.LeftBytes < blockSize)
        {
            Data = [];
            return;
        }

        var dataBytes = stream.ReadBytes(blockSize);
        if (stream.Overran || dataBytes.Length != blockSize || stream.LeftBytes != 0)
        {
            Data = [];
            return;
        }

        Data = dataBytes;
    }

    public override void Execute()
    {
        var character = Connection?.ActiveChar;
        if (character == null)
            return;

        if (!MusicManager.Instance.EnsemblePartReady(character, Bc, Bc2, Size, Data))
            Logger.Warn("Ensemble: {0} sent a part that belongs to no ensemble of theirs", character.Name);
    }
}
