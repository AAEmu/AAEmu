using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Loots;
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
    private static Logger Logger = LogManager.GetCurrentClassLogger();
    /// <summary>
    /// Unit this looting container is attached to
    /// </summary>
    private IBaseUnit LootOwner { get; } = owner;
    private LootOwnerType LootOwnerType { get; set; } = LootOwnerType.None;

    /// <summary>
    /// Unit that dealt the killing blow
    /// </summary>
    public IBaseUnit Killer { get; private set; }
    public Team.Team KillerTeam { get; private set; }
    public LootingRule TeamLootingRule { get; private set; }

    private DateTime CreationTime { get; set; } = DateTime.MinValue;
    private DateTime ExpireTime { get; set; } = DateTime.MaxValue;

    /// <summary>
    /// List of item entries (itemIndex, LootItemEntry)
    /// </summary>
    public Dictionary<ushort, LootingContainerItemEntry> Items { get; init; } = new();
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
        ExpireTime = CreationTime + TimeSpan.FromMinutes(5);
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
            TeamLootingRule = KillerTeam.LootingRule.Clone();
            
            if (npc.CharacterTagging.TagTeam != 0)
            {
                // A team has tagging rights
                if (KillerTeam != null)
                {
                    foreach (var member in KillerTeam.Members)
                    {
                        if (member == null || member.Character == null)
                            continue;

                        var distance = member.Character.Transform.World.Position - npc.Transform.World.Position;
                        if (distance.Length() <= 200)
                        {
                            //This player is in range of the mob and in a group with tagging rights.
                            EligiblePlayers.Add(member.Character);
                        }
                    }
                }
                else if (npc.CharacterTagging.Tagger != null)
                {
                    //A player has tag rights
                    EligiblePlayers.Add(npc.CharacterTagging.Tagger);
                }

            }
            else if (npc.CharacterTagging.Tagger != null)
            {
                // Set to FreeForAll when only the tagger has rights
                TeamLootingRule = new LootingRule
                {
                    LootMethod = LootingRuleMethod.FreeForAll,
                    MinimumGrade = 0xFF,
                    LootMaster = (killer as Character)?.Id ?? 0
                };
                // A player has tag rights
                EligiblePlayers.Add(npc.CharacterTagging.Tagger);
            }

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
                lootDropRate *= (100f + player.DropRateMul) / 100f;
                lootGoldRate *= (100f + player.LootGoldMul) / 100f;
                Logger.Info($"Unit killed without aggro: {npc.ObjId} ({npc.TemplateId}) by {player.Name}");
            }

            // Base ID used for identifying the loot
            var baseId = ((ulong)LootOwner.ObjId << 32) + ((ulong)LootOwnerType << 16) + 1;

            // Generate the actual loot
            foreach (var lootPackDropping in lootPackDroppingNpcs)
            {
                var lootPack = LootGameData.Instance.GetPack(lootPackDropping.LootPackId);
                if (lootPack == null)
                    continue;
                var items = lootPack.GenerateNpcPackItems(ref baseId, killer, lootDropRate, lootGoldRate);

                RegisterItems(items);
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

            // Add roll settings for everybody
            foreach (var player in EligiblePlayers)
            {
                newItem.PlayerRolls.TryAdd(player.Id, -1);
            }

            // Add the actual entry
            Items.Add(newItem.ItemIndex, newItem);
        }
    }

    /// <summary>
    /// Returns true if all items are looted, or if loot time has expired
    /// </summary>
    /// <returns></returns>
    public bool AllowDespawn()
    {
        return Items.Count <= 0 || ExpireTime <= DateTime.UtcNow;
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
    /// Sends the SCLootableStatePacket to all involved players
    /// </summary>
    public void UpdateLootState()
    {
        SendPacketToPlayers(EligiblePlayers, new SCLootableStatePacket(LootOwnerType, LootOwner.ObjId, Items.Count > 0));
    }

    /// <summary>
    /// Player opens the loot bag
    /// </summary>
    /// <param name="player"></param>
    /// <param name="object2"></param>
    /// <param name="lootAll"></param>
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
                if (TryTakeLoot(player, itemIndex, itemEntry, itemEntry.Item.Count))
                    lootedItems.Add(itemIndex);
            }
            // Remove actually looted items
            foreach(var lootedItemIndex in lootedItems)
                Items.Remove(lootedItemIndex);
        }
        // Send packet update of remaining items, or loot state if all has been looted already
        if (Items.Count <= 0)
        {
            UpdateLootState();
        }
        else
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
    /// <param name="count"></param>
    /// <returns>Returns true if the item was granted to the player</returns>
    public bool TryTakeLoot(Character player, ushort itemIndex, LootingContainerItemEntry itemEntry, int count)
    {
        // If itemEntry not specified, grab it from its index
        itemEntry ??= Items.GetValueOrDefault(itemIndex);

        // Invalid item?
        if (itemEntry == null)
            return false;

        // First check for quest items eligibility
        if (itemEntry.Item.Template.LootQuestId > 0)
        {
            if (!player.Quests.HasQuest(itemEntry.Item.Template.LootQuestId))
            {
                player.SendPacket(new SCLootItemFailedPacket(ErrorMessageType.NeedQuestToInteract, LootOwnerType, LootOwner.ObjId, itemEntry.ItemIndex, itemEntry.Item.TemplateId));
                return false;
            }
        }
        
        // Check party/raid loot settings (if applicable)
        
        
        // TODO: Handle pickup limit

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
        else
        {
            // On a loot attempt, it's probably safe to try and assign it a real itemId
            itemEntry.Item.Id = ItemIdManager.Instance.GetNextId();
            // Try to add the new item
            if (!player.Inventory.Bag.AcquireDefaultItem(ItemTaskType.Loot, itemEntry.Item.TemplateId,
                    count > itemEntry.Item.Count ? itemEntry.Item.Count : count, itemEntry.Item.Grade))
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
        player.SendPacket(new SCLootItemTookPacket(itemEntry.Item.TemplateId, itemEntry.ItemIndex, LootOwnerType, LootOwner.ObjId, fullOldItemId, itemEntry.Item.Count));
        Items.Remove(itemEntry.ItemIndex);
        
        return true;
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
}
