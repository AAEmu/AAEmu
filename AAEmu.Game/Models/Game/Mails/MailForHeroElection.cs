using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.StaticValues;
using NLog;

namespace AAEmu.Game.Models.Game.Mails;

/// <summary>
/// The regalia an elected hero receives: their cloak, and the consumables that come with the office.
/// </summary>
/// <remarks>
/// Sent by mail because it has to reach a winner who is not logged in - most of a real ballot's field
/// will be offline when the count runs - and because that is how retail delivers it: hero_conditions
/// carries an election_mail_body string for exactly this.
///
/// The contents are not chosen here. hero_rewards names an item_set_id per (nation, placing), and the
/// set holds one cloak plus the office's consumables:
///
///   grade 4 Erenor      38038 Outlaw, 38039 Nuia, 38040 Haranya, 38041 Independent
///   grade 3 Ayanad      38042 Outlaw, 38045 Nuia, 38048 Haranya, 38051 Independent
///   grade 2 Delphinad   38046 Nuia,   38049 Haranya
///
/// so the cloak differs by nation as well as by rank, and picking the set by placing gets both without
/// this code knowing anything about cloaks.
/// </remarks>
public class MailForHeroElection : BaseMail
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private static readonly string HeroSenderName = ".heroElection";

    private MailForHeroElection() : base()
    {
    }

    /// <summary>
    /// Builds and sends the reward mail for one newly seated hero, or does nothing if the data has no
    /// reward for that placing.
    /// </summary>
    /// <returns>How many items were attached; 0 means nothing was sent.</returns>
    public static int Send(uint characterId, uint nationId, int ranking)
    {
        var receiverName = NameManager.Instance.GetCharacterName(characterId);
        if (receiverName == null)
        {
            Logger.Warn("HeroElection mail: no name for character {0}; not sent", characterId);
            return 0;
        }

        var reward = Hero.HeroRewards.For(nationId, ranking);
        if (reward is not { ItemSetId: > 0 })
        {
            Logger.Warn("HeroElection mail: nation {0} rank {1} has no reward item set", nationId, ranking);
            return 0;
        }

        var itemSet = ItemManager.Instance.GetItemSet(reward.Value.ItemSetId);
        if (itemSet == null || itemSet.Items.Count == 0)
        {
            Logger.Warn("HeroElection mail: item set {0} is empty or missing", reward.Value.ItemSetId);
            return 0;
        }

        var mail = new MailForHeroElection
        {
            MailType = MailType.SysExpress,
            Title = "Hero Election",
            ReceiverName = receiverName
        };
        mail.Header.ReceiverId = characterId;
        mail.Header.SenderId = 0;
        mail.Header.SenderName = HeroSenderName;
        mail.Body.RecvDate = DateTime.UtcNow;
        mail.Body.Text = $"Congratulations on your election. Grade {reward.Value.Grade} regalia is attached.";

        var online = WorldManager.Instance.GetCharacterById(characterId);
        var attached = 0;

        foreach (var setItem in itemSet.Items.Values)
        {
            var template = ItemManager.Instance.GetTemplate(setItem.ItemId);
            if (template == null)
            {
                Logger.Warn("HeroElection mail: unknown item {0} in set {1}", setItem.ItemId, itemSet.Id);
                continue;
            }

            var grade = (byte)Math.Max(template.FixedGrade, 0);
            var item = ItemManager.Instance.Create(setItem.ItemId, setItem.Count, grade);
            if (item == null)
                continue;

            item.OwnerId = characterId;

            // A loaded character owns their own containers, so the item has to go through the inventory
            // to stay consistent with what they are looking at. For anyone offline, setting the slot is
            // all there is - the container is rebuilt from the database when they log in.
            if (online != null)
                online.Inventory.MailAttachments.AddOrMoveExistingItem(ItemTaskType.Invalid, item);
            else
                item.SlotType = SlotType.Mail;

            ApplyTermExpiry(item);

            mail.Body.Attachments.Add(item);
            attached++;

            // The client shows ten per mail; a set that outgrew that would silently lose the overflow.
            if (attached >= 10)
                break;
        }

        if (attached == 0)
            return 0;

        // MailManager.Send rejects a receiver whose name and id do not agree, and says so only at Debug -
        // silent enough to look like nothing happened at all. Report it here instead.
        if (!mail.Send())
        {
            Logger.Warn("HeroElection mail: MailManager refused the mail to {0} (id {1}); nothing delivered",
                receiverName, characterId);
            return 0;
        }

        Logger.Info("HeroElection mail: sent {0} item(s) from set {1} to {2} (rank {3}, nation {4})",
            attached, itemSet.Id, receiverName, ranking, nationId);
        return attached;
    }

    /// <summary>
    /// Ties the regalia to the term rather than to the template's expiry date.
    /// </summary>
    /// <remarks>
    /// The cloak is meant to last one hero period, and the item data cannot express that: item 38039
    /// carries exp_date 2015-01-14, a leftover absolute date, and ItemContainer stamps any template
    /// ExpDate above DateTime.MinValue onto the item as it enters a container (ItemContainer.cs:725). So
    /// a freshly granted cloak arrived already marked "Item expired" - cosmetic, since nothing enforces
    /// it and the equip effect still applies, but wrong.
    ///
    /// The term ends when hero_schedules says the hero_period window ends. When that is already past -
    /// which it is whenever the phases are being forced outside the real schedule - no expiry is set at
    /// all. A term that ended before it began is not a limit worth applying, and inheriting the 2015
    /// date is worse than none.
    /// </remarks>
    private static void ApplyTermExpiry(Item item)
    {
        var window = Hero.HeroSchedule.Find(HeroElectionManager.Season, Hero.HeroPhase.HeroPeriod);
        var termEnd = window?.End ?? DateTime.MinValue;

        item.ExpirationTime = termEnd > DateTime.UtcNow ? termEnd : DateTime.MinValue;
    }
}
