﻿
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Skills;

using AAEmu.Game.Models.Game.Units;


namespace AAEmu.Game.Models.Game.World
{
    public interface IWorldInteraction
    {

        void Execute(Unit caster, SkillCaster casterType, BaseUnit target, SkillCastTarget targetType, uint skillId, uint doodadId, DoodadFuncTemplate objectFunc); 

    }
    
    public enum WorldInteractionType
    {
        Looting = 0,
        Cutdown = 1,
        Seeding = 2,
        Watering = 3,
        Harvest = 4,
        Remove = 5,
        Cancel = 6,
        Error = 7,
        CheckWater = 8,
        CheckGrowth = 9,
        DigTerrain = 10,
        Spray = 11,
        LineSpray = 12,
        WaterLevel = 13,
        SummonMineSpot = 14,
        DigMine = 15,
        SummonCattle = 16,
        Shearing = 17,
        Feeding = 18,
        Use = 19,
        Butcher = 20,
        CraftStart = 21,
        CraftAct = 22,
        CraftInfo = 23,
        CraftGetItem = 24,
        CraftCancel = 25,
        DirectLoot = 26,
        Hang = 27,
        Binding = 28,
        SummonBeanstalk = 29,
        GiveQuest = 30,
        SummonDoodad = 31,
        CraftDefInteraction = 32,
        Mow = 33,
        CompleteQuest = 34,
        Building = 35,
        Dooring = 36,
        FurnitureMake = 37,
        RubberProcess = 38,
        SiegeWeaponMake = 39,
        MachineryAssemble = 40,
        ToolMake = 41,
        LumberProcess = 42,
        WeaponMake = 43,
        Tanning = 44,
        ArmorMake = 45,
        FodderMake = 46,
        DailyproductMake = 47,
        StoneProcess = 48,
        ArchiumExtract = 49,
        PotionMake = 50,
        Alchemy = 51,
        DyePurify = 52,
        Cooking = 53,
        GlassceramicMake = 54,
        OilExtract = 55,
        CostumeMake = 56,
        AccessoryMake = 57,
        BookBind = 58,
        FlourMill = 59,
        PaperMill = 60,
        SeasoningPurify = 61,
        MetalCast = 62,
        Weave = 63,
        MountMake = 64,
        PulpProcess = 65,
        GasExtract = 66,
        SkinOff = 67,
        CrystalCollect = 68,
        TreeproductCollect = 69,
        DairyCollect = 70,
        Catch = 71,
        FiberCollect = 72,
        OreMine = 73,
        RockMine = 74,
        MedicalingredientMine = 75,
        FruitPick = 76,
        DyeingredientCollect = 77,
        CropHarvest = 78,
        SeedCollect = 79,
        CerealHarvest = 80,
        SoilCollect = 81,
        SpiceCollect = 82,
        PlantCollect = 83,
        GroundBuild = 84,
        SoilFrameworkBuild = 85,
        PulpFrameworkBuild = 86,
        StoneFrameworkBuild = 87,
        InteriorFinishBuild = 88,
        ExteriorFinishBuild = 89,
        RepairHouse = 90,
        MachinePartsCollect = 91,
        MagicalEnchant = 92,
        RecoverItem = 93,
        Demolish = 94,
        CraftStartShip = 96,
        SummonDoodadWithUcc = 97,
        NaviDoodadRemove = 98,
        Throw = 99,
        Putdown = 100,
        Kick = 101,
        Grasp = 102,
        DeclareSiege = 103,
        BuySiegeTicket = 104,
        SellBackpack = 105
    }
}
