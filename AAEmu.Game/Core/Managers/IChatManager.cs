using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.Crime;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Team;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Managers;

public interface IChatManager : IInitializable
{
    List<ChatChannel> ListAllChannels();
    void LeaveAllChannels(Character character);
    int CleanUpChannels();
    ChatChannel GetFactionChat(FactionsEnum factionMotherId);
    ChatChannel GetFactionChat(Character character);
    ChatChannel GetNationChat(Race race);
    ChatChannel GetNationChat(Character character);
    ChatChannel GetZoneChat(uint zoneKey);
    ChatChannel GetGuildChat(Expedition guild);
    ChatChannel GetFamilyChat(uint familyId);
    ChatChannel GetPartyChat(Team party, Character myChar);
    ChatChannel GetRaidChat(Team party);
    ChatChannel GetTrialChat(Character character);
    ChatChannel GetTrialChat(CourtRoomRegion courtRegion);
}
