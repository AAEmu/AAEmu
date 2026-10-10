//! The subset of Lua 5.4 (KeraLua / NLua 1.7) the AI parameter strings need.
//!
//! `npc_ai_params.ai_param` is Lua table-constructor text that the C# runs through
//! `AiLua.DoString($"data = {{\n{data}\n}}")` (with a few predefined constants) and then reads
//! with `GetObjectFromPath` / `GetNumber` / `GetInteger` / `GetTable` and the `LuaTable`
//! indexers, converting the values with `System.Convert`. This module implements:
//!
//! * a Lua lexer (comments, long brackets, all string escapes, decimal / hex numerals with the
//!   integer / float distinction of 5.4) and a parser for chunks made of `Name = exp`
//!   assignments, with full expression syntax (table constructors, unary / binary operators with
//!   Lua precedence, indexing, calls);
//! * an evaluator with the Lua 5.4 value model (integer / float subtypes, string coercion,
//!   table constructors where positional fields win over explicit integer keys); any runtime
//!   error (calling a non-function, indexing a non-table, `nil` / NaN table keys, ...) makes the
//!   whole `DoString` fail, which the C# reports as "Impossible to parse npc_ai_params";
//! * the `System.Convert` conversions of the NLua objects (`long`, `double`, `string`, `bool`,
//!   `LuaTable`, `null`) the parameter classes use, including their exceptions.
//!
//! `LuaTable.Values` enumerates like `lua_next`: the array part (positional fields, in order,
//! nils skipped) first, then the keyed fields. Lua's hash-part order is unspecified; the keyed
//! fields are enumerated in source order here (the parameter loaders only enumerate positional
//! lists).

use std::fmt;
use std::sync::Arc;

/// A Lua value as NLua hands it to .NET.
#[derive(Debug, Clone, PartialEq)]
pub enum LuaValue {
    Nil,
    Bool(bool),
    /// Lua integer subtype (`long` in .NET).
    Int(i64),
    /// Lua float subtype (`double` in .NET).
    Float(f64),
    Str(String),
    Table(Arc<LuaTable>),
    /// A function value (only produced by `function` expressions; never callable here).
    Function,
}

/// A normalized table key.
#[derive(Debug, Clone, PartialEq)]
pub enum LuaKey {
    Int(i64),
    /// A float key with a fractional part (integral floats are normalized to [`LuaKey::Int`]).
    Float(u64),
    Str(String),
    Bool(bool),
    /// Tables / functions used as keys (identity is not needed by any caller).
    Other(usize),
}

/// A Lua table.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct LuaTable {
    /// The positional fields (`t[1]..t[n]`), `Nil` for holes.
    array: Vec<LuaValue>,
    /// The keyed fields in insertion order (never `Nil`, never an integer key inside `array`).
    hash: Vec<(LuaKey, LuaValue)>,
}

impl LuaTable {
    /// `t[key]` (`nil` when absent).
    pub fn get(&self, key: &LuaKey) -> &LuaValue {
        if let LuaKey::Int(i) = key {
            if *i >= 1 && (*i as u64) <= self.array.len() as u64 {
                return &self.array[(*i - 1) as usize];
            }
        }
        self.hash
            .iter()
            .find(|(k, _)| k == key)
            .map_or(&LuaValue::Nil, |(_, v)| v)
    }

    /// `table["name"]` (the NLua string indexer).
    pub fn field(&self, name: &str) -> &LuaValue {
        self.get(&LuaKey::Str(name.to_string()))
    }

    /// `table[index]` with an integer (the NLua `object` indexer with an `int`).
    pub fn at(&self, i: i64) -> &LuaValue {
        self.get(&LuaKey::Int(i))
    }

    /// `LuaTable.Values` (`lua_next` order: array part, then keyed fields).
    pub fn values(&self) -> Vec<&LuaValue> {
        self.array
            .iter()
            .filter(|v| !matches!(v, LuaValue::Nil))
            .chain(self.hash.iter().map(|(_, v)| v))
            .collect()
    }

    /// Number of non-nil entries.
    pub fn len(&self) -> usize {
        self.values().len()
    }

    pub fn is_empty(&self) -> bool {
        self.len() == 0
    }

    fn set(&mut self, key: LuaKey, value: LuaValue) {
        if let LuaKey::Int(i) = key {
            if i >= 1 && (i as u64) <= self.array.len() as u64 {
                self.array[(i - 1) as usize] = value;
                return;
            }
        }
        if let Some(pos) = self.hash.iter().position(|(k, _)| *k == key) {
            if matches!(value, LuaValue::Nil) {
                self.hash.remove(pos);
            } else {
                self.hash[pos].1 = value;
            }
        } else if !matches!(value, LuaValue::Nil) {
            self.hash.push((key, value));
        }
    }
}

/// A Lua error (`LuaException` / `LuaScriptException` in NLua).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct LuaError(pub String);

impl fmt::Display for LuaError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(&self.0)
    }
}

impl std::error::Error for LuaError {}

type LResult<T> = Result<T, LuaError>;

fn err<T>(msg: impl Into<String>) -> LResult<T> {
    Err(LuaError(msg.into()))
}

// ------------------------------------------------------------------------------------------
// Lexer
// ------------------------------------------------------------------------------------------

#[derive(Debug, Clone, PartialEq)]
enum Tok {
    Name(String),
    Int(i64),
    Float(f64),
    Str(String),
    Kw(&'static str),
    Op(&'static str),
    Eof,
}

const KEYWORDS: [&str; 22] = [
    "and", "break", "do", "else", "elseif", "end", "false", "for", "function", "goto", "if", "in",
    "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while",
];

/// Longest-first so that `...` wins over `..` over `.`.
const OPS: [&str; 33] = [
    "...", "<<", ">>", "//", "==", "~=", "<=", ">=", "::", "..", "+", "-", "*", "/", "%", "^", "#",
    "&", "~", "|", "<", ">", "=", "(", ")", "{", "}", "[", "]", ";", ":", ",", ".",
];

struct Lexer<'a> {
    s: &'a [u8],
    p: usize,
    line: usize,
}

impl<'a> Lexer<'a> {
    fn new(s: &'a str) -> Self {
        Self {
            s: s.as_bytes(),
            p: 0,
            line: 1,
        }
    }

    fn peek(&self, off: usize) -> u8 {
        self.s.get(self.p + off).copied().unwrap_or(0)
    }

    fn error<T>(&self, msg: &str) -> LResult<T> {
        err(format!("[string \"chunk\"]:{}: {msg}", self.line))
    }

    /// `[==[` → level (number of `=`), or `None` when not a long bracket opener.
    fn long_bracket_level(&self) -> Option<usize> {
        if self.peek(0) != b'[' {
            return None;
        }
        let mut n = 0;
        while self.peek(1 + n) == b'=' {
            n += 1;
        }
        (self.peek(1 + n) == b'[').then_some(n)
    }

    fn read_long_bracket(&mut self, level: usize) -> LResult<String> {
        // skip `[`, `=`*level, `[`
        self.p += level + 2;
        // a first newline is skipped
        if self.peek(0) == b'\r' || self.peek(0) == b'\n' {
            let c = self.peek(0);
            self.p += 1;
            if (self.peek(0) == b'\r' || self.peek(0) == b'\n') && self.peek(0) != c {
                self.p += 1;
            }
            self.line += 1;
        }
        let start = self.p;
        loop {
            if self.p >= self.s.len() {
                return self.error("unfinished long string/comment");
            }
            if self.peek(0) == b']' {
                let mut n = 0;
                while self.peek(1 + n) == b'=' {
                    n += 1;
                }
                if n == level && self.peek(1 + n) == b']' {
                    let body = String::from_utf8_lossy(&self.s[start..self.p]).into_owned();
                    self.p += level + 2;
                    return Ok(body);
                }
            }
            if self.peek(0) == b'\n' {
                self.line += 1;
            }
            self.p += 1;
        }
    }

    fn skip_ws_and_comments(&mut self) -> LResult<()> {
        loop {
            match self.peek(0) {
                b'\n' => {
                    self.line += 1;
                    self.p += 1;
                }
                b' ' | b'\t' | b'\r' | 0x0b | 0x0c => self.p += 1,
                b'-' if self.peek(1) == b'-' => {
                    self.p += 2;
                    if let Some(level) = self.long_bracket_level() {
                        self.read_long_bracket(level)?;
                    } else {
                        while self.p < self.s.len()
                            && self.peek(0) != b'\n'
                            && self.peek(0) != b'\r'
                        {
                            self.p += 1;
                        }
                    }
                }
                _ => return Ok(()),
            }
        }
    }

    fn read_number(&mut self) -> LResult<Tok> {
        let start = self.p;
        let hex = self.peek(0) == b'0' && matches!(self.peek(1), b'x' | b'X');
        let (exp_a, exp_b) = if hex { (b'p', b'P') } else { (b'e', b'E') };
        if hex {
            self.p += 2;
        }
        loop {
            let c = self.peek(0);
            if (c == exp_a || c == exp_b) && matches!(self.peek(1), b'+' | b'-') {
                self.p += 2;
            } else if c.is_ascii_hexdigit() || c == b'.' || c.is_ascii_alphanumeric() || c == b'_' {
                self.p += 1;
            } else {
                break;
            }
        }
        let text = std::str::from_utf8(&self.s[start..self.p]).unwrap_or("");
        match parse_numeral(text) {
            Some(t) => Ok(t),
            None => self.error(&format!("malformed number near '{text}'")),
        }
    }

    fn read_string(&mut self, quote: u8) -> LResult<String> {
        self.p += 1;
        let mut out: Vec<u8> = Vec::new();
        loop {
            let c = self.peek(0);
            if self.p >= self.s.len() || c == b'\n' || c == b'\r' {
                return self.error("unfinished string");
            }
            self.p += 1;
            if c == quote {
                break;
            }
            if c != b'\\' {
                out.push(c);
                continue;
            }
            let e = self.peek(0);
            self.p += 1;
            match e {
                b'n' => out.push(b'\n'),
                b't' => out.push(b'\t'),
                b'r' => out.push(b'\r'),
                b'a' => out.push(0x07),
                b'b' => out.push(0x08),
                b'f' => out.push(0x0c),
                b'v' => out.push(0x0b),
                b'\\' => out.push(b'\\'),
                b'"' => out.push(b'"'),
                b'\'' => out.push(b'\''),
                b'\n' | b'\r' => {
                    out.push(b'\n');
                    self.line += 1;
                    let n = self.peek(0);
                    if (n == b'\n' || n == b'\r') && n != e {
                        self.p += 1;
                    }
                }
                b'x' => {
                    let h = std::str::from_utf8(self.s.get(self.p..self.p + 2).unwrap_or(&[]))
                        .ok()
                        .and_then(|h| u8::from_str_radix(h, 16).ok());
                    match h {
                        Some(v) => {
                            out.push(v);
                            self.p += 2;
                        }
                        None => return self.error("hexadecimal digit expected"),
                    }
                }
                b'z' => {
                    while matches!(self.peek(0), b' ' | b'\t' | b'\r' | b'\n' | 0x0b | 0x0c) {
                        if self.peek(0) == b'\n' {
                            self.line += 1;
                        }
                        self.p += 1;
                    }
                }
                b'u' => {
                    if self.peek(0) != b'{' {
                        return self.error("missing '{' in \\u{xxxx}");
                    }
                    self.p += 1;
                    let mut v: u32 = 0;
                    let mut digits = 0;
                    while self.peek(0).is_ascii_hexdigit() {
                        v = v
                            .checked_mul(16)
                            .and_then(|v| {
                                v.checked_add((self.peek(0) as char).to_digit(16).unwrap_or(0))
                            })
                            .unwrap_or(u32::MAX);
                        digits += 1;
                        self.p += 1;
                    }
                    if digits == 0 || self.peek(0) != b'}' || v > 0x7FFF_FFFF {
                        return self.error("UTF-8 value too large / malformed");
                    }
                    self.p += 1;
                    match char::from_u32(v) {
                        Some(ch) => {
                            let mut buf = [0u8; 4];
                            out.extend_from_slice(ch.encode_utf8(&mut buf).as_bytes());
                        }
                        None => out.extend_from_slice("\u{FFFD}".as_bytes()),
                    }
                }
                d if d.is_ascii_digit() => {
                    let mut v: u32 = u32::from(d - b'0');
                    for _ in 0..2 {
                        if self.peek(0).is_ascii_digit() {
                            v = v * 10 + u32::from(self.peek(0) - b'0');
                            self.p += 1;
                        }
                    }
                    if v > 255 {
                        return self.error("decimal escape too large");
                    }
                    out.push(v as u8);
                }
                _ => return self.error("invalid escape sequence"),
            }
        }
        Ok(String::from_utf8_lossy(&out).into_owned())
    }

    fn next(&mut self) -> LResult<Tok> {
        self.skip_ws_and_comments()?;
        if self.p >= self.s.len() {
            return Ok(Tok::Eof);
        }
        let c = self.peek(0);
        if c.is_ascii_alphabetic() || c == b'_' {
            let start = self.p;
            while self.peek(0).is_ascii_alphanumeric() || self.peek(0) == b'_' {
                self.p += 1;
            }
            let name = std::str::from_utf8(&self.s[start..self.p]).unwrap_or("");
            if let Some(k) = KEYWORDS.iter().find(|k| **k == name) {
                return Ok(Tok::Kw(k));
            }
            return Ok(Tok::Name(name.to_string()));
        }
        if c.is_ascii_digit() || (c == b'.' && self.peek(1).is_ascii_digit()) {
            return self.read_number();
        }
        if c == b'"' || c == b'\'' {
            return Ok(Tok::Str(self.read_string(c)?));
        }
        if let Some(level) = self.long_bracket_level() {
            return Ok(Tok::Str(self.read_long_bracket(level)?));
        }
        for op in OPS {
            if self.s[self.p..].starts_with(op.as_bytes()) {
                self.p += op.len();
                return Ok(Tok::Op(op));
            }
        }
        self.error(&format!("unexpected symbol near '{}'", c as char))
    }
}

/// `l_str2int` / `l_str2d`: a Lua numeral (no sign).
fn parse_numeral(text: &str) -> Option<Tok> {
    let lower = text.to_ascii_lowercase();
    if let Some(h) = lower.strip_prefix("0x") {
        if h.is_empty() {
            return None;
        }
        if !h.contains('.') && !h.contains('p') {
            // hex integers wrap around
            let mut v: u64 = 0;
            for ch in h.chars() {
                v = v.wrapping_mul(16).wrapping_add(ch.to_digit(16)? as u64);
            }
            return Some(Tok::Int(v as i64));
        }
        // hex float: mantissa [. fraction] [p exponent]
        let (mant, exp) = match h.split_once('p') {
            Some((m, e)) => (m, e.parse::<i32>().ok()?),
            None => (h, 0),
        };
        let (ip, fp) = mant.split_once('.').unwrap_or((mant, ""));
        if ip.is_empty() && fp.is_empty() {
            return None;
        }
        let mut v = 0f64;
        for ch in ip.chars() {
            v = v * 16.0 + f64::from(ch.to_digit(16)?);
        }
        let mut scale = 1.0 / 16.0;
        for ch in fp.chars() {
            v += f64::from(ch.to_digit(16)?) * scale;
            scale /= 16.0;
        }
        return Some(Tok::Float(v * 2f64.powi(exp)));
    }
    if !lower
        .bytes()
        .all(|b| b.is_ascii_digit() || b == b'.' || b == b'e' || b == b'+' || b == b'-')
    {
        return None;
    }
    let is_float = lower.contains('.') || lower.contains('e');
    if !is_float {
        if let Ok(v) = lower.parse::<i64>() {
            return Some(Tok::Int(v));
        }
        // decimal integers that overflow become floats
        return lower.parse::<f64>().ok().map(Tok::Float);
    }
    // Rust's f64 parser rejects "5." and ".5e"; normalize the forms Lua accepts.
    let mut norm = lower.clone();
    if norm.starts_with('.') {
        norm.insert(0, '0');
    }
    if let Some(i) = norm.find('.') {
        let next = norm.as_bytes().get(i + 1).copied();
        if next.is_none() || next == Some(b'e') {
            norm.insert(i + 1, '0');
        }
    }
    norm.parse::<f64>().ok().map(Tok::Float)
}

// ------------------------------------------------------------------------------------------
// Parser (AST)
// ------------------------------------------------------------------------------------------

#[derive(Debug, Clone)]
enum Field {
    Positional(Expr),
    Named(String, Expr),
    Keyed(Expr, Expr),
}

#[derive(Debug, Clone)]
enum Expr {
    Nil,
    Bool(bool),
    Int(i64),
    Float(f64),
    Str(String),
    Vararg,
    Global(String),
    Table(Vec<Field>),
    Index(Box<Expr>, Box<Expr>),
    Call(Box<Expr>),
    Unary(&'static str, Box<Expr>),
    Binary(&'static str, Box<Expr>, Box<Expr>),
}

struct Parser<'a> {
    lex: Lexer<'a>,
    tok: Tok,
}

/// (left, right) binding powers of the binary operators (Lua 5.4 `priority` table).
fn binary_priority(op: &str) -> Option<(u8, u8)> {
    Some(match op {
        "or" => (1, 1),
        "and" => (2, 2),
        "<" | ">" | "<=" | ">=" | "~=" | "==" => (3, 3),
        "|" => (4, 4),
        "~" => (5, 5),
        "&" => (6, 6),
        "<<" | ">>" => (7, 7),
        ".." => (9, 8), // right associative
        "+" | "-" => (10, 10),
        "*" | "/" | "//" | "%" => (11, 11),
        "^" => (14, 13), // right associative
        _ => return None,
    })
}

const UNARY_PRIORITY: u8 = 12;

impl<'a> Parser<'a> {
    fn new(src: &'a str) -> LResult<Self> {
        let mut lex = Lexer::new(src);
        let tok = lex.next()?;
        Ok(Self { lex, tok })
    }

    fn advance(&mut self) -> LResult<Tok> {
        let next = self.lex.next()?;
        Ok(std::mem::replace(&mut self.tok, next))
    }

    fn error<T>(&self, msg: &str) -> LResult<T> {
        self.lex.error(&format!("{msg} near {:?}", self.tok))
    }

    fn is_op(&self, op: &str) -> bool {
        matches!(self.tok, Tok::Op(o) if o == op)
    }

    fn expect_op(&mut self, op: &str) -> LResult<()> {
        if self.is_op(op) {
            self.advance()?;
            Ok(())
        } else {
            self.error(&format!("'{op}' expected"))
        }
    }

    /// chunk ::= { Name '=' exp [';'] } — the only statements the AI params use.
    fn chunk(&mut self) -> LResult<Vec<(String, Expr)>> {
        let mut out = Vec::new();
        loop {
            match self.tok.clone() {
                Tok::Eof => return Ok(out),
                Tok::Op(";") => {
                    self.advance()?;
                }
                Tok::Name(n) => {
                    self.advance()?;
                    self.expect_op("=")?;
                    let e = self.expr(0)?;
                    out.push((n, e));
                }
                _ => return self.error("unexpected symbol"),
            }
        }
    }

    fn expr(&mut self, limit: u8) -> LResult<Expr> {
        let mut left = match self.tok.clone() {
            Tok::Kw("not") => {
                self.advance()?;
                Expr::Unary("not", Box::new(self.expr(UNARY_PRIORITY)?))
            }
            Tok::Op(op @ ("-" | "#" | "~")) => {
                self.advance()?;
                Expr::Unary(op, Box::new(self.expr(UNARY_PRIORITY)?))
            }
            _ => self.simple_expr()?,
        };
        loop {
            let op: &'static str = match &self.tok {
                Tok::Op(o) => o,
                Tok::Kw(k @ ("and" | "or")) => k,
                _ => break,
            };
            let Some((lp, rp)) = binary_priority(op) else {
                break;
            };
            if lp <= limit {
                break;
            }
            self.advance()?;
            let right = self.expr(rp)?;
            left = Expr::Binary(op, Box::new(left), Box::new(right));
        }
        Ok(left)
    }

    fn simple_expr(&mut self) -> LResult<Expr> {
        let e = match self.tok.clone() {
            Tok::Int(v) => Expr::Int(v),
            Tok::Float(v) => Expr::Float(v),
            Tok::Str(s) => Expr::Str(s),
            Tok::Kw("nil") => Expr::Nil,
            Tok::Kw("true") => Expr::Bool(true),
            Tok::Kw("false") => Expr::Bool(false),
            Tok::Op("...") => Expr::Vararg,
            Tok::Op("{") => return self.table(),
            Tok::Kw("function") => {
                // Not used by any parameter string: reported as a syntax error.
                return self.error("function bodies are not supported");
            }
            _ => return self.suffixed_expr(),
        };
        self.advance()?;
        Ok(e)
    }

    fn primary_expr(&mut self) -> LResult<Expr> {
        match self.tok.clone() {
            Tok::Name(n) => {
                self.advance()?;
                Ok(Expr::Global(n))
            }
            Tok::Op("(") => {
                self.advance()?;
                let e = self.expr(0)?;
                self.expect_op(")")?;
                Ok(e)
            }
            _ => self.error("unexpected symbol"),
        }
    }

    fn suffixed_expr(&mut self) -> LResult<Expr> {
        let mut e = self.primary_expr()?;
        loop {
            match self.tok.clone() {
                Tok::Op(".") => {
                    self.advance()?;
                    match self.advance()? {
                        Tok::Name(n) => e = Expr::Index(Box::new(e), Box::new(Expr::Str(n))),
                        _ => return self.error("<name> expected"),
                    }
                }
                Tok::Op("[") => {
                    self.advance()?;
                    let k = self.expr(0)?;
                    self.expect_op("]")?;
                    e = Expr::Index(Box::new(e), Box::new(k));
                }
                Tok::Op(":") => {
                    self.advance()?;
                    if !matches!(self.advance()?, Tok::Name(_)) {
                        return self.error("<name> expected");
                    }
                    self.call_args()?;
                    e = Expr::Call(Box::new(e));
                }
                Tok::Op("(") | Tok::Op("{") | Tok::Str(_) => {
                    self.call_args()?;
                    e = Expr::Call(Box::new(e));
                }
                _ => return Ok(e),
            }
        }
    }

    fn call_args(&mut self) -> LResult<()> {
        match self.tok.clone() {
            Tok::Str(_) => {
                self.advance()?;
            }
            Tok::Op("{") => {
                self.table()?;
            }
            Tok::Op("(") => {
                self.advance()?;
                if !self.is_op(")") {
                    self.expr(0)?;
                    while self.is_op(",") {
                        self.advance()?;
                        self.expr(0)?;
                    }
                }
                self.expect_op(")")?;
            }
            _ => return self.error("function arguments expected"),
        }
        Ok(())
    }

    fn table(&mut self) -> LResult<Expr> {
        self.expect_op("{")?;
        let mut fields = Vec::new();
        loop {
            if self.is_op("}") {
                break;
            }
            let field = match self.tok.clone() {
                Tok::Op("[") => {
                    self.advance()?;
                    let k = self.expr(0)?;
                    self.expect_op("]")?;
                    self.expect_op("=")?;
                    Field::Keyed(k, self.expr(0)?)
                }
                Tok::Name(n) if matches!(self.lex.clone_peek_is_assign(), Ok(true)) => {
                    self.advance()?;
                    self.expect_op("=")?;
                    Field::Named(n, self.expr(0)?)
                }
                _ => Field::Positional(self.expr(0)?),
            };
            fields.push(field);
            if self.is_op(",") || self.is_op(";") {
                self.advance()?;
            } else {
                break;
            }
        }
        self.expect_op("}")?;
        Ok(Expr::Table(fields))
    }
}

impl Lexer<'_> {
    /// Whether the next token (after the current one) is a single `=` (not `==`).
    fn clone_peek_is_assign(&self) -> LResult<bool> {
        let mut l = Lexer {
            s: self.s,
            p: self.p,
            line: self.line,
        };
        Ok(matches!(l.next()?, Tok::Op("=")))
    }
}

// ------------------------------------------------------------------------------------------
// Evaluator
// ------------------------------------------------------------------------------------------

fn type_name(v: &LuaValue) -> &'static str {
    match v {
        LuaValue::Nil => "nil",
        LuaValue::Bool(_) => "boolean",
        LuaValue::Int(_) | LuaValue::Float(_) => "number",
        LuaValue::Str(_) => "string",
        LuaValue::Table(_) => "table",
        LuaValue::Function => "function",
    }
}

fn to_key(v: LuaValue) -> LResult<LuaKey> {
    Ok(match v {
        LuaValue::Nil => return err("table index is nil"),
        LuaValue::Bool(b) => LuaKey::Bool(b),
        LuaValue::Int(i) => LuaKey::Int(i),
        LuaValue::Float(f) => {
            if f.is_nan() {
                return err("table index is NaN");
            }
            if f.fract() == 0.0
                && (-9.223_372_036_854_776e18..9.223_372_036_854_776e18).contains(&f)
            {
                LuaKey::Int(f as i64)
            } else {
                LuaKey::Float(f.to_bits())
            }
        }
        LuaValue::Str(s) => LuaKey::Str(s),
        LuaValue::Table(t) => LuaKey::Other(Arc::as_ptr(&t) as usize),
        LuaValue::Function => LuaKey::Other(0),
    })
}

/// `luaO_str2num` on a string operand of an arithmetic operation.
fn str_to_number(s: &str) -> Option<LuaValue> {
    let t = s.trim_matches(|c: char| c.is_ascii_whitespace());
    let (neg, body) = match t.strip_prefix('-') {
        Some(b) => (true, b),
        None => (false, t.strip_prefix('+').unwrap_or(t)),
    };
    let v = match parse_numeral(body)? {
        Tok::Int(i) => LuaValue::Int(if neg { i.wrapping_neg() } else { i }),
        Tok::Float(f) => LuaValue::Float(if neg { -f } else { f }),
        _ => return None,
    };
    Some(v)
}

fn arith_operand(v: &LuaValue) -> Option<LuaValue> {
    match v {
        LuaValue::Int(_) | LuaValue::Float(_) => Some(v.clone()),
        LuaValue::Str(s) => str_to_number(s),
        _ => None,
    }
}

fn as_f64(v: &LuaValue) -> f64 {
    match v {
        LuaValue::Int(i) => *i as f64,
        LuaValue::Float(f) => *f,
        _ => 0.0,
    }
}

/// `luaV_tointegerns` (exact conversion only) for the bitwise operators.
fn as_int_exact(v: &LuaValue) -> Option<i64> {
    match v {
        LuaValue::Int(i) => Some(*i),
        LuaValue::Float(f)
            if f.fract() == 0.0
                && (-9.223_372_036_854_776e18..9.223_372_036_854_776e18).contains(f) =>
        {
            Some(*f as i64)
        }
        _ => None,
    }
}

/// `lua_Number` → string as `LUAI_NUMFFORMAT` ("%.14g") does, with the ".0" suffix of 5.4.
fn float_to_lua_string(f: f64) -> String {
    if f.is_nan() {
        return if f.is_sign_negative() { "-nan" } else { "nan" }.to_string();
    }
    if f.is_infinite() {
        return if f > 0.0 { "inf" } else { "-inf" }.to_string();
    }
    let s = format_g14(f);
    if s.contains(['.', 'e', 'n', 'i']) {
        s
    } else {
        format!("{s}.0")
    }
}

/// C's `%.14g`.
fn format_g14(f: f64) -> String {
    if f == 0.0 {
        return if f.is_sign_negative() { "-0" } else { "0" }.to_string();
    }
    let exp = f.abs().log10().floor() as i32;
    let sci = format!("{:.13e}", f);
    // exponent as printed by Rust (rounding may bump it)
    let (mant, e) = sci.split_once('e').unwrap_or((&sci, "0"));
    let e: i32 = e.parse().unwrap_or(exp);
    if !(-4..14).contains(&e) {
        let mant = trim_zeros(mant);
        format!("{mant}e{}{:02}", if e < 0 { '-' } else { '+' }, e.abs())
    } else {
        let decimals = (13 - e).max(0) as usize;
        trim_zeros(&format!("{:.*}", decimals, f)).to_string()
    }
}

fn trim_zeros(s: &str) -> &str {
    if s.contains('.') {
        s.trim_end_matches('0').trim_end_matches('.')
    } else {
        s
    }
}

fn to_concat_str(v: &LuaValue) -> Option<String> {
    match v {
        LuaValue::Str(s) => Some(s.clone()),
        LuaValue::Int(i) => Some(i.to_string()),
        LuaValue::Float(f) => Some(float_to_lua_string(*f)),
        _ => None,
    }
}

fn lua_eq(a: &LuaValue, b: &LuaValue) -> bool {
    match (a, b) {
        (LuaValue::Int(x), LuaValue::Float(y)) | (LuaValue::Float(y), LuaValue::Int(x)) => {
            (*x as f64) == *y
        }
        (LuaValue::Table(x), LuaValue::Table(y)) => Arc::ptr_eq(x, y),
        _ => a == b,
    }
}

fn truthy(v: &LuaValue) -> bool {
    !matches!(v, LuaValue::Nil | LuaValue::Bool(false))
}

fn floor_div_i(a: i64, b: i64) -> LResult<i64> {
    if b == 0 {
        return err("attempt to perform 'n//0'");
    }
    let q = a.wrapping_div(b);
    Ok(if (a % b != 0) && ((a < 0) != (b < 0)) {
        q - 1
    } else {
        q
    })
}

fn mod_i(a: i64, b: i64) -> LResult<i64> {
    if b == 0 {
        return err("attempt to perform 'n%%0'");
    }
    let r = a.wrapping_rem(b);
    Ok(if r != 0 && ((r ^ b) < 0) { r + b } else { r })
}

fn mod_f(a: f64, b: f64) -> f64 {
    let r = a % b;
    if r != 0.0 && ((r > 0.0) != (b > 0.0)) {
        r + b
    } else {
        r
    }
}

struct Eval<'g> {
    globals: &'g [(String, LuaValue)],
}

impl Eval<'_> {
    fn global(&self, name: &str) -> LuaValue {
        self.globals
            .iter()
            .rev()
            .find(|(n, _)| n == name)
            .map_or(LuaValue::Nil, |(_, v)| v.clone())
    }

    fn eval(&self, e: &Expr) -> LResult<LuaValue> {
        Ok(match e {
            Expr::Nil | Expr::Vararg => LuaValue::Nil,
            Expr::Bool(b) => LuaValue::Bool(*b),
            Expr::Int(i) => LuaValue::Int(*i),
            Expr::Float(f) => LuaValue::Float(*f),
            Expr::Str(s) => LuaValue::Str(s.clone()),
            Expr::Global(n) => self.global(n),
            Expr::Table(fields) => self.table(fields)?,
            Expr::Index(t, k) => {
                let t = self.eval(t)?;
                let k = self.eval(k)?;
                match &t {
                    LuaValue::Table(tab) => match to_key(k) {
                        Ok(key) => tab.get(&key).clone(),
                        // reading t[nil] / t[NaN] is just nil
                        Err(_) => LuaValue::Nil,
                    },
                    // strings have a metatable (string library) that is not loaded by NLua's
                    // `new Lua()`? It is: string methods exist, but indexing yields nil/functions.
                    LuaValue::Str(_) => LuaValue::Nil,
                    other => return err(format!("attempt to index a {} value", type_name(other))),
                }
            }
            Expr::Call(f) => {
                let f = self.eval(f)?;
                return err(format!("attempt to call a {} value", type_name(&f)));
            }
            Expr::Unary(op, a) => {
                let a = self.eval(a)?;
                match *op {
                    "not" => LuaValue::Bool(!truthy(&a)),
                    "-" => match arith_operand(&a) {
                        Some(LuaValue::Int(i)) => LuaValue::Int(i.wrapping_neg()),
                        Some(LuaValue::Float(f)) => LuaValue::Float(-f),
                        _ => {
                            return err(format!(
                                "attempt to perform arithmetic on a {} value",
                                type_name(&a)
                            ))
                        }
                    },
                    "#" => match &a {
                        LuaValue::Str(s) => LuaValue::Int(s.len() as i64),
                        LuaValue::Table(t) => {
                            // border of the array part (no holes in constructor-built lists)
                            let mut n = t.array.len();
                            while n > 0 && matches!(t.array[n - 1], LuaValue::Nil) {
                                n -= 1;
                            }
                            LuaValue::Int(n as i64)
                        }
                        other => {
                            return err(format!(
                                "attempt to get length of a {} value",
                                type_name(other)
                            ))
                        }
                    },
                    "~" => match arith_operand(&a).as_ref().and_then(as_int_exact) {
                        Some(i) => LuaValue::Int(!i),
                        None => return err("attempt to perform bitwise operation"),
                    },
                    _ => return err("bad unary operator"),
                }
            }
            Expr::Binary(op, a, b) => {
                if *op == "and" {
                    let l = self.eval(a)?;
                    return if truthy(&l) { self.eval(b) } else { Ok(l) };
                }
                if *op == "or" {
                    let l = self.eval(a)?;
                    return if truthy(&l) { Ok(l) } else { self.eval(b) };
                }
                let l = self.eval(a)?;
                let r = self.eval(b)?;
                self.binary(op, l, r)?
            }
        })
    }

    fn binary(&self, op: &str, l: LuaValue, r: LuaValue) -> LResult<LuaValue> {
        match op {
            "==" => return Ok(LuaValue::Bool(lua_eq(&l, &r))),
            "~=" => return Ok(LuaValue::Bool(!lua_eq(&l, &r))),
            "<" | "<=" | ">" | ">=" => {
                let ord = match (&l, &r) {
                    (LuaValue::Str(a), LuaValue::Str(b)) => a.as_bytes().partial_cmp(b.as_bytes()),
                    (LuaValue::Int(a), LuaValue::Int(b)) => a.partial_cmp(b),
                    (
                        x @ (LuaValue::Int(_) | LuaValue::Float(_)),
                        y @ (LuaValue::Int(_) | LuaValue::Float(_)),
                    ) => as_f64(x).partial_cmp(&as_f64(y)),
                    _ => {
                        return err(format!(
                            "attempt to compare {} with {}",
                            type_name(&l),
                            type_name(&r)
                        ))
                    }
                };
                let res = match (op, ord) {
                    (_, None) => false,
                    ("<", Some(o)) => o.is_lt(),
                    ("<=", Some(o)) => o.is_le(),
                    (">", Some(o)) => o.is_gt(),
                    (_, Some(o)) => o.is_ge(),
                };
                return Ok(LuaValue::Bool(res));
            }
            ".." => {
                return match (to_concat_str(&l), to_concat_str(&r)) {
                    (Some(a), Some(b)) => Ok(LuaValue::Str(a + &b)),
                    _ => err(format!(
                        "attempt to concatenate a {} value",
                        type_name(if to_concat_str(&l).is_none() { &l } else { &r })
                    )),
                };
            }
            _ => {}
        }
        let (Some(a), Some(b)) = (arith_operand(&l), arith_operand(&r)) else {
            return err(format!(
                "attempt to perform arithmetic on a {} value",
                type_name(if arith_operand(&l).is_none() { &l } else { &r })
            ));
        };
        if matches!(op, "&" | "|" | "~" | "<<" | ">>") {
            let (Some(x), Some(y)) = (as_int_exact(&a), as_int_exact(&b)) else {
                return err("number has no integer representation");
            };
            return Ok(LuaValue::Int(match op {
                "&" => x & y,
                "|" => x | y,
                "~" => x ^ y,
                "<<" => shift_left(x, y),
                _ => shift_left(x, y.wrapping_neg()),
            }));
        }
        Ok(match (op, &a, &b) {
            ("+", LuaValue::Int(x), LuaValue::Int(y)) => LuaValue::Int(x.wrapping_add(*y)),
            ("-", LuaValue::Int(x), LuaValue::Int(y)) => LuaValue::Int(x.wrapping_sub(*y)),
            ("*", LuaValue::Int(x), LuaValue::Int(y)) => LuaValue::Int(x.wrapping_mul(*y)),
            ("//", LuaValue::Int(x), LuaValue::Int(y)) => LuaValue::Int(floor_div_i(*x, *y)?),
            ("%", LuaValue::Int(x), LuaValue::Int(y)) => LuaValue::Int(mod_i(*x, *y)?),
            ("+", x, y) => LuaValue::Float(as_f64(x) + as_f64(y)),
            ("-", x, y) => LuaValue::Float(as_f64(x) - as_f64(y)),
            ("*", x, y) => LuaValue::Float(as_f64(x) * as_f64(y)),
            ("/", x, y) => LuaValue::Float(as_f64(x) / as_f64(y)),
            ("//", x, y) => LuaValue::Float((as_f64(x) / as_f64(y)).floor()),
            ("%", x, y) => LuaValue::Float(mod_f(as_f64(x), as_f64(y))),
            ("^", x, y) => LuaValue::Float(as_f64(x).powf(as_f64(y))),
            _ => return err(format!("unsupported operator {op}")),
        })
    }

    fn table(&self, fields: &[Field]) -> LResult<LuaValue> {
        let mut t = LuaTable::default();
        let mut positional = Vec::new();
        for f in fields {
            match f {
                Field::Positional(e) => positional.push(self.eval(e)?),
                Field::Named(n, e) => {
                    let v = self.eval(e)?;
                    t.set(LuaKey::Str(n.clone()), v);
                }
                Field::Keyed(k, e) => {
                    let k = to_key(self.eval(k)?)?;
                    let v = self.eval(e)?;
                    t.set(k, v);
                }
            }
        }
        // Positional fields are stored last (OP_SETLIST) and win over explicit integer keys.
        t.hash.retain(|(k, _)| match k {
            LuaKey::Int(i) => !(*i >= 1 && (*i as u64) <= positional.len() as u64),
            _ => true,
        });
        t.array = positional;
        Ok(LuaValue::Table(Arc::new(t)))
    }
}

fn shift_left(x: i64, n: i64) -> i64 {
    if n <= -64 || n >= 64 {
        0
    } else if n >= 0 {
        ((x as u64) << n) as i64
    } else {
        ((x as u64) >> (-n)) as i64
    }
}

/// A Lua state after `DoString`: its global variables.
#[derive(Debug, Clone, Default)]
pub struct LuaState {
    globals: Vec<(String, LuaValue)>,
}

impl LuaState {
    /// A state with preset global numbers (the `AiLua` constructor's constants).
    pub fn with_globals(globals: &[(&str, i64)]) -> Self {
        Self {
            globals: globals
                .iter()
                .map(|(n, v)| (n.to_string(), LuaValue::Int(*v)))
                .collect(),
        }
    }

    /// `DoString(chunk)` for a chunk of `Name = exp` assignments.
    pub fn do_string(&mut self, chunk: &str) -> Result<(), LuaError> {
        let stmts = Parser::new(chunk)?.chunk()?;
        for (name, e) in stmts {
            let v = Eval {
                globals: &self.globals,
            }
            .eval(&e)?;
            self.globals.retain(|(n, _)| *n != name);
            if !matches!(v, LuaValue::Nil) {
                self.globals.push((name, v));
            }
        }
        Ok(())
    }

    /// `GetObjectFromPath("a.b.c")` (`None` = `null`).
    pub fn get_object_from_path(&self, path: &str) -> Option<LuaValue> {
        let mut parts = path.split('.');
        let first = parts.next()?;
        let mut cur = Eval {
            globals: &self.globals,
        }
        .global(first);
        for p in parts {
            cur = match &cur {
                LuaValue::Table(t) => t.field(p).clone(),
                _ => return None,
            };
        }
        (!matches!(cur, LuaValue::Nil)).then_some(cur)
    }

    /// `GetNumber(path)`: `long` → `double`, `double` as is; anything else is an
    /// `InvalidCastException` (`null` a `NullReferenceException`).
    pub fn get_number(&self, path: &str) -> Result<f64, ConvertError> {
        match self.get_object_from_path(path) {
            Some(LuaValue::Int(i)) => Ok(i as f64),
            Some(LuaValue::Float(f)) => Ok(f),
            Some(_) => Err(ConvertError::InvalidCast),
            None => Err(ConvertError::NullReference),
        }
    }

    /// `GetInteger(path)`: `null` → 0, `long` → `(int)` (unchecked), anything else is an
    /// `InvalidCastException`.
    pub fn get_integer(&self, path: &str) -> Result<i32, ConvertError> {
        match self.get_object_from_path(path) {
            None => Ok(0),
            Some(LuaValue::Int(i)) => Ok(i as i32),
            Some(_) => Err(ConvertError::InvalidCast),
        }
    }

    /// `GetTable(path)` + `is LuaTable` (`None` for `null`; a non-table value is an
    /// `InvalidCastException`).
    pub fn get_table(&self, path: &str) -> Result<Option<Arc<LuaTable>>, ConvertError> {
        match self.get_object_from_path(path) {
            None => Ok(None),
            Some(LuaValue::Table(t)) => Ok(Some(t)),
            Some(_) => Err(ConvertError::InvalidCast),
        }
    }

    /// `(string)GetObjectFromPath(path)` (`None` for `null`).
    pub fn get_string_cast(&self, path: &str) -> Result<Option<String>, ConvertError> {
        match self.get_object_from_path(path) {
            None => Ok(None),
            Some(LuaValue::Str(s)) => Ok(Some(s)),
            Some(_) => Err(ConvertError::InvalidCast),
        }
    }
}

/// The .NET exceptions `System.Convert` / casts throw on NLua objects.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ConvertError {
    InvalidCast,
    Overflow,
    Format,
    NullReference,
}

impl fmt::Display for ConvertError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(match self {
            ConvertError::InvalidCast => "Specified cast is not valid.",
            ConvertError::Overflow => "Value was either too large or too small.",
            ConvertError::Format => "Input string was not in a correct format.",
            ConvertError::NullReference => "Object reference not set to an instance of an object.",
        })
    }
}

impl std::error::Error for ConvertError {}

/// `Convert.ToBoolean(object)`.
pub fn convert_to_bool(v: &LuaValue) -> Result<bool, ConvertError> {
    match v {
        LuaValue::Nil => Ok(false),
        LuaValue::Bool(b) => Ok(*b),
        LuaValue::Int(i) => Ok(*i != 0),
        LuaValue::Float(f) => Ok(*f != 0.0),
        LuaValue::Str(s) => {
            // Boolean.Parse: "True" / "False" (ignoring case and surrounding white space)
            let t = s.trim();
            if t.eq_ignore_ascii_case("true") {
                Ok(true)
            } else if t.eq_ignore_ascii_case("false") {
                Ok(false)
            } else {
                Err(ConvertError::Format)
            }
        }
        LuaValue::Table(_) | LuaValue::Function => Err(ConvertError::InvalidCast),
    }
}

/// `Convert.ToDouble(double)` → integer with .NET's round-half-to-even and range check.
fn round_to_even(f: f64) -> f64 {
    let r = f.round();
    if (f - f.trunc()).abs() == 0.5 {
        2.0 * (f / 2.0).round()
    } else {
        r
    }
}

/// `double.Parse` / `float.Parse` with the invariant culture (what the server runs with).
fn parse_dotnet_f64(s: &str) -> Result<f64, ConvertError> {
    let t = s.trim();
    t.parse::<f64>().map_err(|_| ConvertError::Format)
}

/// `Convert.ToUInt32(object)`.
pub fn convert_to_u32(v: &LuaValue) -> Result<u32, ConvertError> {
    match v {
        LuaValue::Nil => Ok(0),
        LuaValue::Bool(b) => Ok(u32::from(*b)),
        LuaValue::Int(i) => u32::try_from(*i).map_err(|_| ConvertError::Overflow),
        LuaValue::Float(f) => {
            if *f >= -0.5 && *f < 4_294_967_295.5 {
                Ok(round_to_even(*f) as u32)
            } else {
                Err(ConvertError::Overflow)
            }
        }
        LuaValue::Str(s) => s.trim().parse::<u32>().map_err(|_| ConvertError::Format),
        LuaValue::Table(_) | LuaValue::Function => Err(ConvertError::InvalidCast),
    }
}

/// `Convert.ToInt32(object)`.
pub fn convert_to_i32(v: &LuaValue) -> Result<i32, ConvertError> {
    match v {
        LuaValue::Nil => Ok(0),
        LuaValue::Bool(b) => Ok(i32::from(*b)),
        LuaValue::Int(i) => i32::try_from(*i).map_err(|_| ConvertError::Overflow),
        LuaValue::Float(f) => {
            if *f >= -2_147_483_648.5 && *f < 2_147_483_647.5 {
                Ok(round_to_even(*f) as i32)
            } else {
                Err(ConvertError::Overflow)
            }
        }
        LuaValue::Str(s) => s.trim().parse::<i32>().map_err(|_| ConvertError::Format),
        LuaValue::Table(_) | LuaValue::Function => Err(ConvertError::InvalidCast),
    }
}

/// `Convert.ToSingle(object)`.
pub fn convert_to_f32(v: &LuaValue) -> Result<f32, ConvertError> {
    match v {
        LuaValue::Nil => Ok(0.0),
        LuaValue::Bool(b) => Ok(if *b { 1.0 } else { 0.0 }),
        LuaValue::Int(i) => Ok(*i as f32),
        LuaValue::Float(f) => Ok(*f as f32),
        LuaValue::Str(s) => parse_dotnet_f64(s).map(|f| f as f32),
        LuaValue::Table(_) | LuaValue::Function => Err(ConvertError::InvalidCast),
    }
}

/// `Convert.ToString(object)` (invariant culture; `null` → `""`).
pub fn convert_to_string(v: &LuaValue) -> String {
    match v {
        LuaValue::Nil => String::new(),
        LuaValue::Bool(b) => if *b { "True" } else { "False" }.to_string(),
        LuaValue::Int(i) => i.to_string(),
        LuaValue::Float(f) => dotnet_double_to_string(*f),
        LuaValue::Str(s) => s.clone(),
        LuaValue::Table(_) => "NLua.LuaTable".to_string(),
        LuaValue::Function => "NLua.LuaFunction".to_string(),
    }
}

/// `double.ToString()` (shortest round-trip, invariant culture).
fn dotnet_double_to_string(f: f64) -> String {
    if f.is_nan() {
        return "NaN".to_string();
    }
    if f.is_infinite() {
        return if f > 0.0 { "∞" } else { "-∞" }.to_string();
    }
    let s = format!("{f}");
    s.strip_suffix(".0").map_or(s.clone(), str::to_string)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn run(src: &str) -> LuaState {
        let mut l = LuaState::with_globals(&[("USE_SEQUENCE", 1), ("AGGRO_TOTAL", 1)]);
        l.do_string(&format!("data = {{\n{src}\n}}")).expect("lua");
        l
    }

    #[test]
    fn numbers_keep_their_subtype() {
        let l = run("a = 3, b = 3.0, c = 6.6, d = -2, e = 0x10, f = 1e2, g = .5, h = 5.");
        assert_eq!(l.get_object_from_path("data.a"), Some(LuaValue::Int(3)));
        assert_eq!(l.get_object_from_path("data.b"), Some(LuaValue::Float(3.0)));
        assert_eq!(l.get_object_from_path("data.c"), Some(LuaValue::Float(6.6)));
        assert_eq!(l.get_object_from_path("data.d"), Some(LuaValue::Int(-2)));
        assert_eq!(l.get_object_from_path("data.e"), Some(LuaValue::Int(16)));
        assert_eq!(
            l.get_object_from_path("data.f"),
            Some(LuaValue::Float(100.0))
        );
        assert_eq!(l.get_object_from_path("data.g"), Some(LuaValue::Float(0.5)));
        assert_eq!(l.get_object_from_path("data.h"), Some(LuaValue::Float(5.0)));
        assert_eq!(l.get_object_from_path("data.missing"), None);
        assert_eq!(l.get_integer("data.a"), Ok(3));
        assert_eq!(l.get_integer("data.b"), Err(ConvertError::InvalidCast));
        assert_eq!(l.get_integer("data.missing"), Ok(0));
        assert_eq!(l.get_number("data.a"), Ok(3.0));
    }

    #[test]
    fn comments_strings_and_constants() {
        let l = run(
            "--[[ long\ncomment ]] useType = USE_SEQUENCE, -- 한국어 주석\n\
             target = { AGGRO_TOTAL, 1 }, other = { AGGRO_DAMAGE, 7 },\n\
             pipe = \"phase_dragon_ground\", s2 = 'a\\tb', s3 = [==[x]]y]==],",
        );
        assert_eq!(
            l.get_object_from_path("data.useType"),
            Some(LuaValue::Int(1))
        );
        let Some(LuaValue::Table(t)) = l.get_object_from_path("data.target") else {
            panic!()
        };
        assert_eq!(t.at(1), &LuaValue::Int(1));
        assert_eq!(t.at(2), &LuaValue::Int(1));
        // undefined constant -> nil hole, skipped by Values
        let Some(LuaValue::Table(t)) = l.get_object_from_path("data.other") else {
            panic!()
        };
        assert_eq!(t.values(), vec![&LuaValue::Int(7)]);
        assert_eq!(t.at(2), &LuaValue::Int(7));
        assert_eq!(
            l.get_string_cast("data.pipe"),
            Ok(Some("phase_dragon_ground".to_string()))
        );
        assert_eq!(
            l.get_object_from_path("data.s2"),
            Some(LuaValue::Str("a\tb".into()))
        );
        assert_eq!(
            l.get_object_from_path("data.s3"),
            Some(LuaValue::Str("x]]y".into()))
        );
    }

    #[test]
    fn syntax_and_runtime_errors_fail_the_chunk() {
        let mut l = LuaState::default();
        // npc_ai_params 835: `melee = { [2,12279] }`
        assert!(l.do_string("data = {\nmelee = { [2,12279] },\n}").is_err());
        assert!(l.do_string("data = { x = random(1, 2) }").is_err());
        assert!(l.do_string("data = { x = y.z }").is_err());
        assert!(l.do_string("data = { [nil] = 1 }").is_err());
        assert!(l.do_string("data = { x = 1 ").is_err());
        assert!(l.do_string("data = { x = 'abc }").is_err());
    }

    #[test]
    fn positional_fields_win_over_explicit_integer_keys() {
        let l = run("t = { [1] = 'a', 'b', [3] = 'c' }");
        let Some(LuaValue::Table(t)) = l.get_object_from_path("data.t") else {
            panic!()
        };
        assert_eq!(t.at(1), &LuaValue::Str("b".into()));
        assert_eq!(t.at(3), &LuaValue::Str("c".into()));
        assert_eq!(t.values().len(), 2);
    }

    #[test]
    fn expressions() {
        let l = run("a = 1 + 2 * 3, b = 2 ^ 3, c = 7 // 2, d = -7 % 3, e = 'x' .. 1, f = 1 < 2, g = nil or 5, h = (4)");
        assert_eq!(l.get_object_from_path("data.a"), Some(LuaValue::Int(7)));
        assert_eq!(l.get_object_from_path("data.b"), Some(LuaValue::Float(8.0)));
        assert_eq!(l.get_object_from_path("data.c"), Some(LuaValue::Int(3)));
        assert_eq!(l.get_object_from_path("data.d"), Some(LuaValue::Int(2)));
        assert_eq!(
            l.get_object_from_path("data.e"),
            Some(LuaValue::Str("x1".into()))
        );
        assert_eq!(l.get_object_from_path("data.f"), Some(LuaValue::Bool(true)));
        assert_eq!(l.get_object_from_path("data.g"), Some(LuaValue::Int(5)));
        assert_eq!(l.get_object_from_path("data.h"), Some(LuaValue::Int(4)));
    }

    #[test]
    fn dotnet_conversions() {
        assert_eq!(convert_to_bool(&LuaValue::Int(3)), Ok(true));
        assert_eq!(convert_to_bool(&LuaValue::Float(0.0)), Ok(false));
        assert_eq!(convert_to_bool(&LuaValue::Nil), Ok(false));
        assert_eq!(convert_to_bool(&LuaValue::Str(" TRUE ".into())), Ok(true));
        assert_eq!(
            convert_to_bool(&LuaValue::Str("yes".into())),
            Err(ConvertError::Format)
        );
        assert_eq!(convert_to_u32(&LuaValue::Float(2.5)), Ok(2));
        assert_eq!(convert_to_u32(&LuaValue::Float(3.5)), Ok(4));
        assert_eq!(
            convert_to_u32(&LuaValue::Int(-1)),
            Err(ConvertError::Overflow)
        );
        assert_eq!(convert_to_i32(&LuaValue::Float(-2.5)), Ok(-2));
        assert_eq!(convert_to_f32(&LuaValue::Int(7)), Ok(7.0));
        assert_eq!(convert_to_string(&LuaValue::Nil), "");
        assert_eq!(convert_to_string(&LuaValue::Float(2.5)), "2.5");
        assert_eq!(convert_to_string(&LuaValue::Float(3.0)), "3");
        assert_eq!(float_to_lua_string(3.0), "3.0");
        assert_eq!(float_to_lua_string(0.1), "0.1");
        assert_eq!(float_to_lua_string(1e20), "1e+20");
    }
}
