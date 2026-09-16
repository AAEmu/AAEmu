using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using NLog;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Asks for one ranker's appearance, which is what the ranking window's "view equipped gear" needs: a
/// line of a board names only who holds the place, so the client asks for the holder's looks by id.
/// </summary>
/// <remarks>
/// Field order and widths come from the 10.0.2.13 client's serializer, which passes each value's name
/// alongside the value, and from the window's own binding: the world the ranker is on, then the id the
/// client holds for them.
/// </remarks>
public class CSRankRankerAppearance() : GamePacket(CSOffsets.CSRankRankerAppearance, 1)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public sbyte Worldid { get; private set; }
    public ulong Type { get; private set; }

    public override void Read(PacketStream stream)
    {
        Worldid = stream.ReadSByte();
        Type = stream.ReadUInt64();
    }

    public override void Execute()
    {
        var character = Connection?.ActiveChar;
        if (character == null)
            return;

        // The window asks about the holder of a line it is showing. Only a holder the World is holding can
        // be described, so one it does not know is left unanswered rather than answered with a blank look.
        var ranker = WorldManager.Instance.GetAllCharacters()?.FirstOrDefault(c => c.Id == Type);
        if (ranker == null)
        {
            Logger.Info("Rankings: appearance for ranker {0} asked by {1}, not in world", Type, character.Name);
            return;
        }

        character.SendPacket(new SCRankerAppearancePacket(Worldid, ranker));
    }
}
