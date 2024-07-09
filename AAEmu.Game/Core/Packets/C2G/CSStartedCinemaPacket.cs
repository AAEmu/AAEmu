using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSStartedCinemaPacket : GamePacket
{
    public CSStartedCinemaPacket() : base(CSOffsets.CSStartedCinemaPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        // Empty struct
        Logger.Warn("StartedCinema");
        Connection.ActiveChar.Events.OnCinemaStarted(Connection.ActiveChar, new OnCinemaStartedArgs() { CinemaId = Connection.ActiveChar.CurrentlyPlayingCinemaId });
    }
}
