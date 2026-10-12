//! Verified 1.2 UI actions and bounded entity telemetry for reproducible real-client tests.
use super::tool;
use crate::Target;
use anyhow::{bail, Context};
use serde_json::{json, Value};

pub fn tools(pid: Value) -> Vec<Value> {
    vec![
        tool("client_perf", "Record DXGI frame intervals (profile resets and enables, off disables, status reads). Reports FPS, mean, p95, p99 and maximum for the latest 2048 rendered frames. Screenshot readback can affect timings; capture after profiling.",json!({"pid":pid,"action":{"type":"string","enum":["profile","status","off"]}}), &["action"], false),
        tool("client_input", "Press a keyboard key or number-row chord through the client's CryInput on its main thread. Optional ctrl/shift/alt modifiers are pressed before the key and released after it. Works with hidden rendering, without desktop keystrokes. Automatic release after 1..10000 ms survives MCP disconnect. release clears all test keys.",json!({"pid":pid,"key":{"type":"string","enum":["w","a","s","d","q","e","r","t","y","u","i","o","p","space","f","g","1","2","3","4","5","6","7","8","9","0","release"]},"modifiers":{"type":"array","maxItems":3,"uniqueItems":true,"items":{"type":"string","enum":["ctrl","shift","alt"]}},"duration_ms":{"type":"integer","minimum":1,"maximum":10000}}), &["key"], false),
        tool("client_pointer", "Move or left-click through the supported 1.2 client's native mouse handler on its main thread. Coordinates are client pixels, bounded to 0..4095. Hidden rendering is supported. Rejects native mouse-lock mode before movement to preserve the desktop cursor. A click holds for 100 ms with automatic release independent of MCP; verify the resulting UI or gameplay action.",json!({"pid":pid,"action":{"type":"string","enum":["move","click"]},"x":{"type":"integer","minimum":0,"maximum":4095},"y":{"type":"integer","minimum":0,"maximum":4095}}), &["action","x","y"], false),
        tool("client_action", "Execute a valid 1.2 UI hotkey action, such as do_interaction_1, cycle_hostile_forward or toggle_bag. Continuous movement uses client_input.",json!({"pid":pid,"action":{"type":"string","minLength":1,"maxLength":96}}), &["action"], false),
        tool("client_inventory", "Read inventory slots, item templates, names, counts and use skills from the live client. bag_id defaults to 1 (inventory).",json!({"pid":pid,"bag_id":{"type":"integer","minimum":1,"maximum":2}}), &[], true),
        tool("client_unit_state", "Read the player or current target's native name, health, maximum health and at most 32 visible buffs and 32 visible debuffs, indexed from 1. An absent target returns present=false without fabricated health. target defaults to target. Requires an in-world character; does not target or cast.",json!({"pid":pid,"target":{"type":"string","enum":["player","target"],"default":"target"}}), &[], true),
        tool("client_loot", "Read the current native loot bag, up to 32 one-based item rows, and effective pixel bounds of its Take All button. Requires an in-world character; does not open a corpse or take items.",json!({"pid":pid}), &[], true),
        tool("client_mode_actions", "Read the active 1.2 mode action bar, including fishing rod actions, native tooltips, client-pixel widget bounds, availability, cooldowns, active hotkeys and staged option binding rows. Requires a character in the world; does not change bindings or cast skills.",json!({"pid":pid}), &[], true),
        tool("client_use_item", "Use an owned inventory item through its normal client skill cast. Returns whether the client accepted the cast, not a guarantee the server completed it; verify entities and vehicle status after casting/portal time.",json!({"pid":pid,"item_template_id":{"type":"integer","minimum":1,"maximum":u32::MAX}}), &["item_template_id"], false),
        tool("client_chat", "Send a message or GM command through the normal Say channel. Server permissions still apply. Requires a character in the world.",json!({"pid":pid,"message":{"type":"string","minLength":1,"maxLength":512}}), &["message"], false),
        tool("client_vehicle", "Read mounted vehicle, driver, speed, turn speed and vehicle telemetry; discard leaves the driver's seat through the normal client action.",json!({"pid":pid,"action":{"type":"string","enum":["status","discard"]}}), &[], false),
        tool("client_entities", "Read at most 200 nearby engine entities with client-local position, rotation, velocity and terrain height. Seamless map origins shift; these coordinates are not server global coordinates. Optional exact class and name substring filters. Includes prefab_unit boats/cars.",json!({"pid":pid,"class":{"type":"string","maxLength":96},"name":{"type":"string","maxLength":128},"limit":{"type":"integer","minimum":1,"maximum":200}}), &[], true),
    ]
}

pub fn is_tool(name: &str) -> bool {
    matches!(
        name,
        "client_action"
            | "client_inventory"
            | "client_unit_state"
            | "client_loot"
            | "client_mode_actions"
            | "client_use_item"
            | "client_chat"
            | "client_vehicle"
            | "client_entities"
    )
}

// Lua 5.1 decimal byte escapes also handle newlines and avoid Lua/JSON escape differences.
fn quote(s: &str) -> String {
    let mut out = String::from("\"");
    for b in s.bytes() {
        out.push_str(&format!("\\{b:03}"));
    }
    out.push('"');
    out
}

const JSON: &str = r#"
local function encode(v,depth)
 if depth>6 then return 'null' end
 local t=type(v)
 if t=='nil' then return 'null' elseif t=='boolean' then return tostring(v)
 elseif t=='number' then if v~=v or v==math.huge or v==-math.huge then return 'null' end return string.format('%.9g',v)
 elseif t=='string' then return '"'..string.gsub(v,'[%z\1-\31\\"]',function(c)
  if c=='"' then return '\\"' elseif c=='\\' then return '\\\\' else return string.format('\\u%04x',string.byte(c)) end end)..'"'
 elseif t=='table' then local out={} for k,w in pairs(v) do
  if type(w)~='function' and type(w)~='userdata' then out[#out+1]=encode(tostring(k),depth+1)..':'..encode(w,depth+1) end end
  table.sort(out) return '{'..table.concat(out,',')..'}' end return 'null'
end
"#;
const WORLD: &str = "assert(X2:IsEnteredWorld() and X2Unit:UnitName('player'), 'a character must be in the world') ";

const LOOT: &str = r#"
local result={count=X2Loot:GetNumLootItems() or 0,items={}}
for i=1,math.min(result.count,32) do
 local item=X2Loot:GetLootingBagItemInfo(i)
 if item then result.items[tostring(i)]={index=i,name=item.name,count=item.stackSize,item_template_id=item.itemType,grade=item.itemGrade,money=item.money,aapoint=item.aapoint} end
end
local button=lootWindow and lootWindow.allLootBtn
if button then
 local x,y=button:GetEffectiveOffset() local width,height=button:GetEffectiveExtent()
 result.take_all_bounds={x=x,y=y,width=width,height=height}
end
"#;

// Stock 1.2 UnitBuff/UnitDeBuff indices are one-based. Nil counts are normal
// before a target exists; do not turn missing native health into zero.
const UNIT_STATE: &str = r#"
local name=X2Unit:UnitName(unit)
local result={unit=unit,present=name~=nil}
if result.present then
 result.name=name
 result.health=X2Unit:UnitHealth(unit)
 result.max_health=X2Unit:UnitMaxHealth(unit)
 result.buff_count=X2Unit:UnitBuffCount(unit) or 0
 result.debuff_count=X2Unit:UnitDeBuffCount(unit) or 0
 result.buffs={} result.debuffs={}
 for i=1,math.min(result.buff_count,32) do
  local b=X2Unit:UnitBuff(unit,i)
  if b then result.buffs[tostring(i)]={index=i,buff_id=b.buff_id,path=b.path} end
 end
 for i=1,math.min(result.debuff_count,32) do
  local b=X2Unit:UnitDeBuff(unit,i)
  if b then result.debuffs[tostring(i)]={index=i,buff_id=b.buff_id,path=b.path} end
 end
end
"#;

#[cfg(any(windows, test))]
pub fn input_command(args: &Value) -> anyhow::Result<String> {
    let key = args["key"].as_str().context("missing key")?;
    let mut prefix = String::new();
    if let Some(modifiers) = args.get("modifiers") {
        let modifiers = modifiers.as_array().context("modifiers must be an array")?;
        if modifiers.len() > 3
            || modifiers
                .iter()
                .any(|m| !matches!(m.as_str(), Some("ctrl" | "shift" | "alt")))
            || modifiers
                .iter()
                .enumerate()
                .any(|(i, m)| modifiers[..i].contains(m))
        {
            bail!("modifiers must be distinct ctrl/shift/alt values");
        }
        for name in ["ctrl", "shift", "alt"] {
            if modifiers.iter().any(|m| m.as_str() == Some(name)) {
                prefix.push_str(name);
                prefix.push('+');
            }
        }
    }
    if key == "release" {
        if !prefix.is_empty() {
            bail!("release cannot have modifiers");
        }
        return Ok(key.into());
    }
    let duration = args["duration_ms"].as_u64().unwrap_or(100);
    if !(1..=10000).contains(&duration) {
        bail!("duration must be 1..10000 ms");
    }
    Ok(format!("{prefix}{key} {duration}"))
}

#[cfg(any(windows, test))]
pub fn pointer_command(args: &Value) -> anyhow::Result<String> {
    let action = args["action"].as_str().context("missing action")?;
    if !matches!(action, "move" | "click") {
        bail!("pointer action must be move or click");
    }
    let x = args["x"].as_u64().context("x must be an integer")?;
    let y = args["y"].as_u64().context("y must be an integer")?;
    if x > 4095 || y > 4095 {
        bail!("pointer coordinates must be 0..4095 client pixels");
    }
    Ok(format!("mouse_{action} {x} {y}"))
}

pub fn request(name: &str, args: &Value) -> anyhow::Result<(Target, String)> {
    let (target, body) = match name {
        "client_action" => (Target::Ui,format!("{WORLD}local action={} assert(X2Hotkey:IsValidActionName(action),'unknown client action') X2Hotkey:ExcuteActionHandler(action) local result={{action=action,submitted=true}}",quote(args["action"].as_str().context("missing action")?))),
        "client_chat" => (Target::Ui,format!("{WORLD}X2Chat:SendChatMessage(CHAT_SAY,{},'') local result={{submitted=true}}",quote(args["message"].as_str().context("missing message")?))),
        "client_use_item" => { let id=args["item_template_id"].as_u64().context("missing item_template_id")?;
            (Target::Ui,format!("{WORLD}local info=X2Bag:FindFirstBagItemInfoByItemType({id}) assert(info,'item is not in the inventory') local accepted=X2Bag:UseItemByType({id}) local result={{item_template_id={id},skill_id=info.skillType,accepted=accepted}}")) },
        "client_inventory" => (Target::Ui,format!("{WORLD}local slots={{}} local bag={} for slot=1, X2Bag:Capacity() do local info=X2Bag:GetBagItemInfo(bag,slot) if info then slots[tostring(slot)]={{slot=slot,item_template_id=info.itemType,name=info.name,count=info.stack,skill_id=info.skillType,dead=info.dead,locked=info.locked}} end end local result={{bag_id=bag,slots=slots}}",args["bag_id"].as_u64().unwrap_or(1))),
        "client_loot" => (Target::Ui,format!("{WORLD}{LOOT}")),
        "client_unit_state" => {
            let unit = match args.get("target") {
                None => "target",
                Some(value) => value.as_str().context("target must be player or target")?,
            };
            if !matches!(unit, "player" | "target") {
                bail!("target must be player or target");
            }
            (Target::Ui, format!("{WORLD}local unit={}\n{UNIT_STATE}",quote(unit)))
        },
        "client_mode_actions" => (Target::Ui,format!("{WORLD}local count=X2Unit:GetModeActionsCount() local slots={{}} for i=1,math.min(count,10) do local b=modeSkillActionBar and modeSkillActionBar.buttons and modeSkillActionBar.buttons[i] if b then local x,y=b:GetEffectiveOffset() local width,height=b:GetEffectiveExtent() slots[tostring(i)]={{slot=i,bounds={{x=x,y=y,width=width,height=height}},binded_type=b:GetBindedType(),info=b:GetTooltip(),can_use=b.canUse,cooldown=b.cooldown,cooldown_duration=b.cooldownDuration,skill_level=b.skillLevel,learned=b.learned,hotkey=b:GetHotKey(),staged_primary=X2Hotkey:GetOptionBinding('mode_action_bar_button',1,true,i),staged_secondary=X2Hotkey:GetOptionBinding('mode_action_bar_button',2,true,i)}} end end local result={{count=count,slots=slots}}")),
        "client_vehicle" => { let act=args["action"].as_str().unwrap_or("status");
            let discard=if act=="discard" { "X2SiegeWeapon:DiscardSiegeWeapon() " } else { "" };
            (Target::Ui,format!("{WORLD}{discard}local result={{has_vehicle=X2Player:HasSlaveUnit(),mounted=X2SiegeWeapon:IsMountedSiegeWeapon(),driver=X2SiegeWeapon:IsDriver(),info=X2SiegeWeapon:GetMountedSiegeWeaponInfo(),vehicle=X2SiegeWeapon:GetVehicleInfo(),speed=X2SiegeWeapon:GetSiegeWeaponSpeed(),turn_speed=X2SiegeWeapon:GetSiegeWeaponTurnSpeed()}}")) },
        "client_entities" => (Target::System,format!("local result={{entities={{}},count=0}} for _,e in pairs(System.GetEntities()or{{}}) do if ({}=='' or e.class=={}) and ({}=='' or string.find(e:GetName(),{},1,true)) then local p=e:GetWorldPos() result.entities[tostring(e.id)]={{name=e:GetName(),class=e.class,position=p,angles=e:GetAngles(),velocity=e:GetVelocity(),terrain_height=System.GetTerrainElevation(p)}} result.count=result.count+1 if result.count>={} then break end end end",quote(args["class"].as_str().unwrap_or("")),quote(args["class"].as_str().unwrap_or("")),quote(args["name"].as_str().unwrap_or("")),quote(args["name"].as_str().unwrap_or("")),args["limit"].as_u64().unwrap_or(50))),
        _ => bail!("unknown client action {name}"),
    };
    Ok((target, format!("{JSON}\n{body}\nreturn encode(result,0)")))
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn strings_cannot_escape_lua_literals() {
        assert_eq!(quote("\"\\\n\0é"), "\"\\034\\092\\010\\000\\195\\169\"");
        let (_, code) =
            request("client_chat", &json!({"message":"'); System.Quit(); --"})).unwrap();
        assert!(!code.contains("System.Quit"));
        assert!(code.contains("SendChatMessage(CHAT_SAY,"));
    }
    #[test]
    fn mutations_require_world_and_item_cast_uses_inventory_skill() {
        let (target, code) =
            request("client_use_item", &json!({"item_template_id":26874})).unwrap();
        assert_eq!(target, Target::Ui);
        assert!(code.contains(WORLD));
        assert!(code.contains("UseItemByType(26874)"));
        assert!(code.contains("accepted=accepted"));
        assert!(code.contains("assert(info"));
    }
    #[test]
    fn loot_telemetry_is_bounded_one_based_and_does_not_take_items() {
        let definition = tools(json!({"type":"integer"}))
            .into_iter()
            .find(|t| t["name"] == "client_loot")
            .unwrap();
        assert_eq!(definition["annotations"]["readOnlyHint"], true);
        assert!(is_tool("client_loot"));
        let (target, code) = request("client_loot", &json!({})).unwrap();
        assert_eq!(target, Target::Ui);
        assert!(code.contains(WORLD));
        assert!(code.contains("for i=1,math.min(result.count,32)"));
        assert!(code.contains("X2Loot:GetLootingBagItemInfo(i)"));
        assert!(code.contains("button:GetEffectiveOffset()"));
        assert!(!code.contains("X2Loot:LootItem("));
        assert!(!code.contains("SelectInteraction("));
        assert!(
            super::super::validate(&definition["inputSchema"], &json!({"action":"take_all"}))
                .is_err()
        );
    }
    #[test]
    fn mode_actions_are_bounded_read_only_telemetry() {
        let definition = tools(json!({"type":"integer"}))
            .into_iter()
            .find(|t| t["name"] == "client_mode_actions")
            .unwrap();
        assert_eq!(definition["annotations"]["readOnlyHint"], true);
        assert!(is_tool("client_mode_actions"));
        let (target, code) = request("client_mode_actions", &json!({})).unwrap();
        assert_eq!(target, Target::Ui);
        assert!(code.contains("math.min(count,10)"));
        assert!(code.contains("b:GetEffectiveOffset()"));
        assert!(code.contains("b:GetEffectiveExtent()"));
        assert!(code.contains("GetOptionBinding('mode_action_bar_button',2,true,i)"));
        assert!(!code.contains("SetOptionBinding"));
        assert!(!code.contains("EstablishSlot"));
        assert!(!code.contains("ExcuteActionHandler"));
    }
    #[test]
    fn unit_state_accepts_only_verified_unit_tokens_and_is_read_only() {
        let definition = tools(json!({"type":"integer"}))
            .into_iter()
            .find(|t| t["name"] == "client_unit_state")
            .unwrap();
        assert_eq!(definition["annotations"]["readOnlyHint"], true);
        assert_eq!(
            definition["inputSchema"]["properties"]["target"]["default"],
            "target"
        );
        assert!(is_tool("client_unit_state"));
        for (args, unit) in [
            (json!({}), "target"),
            (json!({"target":"target"}), "target"),
            (json!({"target":"player"}), "player"),
        ] {
            super::super::validate(&definition["inputSchema"], &args).unwrap();
            let (target, code) = request("client_unit_state", &args).unwrap();
            assert_eq!(target, Target::Ui);
            assert!(code.contains(WORLD));
            assert!(code.contains(&format!("local unit={}", quote(unit))));
        }
        for args in [
            json!({"target":"npc"}),
            json!({"target":1}),
            json!({"target":null}),
            json!({"target":"target'); System.Quit(); --"}),
        ] {
            assert!(super::super::validate(&definition["inputSchema"], &args).is_err());
            assert!(request("client_unit_state", &args).is_err());
        }
        // Guard the telemetry contract: one-based bounded native lists and no
        // fallback health for nil targets. Native values still need live checks.
        let (_, code) = request("client_unit_state", &json!({})).unwrap();
        assert!(code.contains("present=name~=nil"));
        assert!(code.contains("if result.present then"));
        assert!(code.contains("for i=1,math.min(result.buff_count,32)"));
        assert!(code.contains("for i=1,math.min(result.debuff_count,32)"));
        assert!(code.contains("local b=X2Unit:UnitBuff(unit,i)"));
        assert!(code.contains("local b=X2Unit:UnitDeBuff(unit,i)"));
        assert!(!code.contains("UnitHealth(unit) or 0"));
        assert!(!code.contains("TargetUnit("));
        assert!(!code.contains("ExcuteActionHandler"));
    }
    #[test]
    fn feature_schemas_reject_unbounded_or_wrong_inputs() {
        for (name, args) in [
            ("client_input", json!({"key":"w","duration_ms":10001})),
            ("client_input", json!({"key":"escape"})),
            ("client_pointer", json!({"action":"click","x":4096,"y":0})),
            ("client_pointer", json!({"action":"click","x":0,"y":-1})),
            ("client_pointer", json!({"action":"click","x":0.5,"y":1})),
            (
                "client_pointer",
                json!({"action":"right_click","x":1,"y":1}),
            ),
            (
                "client_input",
                json!({"key":"1","modifiers":["ctrl","ctrl"]}),
            ),
            ("client_input", json!({"key":"1","modifiers":["meta"]})),
            ("client_input", json!({"key":"1","modifiers":[1]})),
            ("client_input", json!({"key":"1","modifiers":"ctrl"})),
            (
                "client_input",
                json!({"key":"1","modifiers":["ctrl","shift","alt","ctrl"]}),
            ),
            ("client_entities", json!({"limit":201})),
            ("client_use_item", json!({"item_template_id":0})),
            ("client_chat", json!({"message":""})),
        ] {
            let t = tools(json!({}))
                .into_iter()
                .find(|t| t["name"] == name)
                .unwrap();
            assert!(super::super::validate(&t["inputSchema"], &args).is_err());
        }
    }

    #[test]
    fn input_chords_preserve_plain_commands_and_release_without_modifiers() {
        let schema = tools(json!({}))
            .into_iter()
            .find(|t| t["name"] == "client_input")
            .unwrap();
        for key in ["1", "2", "3", "4", "5", "6", "7", "8", "9", "0"] {
            let args = json!({"key":key,"modifiers":["alt","ctrl","shift"],"duration_ms":10000});
            super::super::validate(&schema["inputSchema"], &args).unwrap();
            assert_eq!(
                input_command(&args).unwrap(),
                format!("ctrl+shift+alt+{key} 10000")
            );
        }
        assert_eq!(
            input_command(&json!({"key":"w","duration_ms":1000})).unwrap(),
            "w 1000"
        );
        assert_eq!(
            input_command(&json!({"key":"1","modifiers":[]})).unwrap(),
            "1 100"
        );
        assert_eq!(input_command(&json!({"key":"release"})).unwrap(), "release");
        assert!(input_command(&json!({"key":"release","modifiers":["ctrl"]})).is_err());
        assert!(input_command(&json!({"key":"1","modifiers":["ctrl","ctrl"]})).is_err());
    }
    #[test]
    fn pointer_commands_preserve_exact_coordinates_and_reject_invalid_requests() {
        assert_eq!(
            pointer_command(&json!({"action":"move","x":0,"y":4095})).unwrap(),
            "mouse_move 0 4095"
        );
        assert_eq!(
            pointer_command(&json!({"action":"click","x":123,"y":87})).unwrap(),
            "mouse_click 123 87"
        );
        for args in [
            json!({"action":"click","x":-1,"y":0}),
            json!({"action":"click","x":0,"y":4096}),
            json!({"action":"click","x":"123","y":0}),
            json!({"action":"drag","x":1,"y":1}),
        ] {
            assert!(pointer_command(&args).is_err());
        }
    }
}
