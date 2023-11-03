using System;
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
    public EventHandler<OnDeathArgs> OnDeath = delegate { };
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
    public EventHandler<OnCraftArgs> OnCraft = delegate { };
    public EventHandler<OnZoneKillArgs> OnZoneKill = delegate { };
    public EventHandler<OnZoneMonsterHuntArgs> OnZoneMonsterHunt = delegate { };
    // на шаге Ready
    public EventHandler<OnReportNpcArgs> OnReportNpc = delegate { };
    public EventHandler<OnReportDoodadArgs> OnReportDoodad = delegate { };
    public EventHandler<OnReportJournalArgs> OnReportJournal = delegate { };
    // на шаге Complete?
    public EventHandler<OnQuestCompleteArgs> OnQuestComplete = delegate { };
    // --- нужен для квестов
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
}
public class OnAbilityLevelUpArgs : EventArgs
{
}
public class OnEnterSphereArgs : EventArgs
{
    public SphereQuest SphereQuest { get; set; }

}
public class OnZoneKillArgs : EventArgs
{
    public uint ZoneId { get; set; }
}
public class OnZoneMonsterHuntArgs : EventArgs
{
    public uint ZoneId { get; set; }
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

}

public class OnMovementArgs : EventArgs
{

}

public class OnChannelingCancelArgs : EventArgs
{

}

public class OnRemoveOnDamagedArgs : EventArgs
{

}

public class OnDeathArgs : EventArgs
{
    public Unit Killer { get; set; }
    public Unit Victim { get; set; }
}

public class OnUnmountArgs : EventArgs
{

}

public class OnKillArgs : EventArgs
{
    public Unit target { get; set; }
}

public class OnDamagedCollisionArgs : EventArgs
{

}

public class OnImmortalityArgs : EventArgs
{

}

public class OnTimeArgs : EventArgs
{

}

public class OnKillAnyArgs : EventArgs
{

}

public class OnHealedArgs : EventArgs
{
    public Unit Healer { get; set; }
    public int HealAmount { get; set; }
}

