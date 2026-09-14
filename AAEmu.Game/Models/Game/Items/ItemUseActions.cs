using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.StaticValues;
using NLog;

namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// Server-side consequences of using an item, keyed off <c>items.impl_id</c>.
///
/// Both cases here are driven from content the 10.0.2.13 client also reads from its own copy of the world
/// database, which is why the server had no code for either: the client draws the paper page itself and
/// knows which craft a recipe item teaches. What the client cannot do is remember the result, so the server
/// owns the thing that has to survive a logout - the recipe a character has learned.
///
/// Called from <see cref="Character.ItemUse(ulong)"/>, which runs on every successful item cast on both the
/// local and the Zone-authority skill path.
/// </summary>
public static class ItemUseActions
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public static void Apply(Character character, Item item)
    {
        if (character == null || item?.Template == null)
            return;

        switch (item.Template.ImplId)
        {
            case ItemImplEnum.Recipe:
                LearnRecipe(character, item);
                break;
            case ItemImplEnum.OpenPaper:
                ResolvePaper(character, item);
                break;
        }
    }

    /// <summary>
    /// Learns what a recipe item teaches and spends the item, both in one transaction.
    /// </summary>
    /// <remarks>
    /// The unlock and the item deduction have to become durable together. Persisting the recipe row on its
    /// own connection first, as this used to, left a window where World could stop between the two saves and
    /// the character reloaded holding both the learned recipe and the recipe item.
    ///
    /// The item is only spent when a craft was actually learned, so using an already-known recipe leaves the
    /// item in the bag. Skill.ApplyEffectsCore's "missing reagent information" fallback would otherwise take
    /// any use_skill_as_reagent item whose skill has no effects, which is exactly what skill 11144 is; recipe
    /// items are excluded there and left to this method, so the item is spent exactly once.
    ///
    /// Follows the pattern the family purchases use for a player-initiated item debit: take the inventory's
    /// mutation lock, plan the debit on the exact live stack, then write the recipe rows and the item
    /// snapshots on one transaction before touching the live state.
    /// </remarks>
    private static void LearnRecipe(Character character, Item item)
    {
        // GetCraftsForRecipeItem already drops item_recipes rows the content contradicts itself on and names
        // a craft the content no longer defines; both would otherwise burn the item for nothing.
        var craftIds = ItemUseGameData.Instance.GetCraftsForRecipeItem(item.TemplateId)
            .Where(CraftManager.Instance.HasCraft)
            .ToList();
        if (craftIds.Count == 0)
        {
            Logger.Warn(
                "Character {0} used recipe item {1} ({2}) which item_recipes does not map to any known craft",
                character.Name, item.TemplateId, item.Template.Name);
            return;
        }

        // A skill with a plot runs ItemUse from both Plot.RunAsync and the effect path, so the second call
        // for the same cast finds nothing new here and stops.
        var newCrafts = craftIds.Where(craftId => !character.Recipes.IsLearned(craftId)).ToList();
        if (newCrafts.Count == 0)
        {
            Logger.Debug("Character {0} already knew every craft of recipe item {1}", character.Name, item.TemplateId);
            return;
        }

        var inventory = character.Inventory;
        if (inventory == null)
            return;

        using (PersistenceOperationScope.Enter())
        lock (inventory.MutationSyncRoot)
        {
            if (!inventory.TryPlanExactBagConsumption(item.Id, 1, out var consumption))
            {
                Logger.Warn(
                    "Character {0} used recipe item {1} without an exact bag stack to spend; the item is kept",
                    character.Name, item.TemplateId);
                return;
            }

            var snapshots = consumption.CapturePersistenceSnapshots(ItemManager.Instance);
            try
            {
                using var connection = MySQL.CreateConnection();
                using var transaction = connection.BeginTransaction();
                try
                {
                    character.Recipes.PersistLearned(newCrafts, connection, transaction);
                    ItemManager.Instance.PersistSnapshots(connection, transaction, snapshots);
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (Exception exception)
            {
                Logger.Error(
                    exception,
                    "Failed to record recipe item {0} for character {1}; neither the recipe nor the item changed",
                    item.TemplateId, character.Name);
                return;
            }

            // Past the commit there is nothing left to decide: the rows are durable, so the live state
            // follows them.
            character.Recipes.ApplyLearned(newCrafts);
            consumption.ApplyCommitted(ItemTaskType.ConsumeSkillSource).PublishPackets();
            Logger.Debug(
                "Character {0} learned craft(s) {1} from recipe item {2}",
                character.Name, string.Join(',', newCrafts), item.TemplateId);
        }
    }

    private static void ResolvePaper(Character character, Item item)
    {
        if (!ItemUseGameData.Instance.TryGetPaper(item.TemplateId, out var paper))
        {
            Logger.Warn(
                "Character {0} used open-paper item {1} ({2}) which item_open_papers does not describe",
                character.Name, item.TemplateId, item.Template.Name);
            return;
        }

        // The client renders the page or book itself out of book_pages / book_page_contents, so there is
        // nothing to send; the server's part is the item-use event this call hangs off and the record that
        // the paper is real content.
        Logger.Debug(
            "Character {0} opened paper for item {1}: page {2}, book {3}",
            character.Name, item.TemplateId, paper.BookPageId, paper.BookId);
    }
}
