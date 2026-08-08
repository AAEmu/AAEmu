using AAEmu.Game.Core.Managers;
using System.Linq;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Mails;

using System.Text;

using NLog;

namespace AAEmu.Game.Models.Game.Char;

public class CharacterMails
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>Capacity of the mails.title / mails.text columns, which are MySQL text.</summary>
    private const int MaxMailTitleBytes = 65535;
    private const int MaxMailTextBytes = 65535;

    private Character Self { get; set; }
    public CountUnreadMail UnreadMailCount { get; set; }

    public CharacterMails(Character self)
    {
        Self = self;

        UnreadMailCount = new CountUnreadMail
        {
            Sent = 0,
        };
        UnreadMailCount.ResetReceived();
    }

    /// <summary>Opens the inbox, sent, or commercial mailbox and publishes one page.</summary>
    public void OpenMailbox(byte mailBoxListKind = 1, uint startIdx = 1, uint pageCount = 100)
    {
        RefreshAllMailCounts();

        var now = DateTime.UtcNow;
        var candidates = new List<BaseMail>();
        foreach (var (_, mail) in MailManager.Instance.AllPlayerMails)
        {
            if (mail.Body.RecvDate > now)
                continue;
            if (!BelongsInMailbox(mail, mailBoxListKind))
                continue;
            candidates.Add(mail);
        }

        // startIdx is 1-based from the client; keep listing compact.
        var start = startIdx == 0 ? 0 : (int)startIdx - 1;
        if (start < 0)
            start = 0;
        var take = pageCount == 0 ? 100 : (int)Math.Min(pageCount, 100);
        var page = candidates
            .OrderByDescending(m => m.Body.RecvDate)
            .Skip(start)
            .Take(take)
            .ToList();

        var total = (uint)candidates.Count;
        var isSentBox = mailBoxListKind == 2;
        foreach (var mail in page)
        {
            Self.SendPacket(new SCMailListPacket(isSentBox, total, mail.Header, mailBoxListKind));
            // Marketplace Mail Lua (comercial_mailbox.FillMailList) requires BOTH title and
            // body for every row: body nil ⇒ MAIL_LIST_CONTINUE + WaitPageCont spinner forever.
            // SCMailList only seeds the title; push SCMailBody (isPrepare) so GetCacheBodyInfo
            // returns non-nil and CompleteMailList can run.
            // Kind 3 is commercial; also send for inbox so recvDate/attachments display match.
            Self.SendPacket(new SCMailBodyPacket(true, isSentBox, mail.Body, false, UnreadMailCount));
        }

        Self.SendPacket(new SCMailListEndPacket(mailBoxListKind, UnreadMailCount));
        Self.SendPacket(new SCCountTotalMailPacket(UnreadMailCount));

        if (mailBoxListKind == 3)
        {
            foreach (var mail in page)
            {
                if (mail.Header.ReceiverId == Self.Id &&
                    mail.Header.Status != MailStatus.Read &&
                    mail.MailType is MailType.Charged or MailType.Promotion)
                {
                    Self.SendPacket(new SCGotMailPacket(mail.Header, UnreadMailCount, mail.Body));
                }
            }
        }
    }

    private bool BelongsInMailbox(BaseMail mail, byte kind)
    {
        return kind switch
        {
            // Sent box
            2 => mail.Header.SenderId == Self.Id && mail.Header.SenderId != 0 &&
                 mail.MailType is not (MailType.Charged or MailType.Promotion),
            // Marketplace / commercial (Charged=9, Promotion=10)
            3 => mail.Header.ReceiverId == Self.Id &&
                 mail.MailType is MailType.Charged or MailType.Promotion,
            // Normal inbox (not commercial, not mia)
            _ => mail.Header.ReceiverId == Self.Id &&
                 mail.MailType is not (MailType.Charged or MailType.Promotion) &&
                 mail.MailType != MailType.MiaRecv
        };
    }

    /// <summary>
    /// Fills both total_* and unread_* for CharacterState / mailbox chrome.
    /// Kind filters still run in <see cref="OpenMailbox"/>; this only feeds count packets.
    /// </summary>
    public void RefreshAllMailCounts()
    {
        UnreadMailCount.ResetAll();
        var now = DateTime.UtcNow;
        foreach (var (_, mail) in MailManager.Instance.AllPlayerMails)
        {
            var isForMe = mail.Header.ReceiverId == Self.Id;
            var isFromMe = mail.Header.SenderId == Self.Id && mail.Header.SenderId != 0;
            if (!isForMe && !isFromMe)
                continue;
            if (mail.Body.RecvDate > now)
                continue;

            if (isFromMe && !isForMe)
            {
                UnreadMailCount.TotalSent++;
                if (mail.Header.Status != MailStatus.Read)
                    UnreadMailCount.Sent++;
                continue;
            }

            // Inbox / commercial addressed to this character
            UnreadMailCount.AddTotal(mail.MailType);
            if (mail.Header.Status != MailStatus.Read)
                UnreadMailCount.UpdateReceived(mail.MailType, 1);
        }
    }

    /// <summary>
    /// Resolves a mail id supplied by the client, rejecting any that this character is not a party to.
    ///
    /// Every mail entry point takes its id straight off the wire and used to look it up in the global store
    /// with nothing but a key test, so a crafted packet naming another player's mail was honoured in full:
    /// ReadMail returned the body, DeleteMail destroyed it, and GetAttached handed over its coin and items.
    /// Ids come from MailIdManager and run consecutively, so they did not even have to be guessed.
    ///
    /// <paramref name="sentBox"/> selects which side of the mail the caller claims to be — the sender for the
    /// sent tab, the receiver for the inbox.
    /// </summary>
    private bool TryGetOwnMail(long id, bool sentBox, out BaseMail mail)
    {
        mail = null;

        if (!MailManager.Instance.AllPlayerMails.TryGetValue(id, out var found))
            return false;

        var party = sentBox ? found.Header.SenderId : found.Header.ReceiverId;
        if (party != Self.Id)
        {
            Logger.Warn($"{Self.Name} ({Self.Id}) requested mail {id}, which belongs to {party}");
            return false;
        }

        mail = found;
        return true;
    }

    public void ReadMail(bool isSent, long id)
    {
        if (TryGetOwnMail(id, isSent, out var mail))
        {
            if (mail.Header.Status == MailStatus.Unread && !isSent)
            {
                UnreadMailCount.UpdateReceived(mail.MailType, -1);
                mail.OpenDate = DateTime.UtcNow;
                mail.Header.Status = MailStatus.Read;
                mail.IsDelivered = true;
            }
            Self.SendPacket(new SCMailBodyPacket(false, isSent, mail.Body, true, UnreadMailCount));
            Self.SendPacket(new SCMailStatusUpdatedPacket(isSent, id, mail.Header.Status));
            SendUnreadMailCount();
        }
    }

    public void SendUnreadMailCount()
    {
        Self.SendPacket(new SCCountTotalMailPacket(UnreadMailCount));
    }

    /// <summary>
    /// Recomputes the unread counters straight off the mail store for this character.
    ///
    /// Login cannot use <see cref="MailManager.GetCurrentMailList"/> for this. That method resolves the
    /// Character through <c>WorldManager.GetCharacterById</c> and null-conditionals every use of it, but
    /// <c>CSSelectCharacter</c> runs <c>Character.Load</c> — and with it the mail load — at line 29, while the
    /// character is only assigned an ObjId at 51 and registered with <c>TryAddCharacter</c> at 55. The lookup
    /// therefore always missed, every count stayed zero, and both <c>SCCharacterState</c> and
    /// <c>SCCountTotalMail</c> reported an empty mailbox until the player opened it by hand.
    ///
    /// Counting is restricted to mail addressed to this character: the sender-side rows
    /// <c>GetCurrentMailList</c> also selects are someone else's unread mail, and folding them in inflated the
    /// icon by every letter the player had sent that had not been read yet. Mail whose RecvDate is still in the
    /// future has not landed and is not counted either.
    /// </summary>
    public void RefreshUnreadCount()
    {
        // Keep totals + unread in lockstep (CharacterState "commercial 9/0" was unread
        // commercial with total always 0).
        RefreshAllMailCounts();
    }

    public MailResult SendMailToPlayer(MailType mailType, string receiverName, string title, string text, byte attachments, int money0, int money1, int money2, long extra, List<(SlotType, byte)> itemSlots)
    {

        if (string.IsNullOrWhiteSpace(receiverName) || NameManager.Instance.GetCharacterId(receiverName) == 0)
        {
            return MailResult.UnableToFindRecipient;
        }

        // The three money fields come off the wire as signed ints and were used unchecked.
        //
        // Negative amounts made the affordability test below trivially true and then bounced off
        // SubtractMoney's own "amount < 0" guard, so the mail went out free of charge carrying a negative
        // balance the recipient could never take - and since GetTotalAttachmentCount counts any non-zero
        // coin value, the mail kept an attachment forever and could never be deleted.
        //
        // Large positive amounts were worse: "mailFee + money0" overflowed int, compared as a negative
        // against the sender's balance, passed, and was then rejected by SubtractMoney for being negative,
        // so nothing was charged at all. The recipient still collected the full sum through ChangeMoney,
        // which adds to Money unchecked. That minted currency out of nothing.
        if (money0 < 0 || money1 < 0 || money2 < 0)
        {
            return MailResult.CanNotBeMailed;
        }

        // title and text were written to the mails row unbounded. Both columns are MySQL text, so anything
        // past 65535 bytes fails the INSERT on the save tick - and that tick batches every dirty mail on the
        // server, so one oversized letter takes the whole save down, not just its sender.
        // TODO(v10): the retail subject and body caps are not yet recovered from the client; CSSendMail notes
        // 1600 for the body. These bounds only keep the column safe, they are not the client's own limits.
        if (title != null && Encoding.UTF8.GetByteCount(title) > MaxMailTitleBytes)
        {
            return MailResult.SubjectLengthLimited;
        }

        if (text != null && Encoding.UTF8.GetByteCount(text) > MaxMailTextBytes)
        {
            return MailResult.TextLengthLimited;
        }

        var mail = new MailPlayerToPlayer(Self, receiverName) {
            MailType = mailType,
            Title = title,
            Header = {
                Attachments = attachments,
                Extra = extra
                },
            Body =
            {
                Text = text,
                SendDate = DateTime.UtcNow,
                RecvDate = DateTime.UtcNow
            }
        };

        mail.AttachMoney(money0, money1, money2);

        // First verify source items, and add them to the attachments of body
        if (!mail.PrepareAttachmentItems(itemSlots))
        {
            // Self.SendErrorMessage(ErrorMessageType.MailInvalidItem);
            return MailResult.InvalidSlot;
        }

        // With attachments in place, we can calculate the send fee.
        // Widened to long so the sum cannot wrap past the balance check.
        var mailFee = mail.GetMailFee();
        var totalCost = (long)mailFee + money0;
        if (totalCost > Self.Money)
        {
            // Self.SendErrorMessage(ErrorMessageType.MailNotEnoughMoney);
            return MailResult.InsufficientCoins;
        }

        if (!mail.FinalizeAttachments())
            return MailResult.InvalidSlot; // Should never fail at this point

        // Add delay if not a normal snail mail
        if (mailType == MailType.Normal)
            mail.Body.RecvDate = DateTime.UtcNow + MailManager.NormalMailDelay;

        // Send it
        if (mail.Send())
        {
            Self.SendPacket(new SCMailSentPacket(mail.Header, itemSlots.ToArray()));
            // Take the fee. totalCost is bounded by the balance check above, so the cast is safe.
            Self.SubtractMoney(SlotType.Inventory, (int)totalCost);
            return MailResult.Success;
        }
        else
        {
            return MailResult.MailErrorOccurred;
        }
    }

    public bool GetAttached(long mailId, bool takeMoney, bool takeItems, bool takeAllSelected, ulong specifiedItemId = 0)
    {
        var res = true;
        // Attachments only ever come out of the inbox side, never the sent side.
        if (TryGetOwnMail(mailId, false, out var thisMail))
        {
            var tookMoney = false;
            if (thisMail.MailType == MailType.AucOffSuccess && thisMail.Body.CopperCoins > 0 && takeMoney)
            {
                // Both pools pay; see Character.ChangeLabor.
                if (Self.LaborPower + Self.LocalLaborPower < 1)
                {
                    Self.SendErrorMessage(ErrorMessageType.NotEnoughLaborPower);
                    takeMoney = false;
                }
                else
                {
                    Self.ChangeLabor(-1, (int)ActabilityType.Commerce);
                }
            }
            if (thisMail.Body.CopperCoins > 0 && takeMoney)
            {
                Self.ChangeMoney(SlotType.Inventory, thisMail.Body.CopperCoins);
                thisMail.Body.CopperCoins = 0;
                // Attachments is a byte; decrementing it at zero wraps to 255 and the mail can never be
                // deleted again, since DeleteMail only lets go of a mail once the count reaches zero.
                if (thisMail.Header.Attachments > 0)
                    thisMail.Header.Attachments -= 1;
                tookMoney = true;
            }

            var itemSlotList = new List<ItemIdAndLocation>();
            // Check if items need to be taken, and add them to a list
            if (takeItems)
            {
                var toRemove = new List<Item>();
                foreach (var itemAttachment in thisMail.Body.Attachments)
                {
                    // if not our specified item, skip this slot
                    if (specifiedItemId > 0 && itemAttachment.Id != specifiedItemId)
                        continue;

                    // Sanity-check
                    if (itemAttachment.Id != 0)
                    {
                        // Free Space Check
                        if (Self.Inventory.Bag.SpaceLeftForItem(itemAttachment, out var foundItems) >= itemAttachment.Count)
                        {
                            Item stackItem = null;
                            // Check if we can stack the item onto an existing one
                            if (itemAttachment.Template.MaxCount > 1 && foundItems.Count > 0)
                            {
                                foreach (var fi in foundItems)
                                {
                                    if (fi.Count + itemAttachment.Count <= fi.Template.MaxCount)
                                    {
                                        stackItem = fi;
                                        break;
                                    }
                                }
                            }

                            var itemIdAndLocation = new ItemIdAndLocation
                            {
                                Id = itemAttachment.Id,
                                SlotType = itemAttachment.SlotType,
                                Slot = (byte)itemAttachment.Slot
                            };

                            // Move item to player inventory
                            if (Self.Inventory.Bag.AddOrMoveExistingItem(ItemTaskType.Mail, itemAttachment, stackItem?.Slot ?? -1))
                            {
                                itemSlotList.Add(itemIdAndLocation);
                                if (thisMail.Header.Attachments > 0)
                                    thisMail.Header.Attachments -= 1;
                                toRemove.Add(itemAttachment);
                            }
                            else
                            {
                                // The free-space check above should have covered this, so getting here means the
                                // bag shifted underneath us. Throwing was an item-duplication vector: it escaped
                                // before the toRemove sweep below ran, so anything already moved sat in the bag
                                // while still counting as an attachment, ready to be taken a second time. Stop
                                // taking and let the bookkeeping for what did move complete.
                                Logger.Warn($"GetAttached: could not move attachment {itemAttachment.Id} of mail {mailId} into {Self.Name}'s bag");
                                Self.SendErrorMessage(ErrorMessageType.BagFull);
                                res = false;
                                break;
                            }
                        }
                        else
                        {
                            // Bag Full
                            Self.SendErrorMessage(ErrorMessageType.BagFull);
                            res = false;
                        }
                    }
                }
                // Removed those marked to be taken
                foreach (var ia in toRemove)
                {
                    thisMail.Body.Attachments.Remove(ia);
                    thisMail.IsDirty = true;
                }

            }
            // Mark taken items

            // Send attachments taken packets (if needed)
            // Money
            if (tookMoney)
            {
                Self.SendPacket(new SCAttachmentTakenPacket(mailId, true, false, takeAllSelected, []));
                thisMail.IsDirty = true;
            }

            // Items
            if (itemSlotList.Count > 0)
            {
                // Self.SendPacket(new SCAttachmentTakenPacket(mailId, takeMoney, false, takeAllSelected, itemSlotList));
                /* 
                 * ZeromusXYZ:
                 * Splitting this packet up to be sent one by one fixes delivery issue in cases where not everything is delivered at once,
                 * like full bag, manual item grabbing.
                 * It's kind of silly, but I don't have a better solution for it 
                */
                foreach (var iSlot in itemSlotList)
                {
                    var dummyItemSlotList = new List<ItemIdAndLocation>
                    {
                        iSlot
                    };
                    Self.SendPacket(new SCAttachmentTakenPacket(mailId, takeMoney, false, takeAllSelected, dummyItemSlotList));
                    thisMail.IsDirty = true;
                }
            }

            // Mark mail as read in case we took at least one item from it
            if (thisMail.Header.Status == MailStatus.Unread && (tookMoney || itemSlotList.Count > 0))
            {
                thisMail.Header.Status = MailStatus.Read;
                UnreadMailCount.UpdateReceived(thisMail.MailType, -1);
                Self.SendPacket(new SCMailStatusUpdatedPacket(false, mailId, MailStatus.Read));
                SendUnreadMailCount();
                thisMail.IsDirty = true;
            }

            // TODO: Make sure attachment settings and mail info is sent back correctly 
            // taking all attachments sometimes doesn't enable the delete button when getting attachments using "GetAllSelected"

            // TODO: if source player is online, update their mail info (sent tab)
        }

        return res;
    }

    public void DeleteMail(long id, bool isSent)
    {
        if (isSent || !TryGetOwnMail(id, false, out var mail))
            return;

        // A mail still holding coin or items is not disposable; its contents would go with it.
        if (mail.Header.Attachments > 0)
            return;

        UnreadMailCount.AddTotal(mail.MailType, -1);
        if (mail.Header.Status != MailStatus.Read)
        {
            UnreadMailCount.UpdateReceived(mail.MailType, -1);
            Self.SendPacket(new SCMailDeletedPacket(isSent, id, true, UnreadMailCount));
        }
        else
        {
            Self.SendPacket(new SCMailDeletedPacket(isSent, id, false, UnreadMailCount));
        }

        MailManager.Instance.DeleteMail(id);
    }

    /// <summary>
    /// Hands a received mail back to whoever sent it.
    ///
    /// This used to rebuild the attachment list by indexing <c>Body.Attachments</c> across all ten
    /// <c>MaxMailAttachments</c> slots and re-sending through <see cref="SendMailToPlayer"/>. Attachments is a
    /// variable-length list holding only the slots actually in use, so that walk threw
    /// ArgumentOutOfRangeException on the first empty slot — every mail with fewer than ten attachments, which
    /// is every mail — straight off a client packet. Past the throw it could not have worked either:
    /// PrepareAttachmentItems only accepts items sitting in SlotType.Inventory, and attachments live in the
    /// mail container, so the re-send would have failed InvalidSlot while DeleteMail ran regardless.
    ///
    /// Turning the existing mail around instead keeps the attachments where they are.
    /// </summary>
    public void ReturnMail(long id)
    {
        if (!TryGetOwnMail(id, false, out var thisMail))
        {
            Self.SendPacket(new SCMailFailedPacket(MailResult.CanNotFindMail, [], false));
            return;
        }

        var wasUnread = thisMail.Header.Status != MailStatus.Read;
        var mailType = thisMail.MailType;

        if (!thisMail.ReturnToSenderFor(Self.Id))
        {
            Self.SendPacket(new SCMailFailedPacket(MailResult.ReturnsNotAllowed, [], false));
            return;
        }

        // It is the sender's mail now, so it leaves this inbox and its counters.
        UnreadMailCount.AddTotal(mailType, -1);
        if (wasUnread)
            UnreadMailCount.UpdateReceived(mailType, -1);
        SendUnreadMailCount();
    }
}
