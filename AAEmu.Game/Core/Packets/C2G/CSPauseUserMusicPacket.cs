using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// The client reporting that a performance is over: the score window was closed, or the player
/// paused or stopped the song.
/// </summary>
/// <remarks>
/// packet has no body. The play buffs an instrument leaves on the player have no duration of their
/// own, so this is what ends the playing pose and lets nearby clients stop the sound; a song that
/// simply runs out ends here as well when the client has no "Close the Score" cast to send.
/// </remarks>
public class CSPauseUserMusicPacket() : GamePacket(CSOffsets.CSPauseUserMusicPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        // No body. State changes belong to Execute so parsing cannot mutate the live world.
    }

    public override void Execute()
    {
        var character = Connection?.ActiveChar;
        if (character == null)
            return;

        MusicManager.EndPerformance(character);

        // A player who stops playing is done with their ensemble too, whether they led it or played in it.
        MusicManager.Instance.LeaveEnsemble(character);
    }
}
