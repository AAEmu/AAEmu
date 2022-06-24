using System;
using System.Collections.Generic;
using System.Numerics;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Plots.Tree;
using AAEmu.Game.Models.Game.Skills.SkillControllers;
using AAEmu.Game.Models.Game.Units.Route;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.Tasks.Skills;

namespace AAEmu.Game.Models.Game.Units
{
    public interface IUnit : IBaseUnit
    {
        PlotState ActivePlotState { get; set; }
        SkillController ActiveSkillController { get; set; }
        byte ActiveWeapon { get; set; }
        float AggroMul { get; }
        int Armor { get; set; }
        SkillTask AutoAttackTask { get; set; }
        int BattleResist { get; set; }
        float BlockRate { get; set; }
        Dictionary<uint, List<Bonus>> Bonuses { get; set; }
        int BullsEye { get; set; }
        float CastTimeMul { get; set; }
        object ChargeLock { get; set; }
        IGameConnection Connection { get; set; }
        UnitCooldowns Cooldowns { get; set; }
        IBaseUnit CurrentTarget { get; set; }
        int DefensePenetration { get; set; }
        float DodgeRate { get; set; }
        int Dps { get; set; }
        int DpsInc { get; set; }
        ItemContainer Equipment { get; set; }
        UnitEvents Events { get; }
        Expedition Expedition { get; set; }
        int Facets { get; set; }
        int Flexibility { get; set; }
        bool ForceAttack { get; set; }
        object GCDLock { get; set; }
        DateTime GlobalCooldown { get; set; }
        float GlobalCooldownMul { get; set; }
        int HDps { get; set; }
        int HDpsInc { get; set; }
        float HealCritical { get; set; }
        float HealCriticalBonus { get; set; }
        float HealCriticalMul { get; set; }
        float HealMul { get; set; }
        int Hp { get; set; }
        int HpRegen { get; set; }
        bool IdleStatus { get; set; }
        float IncomingAggroMul { get; }
        float IncomingDamageMul { get; set; }
        float IncomingHealMul { get; set; }
        float IncomingMeleeDamageMul { get; set; }
        float IncomingRangedDamageMul { get; set; }
        float IncomingSpellDamageMul { get; set; }
        bool Invisible { get; set; }
        bool IsDead { get; }
        bool IsGlobalCooldowned { get; }
        bool IsInBattle { get; set; }
        bool IsInPatrol { get; set; }
        byte Level { get; set; }
        float LevelDps { get; set; }
        int MagicPenetration { get; set; }
        int MagicResistance { get; set; }
        int MaxHp { get; set; }
        int MaxMp { get; set; }
        int MDps { get; set; }
        int MDpsInc { get; set; }
        float MeleeAccuracy { get; set; }
        float MeleeCritical { get; set; }
        float MeleeCriticalBonus { get; set; }
        float MeleeCriticalMul { get; set; }
        float MeleeDamageMul { get; set; }
        float MeleeParryRate { get; set; }
        uint ModelId { get; set; }
        UnitCustomModelParams ModelParams { get; set; }
        float ModelSize { get; }
        int Mp { get; set; }
        int MpRegen { get; set; }
        bool NeedsRegen { get; }
        int OffhandDps { get; set; }
        uint OwnerId { get; set; }
        Patrol Patrol { get; set; }
        int PersistentHpRegen { get; set; }
        int PersistentMpRegen { get; set; }
        UnitProcs Procs { get; set; }
        byte RaceGender { get; }
        float RangedAccuracy { get; set; }
        float RangedCritical { get; set; }
        float RangedCriticalBonus { get; set; }
        float RangedCriticalMul { get; set; }
        float RangedDamageMul { get; set; }
        int RangedDps { get; set; }
        int RangedDpsInc { get; set; }
        float RangedParryRate { get; set; }
        Simulation Simulation { get; set; }
        DateTime SkillLastUsed { get; set; }
        SkillTask SkillTask { get; set; }
        float SpellAccuracy { get; set; }
        float SpellCritical { get; set; }
        float SpellCriticalBonus { get; set; }
        float SpellCriticalMul { get; set; }
        float SpellDamageMul { get; set; }
        int SummarizeDamage { get; set; }
        ushort TlId { get; set; }
        UnitTypeFlag TypeFlag { get; }

        bool CheckMovedPosition(Vector3 oldPosition);
        void DoDie(IUnit killer, KillReason killReason);
        int DoFallDamage(ushort fallVel);
        int GetAbLevel(AbilityType type);
        string GetAttribute(uint attr);
        string GetAttribute(UnitAttribute attr);
        T GetAttribute<T>(UnitAttribute attr, T defaultVal);
        List<Bonus> GetBonuses(UnitAttribute attribute);
        float GetDistanceTo(IBaseUnit baseUnit, bool includeZAxis = false);
        void OnSkillEnd(Skill skill);
        void ReduceCurrentHp(IUnit attacker, int value, KillReason killReason = KillReason.Damage);
        void ReduceCurrentMp(IUnit unit, int value);
        void SendErrorMessage(ErrorMessageType type);
        void SendPacket(GamePacket packet);
        void SetCriminalState(bool criminalState);
        void SetForceAttack(bool value);
        void SetInvisible(bool value);
        void SetPosition(float x, float y, float z, float rotationX, float rotationY, float rotationZ);
        void SetPosition(float x, float y, float z, sbyte rotationX, sbyte rotationY, sbyte rotationZ);
        void StartRegen();
        void StopRegen();
    }
}
