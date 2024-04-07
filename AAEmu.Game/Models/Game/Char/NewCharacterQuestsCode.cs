using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Char;

public partial class CharacterQuests
{
    // I would have preferred a simple List or a normal event handler, but there isn't really a option I can think of
    // how we could make this without having to make duplicate object versions of the ActTemplates
    public ConcurrentDictionary<uint, IQuestAct> OnInteractionList { get; set; } = new();

    /// <summary>
    /// Registers a QuestAct to a specific handler list
    /// </summary>
    /// <param name="list"></param>
    /// <param name="act"></param>
    public void RegisterEventHandler(ConcurrentDictionary<uint, IQuestAct> list, IQuestAct act)
    {
        if (list.ContainsKey(act.Template.ActId))
        {
            Logger.Warn($"RegisterEventHandler, ActId {act.Template.ActId} was already registered for {act.QuestComponent.Parent.Parent.Owner.Name} ({act.QuestComponent.Parent.Parent.Owner.Id}).");
            return;
        }

        if (!list.TryAdd(act.Template.ActId, act))
            Logger.Error($"RegisterEventHandler, Failed to register ActId {act.Template.ActId} was already registered for {act.QuestComponent.Parent.Parent.Owner.Name} ({act.QuestComponent.Parent.Parent.Owner.Id}).");
    }

    /// <summary>
    /// Removes the registration of a QuestAct from the specified list
    /// </summary>
    /// <param name="list"></param>
    /// <param name="act"></param>
    public void UnRegisterEventHandler(ConcurrentDictionary<uint, IQuestAct> list, IQuestAct act)
    {
        if (!list.ContainsKey(act.Template.ActId))
        {
            Logger.Warn(
                $"DeRegisterEventHandler, ActId {act.Template.ActId} was not registered for {act.QuestComponent.Parent.Parent.Owner.Name} ({act.QuestComponent.Parent.Parent.Owner.Id}).");
            return;
        }

        if (!list.TryRemove(act.Template.ActId, out _))
            Logger.Error($"RegisterEventHandler, Failed to un-register ActId {act.Template.ActId} for {act.QuestComponent.Parent.Parent.Owner.Name} ({act.QuestComponent.Parent.Parent.Owner.Id}).");
    }

    #region Events

    // Events called here will loop through all registered acts only

    /// <summary>
    /// Взаимодействие с doodad, например ломаем шахту по квесту (Interaction with doodad, for example, breaking a mine on a quest)
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="eventArgs"></param>
    public void OnInteractionHandler(object sender, EventArgs eventArgs)
    {
        if (eventArgs is not OnInteractionArgs onInteractionArgs)
            return;

        // Loop through all registered act's for the related list
        foreach (var item in OnInteractionList.Values.ToList())
        {
            var questEventArgs = new OnInteractionArgs()
            {
                OwningQuest = item.QuestComponent.Parent.Parent, 
                DoodadId = onInteractionArgs.DoodadId
            };
            item.QuestComponent.Parent.Parent.OnInteractionHandler(sender, questEventArgs);
        }
    }

    /// <summary>
    /// Взаимодействие с doodad, например сбор ресурсов (Interacting with doodad, such as resource collection)
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="eventArgs"></param>
    public void OnItemUseHandler(object sender, EventArgs eventArgs)
    {
        //lock (_lock)
        foreach (var quest in ActiveQuests.Values.ToList())
        {
            quest.OnItemUseHandler(this, eventArgs);
        }
    }
    public void OnItemGroupUseHandler(object sender, EventArgs eventArgs)
    {
        //lock (_lock)
        foreach (var quest in ActiveQuests.Values.ToList())
        {
            quest.OnItemGroupUseHandler(this, eventArgs);
        }
    }
    public void OnItemGatherHandler(object sender, EventArgs eventArgs)
    {
        //lock (_lock)
        {
            var args = eventArgs as OnItemGatherArgs;
            if (args == null)
                throw new NotImplementedException();

            if (ActiveQuests.TryGetValue(args.QuestId, out var questDirect))
            {
                questDirect.OnItemGatherHandler(this, eventArgs);
                return;
            }

            foreach (var quest in ActiveQuests.Values.ToList())
            {
                quest.OnItemGatherHandler(this, eventArgs);
            }
        }
    }
    public void OnItemGroupGatherHandler(object sender, EventArgs eventArgs)
    {
        //lock (_lock)
        foreach (var quest in ActiveQuests.Values)
        {
            quest.OnItemGroupGatherHandler(this, eventArgs);
        }
    }
    public void OnMonsterHuntHandler(object sender, EventArgs eventArgs)
    {
        //lock (_lock)
        foreach (var quest in ActiveQuests.Values)
        {
            quest.OnMonsterHuntHandler(this, eventArgs);
        }
    }
    public void OnMonsterGroupHuntHandler(object sender, EventArgs eventArgs)
    {
        //lock (_lock)
        foreach (var quest in ActiveQuests.Values)
        {
            quest.OnMonsterGroupHuntHandler(this, eventArgs);
        }
    }
    public void OnAggroHandler(object sender, EventArgs eventArgs)
    {
        //lock (_lock)
        foreach (var quest in ActiveQuests.Values)
        {
            quest.OnAggroHandler(this, eventArgs);
        }
    }
    public void OnExpressFireHandler(object sender, EventArgs eventArgs)
    {
        //lock (_lock)
        foreach (var quest in ActiveQuests.Values)
        {
            quest.OnExpressFireHandler(this, eventArgs);
        }
    }
    public void OnLevelUpHandler(object sender, EventArgs eventArgs)
    {
        //lock (_lock)
        foreach (var quest in ActiveQuests.Values)
        {
            quest.OnLevelUpHandler(this, eventArgs);
        }
    }
    public void OnAbilityLevelUpHandler(object sender, EventArgs eventArgs)
    {
        //lock (_lock)
        foreach (var quest in ActiveQuests.Values)
        {
            quest.OnAbilityLevelUpHandler(this, eventArgs);
        }
    }
    public void OnCraftHandler(object sender, EventArgs eventArgs)
    {
        //lock (_lock)
        foreach (var quest in ActiveQuests.Values)
        {
            quest.OnCraftHandler(this, eventArgs);
        }
    }
    public void OnEnterSphereHandler(object sender, EventArgs eventArgs)
    {
        //lock (_lock)
        foreach (var quest in ActiveQuests.Values)
        {
            quest.OnEnterSphereHandler(this, eventArgs);
        }
    }
    public void OnZoneKillHandler(object sender, EventArgs eventArgs)
    {
        //lock (_lock)
        foreach (var quest in ActiveQuests.Values)
        {
            quest.OnZoneKillHandler(this, eventArgs);
        }
    }
    public void OnZoneMonsterHuntHandler(object sender, EventArgs eventArgs)
    {
        //lock (_lock)
        foreach (var quest in ActiveQuests.Values)
        {
            quest.OnZoneMonsterHuntHandler(this, eventArgs);
        }
    }

    // Внимание!!!
    // для этого события будет известен QuestId
    public void OnTalkMadeHandler(object sender, EventArgs eventArgs)
    {
        //lock (_lock)
        {
            var args = eventArgs as OnTalkMadeArgs;
            if (args == null)
                throw new NotImplementedException();

            if (!ActiveQuests.TryGetValue(args.QuestId, out var quest))
                return;

            quest.OnTalkMadeHandler(this, eventArgs);
        }
    }
    public void OnTalkNpcGroupMadeHandler(object sender, EventArgs eventArgs)
    {
        //lock (_lock)
        {
            var args = eventArgs as OnTalkNpcGroupMadeArgs;
            if (args == null)
                throw new NotImplementedException();

            if (!ActiveQuests.TryGetValue(args.QuestId, out var quest))
                return;

            quest.OnTalkNpcGroupMadeHandler(this, eventArgs);
        }
    }
    public void OnReportDoodadHandler(object sender, EventArgs eventArgs)
    {
        //lock (_lock)
        {
            var args = eventArgs as OnReportDoodadArgs;
            if (args == null)
                throw new NotImplementedException();

            if (!ActiveQuests.TryGetValue(args.QuestId, out var quest))
                return;

            quest.OnReportDoodadHandler(this, eventArgs);
        }
    }
    public void OnReportNpcHandler(object sender, EventArgs eventArgs)
    {
        //lock (_lock)
        {
            var args = eventArgs as OnReportNpcArgs;
            if (args == null)
                throw new NotImplementedException();

            if (!ActiveQuests.TryGetValue(args.QuestId, out var quest))
                return;

            quest.OnReportNpcHandler(this, eventArgs);
        }
    }

    #endregion Events
}
