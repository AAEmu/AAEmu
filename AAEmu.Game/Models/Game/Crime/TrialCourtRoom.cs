using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Models.StaticValues;

using NLog;

namespace AAEmu.Game.Models.Game.Crime;

// Related info https://archeage-archive.fandom.com/wiki/Crime_and_Punishment

public class TrialCourtRoom
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public uint Id { get; set; }
    public string Name { get; set; } = "Court Room";
    public CourtRoomRegion Region { get; set; }
    public FactionsEnum Faction { get; set; }
    public NpcSpawner JudgeSpawner { get; set; } // Npc Template = 10895
    public Dictionary<int, Doodad> JurySeats { get; set; } = [];
    /// <summary>Storing these seat doodads, but not sure if we actually need these</summary>
    public List<Doodad> AudienceSeats { get; set; } = [];
    public HashSet<Character> AudienceMembers { get; set; } = []; 
    public WorldSpawnPosition Defendant { get; set; }
    public WorldSpawnPosition Jail { get; set; }
    public ChatChannel TrialChatChannel { get; set; }
    public TrialData CurrentTrial { get; set; } = null;

    // Nuian Main Court
    // Legal Secretary 12530
    // Judge 10894
    // 4937 - Seat 1
    // 4938 - Seat 2
    // 4939 - Seat 3
    // 4940 - Seat 4
    // 4941 - Seat 5
    // 5137 - Pew x 8 (Audience bench)
    // 4442 - Ergonomic Chair x 12 (Audience chair)

    // Nuian Alt Court
    // Judge 10894
    // 4942 - Seat 1
    // 4943 - Seat 2
    // 4944 - Seat 3
    // 4945 - Seat 4
    // 4946 - Seat 5
    // 5138 - Pew x 8 (Audience bench)
    // 4442 - Ergonomic Chair x 12 (Audience chair)

    // Haranyan Main Court
    // Legal Secretary 12531
    // Judge 10895
    // 4947 - Seat 1
    // 4948 - Seat 2
    // 4949 - Seat 3
    // 4950 - Seat 4
    // 4951 - Seat 5
    // 5139 - Pew x 9 (Audience bench)

    // Haranyan Alt Court
    // Judge 10895
    // 4952 - Seat 1
    // 4953 - Seat 2
    // 4954 - Seat 3
    // 4955 - Seat 4
    // 4956 - Seat 5
    // 5140 - Pew x 9 (Audience bench)
    public void ClearRoom()
    {
        CurrentTrial = null;
        JurySeats.Clear();
    }
}
