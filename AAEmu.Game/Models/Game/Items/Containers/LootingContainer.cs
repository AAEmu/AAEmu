using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Loots;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Team;
using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Models.Game.Items.Containers;

/// <summary>
/// Unlike other item containers this one is not an actual ItemContainer
/// </summary>
public class LootingContainer(IBaseUnit owner)
{
    // ReSharper disable once FieldCanBeMadeReadOnly.Local
    // ReSharper disable once InconsistentNaming
    private static Logger Logger = LogManager.GetCurrentClassLogger();

    // TODO: Make loot range and timing settings configurable
    /// <summary>
    /// Maximum range from owner to be permitted to loot (don't allow looting by far away people)
    /// </summary>
    public const float MaxLootingRange = 200f;

    /// <summary>
    /// Time before loot goes to public in seconds
    /// </summary>
    private const float MakeLootPublicTime = 180f;

    /// <summary>
    /// When loot has been generated, extend the despawn timer by this amount (in seconds)
    /// </summary>
    public const float LootDespawnExtensionTime = 300f;

    /// <summary>
    /// Minimum time that a corpse should remain after it has been looted for all items (if it had items)
    /// </summary>
    private const float PostLootMinimumDespawnTime = 2f;

    /// <summary>
    /// Unit this looting container is attached to
    /// </summary>
    private IBaseUnit LootOwner { get; } = owner;
    private LootOwnerType LootOwnerType { get; set; } = LootOwnerType.None;

    /// <summary>
    /// Unit that dealt the killing blow
    /// </summary>
    private IBaseUnit Killer { get; set; }

    private Team.Team KillerTeam { get; set; }
    private LootingRule TeamLootingRule { get; set; }

    /// <summary>
    /// Time this loot was generated
    /// </summary>
    private DateTime CreationTime { get; set; } = DateTime.MinValue;

    /// <summary>
    /// List of item entries (itemIndex, LootItemEntry)
    /// </summary>
    public Dictionary<ushort, LootingContainerItemEntry> Items { get; } = [];
    private bool AlreadyGenerated { get; set; }
    private HashSet<Character> EligiblePlayers { get; } = [];
    private HashSet<Character> OpenedBy { get; } = [];

    /// <summary>
    /// Generate appropriate loot
    /// </summary>
    /// <param name="killer"></param>
    public void GenerateLoot(IBaseUnit killer)
    {
        // Do not allow multiple generations of loot 
        if (AlreadyGenerated)
            return;
        AlreadyGenerated = true;

        // Initialize some things
        LootOwnerType = LootOwner switch
        {
            Npc => LootOwnerType.Npc,
            Doodad => LootOwnerType.Doodad,
            _ => LootOwnerType.None
        };
        Killer = killer;
        CreationTime = DateTime.UtcNow;
        Items.Clear();

        // NPC Loot handling
        if (LootOwnerType == LootOwnerType.Npc && LootOwner is Npc npc)
        {
            // Get drop list for this NPC
            var lootPackDroppingNpcs = ItemManager.Instance.GetLootPackIdByNpcId(npc.TemplateId);
            if (lootPackDroppingNpcs.Count <= 0)
            {
                return;
            }

            // Calculate loot rates
            var lootDropRate = 1f;
            var lootGoldRate = 1f;

            // Check all people with a claim on the NPC
            EligiblePlayers.Clear();
            KillerTeam = TeamManager.Instance.GetActiveTeam(npc.CharacterTagging.TagTeam);
            TeamLootingRule = KillerTeam?.LootingRule.Clone() ?? new LootingRule()
            {
                LootMethod = LootingRuleMethod.FreeForAll,
            };

            if (npc.CharacterTagging.TagTeam != 0)
            {
                // A team has tagging rights
                if (KillerTeam != null)
                {
                    foreach (var member in KillerTeam.Members)
                    {
                        if (member == null || member.Character == null)
                            continue;

                        //if (member.HasGoneRoundRobin)
                        //    continue;

                        if (member.Character.GetDistanceTo(npc) <= MaxLootingRange)
                        {
                            //This player is in range of the mob and in a group with tagging rights.
                            EligiblePlayers.Add(member.Character);
                        }
                    }
                }
                else if (npc.CharacterTagging.Tagger != null)
                {
                    // If a team tag, but no valid team found then use the tagger
                    EligiblePlayers.Add(npc.CharacterTagging.Tagger);
                }
            }
            else if (npc.CharacterTagging.Tagger != null)
            {
                // Set to FreeForAll when only the tagger has rights
                TeamLootingRule = new LootingRule
                {
                    LootMethod = LootingRuleMethod.FreeForAll,
                    MinimumGrade = 0,
                    LootMaster = (Killer as Character)?.Id ?? 0
                };
                // A player has tag rights
                EligiblePlayers.Add(npc.CharacterTagging.Tagger);
            }

            // Calculate required drop-rate multipliers
            if (EligiblePlayers.Count > 0)
            {
                var maxDropRateMul = -100f;
                var maxLootGoldMul = -100f;

                foreach (var pl in EligiblePlayers)
                {
                    var aggroDropMul = (100f + pl.DropRateMul) / 100f;
                    var aggroGoldMul = (100f + pl.LootGoldMul) / 100f;
                    if (aggroDropMul > maxDropRateMul)
                        maxDropRateMul = aggroDropMul;
                    if (aggroGoldMul > maxLootGoldMul)
                        maxLootGoldMul = aggroGoldMul;

                }

                lootDropRate = maxDropRateMul;
                lootGoldRate = maxLootGoldMul;
            }
            else if (killer is Character player)
            {
                // If no eligible players defined, then try to use the killer's loot rates and mark it as the sole valid option
                lootDropRate *= (100f + player.DropRateMul) / 100f;
                lootGoldRate *= (100f + player.LootGoldMul) / 100f;
                Logger.Info($"Unit killed without aggro: {npc.ObjId} ({npc.TemplateId}) by {player.Name}");
                EligiblePlayers.Add(player);
            }

            // Base ID used for identifying the loot
            // var baseId = ((ulong)LootOwner.ObjId << 32) + ((ulong)LootOwnerType << 16) + 1;

            // Generate the actual loot
            List<(uint itemId, int count, byte grade, uint lootGroupOrigin)> lootPackResults = [];
            foreach (var lootPackDropping in lootPackDroppingNpcs)
            {
                var lootPack = LootGameData.Instance.GetPack(lootPackDropping.LootPackId);
                if (lootPack == null)
                    continue;
                lootPackResults.AddRange(lootPack.GeneratePackNew(lootDropRate, lootGoldRate, killer as Character, ActabilityType.None, true));
                // var items = lootPack.GenerateNpcPackItems(ref baseId, killer, lootDropRate, lootGoldRate);
                // RegisterItems(items);
            }

            // Make Group list to enumerate with
            var groups = lootPackResults.GroupBy(x => x.lootGroupOrigin).Select(x => x.Key).ToList();

            // Pick results by groups total of all packs
            foreach (var group in groups)
            {
                var selectByGroup = lootPackResults.Where(x => x.lootGroupOrigin == group).ToList();
                if (selectByGroup.Count <= 0)
                    continue;

                var resultsToAdd = new List<Item>();

                // If it's group is larger than 1, pick one at random
                if (group > 1)
                {
                    var rngPos = Random.Shared.Next(selectByGroup.Count);
                    var item = ItemManager.Instance.Create(selectByGroup[rngPos].itemId, selectByGroup[rngPos].count, selectByGroup[rngPos].grade, false);
                    resultsToAdd.Add(item);
                }
                else
                {
                    foreach (var singleItemInGroup in selectByGroup)
                    {
                        var item = ItemManager.Instance.Create(singleItemInGroup.itemId, singleItemInGroup.count, singleItemInGroup.grade, false);
                        resultsToAdd.Add(item);
                    }
                }

                RegisterItems(resultsToAdd);
            }

            if (Items.Count <= 0)
            {
                return;
            }

            UpdateLootState();
        }
        else
        if (LootOwnerType == LootOwnerType.Doodad && LootOwner is Doodad doodad)
        {
            // TODO: LootOwnerType.Doodad
            Logger.Warn($"Not yet implemented for doodads, LootOwner: {LootOwnerType}:{doodad.ObjId}");
        }
        else
        {
            // TODO: Either loot generated for a not supported type or it no longer exists 
            Logger.Warn($"Unsupported LootOwner: {LootOwnerType}:{LootOwner.ObjId}");
        }
    }

    /// <summary>
    /// Add generated loot to the loot container
    /// </summary>
    /// <param name="items">Loot</param>
    private void RegisterItems(List<Item> items)
    {
        foreach (var item in items)
        {
            var newItem = new LootingContainerItemEntry
            {
                Owner = this,
                Item = item,
                ItemIndex = (ushort)(Items.Count + 1),
                HighestRoller = 0
            };
            // Update ItemId to what is expected to be used
            // Note that the actual Item.Id needs to be updated upon actual looting
            newItem.Item.Id = ((ulong)LootOwner.ObjId << 32) + ((ulong)LootOwnerType << 16) + newItem.ItemIndex;

            // Add the actual entry
            Items.Add(newItem.ItemIndex, newItem);
        }
    }

    /// <summary>
    /// Broadcasts packet to all players in the list of targets
    /// </summary>
    /// <param name="players"></param>
    /// <param name="packet"></param>
    private void SendPacketToPlayers(HashSet<Character> players, GamePacket packet)
    {
        foreach (var target in players)
        {
            target.SendPacket(packet);
        }
    }

    /// <summary>
    /// Sends the SCLootableStatePacket to all involved players and updates despawn times if needed
    /// </summary>
    private void UpdateLootState()
    {
        SendPacketToPlayers(EligiblePlayers, new SCLootableStatePacket(LootOwnerType, LootOwner.ObjId, Items.Count > 0));
        // If no items left, then reduce the despawn timer if needed
        if (Items.Count <= 0)
        {
            var minimumDespawnTime = CreationTime;
            switch (LootOwnerType)
            {
                case LootOwnerType.Npc:
                    if (LootOwner is Npc npc)
                    {
                        if (npc.Spawner != null)
                            minimumDespawnTime = CreationTime.AddSeconds(npc.Spawner.DespawnTime);
                        if (minimumDespawnTime < DateTime.UtcNow)
                            minimumDespawnTime = DateTime.UtcNow.AddSeconds(PostLootMinimumDespawnTime);
                        npc.Despawn = minimumDespawnTime;
                    }
                    break;
                default:
                    Logger.Warn($"UpdateLootState, Unsupported LootOwnerType: {LootOwnerType} after looting");
                    break;
            }
        }
    }

    /// <summary>
    /// Player opens the loot bag
    /// </summary>
    /// <param name="player">Player opening the loot container</param>
    /// <param name="object2">unused</param>
    /// <param name="lootAll">True when the player opened the loot using (G) to loot all</param>
    public void OpenBag(Character player, BaseUnit object2, bool lootAll)
    {
        OpenedBy.Add(player);

        // If LootAll is set, try to loot all items immediately
        if (lootAll)
        {
            // Try to loot all items
            var lootedItems = new List<ushort>();
            foreach (var (itemIndex, itemEntry) in Items)
            {
                if (TryTakeLoot(player, itemIndex, itemEntry, true))
                    lootedItems.Add(itemIndex);
            }
            // Remove actually looted items
            foreach (var lootedItemIndex in lootedItems)
                Items.Remove(lootedItemIndex);
        }

        // Send packet update of remaining items, or loot state if all has been looted already
        // if (Items.Count <= 0)
        // {
        //     UpdateLootState();
        // }
        // else
        if (Items.Count > 0)
        {
            var remainingItems = new List<Item>();
            foreach (var (_, itemEntry) in Items)
            {
                remainingItems.Add(itemEntry.Item);
            }

            SendPacketToPlayers(OpenedBy, new SCLootBagDataPacket(remainingItems, lootAll));
        }
    }

    /// <summary>
    /// Tries to add a LootingContainerItemEntry's item to the player's Bag, does not actually remove the itemEntry
    /// </summary>
    /// <param name="player"></param>
    /// <param name="itemIndex"></param>
    /// <param name="itemEntry"></param>
    /// <param name="didLootAll"></param>
    /// <returns>Returns true if the item was granted to the player</returns>
    public bool TryTakeLoot(Character player, ushort itemIndex, LootingContainerItemEntry itemEntry, bool didLootAll)
    {
        var lootTarget = player;
        // If itemEntry not specified, grab it from its index
        itemEntry ??= Items.GetValueOrDefault(itemIndex);

        // Invalid item?
        if (itemEntry == null)
            return false;

        // Check if it's already claimed by somebody else
        if ((itemEntry.HighestRoller > 0) && (itemEntry.HighestRoller != player.Id))
        {
            if (itemEntry.HighestRoller > 0)
            {
                player.SendErrorMessage(ErrorMessageType.NoPermissionToLoot, itemEntry.HighestRoller);
            }
            player.SendPacket(new SCLootItemFailedPacket(ErrorMessageType.NoPermissionToLoot, LootOwnerType, LootOwner.ObjId, itemEntry.ItemIndex, itemEntry.Item.TemplateId));
            return false;
        }

        // Check for quest items eligibility
        if (itemEntry.Item.Template.LootQuestId > 0)
        {
            if (!player.Quests.HasQuest(itemEntry.Item.Template.LootQuestId))
            {
                player.SendPacket(new SCLootItemFailedPacket(ErrorMessageType.NeedQuestToInteract, LootOwnerType, LootOwner.ObjId, itemEntry.ItemIndex, itemEntry.Item.TemplateId));
                return false;
            }
        }

        // Check if we already have looting right, if so, try to loot again
        if (itemEntry.HighestRoller == player.Id)
        {
            return TryDistributeLootToPlayer(player, itemEntry, didLootAll);
        }

        // Check if rolls in progress (for more than one player only)
        if (itemEntry.PlayerRolls.Count > 1)
        {
            return false;
        }

        // Do the Team looting rules require us to do a manual roll?
        var rollMandatory = (TeamLootingRule.MinimumGrade > 0 && itemEntry.Item.Grade >= TeamLootingRule.MinimumGrade) || (TeamLootingRule.RollForBindOnPickup && itemEntry.Item.Template.BindType.HasFlag(ItemBindType.BindOnPickup));

        // Check the other party/raid loot settings (if applicable)
        var allowLootingNow = false;
        switch (TeamLootingRule.LootMethod)
        {
            case LootingRuleMethod.FreeForAll:
                allowLootingNow = true;
                break;
            case LootingRuleMethod.RotateWinner:
                if (EligiblePlayers.Count <= 1)
                {
                    // Only one possible player, so always allow 
                    allowLootingNow = true;
                }
                else if (KillerTeam != null)
                {
                    // Kill credits go to a team, pick a winner at random
                    var winner = KillerTeam.GetNextLootWinner(EligiblePlayers, itemEntry.Owner.LootOwner);
                    itemEntry.HighestRoller = winner?.Id ?? 0;

                    if (itemEntry.HighestRoller > 0)
                    {
                        var res = TryDistributeLootToPlayer(winner, itemEntry, didLootAll);
                        return winner == player && res;
                    }
                }
                else
                {
                    Logger.Warn($"TryTakeLoot, We have no valid Team to apply {TeamLootingRule.LootMethod} to. Reverting it to public as a failsafe");
                    allowLootingNow = true;
                    rollMandatory = false;
                    TeamLootingRule.LootMethod = LootingRuleMethod.Public;
                }

                // TODO: Handle edge-case where party is removed before rolls are executed
                break;
            case LootingRuleMethod.LootMaster:
                allowLootingNow = true;
                lootTarget = WorldManager.Instance.GetCharacterById(TeamLootingRule.LootMaster) ?? player;
                // TODO: verify if looting range matters
                break;
            case LootingRuleMethod.Public:
                allowLootingNow = true;
                rollMandatory = false;
                break;
        }

        if (allowLootingNow == false)
        {
            return false;
        }

        // If a roll is required, then add all eligible players to the roll pool
        if (rollMandatory && itemEntry.HighestRoller <= 0)
        {
            foreach (var eligiblePlayer in EligiblePlayers)
            {
                itemEntry.PlayerRolls.TryAdd(eligiblePlayer, 0);
            }
        }

        //  If more than one person needs to roll, send out rolls to all players
        if (itemEntry.PlayerRolls.Count > 1)
        {
            foreach (var (character, _) in itemEntry.PlayerRolls)
            {
                character.SendPacket(new SCLootDicePacket(itemEntry.Item));
            }
            return false;
        }

        // TODO: Handle pickup limit, not sure if we should prevent looting/rolling in the first place, or just prevent adding to inventory

        return TryDistributeLootToPlayer(lootTarget, itemEntry, didLootAll);
    }

    /// <summary>
    /// Player manually closes the loot bag
    /// </summary>
    /// <param name="player"></param>
    /// <param name="itemIndex"></param>
    /// <param name="ownerType"></param>
    /// <param name="ownerObjId"></param>
    /// <param name="b"></param>
    public void CloseBag(Character player, ushort itemIndex, LootOwnerType ownerType, uint ownerObjId, byte b)
    {
        OpenedBy.Remove(player);
    }

    /// <summary>
    /// Apply a player roll to loot
    /// </summary>
    /// <param name="player"></param>
    /// <param name="itemIndex"></param>
    /// <param name="rollRequest"></param>
    public void DoPlayerRoll(Character player, ushort itemIndex, bool rollRequest)
    {
        var itemEntry = Items.GetValueOrDefault(itemIndex);
        if (itemEntry == null)
            return;

        var rollResult = rollRequest ? (sbyte)Random.Shared.Next(1, 100) : (sbyte)-1;
        itemEntry.PlayerRolls[player] = rollResult;

        // Notify the others of this roll result
        foreach (var targetPlayer in itemEntry.PlayerRolls.Keys)
        {
            targetPlayer.SendPacket(new SCLootDiceNotifyPacket(player.Name, itemEntry.Item, rollResult));
        }

        // Check if everybody has rolled
        if (itemEntry.PlayerRolls.Any(m => m.Value == 0))
            return;

        FinishRolling(itemEntry);
    }

    /// <summary>
    /// Handle the distribution when all rolls are finished
    /// </summary>
    /// <param name="itemEntry"></param>
    private void FinishRolling(LootingContainerItemEntry itemEntry)
    {
        // All done? Send summary as well to all
        foreach (var (targetPlayer, _) in itemEntry.PlayerRolls)
        {
            targetPlayer?.SendPacket(new SCLootDiceSummaryPacket(LootOwnerType, LootOwner.ObjId, itemEntry.ItemIndex, itemEntry.PlayerRolls));
        }

        // Find the highest rolled value
        var highestResult = itemEntry.PlayerRolls
            .Where(x => x.Value > 0)
            .OrderBy(x => x.Value)
            .Select(x => x.Value)
            .FirstOrDefault();

        // Find highest roller(s)
        var highestEntries = itemEntry.PlayerRolls
            .Where(x => x.Value >= highestResult)
            .OrderBy(x => x.Value)
            .ToList();

        if (highestEntries.Count <= 0)
        {
            // Everybody passed or didn't roll, set it to public
            itemEntry.PlayerRolls.Clear();
            return;
        }

        // Select random winner (if multiple people roll the same)
        var pickIndex = Random.Shared.Next(highestEntries.Count);
        var highestEntry = highestEntries[pickIndex];

        // Mark winner
        itemEntry.HighestRoller = highestEntry.Key.Id;
        TryDistributeLootToPlayer(highestEntry.Key, itemEntry, false);
    }

    private bool TryDistributeLootToPlayer(Character player, LootingContainerItemEntry itemEntry, bool didLootAll)
    {
        var freeSpace = player.Inventory.Bag.SpaceLeftForItem(itemEntry.Item, out _);
        if (freeSpace < itemEntry.Item.Count)
        {
            // player.SendErrorMessage(ErrorMessageType.BagFull);
            player.SendPacket(new SCLootItemFailedPacket(ErrorMessageType.BagFull, LootOwnerType, LootOwner.ObjId, itemEntry.ItemIndex, itemEntry.Item.TemplateId));
            return false;
        }

        var fullOldItemId = itemEntry.Item.Id;

        // var objId = (uint)(lootDropItem.Id >> 32);
        if (itemEntry.Item.TemplateId == Item.Coins)
        {
            player.AddMoney(SlotType.Inventory, itemEntry.Item.Count);
        }
        else if (ItemManager.Instance.IsAutoEquipTradePack(itemEntry.Item.TemplateId))
        {
            // Auto-equip tradepack item branch.
            // Create a new item instance (non-binding on creation).
            var item = ItemManager.Instance.Create(itemEntry.Item.TemplateId, itemEntry.Item.Count, itemEntry.Item.Grade, false);

            // Attempt to remove the current backpack item to free up the slot.
            if (player.Inventory.TakeoffBackpack(ItemTaskType.RecoverDoodadItem, true))
            {
                // Try to add the new item to the Equipment container's Backpack slot.
                if (!player.Inventory.Equipment.AddOrMoveExistingItem(ItemTaskType.RecoverDoodadItem, item, (int)EquipmentItemSlot.Backpack))
                {
                    // If adding fails, release the item ID and restore original.
                    ItemIdManager.Instance.ReleaseId((uint)item.Id);
                    itemEntry.Item.Id = fullOldItemId;
                    player.SendPacket(new SCLootItemFailedPacket(ErrorMessageType.BagFull, LootOwnerType, LootOwner.ObjId, itemEntry.ItemIndex, itemEntry.Item.TemplateId));
                    return false;
                }
                else
                {
                    Logger.Trace("AutoEquipTradePack: Tradepack item added to Equipment container successfully.");
                }
            }
            else
            {
                Logger.Warn("AutoEquipTradePack: Failed to take off backpack for auto-equip tradepack item TemplateId={0}.", itemEntry.Item.TemplateId);
                player.SendPacket(new SCLootItemFailedPacket(ErrorMessageType.BagFull, LootOwnerType, LootOwner.ObjId, itemEntry.ItemIndex, itemEntry.Item.TemplateId));
                return false;
            }
        }
        else
        {
            // On a loot attempt, it's probably safe to try and assign it a real itemId
            itemEntry.Item.Id = ItemIdManager.Instance.GetNextId();
            // Try to add the new item
            if (!player.Inventory.Bag.AcquireDefaultItem(didLootAll ? ItemTaskType.LootAll : ItemTaskType.Loot, itemEntry.Item.TemplateId, itemEntry.Item.Count, itemEntry.Item.Grade))
            {
                // Free the Id again if failed
                ItemIdManager.Instance.ReleaseId((uint)itemEntry.Item.Id);
                // Re-assign the original loot bag id 
                itemEntry.Item.Id = fullOldItemId;
                // Send a bag full fail message
                // player.SendErrorMessage(ErrorMessageType.BagFull);
                player.SendPacket(new SCLootItemFailedPacket(ErrorMessageType.BagFull, LootOwnerType, LootOwner.ObjId, itemEntry.ItemIndex, itemEntry.Item.TemplateId));
                return false;
            }
        }
        // TODO: check what packet this sends to others
        player.SendPacket(new SCLootItemTookPacket(itemEntry.Item.TemplateId, itemEntry.ItemIndex, LootOwnerType, LootOwner.ObjId, itemEntry.Item.Count));
        Items.Remove(itemEntry.ItemIndex);

        if (Items.Count <= 0)
            UpdateLootState();
        return true;
    }

    /// <summary>
    /// Forces any ongoing loot rolls to end by making the remaining players auto-pass
    /// </summary>
    private void ForceLootRollToFinish()
    {
        foreach (var (_, itemEntry) in Items)
        {
            if (itemEntry.PlayerRolls.All(m => m.Value != 0))
                continue;

            foreach (var (player, roll) in itemEntry.PlayerRolls)
            {
                if (roll == 0)
                {
                    itemEntry.PlayerRolls[player] = -1;
                    // Notify the others of this roll result (not sure if we should add this)
                    // foreach (var targetPlayer in itemEntry.PlayerRolls.Keys)
                    // {
                    //     targetPlayer.SendPacket(new SCLootDiceNotifyPacket(player.Name, itemEntry.Item, -1));
                    // }
                }
            }

            FinishRolling(itemEntry);
        }
    }

    public void MakeLootPublic()
    {
        ForceLootRollToFinish();
        if (Items.Count <= 0)
            return;

        // Force full public looting for all non-claimed items
        TeamLootingRule.LootMethod = LootingRuleMethod.Public;
        TeamLootingRule.MinimumGrade = 0;
        TeamLootingRule.RollForBindOnPickup = false;

        // Broadcast the new state it to everybody nearby
        LootOwner?.BroadcastPacket(new SCLootableStatePacket(LootOwnerType, LootOwner.ObjId, true), false);
    }

    public bool CanMakePublic()
    {
        return ((TeamLootingRule != null) &&
            (TeamLootingRule.LootMethod != LootingRuleMethod.Public) &&
            (Items.Count > 0) &&
            (CreationTime > DateTime.MinValue) &&
            (CreationTime.AddSeconds(MakeLootPublicTime) <= DateTime.UtcNow));
    }
}
