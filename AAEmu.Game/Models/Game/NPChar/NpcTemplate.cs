using System.Collections.Generic;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.NPChar
{
    public enum NpcGradeType : byte
    {
        Normal = 1,
        Elite = 2,
        BossA = 3,
        BossB = 4,
        BossC = 5,
        Weak = 6,
        Strong = 7,
        BossS = 8
    }
    
    public enum NpcKindType : byte
    {
        Human = 1,
        Beast = 2,
        Undead = 3,
        Devil = 4,
        Spirit = 5,
        Dragon = 8,
        SiegeWeapon = 9,
        Ship = 10,
        Horse = 11,
        Carriage = 12,
        Fantastic = 13,
        Machine = 14,
        Unknown = 15
    }
    
    public enum NpcTemplateType : byte
    {
        Tanker = 1,
        Caster = 2,
        PDealer = 3,
        MDealer = 4,
        DungeonTanker = 5,
        DungeonCaster = 6,
        DungeonPDealer = 7,
        DungeonMDealer = 8,
        Default = 9
    }
    
    public class NpcTemplate
    {
        public uint Id { get; set; }
        public string Name { get; set; }
        public int CharRaceId { get; set; }
        public NpcGradeType NpcGradeId { get; set; }
        public NpcKindType NpcKindId { get; set; }
        public byte Level { get; set; }
        public NpcTemplateType NpcTemplateId { get; set; }
        public uint ModelId { get; set; }
        public uint FactionId { get; set; }
        public bool SkillTrainer { get; set; }
        public int AiFileId { get; set; }
        public bool Merchant { get; set; }
        public int NpcNicknameId { get; set; }
        public bool Auctioneer { get; set; }
        public bool ShowNameTag { get; set; }
        public bool VisibleToCreatorOnly { get; set; }
        public bool NoExp { get; set; }
        public int PetItemId { get; set; }
        public int BaseSkillId { get; set; }
        public bool TrackFriendship { get; set; }
        public bool Priest { get; set; }
        public int NpcTedencyId { get; set; }
        public bool Blacksmith { get; set; }
        public bool Teleporter { get; set; }
        public float Opacity { get; set; }
        public bool AbilityChanger { get; set; }
        public float Scale { get; set; }
        public float SightRangeScale { get; set; }
        public float SightFovScale { get; set; }
        public int MilestoneId { get; set; }
        public float AttackStartRangeScale { get; set; }
        public bool Aggression { get; set; }
        public float ExpMultiplier { get; set; }
        public int ExpAdder { get; set; }
        public bool Stabler { get; set; }
        public bool AcceptAggroLink { get; set; }
        public int RecrutingBattlefieldId { get; set; }
        public float ReturnDistance { get; set; }
        public int NpcAiParamId { get; set; }
        public bool NonPushableByActor { get; set; }
        public bool Banker { get; set; }
        public int AggroLinkSpecialRuleId { get; set; }
        public float AggroLinkHelpDist { get; set; }
        public bool AggroLinkSightCheck { get; set; }
        public bool Expedition { get; set; }
        public int HonorPoint { get; set; }
        public bool Trader { get; set; }
        public bool AggroLinkSpecialGuard { get; set; }
        public bool AggroLinkSpecialIgnoreNpcAttacker { get; set; }
        public float AbsoluteReturnDistance { get; set; }
        public bool Repairman { get; set; }
        public bool ActivateAiAlways { get; set; }
        public bool Specialty { get; set; }
        public bool UseRangeMod { get; set; }
        public int NpcPostureSetId { get; set; }
        public int MateEquipSlotPackId { get; set; }
        public int MateKindId { get; set; }
        public int EngageCombatGiveQuestId { get; set; }
        public bool NoApplyTotalCustom { get; set; }
        public bool BaseSkillStrafe { get; set; }
        public float BaseSkillDelay { get; set; }
        public int NpcInteractionSetId { get; set; }
        public bool UseAbuserList { get; set; }
        public bool ReturnWhenEnterHousingArea { get; set; }
        public bool LookConverter { get; set; }
        public bool UseDDCMSMountSkill { get; set; }
        public bool CrowdEffect { get; set; }
        public uint AnimActionId { get; set; }
        public byte Race { get; set; }
        public byte Gender { get; set; }
        public uint MerchantPackId { get; set; }

        public uint HairId { get; set; }
        public UnitCustomModelParams ModelParams { get; set; }
        public EquipItemsTemplate Items { get; set; }
        public (uint ItemId, bool NpcOnly)[] BodyItems { get; set; }
        public List<uint> Buffs { get; set; }
        public List<BonusTemplate> Bonuses { get; set; }

        public NpcTemplate()
        {
            HairId = 0;
            Items = new EquipItemsTemplate();
            ModelParams = new UnitCustomModelParams();
            BodyItems = new (uint, bool)[7];
            Buffs = new List<uint>();
            Bonuses = new List<BonusTemplate>();
        }
    }
}
