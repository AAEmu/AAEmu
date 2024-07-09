using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSCompletedCinemaPacket : GamePacket
{
    public CSCompletedCinemaPacket() : base(CSOffsets.CSCompletedCinemaPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        // Empty struct
        Logger.Warn("CompletedCinema");

        WorldManager.ResendVisibleObjectsToCharacter(Connection.ActiveChar);
        Connection.ActiveChar.Events.OnCinemaEnded(Connection.ActiveChar, new OnCinemaEndedArgs() { CinemaId = Connection.ActiveChar.CurrentlyPlayingCinemaId });
    }
}
