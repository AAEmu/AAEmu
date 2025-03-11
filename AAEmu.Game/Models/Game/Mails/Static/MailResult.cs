namespace AAEmu.Game.Models.Game.Mails.Static;

// There are many repetitions with ErrorMessageType
public enum MailResult : byte
{
    // 0 Success
    Success = 0,

    // 1 Insufficient Coins
    InsufficientCoins = 1,

    // 2 Can't be mailed
    CanNotBeMailed = 2,

    // 3 Invalid Slot
    InvalidSlot = 3,

    // 4 Can't Find Mail
    CanNotFindMail = 4,

    // 5 No Attached Money
    NoAttachedMoney = 5,

    // No attached items.
    NoAttachedItems = 6,

    // InsufficientCoins
    InsufficientCoins_2 = 7,

    // Returns aren't allowed.
    ReturnsNotAllowed = 8,

    // Subject length is limited.
    SubjectLengthLimited = 9,

    // Text length is limited.

    TextLengthLimited = 10,

    // Mail hasn't been initialized.
    MailNotInitialized = 11,

    // Invalid letter format.
    InvalidLetterFormat = 12,

    // Incorrect item information; check your bag.
    IncorrectItemInformation = 13,

    // Unable to access selected mail.
    UnableToAccessSelectedMail = 14,

    // Unable to find recipient.
    UnableToFindRecipient = 15,

    // No mailbox nearby.
    NoMailboxNearby = 16,

    // InsufficientCoins
    InsufficientCoins_3 = 17,

    // Feature under maintenance.
    FeatureUnderMaintenance = 18,

    // Bound item.
    BoundItem = 19,

    // Not available in the free trial.
    NotAvailableInFreeTrial = 20,

    // Not available for free accounts.
    NotAvailableForFreeAccounts = 21,

    // Your level is too low.
    LevelTooLow = 22,

    // You must pay first.
    YouMustPayFirst = 23,

    // Characters transferring servers can't receive mail with attachments.
    CharactersTransferringServers = 24,

    // A secondary password is required.
    SecondaryPasswordRequired = 25,

    // Item locked.
    ItemLocked = 26,

    // You cannot perform any account-sensitive actions.
    CannotPerformSensitiveActions = 27,

    // You are sending mails too frequently. Please try again later.
    SendingMailsTooFrequent = 28,

    // You have sent the same mail 3 or more times in a row. Please try again later.
    SentSameMailTooManyTimes = 29,

    // A mail error occurred.
    MailErrorOccurred = 30
}
