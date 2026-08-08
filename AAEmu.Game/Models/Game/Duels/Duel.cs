using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Tasks.Duels;

namespace AAEmu.Game.Models.Game.Duels;

public class Duel(Character challenger, Character challenged, byte duelType = 1)
{
    /// <summary>A plain 1v1. The client's start handler maps this to its "start" cue.</summary>
    public const byte NormalDuel = 1;

    /// <summary>A party duel - the client's "start_party_duel" cue.</summary>
    public const byte PartyDuel = 2;

    public Character Challenger { get; set; } = challenger; // это персонаж который вызвал нас на дуэль
    public Character Challenged { get; set; } = challenged; // это наш персонаж (т.е. connection.ActiveChar)

    /// <summary>
    /// Echoed back in SCDuelStarted. The client ignores that packet outright when this is 0, so it has
    /// to be carried from the challenge all the way to the start.
    /// </summary>
    public byte DuelType { get; set; } = duelType;
    public Doodad DuelFlag { get; set; }
    public DuelStartTask DuelStartTask { get; set; }

    /// <summary>Releases both players again if the invitation is never answered.</summary>
    public DuelRequestTimeoutTask DuelRequestTimeoutTask { get; set; }
    public DuelEndTimerTask DuelEndTimerTask { get; set; }
    public DuelDistanceСheckTask DuelDistanceСheckTask { get; set; }
    public DuelResultСheckTask DuelResultСheckTask { get; set; }
    public bool DuelStarted { get; set; } = false;
    public bool DuelAllowed { get; set; } = false;

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
