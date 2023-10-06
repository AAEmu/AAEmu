using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Tasks.Duels;

namespace AAEmu.Game.Models.Game.Duels;

public class Duel
{
    public Character Challenger { get; set; }
    public Character Challenged { get; set; }
    public Doodad DuelFlag { get; set; }
    public DuelStartTask DuelStartTask { get; set; }
    public DuelEndTimerTask DuelEndTimerTask { get; set; }
    public DuelDistanceСheckTask DuelDistanceСheckTask { get; set; }
    public DuelResultСheckTask DuelResultСheckTask { get; set; }
    public bool DuelStarted { get; set; } = false;
    public bool DuelAllowed { get; set; } = false;

    public Duel(Character challenger, Character challenged)
    {
        Challenger = challenger; // это персонаж который вызвал нас на дуэль
        Challenged = challenged; // это наш персонаж (т.е. connection.ActiveChar)
    }

    public void SendPacketsBoth(GamePacket packet)
    {
        // нужен когда пакеты одинаковы у обоих персонажей
        Challenger.SendPacket(packet); // по типу Broadcast только тем, кто в дуэли
        Challenged.SendPacket(packet);
    }
    public void SendPacketChallenger(GamePacket packet)
    {
        Challenger.SendPacket(packet); // только вызвавшему дуэль
    }
    public void SendPacketChallenged(GamePacket packet)
    {
        Challenged.SendPacket(packet); // только вызываемому на дуэль
    }
}
