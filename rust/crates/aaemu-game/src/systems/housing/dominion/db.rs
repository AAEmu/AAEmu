//! Mutable territory authority, separate from read-only castle templates. Rows and current
//! siege participants are written in the caller's world-save transaction. Runtime engraving
//! handles are never serialized or reconstructed from these rows.

use std::collections::BTreeMap;

use anyhow::Context;
use sqlx::{MySql, MySqlPool, Row, Transaction};

use crate::server::GameServer;

use super::state::{
    DominionState, SaveBatch, SiegeOutcome, SiegeParticipant, SiegeSide, SiegeState, SiegeStatus,
    TowerIdentity, Treasury,
};

pub async fn load(server: &GameServer) -> anyhow::Result<()> {
    let states = load_rows(&server.db).await?;
    validate_loaded_identity(server, &states)?;
    server
        .systems
        .housing
        .dominions
        .restore(states)
        .context("restore dominion authority")?;
    Ok(())
}

/// Persistent authority must refer to this boot's actual tower/template/instance, not a
/// missing or replaced house. Historical completed siege expeditions may no longer exist.
pub fn validate_loaded_identity(
    server: &GameServer,
    states: &[DominionState],
) -> anyhow::Result<()> {
    for state in states {
        state.validate()?;
        let house = server
            .systems
            .housing
            .manager
            .by_id(state.tower.house_id)
            .with_context(|| {
                format!(
                    "dominion {} references missing tower {}",
                    state.zone_group_id, state.tower.house_id
                )
            })?;
        let (house_id, template_id, instance_id, zone_id) = {
            let h = house.data();
            (
                h.id,
                h.unit.template_id,
                h.unit.transform.instance_id,
                h.unit.transform.zone_id,
            )
        };
        let template = server
            .game_data
            .systems
            .housing
            .template(template_id)
            .context("dominion tower template missing")?;
        let zone_group = server
            .game_data
            .zones
            .zone_by_key(zone_id)
            .context("dominion tower zone missing")?
            .group_id;
        let world = server
            .world
            .world(instance_id)
            .context("dominion tower instance missing")?;
        let live_house = world
            .house(house.obj_id)
            .context("dominion tower is not spawned in its recorded world")?;
        anyhow::ensure!(
            std::sync::Arc::ptr_eq(&live_house, &house)
                && state.zone_group_id == zone_group
                && state.tower.house_id == house_id
                && state.tower.instance_id == instance_id
                && state.tower.guard_tower_setting_id == template.guard_tower_setting_id
                && server
                    .game_data
                    .systems
                    .housing
                    .castles
                    .guard_tower_settings
                    .contains_key(&state.tower.guard_tower_setting_id),
            "persisted dominion tower identity does not match live tower/template/zone"
        );
        let expedition_exists = |id| server.expeditions.get(id).is_some_and(|e| !e.is_disbanded);
        anyhow::ensure!(
            state.owner_expedition_id == 0 || expedition_exists(state.owner_expedition_id),
            "persisted dominion owner expedition is missing"
        );
        if let Some(siege) = state
            .siege
            .as_ref()
            .filter(|s| s.status != SiegeStatus::Finished)
        {
            anyhow::ensure!(
                expedition_exists(siege.attacker_expedition_id)
                    && expedition_exists(siege.defender_expedition_id),
                "active dominion siege expedition is missing"
            );
        }
    }
    Ok(())
}

pub async fn load_rows(pool: &MySqlPool) -> anyhow::Result<Vec<DominionState>> {
    let rows = sqlx::query("SELECT * FROM dominions ORDER BY zone_group_id")
        .fetch_all(pool)
        .await?;
    let mut states = BTreeMap::new();
    for row in rows {
        let siege_id: u64 = row.try_get("siege_id")?;
        let status: u8 = row.try_get("siege_status")?;
        let outcome: u8 = row.try_get("siege_outcome")?;
        let siege = if siege_id == 0 {
            None
        } else {
            Some(SiegeState {
                siege_id,
                attacker_expedition_id: row.try_get("attacker_expedition_id")?,
                defender_expedition_id: row.try_get("defender_expedition_id")?,
                ownership_generation: row.try_get("siege_ownership_generation")?,
                status: match status {
                    0 => SiegeStatus::Scheduled,
                    1 => SiegeStatus::Active,
                    2 => SiegeStatus::Finished,
                    _ => anyhow::bail!("invalid dominion siege status {status}"),
                },
                outcome: match outcome {
                    0 => SiegeOutcome::Pending,
                    1 => SiegeOutcome::DefenderHeld,
                    2 => SiegeOutcome::AttackerCaptured,
                    3 => SiegeOutcome::Cancelled,
                    _ => anyhow::bail!("invalid dominion siege outcome {outcome}"),
                },
                scheduled_at: row.try_get("siege_scheduled_at")?,
                starts_at: row.try_get("siege_starts_at")?,
                ends_at: row.try_get("siege_ends_at")?,
                ended_at: row.try_get("siege_ended_at")?,
                participants: BTreeMap::new(),
            })
        };
        let state = DominionState {
            zone_group_id: row.try_get("zone_group_id")?,
            tower: TowerIdentity {
                house_id: row.try_get("tower_house_id")?,
                instance_id: row.try_get("instance_id")?,
                guard_tower_setting_id: row.try_get("guard_tower_setting_id")?,
            },
            owner_expedition_id: row.try_get("owner_expedition_id")?,
            ownership_generation: row.try_get("ownership_generation")?,
            reign_start_at: row.try_get("reign_start_at")?,
            last_siege_end_at: row.try_get("last_siege_end_at")?,
            tax_rate: row.try_get("tax_rate")?,
            national_tax_rate: row.try_get("national_tax_rate")?,
            last_paid_at: row.try_get("last_paid_at")?,
            last_tax_rate_changed_at: row.try_get("last_tax_rate_changed_at")?,
            last_national_tax_rate_changed_at: row.try_get("last_national_tax_rate_changed_at")?,
            treasury: Treasury {
                cur_house_tax_money: row.try_get("cur_house_tax_money")?,
                cur_hunt_tax_money: row.try_get("cur_hunt_tax_money")?,
                peace_tax_money: row.try_get("peace_tax_money")?,
                cur_house_tax_aa_point: row.try_get("cur_house_tax_aa_point")?,
                peace_tax_aa_point: row.try_get("peace_tax_aa_point")?,
            },
            tower_destroyed: row.try_get("tower_destroyed")?,
            tower_hp: row.try_get("tower_hp")?,
            last_siege_id: row.try_get("last_siege_id")?,
            siege,
        };
        if states.insert(state.zone_group_id, state).is_some() {
            anyhow::bail!("duplicate dominion zone group");
        }
    }
    let participants = sqlx::query(
        "SELECT * FROM dominion_siege_participants ORDER BY zone_group_id, character_id",
    )
    .fetch_all(pool)
    .await?;
    for row in participants {
        let zone: u32 = row.try_get("zone_group_id")?;
        let siege_id: u64 = row.try_get("siege_id")?;
        let character_id: u32 = row.try_get("character_id")?;
        let side: u8 = row.try_get("side")?;
        let state = states
            .get_mut(&zone)
            .context("orphan dominion siege participant")?;
        let siege = state
            .siege
            .as_mut()
            .context("participant without a dominion siege")?;
        anyhow::ensure!(
            siege.siege_id == siege_id,
            "participant belongs to a stale dominion siege"
        );
        let participant = SiegeParticipant {
            character_id,
            expedition_id: row.try_get("expedition_id")?,
            side: match side {
                1 => SiegeSide::Attacker,
                2 => SiegeSide::Defender,
                _ => anyhow::bail!("invalid dominion participant side {side}"),
            },
        };
        anyhow::ensure!(
            siege
                .participants
                .insert(character_id, participant)
                .is_none(),
            "duplicate dominion participant"
        );
    }
    for state in states.values() {
        state
            .validate()
            .context("invalid persisted dominion state")?;
    }
    Ok(states.into_values().collect())
}

pub async fn write_batch(
    tx: &mut Transaction<'_, MySql>,
    batch: &SaveBatch,
) -> sqlx::Result<usize> {
    for (zone, _) in &batch.deleted {
        sqlx::query("DELETE FROM dominion_siege_participants WHERE zone_group_id=?")
            .bind(zone)
            .execute(&mut **tx)
            .await?;
        sqlx::query("DELETE FROM dominions WHERE zone_group_id=?")
            .bind(zone)
            .execute(&mut **tx)
            .await?;
    }
    for row in &batch.rows {
        write_row(tx, &row.state).await?;
        sqlx::query("DELETE FROM dominion_siege_participants WHERE zone_group_id=?")
            .bind(row.state.zone_group_id)
            .execute(&mut **tx)
            .await?;
        if let Some(siege) = &row.state.siege {
            for participant in siege.participants.values() {
                sqlx::query("INSERT INTO dominion_siege_participants (zone_group_id,siege_id,character_id,expedition_id,side) VALUES(?,?,?,?,?)")
                    .bind(row.state.zone_group_id).bind(siege.siege_id).bind(participant.character_id)
                    .bind(participant.expedition_id).bind(participant.side as u8)
                    .execute(&mut **tx).await?;
            }
        }
    }
    Ok(batch.changed_count())
}

async fn write_row(tx: &mut Transaction<'_, MySql>, state: &DominionState) -> sqlx::Result<()> {
    let siege = state.siege.as_ref();
    sqlx::query(
        "REPLACE INTO dominions (zone_group_id,tower_house_id,instance_id,guard_tower_setting_id,\
         owner_expedition_id,ownership_generation,reign_start_at,last_siege_end_at,tax_rate,\
         national_tax_rate,last_paid_at,last_tax_rate_changed_at,last_national_tax_rate_changed_at,\
         cur_house_tax_money,cur_hunt_tax_money,peace_tax_money,cur_house_tax_aa_point,peace_tax_aa_point,\
         tower_destroyed,tower_hp,last_siege_id,siege_id,attacker_expedition_id,defender_expedition_id,\
         siege_ownership_generation,siege_status,siege_outcome,siege_scheduled_at,siege_starts_at,\
         siege_ends_at,siege_ended_at) VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)"
    )
    .bind(state.zone_group_id).bind(state.tower.house_id).bind(state.tower.instance_id)
    .bind(state.tower.guard_tower_setting_id).bind(state.owner_expedition_id)
    .bind(state.ownership_generation).bind(state.reign_start_at).bind(state.last_siege_end_at)
    .bind(state.tax_rate).bind(state.national_tax_rate).bind(state.last_paid_at)
    .bind(state.last_tax_rate_changed_at).bind(state.last_national_tax_rate_changed_at)
    .bind(state.treasury.cur_house_tax_money).bind(state.treasury.cur_hunt_tax_money)
    .bind(state.treasury.peace_tax_money).bind(state.treasury.cur_house_tax_aa_point)
    .bind(state.treasury.peace_tax_aa_point).bind(state.tower_destroyed).bind(state.tower_hp).bind(state.last_siege_id)
    .bind(siege.map_or(0, |s| s.siege_id)).bind(siege.map_or(0, |s| s.attacker_expedition_id))
    .bind(siege.map_or(0, |s| s.defender_expedition_id)).bind(siege.map_or(0, |s| s.ownership_generation))
    .bind(siege.map_or(0, |s| s.status as u8)).bind(siege.map_or(0, |s| s.outcome as u8))
    .bind(siege.map_or(0, |s| s.scheduled_at)).bind(siege.map_or(0, |s| s.starts_at))
    .bind(siege.map_or(0, |s| s.ends_at)).bind(siege.map_or(0, |s| s.ended_at))
    .execute(&mut **tx).await?;
    Ok(())
}
