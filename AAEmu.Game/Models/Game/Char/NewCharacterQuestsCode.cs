using System;
using System.Linq;

using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Char;

public partial class CharacterQuests
{
    private object _lock = new();


    #region Events

    // Внимание!!!
    // для этих событий не будет известен QuestId и будет перебор всех активных квестов
    // что-бы по два раза не вызывались надо перед подпиской на событие отписываться!!!

    /// <summary>
    /// Взаимодействие с doodad, например ломаем шахту по квесту (Interaction with doodad, for example, breaking a mine on a quest)
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="eventArgs"></param>
    public void OnInteractionHandler(object sender, EventArgs eventArgs)
    {
        lock (_lock)
            foreach (var quest in ActiveQuests.Values.ToList())
            {
                quest.OnInteractionHandler(this, eventArgs);
            }
    }
    /// <summary>
    /// Взаимодействие с doodad, например сбор ресурсов (Interacting with doodad, such as resource collection)
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="eventArgs"></param>
    public void OnItemUseHandler(object sender, EventArgs eventArgs)
    {
        lock (_lock)
            foreach (var quest in ActiveQuests.Values.ToList())
            {
                quest.OnItemUseHandler(this, eventArgs);
            }
    }
    public void OnItemGroupUseHandler(object sender, EventArgs eventArgs)
    {
        lock (_lock)
            foreach (var quest in ActiveQuests.Values.ToList())
            {
                quest.OnItemGroupUseHandler(this, eventArgs);
            }
    }
    public void OnItemGatherHandler(object sender, EventArgs eventArgs)
    {
        lock (_lock)
            foreach (var quest in ActiveQuests.Values.ToList())
            {
                quest.OnItemGatherHandler(this, eventArgs);
            }
    }
    public void OnItemGroupGatherHandler(object sender, EventArgs eventArgs)
    {
        lock (_lock)
            foreach (var quest in ActiveQuests.Values)
            {
                quest.OnItemGroupGatherHandler(this, eventArgs);
            }
    }
    public void OnMonsterHuntHandler(object sender, EventArgs eventArgs)
    {
        lock (_lock)
            foreach (var quest in ActiveQuests.Values)
            {
                quest.OnMonsterHuntHandler(this, eventArgs);
            }
    }
    public void OnMonsterGroupHuntHandler(object sender, EventArgs eventArgs)
    {
        lock (_lock)
            foreach (var quest in ActiveQuests.Values)
            {
                quest.OnMonsterGroupHuntHandler(this, eventArgs);
            }
    }
    public void OnAggroHandler(object sender, EventArgs eventArgs)
    {
        lock (_lock)
            foreach (var quest in ActiveQuests.Values)
            {
                quest.OnAggroHandler(this, eventArgs);
            }
    }
    public void OnExpressFireHandler(object sender, EventArgs eventArgs)
    {
        lock (_lock)
            foreach (var quest in ActiveQuests.Values)
            {
                quest.OnExpressFireHandler(this, eventArgs);
            }
    }
    public void OnLevelUpHandler(object sender, EventArgs eventArgs)
    {
        lock (_lock)
            foreach (var quest in ActiveQuests.Values)
            {
                quest.OnLevelUpHandler(this, eventArgs);
            }
    }
    public void OnAbilityLevelUpHandler(object sender, EventArgs eventArgs)
    {
        lock (_lock)
            foreach (var quest in ActiveQuests.Values)
            {
                quest.OnAbilityLevelUpHandler(this, eventArgs);
            }
    }
    public void OnCraftHandler(object sender, EventArgs eventArgs)
    {
        lock (_lock)
            foreach (var quest in ActiveQuests.Values)
            {
                quest.OnCraftHandler(this, eventArgs);
            }
    }
    public void OnEnterSphereHandler(object sender, EventArgs eventArgs)
    {
        lock (_lock)
            foreach (var quest in ActiveQuests.Values)
            {
                quest.OnEnterSphereHandler(this, eventArgs);
            }
    }
    public void OnZoneKillHandler(object sender, EventArgs eventArgs)
    {
        lock (_lock)
            foreach (var quest in ActiveQuests.Values)
            {
                quest.OnZoneKillHandler(this, eventArgs);
            }
    }
    public void OnZoneMonsterHuntHandler(object sender, EventArgs eventArgs)
    {
        lock (_lock)
            foreach (var quest in ActiveQuests.Values)
            {
                quest.OnZoneMonsterHuntHandler(this, eventArgs);
            }
    }

    // Внимание!!!
    // для этого события будет известен QuestId
    public void OnTalkMadeHandler(object sender, EventArgs eventArgs)
    {
        lock (_lock)
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
        lock (_lock)
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
        lock (_lock)
        {
            var args = eventArgs as OnReportNpcArgs;
            if (args == null)
                throw new NotImplementedException();

            if (!ActiveQuests.TryGetValue(args.QuestId, out var quest))
                return;

            quest.OnReportDoodadHandler(this, eventArgs);
        }
    }
    public void OnReportNpcHandler(object sender, EventArgs eventArgs)
    {
        lock (_lock)
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
