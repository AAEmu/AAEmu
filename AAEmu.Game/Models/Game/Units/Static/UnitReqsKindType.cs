namespace AAEmu.Game.Models.Game.Units.Static;

public enum UnitReqsKindType : uint
{
    // Name , KindId,                   // Value1 , Value2, additional info
    None = 0x00,                        // Unused
    Level = 0x01,                       // MinLevel, MaxLevel
    Ability = 0x02,                     // AbilityType, Level
    Race = 0x03,                        // RaceId
    Gender = 0x04,                      // Gender (1 male, 2 female), unused
    // 0x05
    // 0x06
    // 0x07
    EquipSlot = 0x08,                   // Slot, unused
    EquipItem = 0x09,                   // ItemId, unused
    OwnItem = 0x0A,                     // ItemId, unused
    TrainedSkill = 0x0B,                // SkillId?, unused?, there are no entries using this
    Combat = 0x0C,                      // unused, unused, skill can only be used outside of combat
    Stealth = 0x0D,                     // unused, unused, there are no entries for this? Must be in stealth mode maybe?
    Health = 0x0E,                      // unused, unused, there are no entries using this
    Buff = 0x0F,                        // BuffId, unused, must have buff active
    TargetBuff = 0x10,                  // BuffId, unused, target must have buff
    TargetCombat = 0x11,                // unused, unused, target must not be in combat
    // 0x12
    CanLearnCraft = 0x13,               // CraftId, unused, must not have learned craft
    DoodadRange = 0x14,                 // DoodadId, range, range seems to be in millimeters
    EquipShield = 0x15,                 // always 1, unused, have a shield equipped. 1 Might be the shield style?
    NoBuff = 0x16,                      // BuffId, 0 or 1, must not have the buff, don't know what the 0 or 1 means
    TargetBuffTag = 0x17,               // Buff Tag, unused, Target must have buff tag
    CorpseRange = 0x18,                 // range?, unused, there are no entries using this
    EquipWeaponType = 0x19,             // weapon type?, unused, there are no entries using this
    TargetHealthLessThan = 0x1A,        // MinHp% (always 1), MaxHp%
    TargetNpc = 0x1B,                   // NpcId, unused, must target Npc
    TargetDoodad = 0x1C,                // DoodadId, unused, skill target must be a doodad
    EquipRanged = 0x1D,                 // ranged weapon type, unused, ranged type: 0=Bow, 1=(any)Instrument
    NoBuffTag = 0x1E,                   // Buff Tag, unused, must not have tag
    CompleteQuestContext = 0x1F,        // QuestId, unused, must have completed quest
    ProgressQuestContext = 0x20,        // QuestId, unused, quest must be in progress
    ReadyQuestContext = 0x21,           // QuestId, unused, quest must be in ready state
    TargetNpcGroup = 0x22,              // NpcGroupId, unknown, target must be from NpcGroup (don't know where to get those), unknown is 1 for one skill
    AreaSphere = 0x23,                  // SphereId, where 0=OutSide 1=Inside?
    ExceptCompleteQuestContext = 0x24,  // QuestId, unused, most not have quest in completed state?
    PreCompleteQuestContext = 0x25,     // QuestId, unused, quest must be before it's completed state?
    TargetOwnerType = 0x26,             // OwnerType(BaseUnitType), unused
    NotUnderWater = 0x27,               // unused, unused, must not be under water
    FactionMatch = 0x28,                // FactionId, unused, must be of faction
    Tod = 0x29,                         // TimeOfDay Start, TimeOfDay End, format is in-game hours x 100 (e.g. 1150 => 11h30)
    MotherFaction = 0x2A,               // FactionId, unused, mother faction must be
    ActAbilityPoint = 0x2B,             // ActAbilityId, Points, requires points amount of act ability to use
    CrimePoint = 0x2C,                  // points min, points max
    HonorPoint = 0x2D,                  // points min, points max
    CrimeRecord = 0x2E,                 // min val, max val, val seems to be more of a standing rating that record count, 0=has crime point, 1= has no crime points, 9=???
    JuryPoint = 0x2F,                   // can be jury, unknown, value1 seems like a "can be jury" flag, not sure about the 2nd
    SourceOwnerType = 0x30,             // unused, unused, no idea how this is supposed to be owner type, all values are 0 and only used for AiEvents
    Appellation = 0x31,                 // unused, unused, there are no entries using this
    LivingPoint = 0x32,                 // unused, unused, there are no entries using this
    InZone = 0x33,                      // Zone ID, unused, Must be inside Zone ID
    OutZone = 0x34,                     // Zone ID, unused, Must be outside Zone ID (unused)
    DominionOwner = 0x35,               // unused, unused, looks like this is meant for castle area rulers
    VerdictOnly = 0x36,                 // unused, unused, target must be suspect (used to catch bots)
    FactionMatchOnly = 0x37,            // FactionId, unused, must be of faction (used for pirates 161)
    MotherFactionOnly = 0x38,           // FactionId, unused, must be of given mother faction
    NationOwner = 0x39,                 // unused, unused, must be nation monarch
    FactionMatchOnlyNot = 0x3A,         // FactionId, unused, must NOT be of faction
    MotherFactionOnlyNot = 0x3B,        // FactionId, unused, must NOT be of given mother faction
    NationMember = 0x3C,                // unused, unused, must be member of a player nation
    NationMemberNot = 0x3D,             // unused, unused, must NOT be member of a player nation (unused)
    NationOwnerAtPos = 0x3E,            // unused, unused, player nation owner needs to be in their owning zone
    DominionOwnerAtPos = 0x3F,          // unused, unused, castle owner needs to be in their owning zone
    Housing = 0x40,                     // unused, unused, housing area id, housing type owned maybe? unused as it reference a non-existing quest
    HealthMargin = 0x41,                // Health Margin, unused, margin <= max - current, there are no entries using this
    ManaMargin = 0x42,                  // Mana Margin, unused, margin <= max - current, there are no entries using this
    LaborPowerMargin = 0x43,            // Labor Margin, unused, minimum amount of labor below cap (margin <= max - current)
    NotOnMovingPhysicalVehicle = 0x44,  // unused, unused, must not be driving/sitting on a moving vehicle
    MaxLevel = 0x45,                    // MaxLevel, unused, maximum allowed level to use
    ExpeditionOwner = 0x46,             // unused, unused, must be guild owner, unused
    ExpeditionMember = 0x47,            // unused, unused, must be guild member, unused
    ExceptProgressQuestContext = 0x48,  // QuestId, unused, must not have started quest, used for mutually exclusive quests
    ExceptReadyQuestContext = 0x49,     // QuestId, unused, must not have finished quest, used for mutually exclusive quests
    // --- Below is not used in 1.2 ---
    OwnItemNot = 0x4A,                  // ItemId, unused, must NOT own item, unused
    LessActAbilityPoint = 0x4B,         // ActAbility, Points, unused
    OwnQuestItemGroup = 0x4C,           // unused, unused, there are no entries using this
    LeadershipTotal = 0x4D,             // unused, unused, there are no entries using this
    LeadershipCurrent = 0x4E,           // unused, unused, there are no entries using this
    Hero = 0x4F,                        // unused, unused, there are no entries using this
    DominionExpeditionMember = 0x50,    // unused, unused, there are no entries using this
    DominionNationMember = 0x51,        // unused, unused, there are no entries using this
    OwnItemCount = 0x52,                // unused, unused, there are no entries using this
    House = 0x53,                       // unused, unused, there are no entries using this
    DoodadTargetFriendly = 0x54,        // unused, unused, there are no entries using this
    DoodadTargetHostile = 0x55,         // unused, unused, there are no entries using this
    DominionExpeditionMemberNot = 0x56, // unused, unused, there are no entries using this
    DominionMemberNot = 0x57,           // unused, unused, there are no entries using this
    InZoneGroupHousingExist = 0x58,     // unused, unused, there are no entries using this
    TargetNoBuffTag = 0x59,             // unused, unused, there are no entries using this
    ExpeditionLevel = 0x5A,             // unused, unused, there are no entries using this
    IsResident = 0x5B,                  // unused, unused, there are no entries using this
    ResidentServicePoint = 0x5C,        // unused, unused, there are no entries using this
    HighAbilityLevel = 0x5D,            // unused, unused, there are no entries using this
    FamilyRole = 0x5E,                  // unused, unused, there are no entries using this
    TargetManaLessThan = 0x5F,          // unused, unused, there are no entries using this
    TargetManaMoreThan = 0x60,          // unused, unused, there are no entries using this
    TargetHealthMoreThan = 0x61,        // unused, unused, there are no entries using this
    BuffTag = 0x62,                     // unused, unused, there are no entries using this
    LaborPowerMarginLocal = 0x63,       // unused, unused, there are no entries using this
}
