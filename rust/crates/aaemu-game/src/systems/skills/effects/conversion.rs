//! Resource conversion. The 1.2 Create Mana rows11/15 exchange health for mana;
//! its Inspired alternative changes the spent-resource percentage from50 to100.

use crate::packets::world::SCUnitPointsPacket;
use crate::server::GameServer;

use super::super::data::effects::ConversionEffect;
use super::EffectArgs;

fn amount(category: u32, value: i32, max: i32, spent: i32) -> Option<i64> {
    let value = i64::from(value.max(0));
    Some(match category {
        1 => value,
        2 => i64::from(max.max(0)) * value / 100,
        4 => i64::from(spent) * value / 100,
        _ => return None,
    })
}

pub fn apply(server: &GameServer, effect: &ConversionEffect, args: &EffectArgs<'_>) {
    let Some(target) = args.target.as_obj().filter(|u| u.is_unit()) else {
        return;
    };
    if !matches!(effect.category_id, 1 | 2)
        || !matches!(effect.source_category_id, 1 | 2)
        || !matches!(effect.target_category_id, 1 | 2 | 4)
    {
        tracing::warn!(
            effect_id = effect.id,
            "unsupported resource conversion category"
        );
        return;
    }
    let points = target
        .with_unit(|u| {
            let u = u.unit_mut();
            if u.hp <= 0 {
                return None;
            }
            let (source, source_max, dest, dest_max) = if effect.category_id == 2 {
                (u.hp, u.max_hp, u.mp, u.max_mp)
            } else {
                (u.mp, u.max_mp, u.hp, u.max_hp)
            };
            let requested = amount(
                effect.source_category_id,
                effect.source_value,
                source_max,
                0,
            )?;
            // A health cost cannot kill the unit. Mana costs may consume the last point.
            let minimum = i32::from(effect.category_id == 2);
            let spent = requested.min(i64::from((source - minimum).max(0))) as i32;
            if spent == 0 {
                return None;
            }
            let restored = amount(
                effect.target_category_id,
                effect.target_value,
                dest_max,
                spent,
            )?;
            let destination = (i64::from(dest) + restored).min(i64::from(dest_max.max(0))) as i32;
            if effect.category_id == 2 {
                u.hp -= spent;
                u.mp = destination;
            } else {
                u.mp -= spent;
                u.hp = destination;
            }
            Some((u.hp, u.mp))
        })
        .flatten();
    if let Some((hp, mp)) = points {
        super::super::broadcast(
            server,
            target,
            &SCUnitPointsPacket {
                obj_id: target.obj_id(),
                hp,
                mp,
            },
            true,
        );
    }
}
