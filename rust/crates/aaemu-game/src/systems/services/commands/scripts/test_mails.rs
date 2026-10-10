//! `Scripts/Commands/TestMails.cs`.

use super::prelude::*;
use crate::models::container::slot_type;
use crate::systems::economy::inventory::ItemTxn;
use crate::systems::economy::mail::{self, mail_type, BaseMail};
use crate::systems::economy::mail_character;

/// `MailType` member names (`names[value]`).
pub const MAIL_TYPE_NAMES: [&str; 45] = [
    "InvalidMailType",
    "Normal",
    "Express",
    "Registered",
    "Spam",
    "Admin",
    "Billing",
    "BalanceReceipt",
    "TaxrateChanged",
    "Charged",
    "Promotion",
    "MiaSend",
    "MiaRecv",
    "AucOffCancel",
    "AucOffSuccess",
    "AucOffFail",
    "AucBidWin",
    "AucBidFail",
    "SysExpress",
    "SysSellBackpack",
    "DemolishWithPenalty",
    "HousingSale",
    "UserBill",
    "NationTaxRate",
    "NationTaxReceipt",
    "NationRequestFriend",
    "KickedFromBeginnerExped",
    "MobilizationGiveItem",
    "HeroCandidateAlarm",
    "HeroElectionItem",
    "HousingRebuild",
    "Demolish",
    "SiegeWinGiveItem",
    "SiegeLoseGiveItem",
    "TaxInKindReceipt",
    "NationRelationFriend",
    "NationRelationFriendSuccess",
    "NationRelationFriendReject",
    "NationRelationFriendRejectItem",
    "NationRelationHostile",
    "NationRelationHostileSuccess",
    "NationRelationHostileReject",
    "SiegeAuctionStart",
    "ExpeditionImmigrationReject",
    "ResidentBalance",
];

/// `MailStatus` member names.
const MAIL_STATUS_NAMES: [&str; 3] = ["Unread", "Read", "Unpaid"];

/// `Enum.TryParse(text, out MailType)` (a `byte` enum: numbers outside 0..=255 fail).
fn parse_mail_type(text: &str) -> Option<u8> {
    let t = text.trim();
    if t.starts_with(|c: char| c.is_ascii_digit() || c == '-' || c == '+') {
        return try_parse::<u8>(t);
    }
    enum_try_parse(t, &MAIL_TYPE_NAMES, i32::MIN.into(), i32::MAX.into())
        .and_then(|v| u8::try_from(v).ok())
}

/// `TestMails`.
pub struct TestMails;

impl TestMails {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }

    /// The `try { ... }` body (`Err` = the caught exception's message).
    fn send_test_mail(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        m_type: u8,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> Result<(), String> {
        let (char_id, char_name) = {
            let c = player.data();
            (c.id, c.unit.name.clone())
        };
        let now = mail::now();
        let mut mail = BaseMail {
            mail_type: m_type,
            title: format!(
                "TestMail {}",
                enum_name(i64::from(m_type), &MAIL_TYPE_NAMES)
            ),
            receiver_name: char_name.clone(),
            ..BaseMail::default()
        };
        mail.header.sender_id = char_id;
        mail.header.sender_name = char_name;
        mail.header.receiver_id = char_id;
        mail.header.attachments = 0;
        mail.header.extra = 0;
        mail.body.text = "Test Mail Body".into();
        mail.body.send_date = now;
        mail.body.recv_date = now;

        if let Some(n) = args.get(1) {
            mail.header.sender_name = n.clone();
            mail.header.sender_id = server.names.get_character_id(n);
            CommandManager::send_normal_text(self, out, &format!("From: {n}"));
        }
        if let Some(n) = args.get(2) {
            mail.title = n.clone();
            CommandManager::send_normal_text(self, out, &format!("Title: {n}"));
        }
        if let Some(n) = args.get(3) {
            mail.body.text = n.clone();
            CommandManager::send_normal_text(self, out, &format!("Body: {n}"));
        }
        if let Some(n) = args.get(4) {
            match try_parse::<i32>(n) {
                Some(copper) => {
                    mail.body.copper_coins = copper;
                    CommandManager::send_normal_text(self, out, &format!("Money: {copper}"));
                }
                None => CommandManager::send_error_text(self, out, "Money parse error"),
            }
        }
        if let Some(n) = args.get(5) {
            match try_parse::<i32>(n) {
                Some(billing) => {
                    mail.body.billing_amount = billing;
                    CommandManager::send_normal_text(self, out, &format!("BillingCost: {billing}"));
                }
                None => CommandManager::send_error_text(self, out, "Billing Cost parse error"),
            }
        }
        if args.len() > 6 {
            let mut a = 6;
            while a < args.len() - 1 {
                let (i_str, c_str) = (&args[a], &args[a + 1]);
                a += 2;
                let Some(item_id) = try_parse::<u32>(i_str) else {
                    CommandManager::send_error_text(self, out, "Parse Error on ItemID");
                    continue;
                };
                let Some(mut item_count) = try_parse::<i32>(c_str) else {
                    CommandManager::send_error_text(self, out, "Parse Error on ItemCount");
                    continue;
                };
                let Some(item_template) = server.game_data.items.template(item_id) else {
                    CommandManager::send_error_text(
                        self,
                        out,
                        &format!("Template does not exist for {item_id} !"),
                    );
                    return Ok(());
                };
                if item_count < 1 {
                    item_count = 1;
                }
                if item_count > item_template.max_count {
                    item_count = item_template.max_count;
                }
                let item_grade = item_template.fixed_grade.max(0);
                let mut new_item = server
                    .items
                    .create(
                        item_id,
                        item_count,
                        item_grade as u8,
                        server.config.id,
                        &server.game_data.items,
                        &server.ids.item,
                        now,
                    )
                    .ok_or("Object reference not set to an instance of an object.")?;
                new_item.owner_id = u64::from(char_id);
                new_item.slot_type = slot_type::MAIL;
                new_item.dirty = true;
                mail.body.attachments.push(new_item.id);
                server.items.add_loose(new_item);
                CommandManager::send_normal_text(
                    self,
                    out,
                    &format!("Attachment: @ITEM_NAME({item_id}) ({item_id}) x {item_count}"),
                );
            }
        }
        let mut txn = ItemTxn::begin(server);
        mail::send(&mut txn, mail);
        Ok(())
    }
}

impl Command for TestMails {
    fn command_names(&self) -> Vec<String> {
        names(&["testmail", "test_mail"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        // Example: Dummy pack base 1g at 125% no bonus, seller is not the crafter
        // /testmail 19 .sellBackpack Payment "body('Dummy Pack', 125, 12500, 10000, 0, 10500, 0, 1, 0, 0)"

        // Example: Dummy pack base 1g at 125% no bonus, seller is crafter
        // /testmail 19 .sellBackpack Payment "body('Dummy Pack', 125, 12500, 13125, 0, 0, 2, 1, 0, 0)"

        if args.is_empty() {
            // List all mailTypes
            CommandManager::send_normal_text(
                self,
                out,
                &format!("{}\rPossible MailTypes:", self.get_command_help_text()),
            );
            let mut s = String::new();
            for (i, t) in MAIL_TYPE_NAMES.iter().enumerate() {
                s += &format!("{i}={t}");
                s += "  ";
            }
            CommandManager::send_normal_text(self, out, &s);
            return Ok(());
        }

        let server = ctx.server()?;
        let player = ctx.player()?;
        let character_id = player.character_id;
        let mut m_type = mail_type::INVALID_MAIL_TYPE;
        match args[0].to_lowercase().as_str() {
            "list" => {
                CommandManager::send_normal_text(self, out, "List of Mails");
                let lines: Vec<String> = {
                    let mut txn = ItemTxn::begin(server);
                    let ids = mail::get_current_mail_list(&mut txn, character_id);
                    let st = server.systems.economy.mail.state();
                    ids.iter()
                        .filter_map(|id| st.all.get(*id))
                        .map(|m| {
                            format!(
                                "{} - {} - ({}) {}",
                                m.id,
                                enum_name(i64::from(m.mail_type), &MAIL_TYPE_NAMES),
                                enum_name(i64::from(m.header.status), &MAIL_STATUS_NAMES),
                                m.title
                            )
                        })
                        .collect()
                };
                for line in lines {
                    CommandManager::send_normal_text(self, out, &line);
                }
                CommandManager::send_normal_text(self, out, "End of List");
                return Ok(());
            }
            "clear" => {
                CommandManager::send_normal_text(self, out, "Clear List of Mails");
                let mut txn = ItemTxn::begin(server);
                for id in mail::get_current_mail_list(&mut txn, character_id) {
                    mail_character::delete_mail(&mut txn, &player, id, false);
                }
                return Ok(());
            }
            "check" => {
                {
                    let mut txn = ItemTxn::begin(server);
                    mail_character::send_unread_mail_count(&mut txn.env.fx, &player);
                }
                let received = player.data().unread_mail.received;
                CommandManager::send_normal_text(self, out, &format!("{received} unread mails"));
                return Ok(());
            }
            _ => {
                if let Some(t) = parse_mail_type(&args[0]) {
                    m_type = t;
                }
            }
        }

        let type_name = enum_name(i64::from(m_type), &MAIL_TYPE_NAMES);
        if m_type == mail_type::INVALID_MAIL_TYPE {
            CommandManager::send_error_text(self, out, &format!("Invalid type: {type_name}"));
            return Ok(());
        }

        CommandManager::send_normal_text(self, out, &format!("Testing type: {type_name}"));
        if let Err(e) = self.send_test_mail(server, &player, m_type, args, out) {
            CommandManager::send_error_text(self, out, &format!("Exception: {e}"));
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "<action||mailType> [senderName] [title] [body] [money] [billing] [[attachmentItems ID/Counts]]".into()
    }

    fn get_command_help_text(&self) -> String {
        "Execute a mail related action or sends a dummy mail to yourself of a given type\rAllowed actions: list, clear, check".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use crate::data::items::ItemTemplate;
    use crate::data::GameData;
    use crate::opcodes::sc;
    use crate::systems::economy::mail::mail_type;

    fn data() -> GameData {
        let mut data = GameData::empty();
        let t = ItemTemplate {
            fixed_grade: -1,
            ..ItemTemplate::plain(8000, 10)
        };
        data.items.templates.insert(t.id, t);
        data
    }

    #[tokio::test]
    async fn testmail_lists_types_and_rejects_invalid() {
        let server = harness::server();
        let gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let (_, out) = harness::run(&server, &gm.p, "testmail").await;
        assert_eq!(out.len(), 2);
        assert_eq!(
            out[0],
            harness::normal(
                "testmail",
                "Execute a mail related action or sends a dummy mail to yourself of a given type\rAllowed actions: list, clear, check\rPossible MailTypes:"
            )
        );
        assert!(out[1].starts_with("[testmail] 0=InvalidMailType  1=Normal  2=Express  "));
        assert!(out[1].ends_with("44=ResidentBalance  "));
        for arg in ["nonsense", "0", "InvalidMailType", "normal", "256"] {
            let (_, out) = harness::run(&server, &gm.p, &format!("test_mail {arg}")).await;
            assert_eq!(
                out,
                [harness::error("testmail", "Invalid type: InvalidMailType")]
            );
        }
    }

    #[tokio::test]
    async fn testmail_sends_lists_checks_and_clears() {
        let server = harness::server_with(data());
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        server.names.add_character(1, "Gm", 1001);
        server.names.add_character(7, "Bob", 1007);

        // A plain mail of type Normal (by name).
        let (_, out) = harness::run(&server, &gm.p, "testmail Normal").await;
        assert_eq!(out, [harness::normal("testmail", "Testing type: Normal")]);
        assert!(gm
            .packets()
            .iter()
            .any(|(op, _)| *op == sc::SCGotMailPacket));
        // Every optional argument, by number; undefined numbers print as numbers.
        let (_, out) = harness::run(
            &server,
            &gm.p,
            "testmail 18 Bob \"Hello there\" Body x 25 8000 50 9999 1 x",
        )
        .await;
        assert_eq!(
            out,
            [
                harness::normal("testmail", "Testing type: SysExpress"),
                harness::normal("testmail", "From: Bob"),
                harness::normal("testmail", "Title: Hello there"),
                harness::normal("testmail", "Body: Body"),
                harness::error("testmail", "Money parse error"),
                harness::normal("testmail", "BillingCost: 25"),
                harness::normal("testmail", "Attachment: @ITEM_NAME(8000) (8000) x 10"),
                harness::error("testmail", "Template does not exist for 9999 !"),
            ]
        );
        // The second mail was not sent (the C# returns before `Send`).
        let (_, out) = harness::run(&server, &gm.p, "testmail 99 Bob T B 5 0 8000 0").await;
        assert_eq!(
            out,
            [
                harness::normal("testmail", "Testing type: 99"),
                harness::normal("testmail", "From: Bob"),
                harness::normal("testmail", "Title: T"),
                harness::normal("testmail", "Body: B"),
                harness::normal("testmail", "Money: 5"),
                harness::normal("testmail", "BillingCost: 0"),
                harness::normal("testmail", "Attachment: @ITEM_NAME(8000) (8000) x 1"),
            ]
        );
        let mails: Vec<_> = {
            let st = server.systems.economy.mail.state();
            st.all
                .iter()
                .map(|(_, m)| {
                    (
                        m.mail_type,
                        m.title.clone(),
                        m.header.sender_id,
                        m.body.copper_coins,
                        m.body.attachments.len(),
                    )
                })
                .collect()
        };
        assert_eq!(
            mails,
            vec![
                (mail_type::NORMAL, "TestMail Normal".to_string(), 1, 0, 0),
                (99, "T".to_string(), 7, 5, 1),
            ]
        );

        gm.frames();
        let (_, out) = harness::run(&server, &gm.p, "testmail check").await;
        assert_eq!(out, [harness::normal("testmail", "2 unread mails")]);
        assert!(gm
            .packets()
            .iter()
            .any(|(op, _)| *op == sc::SCCountUnreadMailPacket));

        let ids: Vec<i64> = server.systems.economy.mail.state().all.keys();
        let (_, out) = harness::run(&server, &gm.p, "testmail LIST").await;
        assert_eq!(
            out,
            [
                harness::normal("testmail", "List of Mails"),
                harness::normal(
                    "testmail",
                    &format!("{} - Normal - (Unread) TestMail Normal", ids[0])
                ),
                harness::normal("testmail", &format!("{} - 99 - (Unread) T", ids[1])),
                harness::normal("testmail", "End of List"),
            ]
        );

        // `clear` deletes the mails without attachments only.
        let (_, out) = harness::run(&server, &gm.p, "testmail clear").await;
        assert_eq!(out, [harness::normal("testmail", "Clear List of Mails")]);
        assert_eq!(server.systems.economy.mail.state().all.keys(), vec![ids[1]]);
    }
}
