using System;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using Quartz.Impl.AdoJobStore;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public interface ISpecialEffect
    {
        void Execute(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj, CastAction castObj,
            Skill skill, SkillObject skillObject, DateTime time, int Value1, int Value2, int Value3, int Value4);
    }

    public enum SpecialEffectType
    {
        BuffApply = 1,
        Unk2 = 2,
        Unk3 = 3,
        Unk4 = 4,
        SkillStop = 5, // TODO inspect
        Unk6 = 6,
        Unk7 = 7,
        Unk8 = 8,
        Unk9 = 9,
        Unk10 = 10,
        MoveForward = 11,
        Unk12 = 12,
        MoveBackward = 13,
        Unk14 = 14,
        DamageEffectApply = 15, // TODO inspect
        BuffSteal = 16,
        Unk17 = 17,
        Unk18 = 18,
        UnkEffectApply = 19,
        HoldableEnhance = 20, // grade weapeon
        WearableEnhance = 21, // grade equip
        Resurrection = 22,
        Unk23 = 23,
        PetCall = 24,
        Teleport = 25,
        Unk26 = 26,
        SpecialEffectApply = 27, // TODO inspect
        AddExp = 28,
        Unk29 = 29,
        SavePortal = 30,
        Unk31 = 31,
        Unk32 = 32,
        SkillUse = 33,
        Unk34 = 34,
        Unk35 = 35,
        Unk36 = 36,
        Unk37 = 37,
        Unk38 = 38,
        Unk39 = 39,
        Unk40 = 40,
        Unk41 = 41,
        AnimPlay = 42, // TODO inspect
        SkillCooldownReset = 43,
        Unk44 = 44,
        ItemUse = 45,
        DamageOnBadBuff = 46,
        Unk47 = 47,
        Unk48 = 48,
        LaborPowerConsume = 49,
        Unk50 = 50,
        PetResurrection = 51,
        Unk52 = 52,
        Unk53 = 53,
        UnderwaterBreath = 54,
        Unk55 = 55,
        PetHpMpRestore = 56,
        Unk57 = 57,
        Unk58 = 58,
        Unk59 = 59,
        SlaveCall = 60,
        Unk61 = 61,
        Unk62 = 62,
        Unk63 = 63,
        CouponReceive = 64,
        ReturnToBase = 65,
        Unk66 = 66,
        Unk67 = 67,
        Unk68 = 68,
        GetCoords = 69,
        Unk70 = 70,
        NationAllianceCreate = 71,
        ShipDestroy = 72,
        Unk73 = 73,
        ReturnToResPoint = 74,
        Complaint = 75,
        Unk76 = 76,
        Unk77 = 77,
        Unk78 = 78,
        Unk79 = 79,
        Unk80 = 80,
        Unk81 = 81,
        Unk82 = 82,
        Unk83 = 83,
        Unk84 = 84,
        Unk85 = 85,
        Unk86 = 86,
        CraftReputationAdd = 87,
        Petition = 88,
        Arrest = 89,
        PetRecall = 90,
        CreateCharacterCount = 91,
        ItemGradeEnchanting = 92,
        PlayMusic = 93,
        StopMusic = 94,
        WashItem = 95,
        ItemMake = 96,
        WriteMusic = 97,
        DyeItem = 98,
        ReceiveLuluLeaflet = 99,
        Unk100 = 100,
        HairMaker = 101,
        LaborPowerAdd = 102,
        RestoreItemLook = 103,
        Unk104 = 104,
        Unk105 = 105,
        RuneItem = 106,
        GenderChange = 107,
        Unk108 = 108,
        Unk109 = 109,
        Unk110 = 110,
        Unk111 = 111,
        Unk112 = 112,
        Unk113 = 113,
        Unk114 = 114,
        ExitIndun = 115,
        Unk116 = 116,
        Unk117 = 117
    }

    public class SpecialEffect : EffectTemplate
    {
        public SpecialEffectType SpecialEffectTypeId { get; set; } // TODO inspect
        public int Value1 { get; set; }
        public int Value2 { get; set; }
        public int Value3 { get; set; }
        public int Value4 { get; set; }

        public override bool OnActionTime => false;

        public override void Apply(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj, CastAction castObj,
            Skill skill, SkillObject skillObject, DateTime time)
        {
            _log.Debug("SpecialEffect, Special: {0}, Value1: {1}, Value2: {2}, Value3: {3}, Value4: {4}", SpecialEffectTypeId, Value1, Value2, Value3, Value4);

            var classType = Type.GetType("AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects." + SpecialEffectTypeId);
            if (classType == null)
            {
                _log.Error("Unknown special effect: {0}", SpecialEffectTypeId);
                return;
            }

            var action = (ISpecialEffect)Activator.CreateInstance(classType);
            action.Execute(caster, casterObj, target, targetObj, castObj, skill, skillObject, time, Value1, Value2, Value3, Value4);
        }
    }
}
