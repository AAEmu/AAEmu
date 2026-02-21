using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Core.Managers;

public interface IMailManager
{
    BaseMail GetMailById(long id);
    uint GetNewMailId();
    bool Send(BaseMail mail);
    [Obsolete]
    void SendMail(MailType type, string receiverName, string senderName, string title, string text, byte attachments, int[] moneyAmounts, long extra, List<Item> items);
    bool DeleteMail(long id);
    bool DeleteMail(BaseMail mail, bool trashItems = false);
    void Load();
    Dictionary<long, BaseMail> GetCurrentMailList(uint characterId);
    void CheckAllMailTimings();
    bool PayChargeMoney(Character character, long mailId, bool autoUseAAPoint);
    void DeleteHouseMails(uint houseId);
    List<BaseMail> GetMyHouseMails(uint houseId);
}
