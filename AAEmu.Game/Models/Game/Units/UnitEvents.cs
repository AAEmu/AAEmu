using System;

using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.Game.Models.Game.Units;

public class UnitEvents
{
    /********************************************************
     *  Please dont uncomment unless you implement these!   *
     *           Commented = Not Invoked!!!                 *
     ********************************************************/

    public EventHandler<OnAttackArgs> OnAttack = delegate { }; //Double check this one
    public EventHandler<OnAttackedArgs> OnAttacked = delegate { }; //Double check this one
    public EventHandler<OnDamageArgs> OnDamage = delegate { };
    public EventHandler<OnDamagedArgs> OnDamaged = delegate { };
    //public EventHandler<OnTimeoutArgs> OnTimeout = delegate { }; //When player disconnects? Buff runs out? idk
    public EventHandler<OnDamagedArgs> OnDamagedMelee = delegate { };
    public EventHandler<OnDamagedArgs> OnDamagedRanged = delegate { };
    public EventHandler<OnDamagedArgs> OnDamagedSpell = delegate { };
    public EventHandler<OnDamagedArgs> OnDamagedSiege = delegate { };
    //public EventHandler<OnLandingArgs> OnLanding = delegate { }; //Assume this is for falling?
    //public EventHandler<OnStartedArgs> OnStarted = delegate { }; // I think this belongs part of effect
    public EventHandler<OnMovementArgs> OnMovement = delegate { }; // Only for walking? Or Movement in general?
    public EventHandler<OnChannelingCancelArgs> OnChannelingCancel = delegate { }; //This one might need fixing
    //public EventHandler<OnRemoveOnDamagedArgs> OnRemoveOnDamaged = delegate { }; // Covered by OnDamaged? Maybe?
    public EventHandler<OnUnmountArgs> OnUnmount = delegate { };
    public EventHandler<OnKillArgs> OnKill = delegate { };
    //public EventHandler<OnDamagedCollisionArgs> OnDamagedCollision = delegate { };//I think for ships
    //public EventHandler<OnImmortalityArgs> OnImmortality = delegate { }; //When unit goes invuln?
    //public EventHandler<OnTimeArgs> OnTime = delegate { }; //Event for effect?
    //public EventHandler<OnTimeArgs> OnTime = delegate { }; //Add it if needed, but I think OnKill is fine?
    public EventHandler<OnHealedArgs> OnHealed = delegate { };

    // --- нужен для квестов
    // на шаге Start
    public EventHandler<OnAcceptDoodadArgs> OnAcceptDoodad = delegate { };
    // на шаге Progress
    public EventHandler<OnMonsterHuntArgs> OnMonsterHunt = delegate { };
    public EventHandler<OnMonsterGroupHuntArgs> OnMonsterGroupHunt = delegate { };
    public EventHandler<OnTalkMadeArgs> OnTalkMade = delegate { };
    public EventHandler<OnTalkNpcGroupMadeArgs> OnTalkNpcGroupMade = delegate { };
    public EventHandler<OnAggroArgs> OnAggro = delegate { };
    public EventHandler<OnItemGatherArgs> OnItemGather = delegate { };
    public EventHandler<OnItemGroupGatherArgs> OnItemGroupGather = delegate { };
    public EventHandler<OnItemUseArgs> OnItemUse = delegate { };
    public EventHandler<OnItemGroupUseArgs> OnItemGroupUse = delegate { };
    public EventHandler<OnInteractionArgs> OnInteraction = delegate { };
    public EventHandler<OnExpressFireArgs> OnExpressFire = delegate { };
    public EventHandler<OnLevelUpArgs> OnLevelUp = delegate { };
    public EventHandler<OnAbilityLevelUpArgs> OnAbilityLevelUp = delegate { };
    public EventHandler<OnEnterSphereArgs> OnEnterSphere = delegate { };
    public EventHandler<OnExitSphereArgs> OnExitSphere = delegate { };
    public EventHandler<OnCraftArgs> OnCraft = delegate { };
    public EventHandler<OnZoneKillArgs> OnZoneKill = delegate { };
    public EventHandler<OnZoneMonsterHuntArgs> OnZoneMonsterHunt = delegate { };
    // на шаге Ready
    public EventHandler<OnReportNpcArgs> OnReportNpc = delegate { };
    public EventHandler<OnReportDoodadArgs> OnReportDoodad = delegate { };
    public EventHandler<OnReportJournalArgs> OnReportJournal = delegate { };
    // на шаге Complete?
    public EventHandler<OnQuestCompleteArgs> OnQuestComplete = delegate { };
    // --- нужен для indun
    public EventHandler<OnCombatStartedArgs> OnCombatStarted = delegate { };
    public EventHandler<InIdleArgs> InIdle = delegate { };
    public EventHandler<InAlertArgs> InAlert = delegate { };
    public EventHandler<InDeadArgs> InDead = delegate { };
    public EventHandler<OnDeathArgs> OnDeath = delegate { };
    public EventHandler<OnSpawnArgs> OnSpawn = delegate { };
    public EventHandler<OnDespawnArgs> OnDespawn = delegate { };
}

public class QuestEventArgs : EventArgs
{
    public Quest OwningQuest { get; set; }
}

public class OnMonsterHuntArgs : QuestEventArgs
{
    public uint NpcId { get; set; }
    public uint Count { get; set; }
    public Transform Transform { get; set; }
}

public class OnMonsterGroupHuntArgs : QuestEventArgs
{
    public uint NpcId { get; set; }
    public uint Count { get; set; }
    public Transform Position { get; set; }
}

public class OnItemGatherArgs : QuestEventArgs
{
    public uint QuestId { get; set; } // QuestContextId
    public uint ItemId { get; set; }
    public int Count { get; set; }
}

public class OnItemGroupGatherArgs : QuestEventArgs
{
    public uint ItemId { get; set; }
    public int Count { get; set; }
}

public class OnTalkMadeArgs : QuestEventArgs
{
    public uint QuestId { get; set; } // QuestContextId
    public uint NpcId { get; set; } // Npc.TemplateId
    public uint QuestComponentId { get; set; }
    public uint QuestActId { get; set; }
    public Transform Transform { get; set; }
}

public class OnTalkNpcGroupMadeArgs : QuestEventArgs
{
    public uint QuestId { get; set; } // QuestContextId
    public uint NpcGroupId { get; set; } // Npc.TemplateId
    public uint QuestComponentId { get; set; }
    public uint QuestActId { get; set; }
    public Transform Transform { get; set; }
}

public class OnAggroArgs : QuestEventArgs
{
    public uint NpcId { get; set; }
    public Transform Transform { get; set; }
}

public class OnItemUseArgs : QuestEventArgs
{
    public uint ItemId { get; set; }
    public int Count { get; set; }
}

public class OnItemGroupUseArgs : QuestEventArgs
{
    public uint ItemGroupId { get; set; }
    public int Count { get; set; }
}

public class OnInteractionArgs : QuestEventArgs
{
    public uint DoodadId { get; set; } // Doodad.TemplateId
}

public class OnCraftArgs : QuestEventArgs
{
    public uint CraftId { get; set; }
}

public class OnExpressFireArgs : QuestEventArgs
{
    public uint NpcId { get; set; } // Npc.TemplateId
    public uint EmotionId { get; set; }
}

public class OnLevelUpArgs : QuestEventArgs
{
    // Empty
}

public class OnAbilityLevelUpArgs : QuestEventArgs
{
    // Empty
}

public class OnEnterSphereArgs : QuestEventArgs
{
    public SphereQuest SphereQuest { get; set; }
}

public class OnExitSphereArgs : QuestEventArgs
{
    public SphereQuest SphereQuest { get; set; }
}

public class OnZoneKillArgs : QuestEventArgs
{
    public uint ZoneGroupId { get; set; }
    public ICharacter Killer { get; set; }
    public Unit Victim { get; set; }
}

public class OnZoneMonsterHuntArgs : QuestEventArgs
{
    public uint ZoneGroupId { get; set; }
}

public class OnReportNpcArgs : QuestEventArgs
{
    public uint QuestId { get; set; } // QuestContextId
    public uint NpcId { get; set; } // Npc.TemplateId
    public int Selected { get; set; }
    public Transform Transform { get; set; } // чтобы проверять расстояние до него
}

public class OnAcceptDoodadArgs : QuestEventArgs
{
    //public uint QuestId { get; set; } // QuestContextId
    public uint DoodadId { get; set; } // Doodad.TemplateId
    //public int Selected { get; set; }
}

public class OnReportDoodadArgs : QuestEventArgs
{
    public uint QuestId { get; set; } // QuestContextId
    public uint DoodadId { get; set; } // Doodad.TemplateId
    public int Selected { get; set; }
    public Transform Transform { get; set; } // чтобы проверять расстояние до него
}

public class OnReportJournalArgs : QuestEventArgs
{
    // Empty
}

public class OnQuestCompleteArgs : QuestEventArgs
{
    public uint QuestId { get; set; }
    public int Selected { get; set; }
}

public class OnAttackArgs : QuestEventArgs
{
    public Unit Attacker { get; set; }
}

public class OnAttackedArgs : QuestEventArgs
{
    // Empty
}

public class OnDamageArgs : QuestEventArgs
{
    public Unit Attacker { get; set; }
    public int Amount { get; set; }
}

public class OnDamagedArgs : QuestEventArgs
{
    public Unit Attacker { get; set; }
    public int Amount { get; set; }
}

public class OnDamagedMeleeArgs : QuestEventArgs
{
    public Unit Attacker { get; set; }
    public int Amount { get; set; }
}

public class OnDamagedRangedArgs : QuestEventArgs
{
    public Unit Attacker { get; set; }
    public int Amount { get; set; }
}

public class OnDamagedSpellArgs : QuestEventArgs
{
    public Unit Attacker { get; set; }
    public int Amount { get; set; }
}

public class OnDamagedSiegeArgs : QuestEventArgs
{
    public Unit Attacker { get; set; }
    public int Amount { get; set; }
}

public class OnLandingArgs : QuestEventArgs
{
    // Empty
}

public class OnMovementArgs : QuestEventArgs
{
    // Empty
}

public class OnChannelingCancelArgs : QuestEventArgs
{
    // Empty
}

public class OnRemoveOnDamagedArgs : QuestEventArgs
{
    // Empty
}

public class OnDeathArgs : QuestEventArgs
{
    public Unit Killer { get; set; }
    public Unit Victim { get; set; }
}

public class OnUnmountArgs : QuestEventArgs
{
    // Empty
}

public class OnKillArgs : QuestEventArgs
{
    public Unit target { get; set; }
}

public class OnDamagedCollisionArgs : QuestEventArgs
{
    // Empty
}

public class OnImmortalityArgs : QuestEventArgs
{
    // Empty
}

public class OnTimeArgs : QuestEventArgs
{
    // Empty
}

public class OnKillAnyArgs : QuestEventArgs
{
    // Empty
}

public class OnHealedArgs : QuestEventArgs
{
    public Unit Healer { get; set; }
    public int HealAmount { get; set; }
}

public class OnCombatStartedArgs : QuestEventArgs
{
    public Unit Owner { get; set; }
    public Unit Target { get; set; }
}

public class InIdleArgs : QuestEventArgs
{
    public Unit Owner { get; set; }
}

public class InAlertArgs : QuestEventArgs
{
    public Unit Npc { get; set; }
}

public class InDeadArgs : QuestEventArgs
{
    public Unit Npc { get; set; }
}

public class OnSpawnArgs : QuestEventArgs
{
    public Unit Npc { get; set; }
}

public class OnDespawnArgs : QuestEventArgs
{
    public Unit Npc { get; set; }
}
