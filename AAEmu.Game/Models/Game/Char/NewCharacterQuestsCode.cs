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
                if (quest.CheckInProgress(quest.TemplateId))
                {
                    Logger.Info($"[OnInteractionHandler] Quest: {quest.TemplateId}. Уже в процессе выполнения...");
                    continue;
                }

                quest.SetInProgress(quest.TemplateId, true);
                quest.OnInteractionHandler(this, eventArgs);
                quest.SetInProgress(quest.TemplateId, false);
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
                if (quest.CheckInProgress(quest.TemplateId))
                {
                    Logger.Info($"[OnItemUseHandler] Quest: {quest.TemplateId}. Уже в процессе выполнения...");
                    continue;
                }

                quest.SetInProgress(quest.TemplateId, true);
                quest.OnItemUseHandler(this, eventArgs);
                quest.SetInProgress(quest.TemplateId, false);
            }
    }
    public void OnItemGroupUseHandler(object sender, EventArgs eventArgs)
    {
        lock (_lock)
            foreach (var quest in ActiveQuests.Values.ToList())
            {
                if (quest.CheckInProgress(quest.TemplateId))
                {
                    Logger.Info($"[OnItemGroupUseHandler] Quest: {quest.TemplateId}. Уже в процессе выполнения...");
                    continue;
                }

                quest.SetInProgress(quest.TemplateId, true);
                quest.OnItemGroupUseHandler(this, eventArgs);
                quest.SetInProgress(quest.TemplateId, false);
            }
    }
    public void OnItemGatherHandler(object sender, EventArgs eventArgs)
    {
        lock (_lock)
            foreach (var quest in ActiveQuests.Values.ToList())
            {
                if (quest.CheckInProgress(quest.TemplateId))
                {
                    Logger.Info($"[OnItemGatherHandler] Quest: {quest.TemplateId}. Уже в процессе выполнения...");
                    continue;
                }
                quest.SetInProgress(quest.TemplateId, true);
                quest.OnItemGatherHandler(this, eventArgs);
                quest.SetInProgress(quest.TemplateId, false);
            }
    }
    public void OnItemGroupGatherHandler(object sender, EventArgs eventArgs)
    {
        lock (_lock)
            foreach (var quest in ActiveQuests.Values.ToList())
                quest.OnItemGroupGatherHandler(this, eventArgs);
    }
    public void OnMonsterHuntHandler(object sender, EventArgs eventArgs)
    {
        lock (_lock)
            foreach (var quest in ActiveQuests.Values)
            {
                if (quest.CheckInProgress(quest.TemplateId))
                {
                    Logger.Info($"[OnMonsterHuntHandler] Quest: {quest.TemplateId}. Уже в процессе выполнения...");
                    continue;
                }

                quest.SetInProgress(quest.TemplateId, true);
                quest.OnMonsterHuntHandler(this, eventArgs);
                quest.SetInProgress(quest.TemplateId, false);
            }
    }
    public void OnMonsterGroupHuntHandler(object sender, EventArgs eventArgs)
    {
        lock (_lock)
            foreach (var quest in ActiveQuests.Values)
            {
                if (quest.CheckInProgress(quest.TemplateId))
                {
                    Logger.Info($"[OnMonsterGroupHuntHandler] Quest: {quest.TemplateId}. Уже в процессе выполнения...");
                    continue;
                }

                quest.SetInProgress(quest.TemplateId, true);
                quest.OnMonsterGroupHuntHandler(this, eventArgs);
                quest.SetInProgress(quest.TemplateId, false);
            }
    }
    public void OnAggroHandler(object sender, EventArgs eventArgs)
    {
        lock (_lock)
            foreach (var quest in ActiveQuests.Values)
            {
                if (quest.CheckInProgress(quest.TemplateId))
                {
                    Logger.Info($"[OnAggroHandler] Quest: {quest.TemplateId}. Уже в процессе выполнения...");
                    continue;
                }

                quest.SetInProgress(quest.TemplateId, true);
                quest.OnAggroHandler(this, eventArgs);
                quest.SetInProgress(quest.TemplateId, false);
            }
    }
    public void OnExpressFireHandler(object sender, EventArgs eventArgs)
    {
        lock (_lock)
            foreach (var quest in ActiveQuests.Values)
            {
                if (quest.CheckInProgress(quest.TemplateId))
                {
                    Logger.Info($"[OnExpressFireHandler] Quest: {quest.TemplateId}. Уже в процессе выполнения...");
                    continue;
                }

                quest.SetInProgress(quest.TemplateId, true);
                quest.OnExpressFireHandler(this, eventArgs);
                quest.SetInProgress(quest.TemplateId, false);
            }
    }
    public void OnLevelUpHandler(object sender, EventArgs eventArgs)
    {
        lock (_lock)
            foreach (var quest in ActiveQuests.Values)
            {
                if (quest.CheckInProgress(quest.TemplateId))
                {
                    Logger.Info($"[OnLevelUpHandler] Quest: {quest.TemplateId}. Уже в процессе выполнения...");
                    continue;
                }

                quest.SetInProgress(quest.TemplateId, true);
                quest.OnLevelUpHandler(this, eventArgs);
                quest.SetInProgress(quest.TemplateId, false);
            }
    }
    public void OnAbilityLevelUpHandler(object sender, EventArgs eventArgs)
    {
        lock (_lock)
            foreach (var quest in ActiveQuests.Values)
            {
                if (quest.CheckInProgress(quest.TemplateId))
                {
                    Logger.Info($"[OnAbilityLevelUpHandler] Quest: {quest.TemplateId}. Уже в процессе выполнения...");
                    continue;
                }

                quest.SetInProgress(quest.TemplateId, true);
                quest.OnAbilityLevelUpHandler(this, eventArgs);
                quest.SetInProgress(quest.TemplateId, false);
            }
    }
    public void OnCraftHandler(object sender, EventArgs eventArgs)
    {
        lock (_lock)
            foreach (var quest in ActiveQuests.Values)
            {
                if (quest.CheckInProgress(quest.TemplateId))
                {
                    Logger.Info($"[OnCraftHandler] Quest: {quest.TemplateId}. Уже в процессе выполнения...");
                    continue;
                }

                quest.SetInProgress(quest.TemplateId, true);
                quest.OnCraftHandler(this, eventArgs);
                quest.SetInProgress(quest.TemplateId, false);
            }
    }
    public void OnEnterSphereHandler(object sender, EventArgs eventArgs)
    {
        lock (_lock)
            foreach (var quest in ActiveQuests.Values)
            {
                if (quest.CheckInProgress(quest.TemplateId))
                {
                    Logger.Info($"[OnEnterSphereHandler] Quest: {quest.TemplateId}. Уже в процессе выполнения...");
                    continue;
                }

                quest.SetInProgress(quest.TemplateId, true);
                quest.OnEnterSphereHandler(this, eventArgs);
                quest.SetInProgress(quest.TemplateId, false);
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

            if (quest.CheckInProgress(quest.TemplateId))
            {
                Logger.Info($"[OnTalkMadeHandler] Quest: {quest.TemplateId}. Уже в процессе выполнения...");
                return;
            }
            quest.SetInProgress(quest.TemplateId, true);
            quest.OnTalkMadeHandler(this, eventArgs);
            quest.SetInProgress(quest.TemplateId, false);
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

            if (quest.CheckInProgress(quest.TemplateId))
            {
                Logger.Info($"[OnTalkNpcGroupMadeHandler] Quest: {quest.TemplateId}. Уже в процессе выполнения...");
                return;
            }
            quest.SetInProgress(quest.TemplateId, true);
            quest.OnTalkNpcGroupMadeHandler(this, eventArgs);
            quest.SetInProgress(quest.TemplateId, false);
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

            if (quest.CheckInProgress(quest.TemplateId))
            {
                Logger.Info($"[OnReportDoodadHandler] Quest: {quest.TemplateId}. Уже в процессе выполнения...");
                return;
            }
            quest.SetInProgress(quest.TemplateId, true);
            quest.OnReportDoodadHandler(this, eventArgs);
            quest.SetInProgress(quest.TemplateId, false);
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

            if (quest.CheckInProgress(quest.TemplateId))
            {
                Logger.Info($"[OnReportNpcHandler] Quest: {quest.TemplateId}. Уже в процессе выполнения...");
                return;
            }
            quest.SetInProgress(quest.TemplateId, true);
            quest.OnReportNpcHandler(this, eventArgs);
            quest.SetInProgress(quest.TemplateId, false);
        }
    }

    #endregion Events
}
