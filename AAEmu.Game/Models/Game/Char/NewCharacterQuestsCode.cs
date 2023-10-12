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
                if (quest.CheckInProgress())
                {
                    Logger.Info($"[OnInteractionHandler] Quest: {quest.TemplateId}. Уже в процессе выполнения...");
                    continue;
                }

                quest.SetInProgress(true);
                quest.OnInteractionHandler(this, eventArgs);
                quest.SetInProgress(false);
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
                if (quest.CheckInProgress())
                {
                    Logger.Info($"[OnItemUseHandler] Quest: {quest.TemplateId}. Уже в процессе выполнения...");
                    continue;
                }

                quest.SetInProgress(true);
                quest.OnItemUseHandler(this, eventArgs);
                quest.SetInProgress(false);
            }
    }
    public void OnItemGroupUseHandler(object sender, EventArgs eventArgs)
    {
        lock (_lock)
            foreach (var quest in ActiveQuests.Values.ToList())
            {
                if (quest.CheckInProgress())
                {
                    Logger.Info($"[OnItemGroupUseHandler] Quest: {quest.TemplateId}. Уже в процессе выполнения...");
                    continue;
                }

                quest.SetInProgress(true);
                quest.OnItemGroupUseHandler(this, eventArgs);
                quest.SetInProgress(false);
            }
    }
    public void OnItemGatherHandler(object sender, EventArgs eventArgs)
    {
        lock (_lock)
            foreach (var quest in ActiveQuests.Values.ToList())
            {
                if (quest.CheckInProgress())
                {
                    Logger.Info($"[OnItemGatherHandler] Quest: {quest.TemplateId}. Уже в процессе выполнения...");
                    continue;
                }
                quest.SetInProgress(true);
                quest.OnItemGatherHandler(this, eventArgs);
                quest.SetInProgress(false);
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
                if (quest.CheckInProgress())
                {
                    Logger.Info($"[OnMonsterHuntHandler] Quest: {quest.TemplateId}. Уже в процессе выполнения...");
                    continue;
                }

                quest.SetInProgress(true);
                quest.OnMonsterHuntHandler(this, eventArgs);
                quest.SetInProgress(false);
            }
    }
    public void OnMonsterGroupHuntHandler(object sender, EventArgs eventArgs)
    {
        lock (_lock)
            foreach (var quest in ActiveQuests.Values)
            {
                if (quest.CheckInProgress())
                {
                    Logger.Info($"[OnMonsterGroupHuntHandler] Quest: {quest.TemplateId}. Уже в процессе выполнения...");
                    continue;
                }

                quest.SetInProgress(true);
                quest.OnMonsterGroupHuntHandler(this, eventArgs);
                quest.SetInProgress(false);
            }
    }
    public void OnAggroHandler(object sender, EventArgs eventArgs)
    {
        lock (_lock)
            foreach (var quest in ActiveQuests.Values)
            {
                if (quest.CheckInProgress())
                {
                    Logger.Info($"[OnAggroHandler] Quest: {quest.TemplateId}. Уже в процессе выполнения...");
                    continue;
                }

                quest.SetInProgress(true);
                quest.OnAggroHandler(this, eventArgs);
                quest.SetInProgress(false);
            }
    }
    public void OnExpressFireHandler(object sender, EventArgs eventArgs)
    {
        lock (_lock)
            foreach (var quest in ActiveQuests.Values)
            {
                if (quest.CheckInProgress())
                {
                    Logger.Info($"[OnExpressFireHandler] Quest: {quest.TemplateId}. Уже в процессе выполнения...");
                    continue;
                }

                quest.SetInProgress(true);
                quest.OnExpressFireHandler(this, eventArgs);
                quest.SetInProgress(false);
            }
    }
    public void OnLevelUpHandler(object sender, EventArgs eventArgs)
    {
        lock (_lock)
            foreach (var quest in ActiveQuests.Values)
            {
                if (quest.CheckInProgress())
                {
                    Logger.Info($"[OnLevelUpHandler] Quest: {quest.TemplateId}. Уже в процессе выполнения...");
                    continue;
                }

                quest.SetInProgress(true);
                quest.OnLevelUpHandler(this, eventArgs);
                quest.SetInProgress(false);
            }
    }
    public void OnAbilityLevelUpHandler(object sender, EventArgs eventArgs)
    {
        lock (_lock)
            foreach (var quest in ActiveQuests.Values)
            {
                if (quest.CheckInProgress())
                {
                    Logger.Info($"[OnAbilityLevelUpHandler] Quest: {quest.TemplateId}. Уже в процессе выполнения...");
                    continue;
                }

                quest.SetInProgress(true);
                quest.OnAbilityLevelUpHandler(this, eventArgs);
                quest.SetInProgress(false);
            }
    }
    public void OnCraftHandler(object sender, EventArgs eventArgs)
    {
        lock (_lock)
            foreach (var quest in ActiveQuests.Values)
            {
                if (quest.CheckInProgress())
                {
                    Logger.Info($"[OnCraftHandler] Quest: {quest.TemplateId}. Уже в процессе выполнения...");
                    continue;
                }

                quest.SetInProgress(true);
                quest.OnCraftHandler(this, eventArgs);
                quest.SetInProgress(false);
            }
    }
    public void OnEnterSphereHandler(object sender, EventArgs eventArgs)
    {
        lock (_lock)
            foreach (var quest in ActiveQuests.Values)
            {
                if (quest.CheckInProgress())
                {
                    Logger.Info($"[OnEnterSphereHandler] Quest: {quest.TemplateId}. Уже в процессе выполнения...");
                    continue;
                }

                quest.SetInProgress(true);
                quest.OnEnterSphereHandler(this, eventArgs);
                quest.SetInProgress(false);
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

            if (quest.CheckInProgress())
            {
                Logger.Info($"[OnTalkMadeHandler] Quest: {quest.TemplateId}. Уже в процессе выполнения...");
                return;
            }
            quest.SetInProgress(true);
            quest.OnTalkMadeHandler(this, eventArgs);
            quest.SetInProgress(false);
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

            if (quest.CheckInProgress())
            {
                Logger.Info($"[OnTalkNpcGroupMadeHandler] Quest: {quest.TemplateId}. Уже в процессе выполнения...");
                return;
            }
            quest.SetInProgress(true);
            quest.OnTalkNpcGroupMadeHandler(this, eventArgs);
            quest.SetInProgress(false);
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

            if (quest.CheckInProgress())
            {
                Logger.Info($"[OnReportDoodadHandler] Quest: {quest.TemplateId}. Уже в процессе выполнения...");
                return;
            }
            quest.SetInProgress(true);
            quest.OnReportDoodadHandler(this, eventArgs);
            quest.SetInProgress(false);
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

            if (quest.CheckInProgress())
            {
                Logger.Info($"[OnReportNpcHandler] Quest: {quest.TemplateId}. Уже в процессе выполнения...");
                return;
            }
            quest.SetInProgress(true);
            quest.OnReportNpcHandler(this, eventArgs);
            quest.SetInProgress(false);
        }
    }

    #endregion Events
}
