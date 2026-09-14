using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.StaticValues;
using NLog;

namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// Server-side consequences of using an item, keyed off <c>items.impl_id</c>.
///
/// Both cases here are driven from content the 10.0.2.13 client also reads from its own copy of the world
/// database, which is why the server had no code for either: the client draws the paper page itself and
/// knows which craft a recipe item teaches. What the client cannot do is remember the result, so the
/// server owns the two things that have to survive a logout - the recipe a character has learned, and the
/// consumption of the item that taught it.
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

        var learned = 0;
        foreach (var craftId in craftIds)
        {
            if (!character.Recipes.Learn(craftId))
                continue;

            learned++;
            // Second field is the client's "index": the position of the new recipe in the character's own
            // recipe list, which is what the client keys its list entry on.
            character.SendPacket(new SCCraftItemUnlockPacket(craftId, (uint)character.Recipes.GetLearnedIndex(craftId)));
            Logger.Debug("Character {0} learned craft {1} from recipe item {2}", character.Name, craftId, item.TemplateId);
        }

        if (learned == 0)
        {
            // Either the recipe was already known, or this is a second call for the same cast: a skill with a
            // plot runs ItemUse from both Plot.RunAsync and the effect path.
            Logger.Debug("Character {0} already knew every craft of recipe item {1}", character.Name, item.TemplateId);
        }

        // The item is not consumed here. Skill 11144 carries no skill_effects and the recipe items are
        // use_skill_as_reagent, so Skill's "missing reagent information" fallback already takes one copy;
        // consuming here as well burned two (measured: one use of a stack of five left three).
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
