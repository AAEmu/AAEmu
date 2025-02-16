namespace AAEmu.Game.Models.Game.Units.Static;

// updated to version 5.0.7.0
public enum UnitReqsKindType : uint
{
    // Name, KindId,                  // Value1 , Value2, additional info
    // --- Below is used in 1.2 ---
    None = 0,                         // Unused
    Level = 1,                        // MinLevel, MaxLevel
    Ability = 2,                      // AbilityType, Level
    Race = 3,                         // RaceId
    Gender = 4,                       // Gender (1 male, 2 female), unused
    // 0x05
    // 0x06
    // 0x07
    EquipSlot = 8,                    // Slot, unused
    EquipItem = 9,                    // ItemId, unused
    OwnItem = 10,                     // ItemId, unused
    TrainedSkill = 11,                // SkillId?, unused?, there are no entries using this
    Combat = 12,                      // unused, unused, skill can only be used outside of combat
    Stealth = 13,                     // unused, unused, there are no entries for this? Must be in stealth mode maybe?
    Health = 14,                      // unused, unused, there are no entries using this
    Buff = 15,                        // BuffId, unused, must have buff active
    TargetBuff = 16,                  // BuffId, unused, target must have buff
    TargetCombat = 17,                // unused, unused, target must not be in combat
    // 0x12
    CanLearnCraft = 19,               // CraftId, unused, must not have learned craft
    DoodadRange = 20,                 // DoodadId, range, range seems to be in millimeters
    EquipShield = 21,                 // always 1, unused, have a shield equipped. 1 Might be the shield style?
    NoBuff = 22,                      // BuffId, 0 or 1, must not have the buff, don't know what the 0 or 1 means
    TargetBuffTag = 23,               // Buff Tag, unused, Target must have buff tag
    CorpseRange = 24,                 // range?, unused, there are no entries using this
    EquipWeaponType = 25,             // weapon type?, unused, there are no entries using this
    TargetHealthLessThan = 26,        // MinHp% (always 1), MaxHp%
    TargetNpc = 27,                   // NpcId, unused, must target Npc
    TargetDoodad = 28,                // DoodadId, unused, skill target must be a doodad
    EquipRanged = 29,                 // ranged weapon type, unused, ranged type: 0=Bow, 1=(any)Instrument
    NoBuffTag = 30,                   // Buff Tag, unused, must not have tag
    CompleteQuestContext = 31,        // Quest Component Id, unused, must have completed quest
    ProgressQuestContext = 32,        // Quest Component Id, unused, quest must be in progress
    ReadyQuestContext = 33,           // Quest Component Id, unused, quest must be in ready state
    TargetNpcGroup = 34,              // NpcGroupId, unknown, target must be from NpcGroup (don't know where to get those), unknown is 1 for one skill
    AreaSphere = 35,                  // SphereId, where 0=OutSide 1=Inside?
    ExceptCompleteQuestContext = 36,  // Quest Component Id, unused, most not have quest in completed state?
    PreCompleteQuestContext = 37,     // Quest Component Id, unused, quest must be before it's completed state?
    TargetOwnerType = 38,             // OwnerType(BaseUnitType), unused
    NotUnderWater = 39,               // unused, unused, must not be under water
    FactionMatch = 40,                // FactionId, unused, must be of faction
    Tod = 41,                         // TimeOfDay Start, TimeOfDay End, format is in-game hours x 100 (e.g. 1150 => 11h30)
    MotherFaction = 42,               // FactionId, unused, mother faction must be
    ActAbilityPoint = 43,             // ActAbilityId, Points, requires points amount of act ability to use
    CrimePoint = 44,                  // points min, points max
    HonorPoint = 45,                  // points min, points max
    LivingPoint = 50,                 // unused, unused, there are no entries using this
    CrimeRecord = 46,                 // min val, max val, val seems to be more of a standing rating that record count, 0=has crime point, 1= has no crime points, 9=???
    JuryPoint = 47,                   // can be jury, unknown, value1 seems like a "can be jury" flag, not sure about the 2nd
    SourceOwnerType = 48,             // unused, unused, no idea how this is supposed to be owner type, all values are 0 and only used for AiEvents
    Appellation = 49,                 // unused, unused, there are no entries using this
    InZone = 51,                      // Zone ID, unused, Must be inside Zone ID
    OutZone = 52,                     // Zone ID, unused, Must be outside Zone ID (unused)
    DominionOwner = 53,               // unused, unused, looks like this is meant for castle area rulers
    VerdictOnly = 54,                 // unused, unused, target must be suspect (used to catch bots)
    FactionMatchOnly = 55,            // FactionId, unused, must be of faction (used for pirates 161)
    MotherFactionOnly = 56,           // FactionId, unused, must be of given mother faction
    NationOwner = 57,                 // unused, unused, must be nation monarch
    FactionMatchOnlyNot = 58,         // FactionId, unused, must NOT be of faction
    MotherFactionOnlyNot = 59,        // FactionId, unused, must NOT be of given mother faction
    NationMember = 60,                // unused, unused, must be member of a player nation
    NationMemberNot = 61,             // unused, unused, must NOT be member of a player nation (unused)
    NationOwnerAtPos = 62,            // unused, unused, player nation owner needs to be in their owning zone
    DominionOwnerAtPos = 63,          // unused, unused, castle owner needs to be in their owning zone
    Housing = 64,                     // unused, unused, housing area id, housing type owned maybe? unused as it reference a non-existing quest
    HealthMargin = 65,                // Health Margin, unused, margin <= max - current, there are no entries using this
    ManaMargin = 66,                  // Mana Margin, unused, margin <= max - current, there are no entries using this
    LaborPowerMargin = 67,            // Labor Margin, unused, minimum amount of labor below cap (margin <= max - current)
    NotOnMovingPhysicalVehicle = 68,  // unused, unused, must not be driving/sitting on a moving vehicle
    MaxLevel = 69,                    // MaxLevel, unused, maximum allowed level to use
    ExpeditionOwner = 70,             // unused, unused, must be guild owner, unused
    ExpeditionMember = 71,            // unused, unused, must be guild member, unused
    ExceptProgressQuestContext = 72,  // Quest Component Id, unused, must not have started quest, used for mutually exclusive quests
    ExceptReadyQuestContext = 73,     // Quest Component Id, unused, must not have finished quest, used for mutually exclusive quests
    // --- Below is used in 3.0 ---
    OwnItemNot = 74,                  // ItemId, unused, must NOT own item, unused
    LessActAbilityPoint = 75,         // ActAbility, Points, unused
    OwnQuestItemGroup = 76,           // unused, unused, there are no entries using this
    LeadershipTotal = 77,             // unused, unused, there are no entries using this
    LeadershipCurrent = 78,           // unused, unused, there are no entries using this
    Hero = 79,                        // unused, unused, there are no entries using this
    DominionExpeditionMember = 80,    // unused, unused, there are no entries using this
    DominionNationMember = 81,        // unused, unused, there are no entries using this
    OwnItemCount = 82,                // unused, unused, there are no entries using this
    House = 83,                       // unused, unused, there are no entries using this
    //HouseOnly = 100,                  // unused, unused, there are no entries using this
    //DecoLimitExpanded = 101,          // unused, unused, there are no entries using this
    //NotExpandable = 102,              // unused, unused, there are no entries using this
    DoodadTargetFriendly = 84,        // unused, unused, there are no entries using this
    DoodadTargetHostile = 85,         // unused, unused, there are no entries using this
    DominionExpeditionMemberNot = 86, // unused, unused, there are no entries using this
    DominionMemberNot = 87,           // unused, unused, there are no entries using this
    InZoneGroupHousingExist = 88,     // unused, unused, there are no entries using this
    CannotUseBuildingHouse = 101,     // unused, unused, there are no entries using this
    TargetNoBuffTag = 89,             // unused, unused, there are no entries using this
    ExpeditionLevel = 90,             // unused, unused, there are no entries using this
    IsResident = 91,                  // unused, unused, there are no entries using this
    ResidentServicePoint = 92,        // unused, unused, there are no entries using this
    HighAbilityLevel = 93,            // unused, unused, there are no entries using this
    FamilyRole = 94,                  // unused, unused, there are no entries using this
    TargetManaLessThan = 95,          // unused, unused, there are no entries using this
    TargetManaMoreThan = 96,          // unused, unused, there are no entries using this
    TargetHealthMoreThan = 97,        // unused, unused, there are no entries using this
    BuffTag = 98,                     // unused, unused, there are no entries using this
    // --- Below is used in 5.0 ---
    LaborPowerMarginLocal = 99,       // unused, unused, there are no entries using this
    HouseOnly = 100,                  // unused, unused, there are no entries using this
    DecoLimitExpanded = 101,          // unused, unused, there are no entries using this
    NotExpandable = 102,              // unused, unused, there are no entries using this
    HeirLevel = 104,                  // unused, unused, there are no entries using this
    InZoneGroup = 105,                // unused, unused, there are no entries using this
    UnderWater = 106,                 // unused, unused, there are no entries using this
    OwnAppellation = 107,             // unused, unused, there are no entries using this
    EquipAppellation = 115,           // unused, unused, there are no entries using this
    FullRechargedLaborPower = 9000000 // unused, unused, there are no entries using this
}
