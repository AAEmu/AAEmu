using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Managers;

// Only Event triggers in this file
public partial class QuestManager
{
    /// <summary>
    /// Event to trigger the quest turn in to a NPC or Doodad
    /// </summary>
    /// <param name="owner">Player</param>
    /// <param name="questContextId">QuestId</param>
    /// <param name="npcObjId"></param>
    /// <param name="doodadObjId"></param>
    /// <param name="selected">Selected reward (if any)</param>
    public void DoReportEvents(ICharacter owner, uint questContextId, uint npcObjId, uint doodadObjId, int selected)
    {
        if (npcObjId > 0)
        {
            // Turning in at a NPC?
            var npc = WorldManager.Instance.GetNpc(npcObjId);
            // Is it a valid NPC?
            if (npc == null)
                return;

            //Connection.ActiveChar.Quests.OnReportToNpc(_npcObjId, _questContextId, _selected);
            // Initiate the event of Npc report on task completion
            owner.Events?.OnReportNpc(owner, new OnReportNpcArgs
            {
                QuestId = questContextId,
                NpcId = npc.TemplateId,
                Selected = selected,
                Transform = npc.Transform
            });
        }
        else if (doodadObjId > 0)
        {
            // Turning in at a Doodad?
            var doodad = WorldManager.Instance.GetDoodad(doodadObjId);
            // Does the Doodad exist?
            if (doodad == null)
                return;

            //Connection.ActiveChar.Quests.OnReportToDoodad(_doodadObjId, _questContextId, _selected);
            // Trigger the Report to Doodad event
            owner.Events?.OnReportDoodad(owner, new OnReportDoodadArgs
            {
                QuestId = questContextId,
                DoodadId = doodad.TemplateId,
                Selected = selected,
                Transform = doodad.Transform
            });
        }
        else
        {
            // Doesn't have a NPC or Doodad to turn in at, just auto-complete it
            // owner.Quests.CompleteQuest(questContextId, selected, true);
            if (owner.Quests.ActiveQuests.TryGetValue(questContextId, out var quest))
            {
                quest.SelectedRewardIndex = selected;
                quest.Step = QuestComponentKind.Reward;
            }
        }
    }

    /// <summary>
    /// Trigger the quest events for handling the consumption or reduction of items
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="templateId"></param>
    /// <param name="count"></param>
    public void DoItemsConsumedEvents(ICharacter owner, uint templateId, int count) => DoItemsAcquiredEvents(owner, templateId, -count);

    /// <summary>
    /// Trigger the quest events for acquiring items
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="templateId"></param>
    /// <param name="count"></param>
    public void DoItemsAcquiredEvents(ICharacter owner, uint templateId, int count)
    {
        // Trigger the item acquire event
        owner?.Events?.OnItemGather(owner, new OnItemGatherArgs
        {
            ItemId = templateId,
            Count = count
        });

        // Trigger the item group acquire event
        // Check what groups this item belongs to
        // TODO: Optimize this to be added after item and quest loading
        var itemGroupsForThisItem = _groupItems.Where(x => x.Value.Contains(templateId)).Select(x => x.Key);
        foreach (var itemGroup in itemGroupsForThisItem)
        {
            owner?.Events?.OnItemGroupGather(owner, new OnItemGroupGatherArgs { ItemId = templateId, Count = count, ItemGroupId = itemGroup});
        }
    }

    /// <summary>
    /// Trigger the quest events to interacting with a Doodad
    /// </summary>
    /// <param name="sourcePlayer"></param>
    /// <param name="targetPlayer">Used for TeamShare, otherwise should be same as sourcePlayer</param>
    /// <param name="doodadTemplateId"></param>
    public void DoDoodadInteractionEvents(ICharacter sourcePlayer, ICharacter targetPlayer, uint doodadTemplateId)
    {
        // Trigger the interaction event
        targetPlayer?.Events?.OnInteraction(sourcePlayer, new OnInteractionArgs
        {
            DoodadId = doodadTemplateId,
            SourcePlayer = sourcePlayer
        });
    }

    /// <summary>
    /// Triggers the events for talking to a NPC
    /// </summary>
    /// <param name="sourcePlayer">Player that is talking to the Npc</param>
    /// <param name="targetPlayer">Player to receive this event, used internally for TeamShared</param>
    /// <param name="npcObjId"></param>
    /// <param name="questContextId"></param>
    /// <param name="questComponentId"></param>
    /// <param name="questActId"></param>
    public void DoTalkMadeEvents(ICharacter sourcePlayer, ICharacter targetPlayer, uint npcObjId, uint questContextId, uint questComponentId, uint questActId)
    {
        var npc = WorldManager.Instance.GetNpc(npcObjId);
        if (npc == null)
            return;

        // Trigger talk to NPC event
        targetPlayer.Events?.OnTalkMade(targetPlayer, new OnTalkMadeArgs
        {
            QuestId = questContextId,
            NpcId = npc.TemplateId,
            QuestComponentId = questComponentId,
            QuestActId = questActId,
            Transform = npc.Transform,
            SourcePlayer = sourcePlayer
        });

        // Trigger Talk to NPC group event
        var npcGroupsForThisNpc = _groupNpcs.Where(x => x.Value.Contains(npc.TemplateId)).Select(x => x.Key);
        foreach (var npcGroup in npcGroupsForThisNpc)
        {
            targetPlayer.Events?.OnTalkNpcGroupMade(targetPlayer,
                new OnTalkNpcGroupMadeArgs
                {
                    QuestId = questContextId,
                    NpcGroupId = npcGroup,
                    NpcId = npc.TemplateId,
                    QuestComponentId = questComponentId,
                    QuestActId = questActId,
                    Transform = npc.Transform
                });
        }
    }

    /// <summary>
    /// Triggers the various events for killing a NPC
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="npc"></param>
    public void DoOnMonsterHuntEvents(ICharacter owner, Npc npc)
    {
        if (npc == null)
            return;

        var npcZoneGroupId = ZoneManager.Instance.GetZoneByKey(npc.Transform.ZoneId)?.GroupId ?? 0;

        // Individual monster kill
        owner.Events?.OnMonsterHunt(owner, new OnMonsterHuntArgs
        {
            NpcId = npc.TemplateId,
            Count = 1,
            Transform = npc.Transform
        });

        // Trigger NPC Group kills
        var npcGroupsForThisNpc = _groupNpcs.Where(x => x.Value.Contains(npc.TemplateId)).Select(x => x.Key);
        foreach (var npcGroup in npcGroupsForThisNpc)
        {
            owner.Events?.OnMonsterGroupHunt(owner, new OnMonsterGroupHuntArgs
            {
                NpcId = npcGroup,
                Count = 1,
                Position = npc.Transform
            });
        }

        // Trigger zone kills with specific Victim and Killer, also handles OnZoneMonsterHunt event
        owner.Events?.OnZoneKill(owner, new OnZoneKillArgs
        {
            ZoneGroupId = npcZoneGroupId,
            Killer = owner,
            Victim = npc
        });
    }

    /// <summary>
    /// Trigger Initial Aggro related quest events
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="npc"></param>
    public void DoOnAggroEvents(ICharacter owner, Npc npc)
    {
        if (npc == null)
            return;

        owner.Events?.OnAggro(owner, new OnAggroArgs
        {
            NpcId = npc.TemplateId,
            Transform = npc.Transform
        });
    }

    /// <summary>
    /// Triggers emote related quest events
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="emotionId"></param>
    /// <param name="characterObjId">User</param>
    /// <param name="npcObjId">Target</param>
    public void DoOnExpressFireEvents(ICharacter owner, uint emotionId, uint characterObjId, uint npcObjId)
    {
        if (owner.ObjId != characterObjId)
        {
            Logger.Warn($"DoOnExpressFireEvents seems to have a invalid characterObjId referenced, Got:{characterObjId}, Expected:{owner.ObjId} ({owner.Name})");
            return;
        }
        var npc = WorldManager.Instance.GetNpc(npcObjId);
        if (npc == null)
            return;

        owner.Events?.OnExpressFire(owner, new OnExpressFireArgs
        {
            NpcId = npc.TemplateId,
            EmotionId = emotionId
        });
    }

    /// <summary>
    /// Triggers a quest related check for levels
    /// </summary>
    /// <param name="owner"></param>
    public void DoOnLevelUpEvents(ICharacter owner)
    {
        owner.Events?.OnLevelUp(owner, new OnLevelUpArgs());

        // Added for quest In the Footsteps of Gods and Heroes ( 5967 ), get all abilities (classes) to 50
        owner.Events?.OnAbilityLevelUp(owner, new OnAbilityLevelUpArgs());

        // Also handle Level-based (character main level) quest starters
        // Un-started quests can't have a level event handler, so we need to do it this way for quest starters
        var levelActs = _actTemplatesByDetailType.GetValueOrDefault("QuestActConAcceptLevelUp")?.Values;
        if (levelActs != default)
            foreach (var levelAct in levelActs)
            {
                if ((levelAct is QuestActConAcceptLevelUp actLevelUp) && // correct Template
                    (owner.Level >= actLevelUp.Level) && // Minimum Level
                    !owner.Quests.HasQuestCompleted(actLevelUp.ParentQuestTemplate.Id) && // NEver completed before
                    !owner.Quests.HasQuest(actLevelUp.ParentQuestTemplate.Id)) // Not active
                {
                    // Start quest
                    owner.Quests.AddQuest(actLevelUp.ParentQuestTemplate.Id);
                }
            }
    }

    /// <summary>
    /// Triggers quest related events when a craft was successful
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="craftId"></param>
    public void DoOnCraftEvents(ICharacter owner, uint craftId)
    {
        // Added for quest Id=6024
        owner.Events?.OnCraft(owner, new OnCraftArgs
        {
            CraftId = craftId
        });
    }

    /// <summary>
    /// Trigger quest events related to entering a QuestSphere
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="sphereQuest"></param>
    /// <param name="oldPosition"></param>
    public void DoOnEnterSphereEvents(ICharacter owner, SphereQuest sphereQuest, Vector3 oldPosition)
    {
        // Check if there's an active quest attached to this sphere
        // var quest = owner.Quests.ActiveQuests.GetValueOrDefault(sphereQuest.QuestId);

        owner.Events?.OnEnterSphere(owner, new OnEnterSphereArgs
        {
            SphereQuest = sphereQuest,
            OldPosition = oldPosition,
            NewPosition = owner.Transform.World.Position
        });
    }

    /// <summary>
    /// Trigger quest events related to exiting a QuestSphere
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="sphereQuest"></param>
    /// <param name="oldPosition"></param>
    public void DoOnExitSphereEvents(ICharacter owner, SphereQuest sphereQuest, Vector3 oldPosition)
    {
        // Check if there's an active quest attached to this sphere
        // var quest = owner.Quests.ActiveQuests.GetValueOrDefault(sphereQuest.QuestId);

        owner.Events?.OnExitSphere(owner, new OnExitSphereArgs
        {
            SphereQuest = sphereQuest,
            OldPosition = oldPosition,
            NewPosition = owner.Transform.World.Position
        });
    }

    /// <summary>
    /// Triggered when a timer expires
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="questId"></param>
    public void OnTimerExpired(ICharacter owner, uint questId)
    {
        owner?.Events?.OnTimerExpired(owner, new OnTimerExpiredArgs() { QuestId = questId });
    }

    /// <summary>
    /// Triggered when a player enters a sphere marked as a quest starter
    /// </summary>
    /// <param name="player"></param>
    /// <param name="sphereQuestStarter"></param>
    /// <param name="oldPosition"></param>
    public void DoOnEnterQuestStarterSphere(ICharacter player, SphereQuestStarter sphereQuestStarter, Vector3 oldPosition)
    {
        if (player.Quests.HasQuestCompleted(sphereQuestStarter.QuestTemplateId) || player.Quests.HasQuest(sphereQuestStarter.QuestTemplateId))
            return;
        player.Quests.AddQuestFromSphere(sphereQuestStarter.QuestTemplateId, sphereQuestStarter.SphereId);

    }
}
