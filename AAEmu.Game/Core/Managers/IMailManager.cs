using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Mails;

using MySql.Data.MySqlClient;

namespace AAEmu.Game.Core.Managers;

public interface IMailManager : ILoadable
{
    BaseMail GetMailById(long id);
    uint GetNewMailId();
    bool Send(BaseMail mail, bool publishNow = true);
    bool TryDeliverOn(BaseMail mail, MySqlConnection connection, MySqlTransaction transaction);
    bool TryCreateExistingItemDeliveryPlan(IReadOnlyList<Item> items,
        Func<int, IReadOnlyList<Item>, BaseMail> createMail,
        out ExistingItemMailDeliveryPlan plan);
    bool TryStageDelivery(BaseMail mail, out string targetName);
    void PublishDelivered(BaseMail mail);
    void DiscardUnpersisted(BaseMail mail);
    bool SendBatch(IReadOnlyList<BaseMail> mails);
    bool TryPrepareBatch(IReadOnlyList<BaseMail> mails, out PreparedMailBatch batch);
    void PersistPreparedBatch(IReadOnlyList<BaseMail> mails, MySqlConnection connection, MySqlTransaction transaction);
    bool PublishPreparedBatch(PreparedMailBatch batch, bool alreadyPersisted = false);
    void CancelPreparedBatch(PreparedMailBatch batch);
    bool TryReturnToSender(BaseMail mail);
    bool TryReturnToSenderFor(BaseMail mail, uint characterId);
    [Obsolete]
    void SendMail(MailType type, string receiverName, string senderName, string title, string text, byte attachments, int[] moneyAmounts, long extra, List<Item> items);
    bool DeleteMail(long id);
    bool DeleteMail(BaseMail mail, bool trashItems = false);
    Dictionary<long, BaseMail> GetCurrentMailList(uint characterId);
    void CheckAllMailTimings();
    bool PayChargeMoney(Character character, long mailId, bool autoUseAAPoint);
    void DeleteHouseMails(uint houseId);
    List<BaseMail> GetMyHouseMails(uint houseId);
    (int, int) Save(MySqlConnection connection, MySqlTransaction transaction);
    void PersistNow();
    IDisposable DeferPersist();
    WorldSaveStatus TakeLastFlushStatus();
    WorldSaveStatus FlushRequestedNow();
    WorldSaveStatus FlushRequestedNow(Action onFailed);
    Dictionary<long, BaseMail> AllPlayerMails { get; }
}
