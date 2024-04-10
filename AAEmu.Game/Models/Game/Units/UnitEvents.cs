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

    // For Quests
    // At Step Start
    public EventHandler<OnAcceptDoodadArgs> OnAcceptDoodad = delegate { };
    // At Step Progress
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
    public EventHandler<OnCinemaStartedArgs> OnCinemaStarted = delegate { };
    public EventHandler<OnCinemaEndedArgs> OnCinemaEnded = delegate { };
    // At Step Ready
    public EventHandler<OnReportNpcArgs> OnReportNpc = delegate { };
    public EventHandler<OnReportDoodadArgs> OnReportDoodad = delegate { };
    public EventHandler<OnReportJournalArgs> OnReportJournal = delegate { };
    // At Step Complete?
    public EventHandler<OnQuestCompleteArgs> OnQuestComplete = delegate { };
    // Dungeon related
    public EventHandler<OnCombatStartedArgs> OnCombatStarted = delegate { };
    public EventHandler<InIdleArgs> InIdle = delegate { };
    public EventHandler<InAlertArgs> InAlert = delegate { };
    public EventHandler<InDeadArgs> InDead = delegate { };
    public EventHandler<OnDeathArgs> OnDeath = delegate { };
    public EventHandler<OnSpawnArgs> OnSpawn = delegate { };
    public EventHandler<OnDespawnArgs> OnDespawn = delegate { };
}

public class OnMonsterHuntArgs : EventArgs
{
    public uint NpcId { get; set; }
    public uint Count { get; set; }
    public Transform Transform { get; set; }
}

public class OnMonsterGroupHuntArgs : EventArgs
{
    public uint NpcId { get; set; }
    public uint Count { get; set; }
    public Transform Position { get; set; }
}

public class OnItemGatherArgs : EventArgs
{
    public uint QuestId { get; set; } // QuestContextId
    public uint ItemId { get; set; }
    public int Count { get; set; }
}

public class OnItemGroupGatherArgs : EventArgs
{
    public uint ItemId { get; set; }
    public int Count { get; set; }
}

public class OnTalkMadeArgs : EventArgs
{
    public uint QuestId { get; set; } // QuestContextId
    public uint NpcId { get; set; } // Npc.TemplateId
    public uint QuestComponentId { get; set; }
    public uint QuestActId { get; set; }
    public Transform Transform { get; set; }
}

public class OnTalkNpcGroupMadeArgs : EventArgs
{
    public uint QuestId { get; set; } // QuestContextId
    public uint NpcGroupId { get; set; } // Npc.TemplateId
    public uint QuestComponentId { get; set; }
    public uint QuestActId { get; set; }
    public Transform Transform { get; set; }
}

public class OnAggroArgs : EventArgs
{
    public uint NpcId { get; set; }
    public Transform Transform { get; set; }
}

public class OnItemUseArgs : EventArgs
{
    public uint ItemId { get; set; }
    public int Count { get; set; }
}

public class OnItemGroupUseArgs : EventArgs
{
    public uint ItemGroupId { get; set; }
    public int Count { get; set; }
}

public class OnInteractionArgs : EventArgs
{
    public uint DoodadId { get; set; } // Doodad.TemplateId
    public ICharacter SourcePlayer { get; set; }
}

public class OnCraftArgs : EventArgs
{
    public uint CraftId { get; set; }
}

public class OnExpressFireArgs : EventArgs
{
    public uint NpcId { get; set; } // Npc.TemplateId
    public uint EmotionId { get; set; }
}

public class OnLevelUpArgs : EventArgs
{
    // Empty
}

public class OnAbilityLevelUpArgs : EventArgs
{
    // Empty
}

public class OnEnterSphereArgs : EventArgs
{
    public SphereQuest SphereQuest { get; set; }
}

public class OnExitSphereArgs : EventArgs
{
    public SphereQuest SphereQuest { get; set; }
}

public class OnZoneKillArgs : EventArgs
{
    public uint ZoneGroupId { get; set; }
    public ICharacter Killer { get; set; }
    public Unit Victim { get; set; }
}

public class OnZoneMonsterHuntArgs : EventArgs
{
    public uint ZoneGroupId { get; set; }
}

public class OnCinemaStartedArgs : EventArgs
{
    public uint CinemaId { get; set; }
}

public class OnCinemaEndedArgs : EventArgs
{
    public uint CinemaId { get; set; }
}

public class OnReportNpcArgs : EventArgs
{
    public uint QuestId { get; set; } // QuestContextId
    public uint NpcId { get; set; } // Npc.TemplateId
    public int Selected { get; set; }
    public Transform Transform { get; set; } // чтобы проверять расстояние до него
}

public class OnAcceptDoodadArgs : EventArgs
{
    //public uint QuestId { get; set; } // QuestContextId
    public uint DoodadId { get; set; } // Doodad.TemplateId
    //public int Selected { get; set; }
}

public class OnReportDoodadArgs : EventArgs
{
    public uint QuestId { get; set; } // QuestContextId
    public uint DoodadId { get; set; } // Doodad.TemplateId
    public int Selected { get; set; }
    public Transform Transform { get; set; } // чтобы проверять расстояние до него
}

public class OnReportJournalArgs : EventArgs
{
    // Empty
}

public class OnQuestCompleteArgs : EventArgs
{
    public uint QuestId { get; set; }
    public int Selected { get; set; }
}

public class OnAttackArgs : EventArgs
{
    public Unit Attacker { get; set; }
}

public class OnAttackedArgs : EventArgs
{
    // Empty
}

public class OnDamageArgs : EventArgs
{
    public Unit Attacker { get; set; }
    public int Amount { get; set; }
}

public class OnDamagedArgs : EventArgs
{
    public Unit Attacker { get; set; }
    public int Amount { get; set; }
}

public class OnDamagedMeleeArgs : EventArgs
{
    public Unit Attacker { get; set; }
    public int Amount { get; set; }
}

public class OnDamagedRangedArgs : EventArgs
{
    public Unit Attacker { get; set; }
    public int Amount { get; set; }
}

public class OnDamagedSpellArgs : EventArgs
{
    public Unit Attacker { get; set; }
    public int Amount { get; set; }
}

public class OnDamagedSiegeArgs : EventArgs
{
    public Unit Attacker { get; set; }
    public int Amount { get; set; }
}

public class OnLandingArgs : EventArgs
{
    // Empty
}

public class OnMovementArgs : EventArgs
{
    // Empty
}

public class OnChannelingCancelArgs : EventArgs
{
    // Empty
}

public class OnRemoveOnDamagedArgs : EventArgs
{
    // Empty
}

public class OnDeathArgs : EventArgs
{
    public Unit Killer { get; set; }
    public Unit Victim { get; set; }
}

public class OnUnmountArgs : EventArgs
{
    // Empty
}

public class OnKillArgs : EventArgs
{
    public Unit target { get; set; }
}

public class OnDamagedCollisionArgs : EventArgs
{
    // Empty
}

public class OnImmortalityArgs : EventArgs
{
    // Empty
}

public class OnTimeArgs : EventArgs
{
    // Empty
}

public class OnKillAnyArgs : EventArgs
{
    // Empty
}

public class OnHealedArgs : EventArgs
{
    public Unit Healer { get; set; }
    public int HealAmount { get; set; }
}

public class OnCombatStartedArgs : EventArgs
{
    public Unit Owner { get; set; }
    public Unit Target { get; set; }
}

public class InIdleArgs : EventArgs
{
    public Unit Owner { get; set; }
}

public class InAlertArgs : EventArgs
{
    public Unit Npc { get; set; }
}

public class InDeadArgs : EventArgs
{
    public Unit Npc { get; set; }
}

public class OnSpawnArgs : EventArgs
{
    public Unit Npc { get; set; }
}

public class OnDespawnArgs : EventArgs
{
    public Unit Npc { get; set; }
}
