//! `Scripts/Commands/GetAttribute.cs`.

use super::prelude::*;
use crate::models::unit::UnitAttribute;
use crate::systems::skills::stats::{self, StatKind};

/// `Enum.GetValues<UnitAttribute>()` (ascending values).
fn all_unit_attributes() -> Vec<UnitAttribute> {
    (0..=u8::MAX).filter_map(UnitAttribute::from_u8).collect()
}

/// `UnitAttribute` names for `Enum.TryParse` / `ToString()`.
fn unit_attribute_names() -> Vec<(String, i64)> {
    all_unit_attributes()
        .into_iter()
        .map(|a| (format!("{a:?}"), a as i64))
        .collect()
}

/// `((UnitAttribute)value).ToString()`.
fn attribute_name(value: u8) -> String {
    UnitAttribute::from_u8(value).map_or_else(|| value.to_string(), |a| format!("{a:?}"))
}

/// `Unit.GetAttribute(attr)` for several attributes: the value of the getter tagged with the
/// attribute (`ToString()` of an int or float), `"NotFound"` when none is.
fn get_attributes(server: &GameServer, target: &WorldObject, attrs: &[u8]) -> Vec<String> {
    let kinds: Vec<Option<StatKind>> = attrs
        .iter()
        .map(|a| UnitAttribute::from_u8(*a).and_then(StatKind::from_unit_attribute))
        .collect();
    let wanted: Vec<StatKind> = kinds.iter().flatten().copied().collect();
    let mut values = stats::stats(server, target, &wanted).into_iter();
    kinds
        .iter()
        .map(|k| match k {
            None => "NotFound".to_string(),
            Some(k) => {
                let v = values.next().unwrap_or(0.0);
                if k.is_int() {
                    (v as i32).to_string()
                } else {
                    f32s(v as f32)
                }
            }
        })
        .collect()
}

/// `GetAttribute`.
pub struct GetAttribute;

impl GetAttribute {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for GetAttribute {
    fn command_names(&self) -> Vec<String> {
        names(&["getattribute", "getattr", "attr"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let mut args_idx = 0;
        if args.is_empty() {
            CommandManager::send_default_help_text(self, out);
            return Ok(());
        }
        let server = ctx.server()?;
        let character = ctx.player()?;
        let mut target = WorldObject::from(&character);

        if args.len() > 1 && args[0] == "target" {
            match current_target(server, &character).filter(WorldObject::is_unit) {
                Some(t) => target = t,
                None => {
                    CommandManager::send_error_text(self, out, "No Target Selected");
                    return Ok(());
                }
            }
            args_idx += 1;
        }

        let (name, obj_id) = (
            target.with_base_unit(|b| b.name().to_string()),
            target.obj_id(),
        );
        CommandManager::send_normal_text(self, out, &format!("Stats for target {name} ({obj_id})"));

        let arg = &args[args_idx];
        let send = |text: String| send_player_message(&character, &text);
        if arg.eq_ignore_ascii_case("all") {
            let attrs: Vec<u8> = all_unit_attributes().into_iter().map(|a| a as u8).collect();
            let values = get_attributes(server, &target, &attrs);
            for (a, value) in attrs.iter().zip(values) {
                send(format!("{}: {value}", attribute_name(*a)));
            }
        } else if arg.eq_ignore_ascii_case("used") {
            let attrs: Vec<u8> = all_unit_attributes().into_iter().map(|a| a as u8).collect();
            let values = get_attributes(server, &target, &attrs);
            for (a, value) in attrs.iter().zip(values) {
                let name = attribute_name(*a);
                let mut hide = value == "NotFound" || value == "0";
                // Exception for multipliers
                if value != "NotFound" && name.contains("Mul") {
                    hide = value == "1";
                }
                if !hide {
                    send(format!("{name}: {value}"));
                }
            }
        } else if let Some(attr_id) = try_parse::<u8>(arg) {
            if UnitAttribute::from_u8(attr_id).is_some() {
                let value = get_attributes(server, &target, &[attr_id]).remove(0);
                send(format!("{}: {value}", attribute_name(attr_id)));
            } else {
                send("Attribute doesn't exist.".to_string());
            }
        } else {
            let names = unit_attribute_names();
            let names: Vec<(&str, i64)> = names.iter().map(|(n, v)| (n.as_str(), *v)).collect();
            match enum_try_parse(arg, &names, 0, i64::from(u8::MAX)) {
                Some(v) => {
                    let attr = v as u8;
                    let value = get_attributes(server, &target, &[attr]).remove(0);
                    send(format!("{}: {value}", attribute_name(attr)));
                }
                None => send("Attribute doesn't exist.".to_string()),
            }
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "[\"target\"] <attrId || attrName || all || used>".into()
    }

    fn get_command_help_text(&self) -> String {
        "Shows a list of all attributes of target".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::add_xp::tests::spawn_npc;
    use super::super::harness;

    #[tokio::test]
    async fn attr_shows_unit_attributes() {
        let server = harness::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);

        let (_, out) = harness::run(&server, &gm.p, "attr 200").await;
        assert_eq!(out, ["[getattribute] Stats for target Gm (257)"]);
        assert_eq!(
            gm.chat(),
            [
                "[getattribute] Stats for target Gm (257)",
                "Attribute doesn't exist."
            ]
        );

        // By name (case insensitive) and by id: a getter that exists / one that doesn't.
        harness::run(&server, &gm.p, "attr movespeedmul").await;
        assert_eq!(gm.chat()[1], "MoveSpeedMul: 1");
        harness::run(&server, &gm.p, "getattr 9").await;
        assert_eq!(gm.chat()[1], "Attribute doesn't exist.");
        harness::run(&server, &gm.p, "getattr 140").await;
        assert_eq!(gm.chat()[1], "DropRateMul: NotFound");
        harness::run(&server, &gm.p, "getattr nothing").await;
        assert_eq!(gm.chat()[1], "Attribute doesn't exist.");

        // `all` lists every attribute, `used` hides zeros, "NotFound" and multipliers at 1.
        harness::run(&server, &gm.p, "attr ALL").await;
        let all = gm.chat();
        assert_eq!(all.len(), 1 + super::all_unit_attributes().len());
        assert_eq!(all[1].split(':').next(), Some("Str"));
        harness::run(&server, &gm.p, "attr used").await;
        let used = gm.chat();
        assert!(used.len() < all.len());
        assert!(!used.iter().any(|l| l.ends_with("NotFound")));
        assert!(!used.iter().any(|l| l == "MoveSpeedMul: 1"));

        // Another unit.
        let npc = spawn_npc(&server, 0x500, 10, 105.0, 100.0, 1_000);
        let (_, out) = harness::run(&server, &gm.p, "attr target 6").await;
        assert_eq!(out, [harness::error("getattribute", "No Target Selected")]);
        gm.p.data().unit.current_target = npc.obj_id;
        gm.chat();
        let (_, out) = harness::run(&server, &gm.p, "attr target 6").await;
        assert_eq!(out, ["[getattribute] Stats for target Npc10 (1280)"]);
        assert_eq!(gm.chat()[1], "MaxHealth: 1000");
    }
}
