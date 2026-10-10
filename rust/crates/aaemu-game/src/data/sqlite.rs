//! Name-based column access for `compact.sqlite3` rows with the conversions of
//! `SQLiteWrapperReader` on top of Microsoft.Data.Sqlite (which uses `sqlite3_column_int64` /
//! `_double` / `_text`, i.e. SQLite's own type coercion). Reading NULL through a getter without a
//! default is an error, like the C# `InvalidOperationException`.

use rusqlite::types::ValueRef;
use rusqlite::{Connection, Row, Rows};

/// A row plus its column names (`SELECT *` keeps the C# "read by name" semantics).
pub struct SqlRow<'a, 'b> {
    row: &'a Row<'b>,
    names: &'a [String],
}

fn null_err(col: &str) -> rusqlite::Error {
    rusqlite::Error::InvalidColumnType(0, format!("{col} is NULL"), rusqlite::types::Type::Null)
}

/// `sqlite3_column_int64` text coercion: leading whitespace, optional sign, digits.
fn text_to_i64(t: &[u8]) -> i64 {
    let s = String::from_utf8_lossy(t);
    let s = s.trim_start();
    let mut end = 0;
    for (i, c) in s.char_indices() {
        if c.is_ascii_digit() || (i == 0 && (c == '-' || c == '+')) {
            end = i + c.len_utf8();
        } else {
            break;
        }
    }
    if let Ok(v) = s[..end].parse::<i64>() {
        return v;
    }
    // Real-looking text ("1.5e3") coerces through a double.
    s.parse::<f64>().map(|f| f as i64).unwrap_or(0)
}

impl<'a, 'b> SqlRow<'a, 'b> {
    pub fn new(row: &'a Row<'b>, names: &'a [String]) -> Self {
        Self { row, names }
    }

    fn value(&self, col: &str) -> rusqlite::Result<ValueRef<'a>> {
        let idx = self
            .names
            .iter()
            .position(|n| n.eq_ignore_ascii_case(col))
            .ok_or_else(|| rusqlite::Error::InvalidColumnName(col.to_string()))?;
        self.row.get_ref(idx)
    }

    pub fn has(&self, col: &str) -> bool {
        self.names.iter().any(|n| n.eq_ignore_ascii_case(col))
    }

    pub fn is_null(&self, col: &str) -> rusqlite::Result<bool> {
        Ok(matches!(self.value(col)?, ValueRef::Null))
    }

    /// `GetInt64`.
    pub fn i64(&self, col: &str) -> rusqlite::Result<i64> {
        match self.value(col)? {
            ValueRef::Null => Err(null_err(col)),
            ValueRef::Integer(i) => Ok(i),
            ValueRef::Real(f) => Ok(f as i64),
            ValueRef::Text(t) | ValueRef::Blob(t) => Ok(text_to_i64(t)),
        }
    }

    /// `GetInt64` with a NULL default.
    pub fn i64_or(&self, col: &str, default: i64) -> rusqlite::Result<i64> {
        if self.is_null(col)? {
            Ok(default)
        } else {
            self.i64(col)
        }
    }

    /// `GetInt32` (`(int)GetInt64`).
    pub fn i32(&self, col: &str) -> rusqlite::Result<i32> {
        Ok(self.i64(col)? as i32)
    }
    pub fn i32_or(&self, col: &str, d: i32) -> rusqlite::Result<i32> {
        Ok(self.i64_or(col, i64::from(d))? as i32)
    }
    /// `GetUInt32` (`(uint)GetInt32`).
    pub fn u32(&self, col: &str) -> rusqlite::Result<u32> {
        Ok(self.i32(col)? as u32)
    }
    pub fn u32_or(&self, col: &str, d: u32) -> rusqlite::Result<u32> {
        Ok(self.i64_or(col, i64::from(d))? as i32 as u32)
    }
    /// `GetByte`.
    pub fn u8(&self, col: &str) -> rusqlite::Result<u8> {
        Ok(self.i64(col)? as u8)
    }
    pub fn u8_or(&self, col: &str, d: u8) -> rusqlite::Result<u8> {
        Ok(self.i64_or(col, i64::from(d))? as u8)
    }
    /// `GetInt16`.
    pub fn i16(&self, col: &str) -> rusqlite::Result<i16> {
        Ok(self.i64(col)? as i16)
    }

    /// `GetDouble`.
    pub fn f64(&self, col: &str) -> rusqlite::Result<f64> {
        match self.value(col)? {
            ValueRef::Null => Err(null_err(col)),
            ValueRef::Integer(i) => Ok(i as f64),
            ValueRef::Real(f) => Ok(f),
            ValueRef::Text(t) | ValueRef::Blob(t) => {
                Ok(String::from_utf8_lossy(t).trim().parse().unwrap_or(0.0))
            }
        }
    }
    /// `GetFloat`.
    pub fn f32(&self, col: &str) -> rusqlite::Result<f32> {
        Ok(self.f64(col)? as f32)
    }
    pub fn f32_or(&self, col: &str, d: f32) -> rusqlite::Result<f32> {
        if self.is_null(col)? {
            Ok(d)
        } else {
            self.f32(col)
        }
    }

    /// `GetString`.
    pub fn string(&self, col: &str) -> rusqlite::Result<String> {
        match self.value(col)? {
            ValueRef::Null => Err(null_err(col)),
            ValueRef::Integer(i) => Ok(i.to_string()),
            ValueRef::Real(f) => Ok(f.to_string()),
            ValueRef::Text(t) | ValueRef::Blob(t) => Ok(String::from_utf8_lossy(t).into_owned()),
        }
    }
    pub fn string_or(&self, col: &str, d: &str) -> rusqlite::Result<String> {
        if self.is_null(col)? {
            Ok(d.to_string())
        } else {
            self.string(col)
        }
    }

    /// `GetBoolean(column, fromString: true)`: NULL -> false, otherwise `"t"` or `"1"`.
    pub fn bool_str(&self, col: &str) -> rusqlite::Result<bool> {
        Ok(match self.value(col)? {
            ValueRef::Null => false,
            ValueRef::Integer(i) => i == 1,
            ValueRef::Real(_) => false,
            ValueRef::Text(t) | ValueRef::Blob(t) => t == b"t" || t == b"1",
        })
    }

    /// `GetBoolean(column)` (Microsoft.Data.Sqlite: `GetInt64 != 0`).
    pub fn bool(&self, col: &str) -> rusqlite::Result<bool> {
        Ok(self.i64(col)? != 0)
    }
}

/// Runs `sql` and calls `f` for every row (the `while (reader.Read())` loops).
pub fn for_each_row(
    conn: &Connection,
    sql: &str,
    params: impl rusqlite::Params,
    mut f: impl FnMut(&SqlRow<'_, '_>) -> rusqlite::Result<()>,
) -> rusqlite::Result<()> {
    let mut stmt = conn.prepare(sql)?;
    let names: Vec<String> = stmt.column_names().iter().map(|s| s.to_string()).collect();
    let mut rows: Rows<'_> = stmt.query(params)?;
    while let Some(row) = rows.next()? {
        f(&SqlRow { row, names: &names })?;
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn coercions_follow_sqlite() {
        let c = Connection::open_in_memory().unwrap();
        c.execute_batch(
            "CREATE TABLE t (a, b, c, d); INSERT INTO t VALUES (' 12x', 't', NULL, 2.7);",
        )
        .unwrap();
        for_each_row(&c, "SELECT * FROM t", [], |r| {
            assert_eq!(r.i64("a")?, 12);
            assert!(r.bool_str("b")?);
            assert!(!r.bool_str("c")?);
            assert!(r.i64("c").is_err());
            assert_eq!(r.u32_or("c", 7)?, 7);
            assert_eq!(r.i64("d")?, 2);
            assert!((r.f32("d")? - 2.7).abs() < 1e-6);
            Ok(())
        })
        .unwrap();
    }
}
