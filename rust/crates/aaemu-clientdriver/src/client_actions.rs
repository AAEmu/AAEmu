//! Verified 1.2 UI actions and bounded entity telemetry for reproducible real-client tests.
use super::tool;
use crate::Target;
use anyhow::{bail, Context};
use serde_json::{json, Value};

pub fn tools(pid: Value) -> Vec<Value> {
    vec![
        tool("client_perf", "Record DXGI frame intervals (profile resets and enables, off disables, status reads). Reports FPS, mean, p95, p99 and maximum for the latest 2048 rendered frames. Screenshot readback can affect timings; capture after profiling.",json!({"pid":pid,"action":{"type":"string","enum":["profile","status","off"]}}), &["action"], false),
        tool("client_input", "Press a keyboard key through the client's CryInput on its main thread. Works with hidden rendering, without desktop keystrokes. Automatic release after 1..10000 ms survives MCP disconnect. release clears all test keys.",json!({"pid":pid,"key":{"type":"string","enum":["w","a","s","d","q","e","space","f","g","release"]},"duration_ms":{"type":"integer","minimum":1,"maximum":10000}}), &["key"], false),
        tool("client_action", "Execute a valid 1.2 UI hotkey action, such as do_interaction_1, cycle_hostile_forward or toggle_bag. Continuous movement uses client_input.",json!({"pid":pid,"action":{"type":"string","minLength":1,"maxLength":96}}), &["action"], false),
        tool("client_inventory", "Read inventory slots, item templates, names, counts and use skills from the live client. bag_id defaults to 1 (inventory).",json!({"pid":pid,"bag_id":{"type":"integer","minimum":1,"maximum":2}}), &[], true),
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
 elseif t=='number' then if v~=v or v==math.huge or v==-math.huge then return 'null' end return tostring(v)
 elseif t=='string' then return '"'..string.gsub(v,'[%z\1-\31\\"]',function(c)
  if c=='"' then return '\\"' elseif c=='\\' then return '\\\\' else return string.format('\\u%04x',string.byte(c)) end end)..'"'
 elseif t=='table' then local out={} for k,w in pairs(v) do
  if type(w)~='function' and type(w)~='userdata' then out[#out+1]=encode(tostring(k),depth+1)..':'..encode(w,depth+1) end end
  table.sort(out) return '{'..table.concat(out,',')..'}' end return 'null'
end
"#;
const WORLD: &str = "assert(X2:IsEnteredWorld() and X2Unit:UnitName('player'), 'a character must be in the world') ";

pub fn request(name: &str, args: &Value) -> anyhow::Result<(Target, String)> {
    let (target, body) = match name {
        "client_action" => (Target::Ui,format!("{WORLD}local action={} assert(X2Hotkey:IsValidActionName(action),'unknown client action') X2Hotkey:ExcuteActionHandler(action) local result={{action=action,submitted=true}}",quote(args["action"].as_str().context("missing action")?))),
        "client_chat" => (Target::Ui,format!("{WORLD}X2Chat:SendChatMessage(CHAT_SAY,{},'') local result={{submitted=true}}",quote(args["message"].as_str().context("missing message")?))),
        "client_use_item" => { let id=args["item_template_id"].as_u64().context("missing item_template_id")?;
            (Target::Ui,format!("{WORLD}local info=X2Bag:FindFirstBagItemInfoByItemType({id}) assert(info,'item is not in the inventory') local accepted=X2Bag:UseItemByType({id}) local result={{item_template_id={id},skill_id=info.skillType,accepted=accepted}}")) },
        "client_inventory" => (Target::Ui,format!("{WORLD}local slots={{}} local bag={} for slot=1, X2Bag:Capacity() do local info=X2Bag:GetBagItemInfo(bag,slot) if info then slots[tostring(slot)]={{slot=slot,item_template_id=info.itemType,name=info.name,count=info.stack,skill_id=info.skillType,dead=info.dead,locked=info.locked}} end end local result={{bag_id=bag,slots=slots}}",args["bag_id"].as_u64().unwrap_or(1))),
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
    fn feature_schemas_reject_unbounded_or_wrong_inputs() {
        for (name, args) in [
            ("client_input", json!({"key":"w","duration_ms":10001})),
            ("client_input", json!({"key":"escape"})),
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
}
