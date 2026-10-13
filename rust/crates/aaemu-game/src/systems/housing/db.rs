//! MySQL persistence of the `housings` table (`LoadPlayerHousing`, `House.Save`,
//! `HousingManager.Save`'s delete).

use sqlx::{MySql, MySqlPool, Row};

use crate::db::{dt, from_unix, int, unix};

use super::manager::HouseRow;

/// `SELECT * FROM housings` (types cast like the other loaders, so any integer column width
/// reads).
pub async fn load_rows(pool: &MySqlPool) -> anyhow::Result<Vec<HouseRow>> {
    let rows = sqlx::query(
        "SELECT CAST(id AS SIGNED) AS id, CAST(account_id AS SIGNED) AS account_id, \
         CAST(owner AS SIGNED) AS owner, CAST(co_owner AS SIGNED) AS co_owner, \
         CAST(template_id AS SIGNED) AS template_id, name, x, y, z, yaw, pitch, roll, \
         CAST(current_step AS SIGNED) AS current_step, \
         CAST(current_action AS SIGNED) AS current_action, \
         CAST(permission AS SIGNED) AS permission, place_date, protected_until, \
         CAST(faction_id AS SIGNED) AS faction_id, CAST(sell_to AS SIGNED) AS sell_to, \
         CAST(sell_price AS SIGNED) AS sell_price, \
         CAST(allow_recover AS SIGNED) AS allow_recover, CAST(hp AS SIGNED) AS hp FROM housings",
    )
    .fetch_all(pool)
    .await?;
    let mut out = Vec::with_capacity(rows.len());
    for r in rows {
        let f = |c: &str| -> anyhow::Result<f32> { Ok(r.try_get::<f32, _>(c)?) };
        out.push(HouseRow {
            id: int(&r, "id")? as u32,
            account_id: int(&r, "account_id")? as u32,
            owner: int(&r, "owner")? as u32,
            co_owner: int(&r, "co_owner")? as u32,
            template_id: int(&r, "template_id")? as u32,
            name: r.try_get("name")?,
            x: f("x")?,
            y: f("y")?,
            z: f("z")?,
            yaw: f("yaw")?,
            pitch: f("pitch")?,
            roll: f("roll")?,
            current_step: int(&r, "current_step")? as i32,
            current_action: int(&r, "current_action")? as i32,
            permission: int(&r, "permission")? as u8,
            place_date: unix(dt(&r, "place_date")),
            protected_until: unix(dt(&r, "protected_until")),
            faction_id: int(&r, "faction_id")? as u32,
            sell_to: int(&r, "sell_to")? as u32,
            sell_price: int(&r, "sell_price")? as u32,
            allow_recover: int(&r, "allow_recover")? != 0,
            hp: r
                .try_get::<Option<i64>, _>("hp")?
                .map(i32::try_from)
                .transpose()
                .map_err(|_| anyhow::anyhow!("housing health does not fit i32"))?,
        });
    }
    Ok(out)
}

/// `DELETE FROM housings WHERE id IN(...)`.
pub async fn delete_rows(tx: &mut sqlx::Transaction<'_, MySql>, ids: &[u32]) -> sqlx::Result<()> {
    let list = ids.iter().map(u32::to_string).collect::<Vec<_>>().join(",");
    sqlx::query(&format!("DELETE FROM housings WHERE id IN({list})"))
        .execute(&mut **tx)
        .await?;
    Ok(())
}

/// `House.Save`'s `REPLACE INTO housings ...`.
pub async fn write_row(tx: &mut sqlx::Transaction<'_, MySql>, r: &HouseRow) -> sqlx::Result<()> {
    sqlx::query(
        "REPLACE INTO `housings` \
         (`id`,`account_id`,`owner`,`co_owner`,`template_id`,`name`,`x`,`y`,`z`,`yaw`,`pitch`,`roll`,\
         `current_step`,`current_action`,`permission`,`place_date`,`protected_until`,`faction_id`,\
         `sell_to`,`sell_price`, `allow_recover`,`hp`) \
         VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)",
    )
    .bind(r.id)
    .bind(r.account_id)
    .bind(r.owner)
    .bind(r.co_owner)
    .bind(r.template_id)
    .bind(&r.name)
    .bind(r.x)
    .bind(r.y)
    .bind(r.z)
    .bind(r.yaw)
    .bind(r.pitch)
    .bind(r.roll)
    .bind(r.current_step)
    .bind(r.current_action)
    .bind(r.permission)
    .bind(from_unix(r.place_date))
    .bind(from_unix(r.protected_until))
    .bind(r.faction_id)
    .bind(r.sell_to)
    .bind(r.sell_price)
    .bind(r.allow_recover)
    .bind(r.hp)
    .execute(&mut **tx)
    .await?;
    Ok(())
}
