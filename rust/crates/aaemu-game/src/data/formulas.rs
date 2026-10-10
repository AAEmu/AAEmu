//! Port of `FormulaManager` (`AAEmu.Game/Core/Managers/FormulaManager.cs`) and the
//! `AAEmu.Game/Models/Game/Formulas/*` models, plus a re-implementation of the expression engine
//! the C# borrows from the NuGet package **Jace 1.0.0** (`Jace.CalculationEngine`).
//!
//! # Expression engine
//!
//! The C# builds one `CalculationEngine` in `FormulaManager.Load` with `CacheEnabled`,
//! `OptimizerEnabled`, `CaseSensitive = true`, `ExecutionMode.Compiled` and `InvariantCulture`,
//! and registers four extra functions (`clamp`, `if_negative`, `if_positive`, `if_zero`).
//! [`Expression`] reproduces that engine: the formula text is tokenized and turned into an AST once
//! ([`Expression::parse`], used by [`Formula::prepare`]) and evaluated many times.
//!
//! The behaviour below was taken from the IL of the real Jace 1.0.0 assembly (`TokenReader`,
//! `AstBuilder`, `Optimizer`, `MathExtended`) and verified with a throw-away .NET 10 harness
//! configured exactly like `FormulaManager.Load`, diffed bit-for-bit (including the sign of zero
//! and error-vs-value) against this module on ~200k generated well-formed and garbage formulas:
//!
//! * **Tokens.** Only `' '` is whitespace (tab / newline / NBSP are "invalid token" errors).
//!   Numbers are ASCII digits, `.` and an `e`/`E` exponent whose sign may only be `-`
//!   (`1e-2` is fine, `1e+2` is a syntax error). A `-` at the start of the formula or right after
//!   `+ - * / % ^ ≤ ≥ ≠ (` starts a number token (`-2`, but `-e` is then a broken number).
//!   A digit run that is not a valid number (`1.2.3`, `2ex`) is **silently dropped** by Jace's
//!   `TokenReader` ("Else we skip") — mirrored, so `"1.2.3 x"` evaluates to `x`.
//!   Identifiers are ASCII: a letter, then letters, digits or `_`. A `-` after a comparison or
//!   logical operator, `,` or an operand is an operator token (unary or binary), not a literal.
//! * **Operators and precedence** (Jace `AstBuilder`): `&& ||` (1) < `< <= > >= == != ≤ ≥ ≠`
//!   (2) < `+ -` (3) < `* / %` (4) < `^` (5) < unary minus (6). Only `+ - * /` are
//!   left-associative; `% ^`, the comparisons and `&& ||` are right-associative, and the
//!   associativity of the *incoming* operator decides ties, so `2*3%4 = 2*(3%4) = 6`,
//!   `8%3*2 = 4`, `2^3^2 = 512`, `1<2<3 = 1<(2<3) = 0`, `1||0&&0 = 1`. Unary minus binds tighter
//!   than `^`: `-2^2 = 4`, `-x^2 = (-x)^2`, `2^-3^2 = 2^((-3)^2)`. There is no unary plus.
//!   Comparisons and `&&`/`||` yield `1.0`/`0.0`; `&&`/`||` test `!= 0.0` (so NaN is "true") and,
//!   like every function, evaluate **both** operands (no short-circuit).
//! * **Functions** (names are case-*insensitive* even with `CaseSensitive = true`; any identifier
//!   that names a function is a function, `max` alone is a syntax error): `sin cos csc sec asin
//!   acos tan cot atan acot loge log10 logn sqrt abs if ifless ifmore ifequal ceiling floor
//!   truncate round random` (fixed arity), `max min avg median` (one or more arguments), plus the
//!   four `FormulaManager` functions. `round` is banker's rounding (`Math.Round`), `logn(a, b)` is
//!   `Math.Log(a, b)`, `max`/`min`/`avg` follow LINQ (`max` skips NaN, `min` propagates it),
//!   `median` is Jace's quickselect of the lower median (NaN sorts first).
//! * **Variables** are case-sensitive. A variable missing from the parameter map falls back to
//!   the constants `e` / `pi` (matched case-insensitively, so `PI` works but a `pi` parameter
//!   overrides the constant); otherwise evaluation fails with `VariableNotDefinedException`
//!   ([`EvalError::VariableNotDefined`]), which `Formula.Evaluate` turns into `0`.
//! * **Optimizer.** Jace folds every variable-free, deterministic sub-tree into a floating-point
//!   constant, recursing only through `+ - * / ^` and function arguments, and rewrites a
//!   multiplication with a *floating-point* `0.0` constant operand to `0.0`. This is observable
//!   and mirrored: `0.0*x` is `0` even when `x` is missing / NaN / infinite, while `0*x` (integer
//!   literal) and `-(0.0*x)` still fail on a missing `x`.
//! * Arithmetic is IEEE `f64` (`1/0 = inf`, `0/0 = NaN`, `%` is `fmod` like C#).
//! * Malformed input is rejected exactly where Jace rejects it, including Jace's shunting-yard
//!   leniencies (`"1 2 +"` is `3`, `"sin 1"` is `sin(1)`, `"2 max()"` is `max(2)`).
//!
//! # Deviations (documented, deliberate)
//!
//! * Error messages are close to, but not byte-identical with, Jace's exceptions (some of Jace's
//!   failures are raw `IndexOutOfRangeException` / `InvalidOperationException`s).
//! * Error positions count Unicode scalars, Jace counts UTF-16 units (only differs after a
//!   character outside the BMP, which is an invalid token anyway).
//! * `random()` uses a small thread-local SplitMix64 generator instead of `System.Random`.
//! * [`Formula::evaluate`] on a formula whose [`Formula::prepare`] failed logs and returns `0`;
//!   the C# would throw `ArgumentNullException` out of `lock (Expression)` (see
//!   [`Formula::evaluate`]).
//! * The C# `_loaded` static guard is not ported; [`FormulaManager::load`] builds a fresh value.

use std::borrow::Borrow;
use std::cell::Cell;
use std::collections::hash_map::{Entry, RandomState};
use std::collections::HashMap;
use std::fmt::Write as _;
use std::hash::{BuildHasher, Hash, Hasher};
use std::ops::Deref;

use rusqlite::types::ValueRef;
use rusqlite::{Connection, Row};

// ---------------------------------------------------------------------------------------------
// Enums
// ---------------------------------------------------------------------------------------------

/// Generates a C#-style enum with its exact discriminants, a checked conversion from the raw
/// value (C# casts any raw value into the enum, so callers keep the raw value when this fails)
/// and the conversion back.
macro_rules! raw_enum {
    (
        $(#[$meta:meta])*
        $vis:vis enum $name:ident : $repr:ident via $from:ident {
            $( $(#[$vmeta:meta])* $variant:ident = $value:literal, )*
        }
    ) => {
        $(#[$meta])*
        #[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, PartialOrd, Ord)]
        #[repr($repr)]
        $vis enum $name {
            $( $(#[$vmeta])* $variant = $value, )*
        }

        impl $name {
            /// Every defined value (`Enum.GetValues`).
            pub const ALL: &'static [$name] = &[$( $name::$variant, )*];

            /// The variant with this raw value, or `None` for a value the C# enum does not name.
            pub const fn $from(value: $repr) -> Option<Self> {
                match value {
                    $( $value => Some(Self::$variant), )*
                    _ => None,
                }
            }

            /// The raw (wire / database) value.
            pub const fn raw(self) -> $repr {
                self as $repr
            }
        }

        impl From<$name> for $repr {
            fn from(value: $name) -> $repr {
                value as $repr
            }
        }

        impl TryFrom<$repr> for $name {
            type Error = $repr;

            fn try_from(value: $repr) -> Result<Self, $repr> {
                Self::$from(value).ok_or(value)
            }
        }
    };
}

raw_enum! {
    /// `FormulaOwnerType` (`unit_formulas.owner_type_id`).
    pub enum FormulaOwnerType: u8 via from_u8 {
        Character = 0,
        Npc = 1,
        Slave = 2,
        Housing = 3,
        Transfer = 4,
        Mate = 5,
        Shipyard = 6,
    }
}

raw_enum! {
    /// `UnitFormulaKind` (`unit_formulas.kind_id`). Values 3, 4, 8 and 9 (`MeleeDodge`,
    /// `MeleeBlock`, `RangedDodge`, `RangedBlock`) are commented out in the C# ("do not exist").
    pub enum UnitFormulaKind: u8 via from_u8 {
        MeleeCritical = 1,
        MeleeAntiMiss = 2,
        MeleeParry = 5,
        RangedCritical = 6,
        RangedAntiMiss = 7,
        SpellCritical = 10,
        SpellAntiMiss = 11,
        LevelDps = 12,
        LevelMana = 13,
        MaxHealth = 14,
        MaxMana = 15,
        HealthRegen = 16,
        ManaRegen = 17,
        Armor = 18,
        MagicResist = 19,
        Facet = 20,
        MeleeDpsInc = 21,
        Str = 22,
        Dex = 23,
        Sta = 24,
        Int = 25,
        Spi = 26,
        Fai = 27,
        RangedDpsInc = 28,
        SpellDpsInc = 29,
        CastingTolerance = 30,
        PersistentHealthRegen = 31,
        PersistentManaRegen = 32,
        BaseMissPercent = 33,
        KillExp = 34,
        MeleeCriticalBonus = 35,
        RangedCriticalBonus = 36,
        SpellCriticalBonus = 37,
        RangedParry = 38,
        IncomingMeleeDamageAdd = 39,
        IncomingRangedDamageAdd = 40,
        IncomingSpellDamageAdd = 41,
        HealCritical = 42,
        HealDpsInc = 43,
        HealCriticalBonus = 44,
        Block = 45,
        Dodge = 46,
    }
}

raw_enum! {
    /// `UnitFormulaVariableType` (`unit_formula_variables.variable_kind_id`).
    pub enum UnitFormulaVariableType: u8 via from_u8 {
        Unknown = 1,
        NpcTemplate = 2,
        NpcKind = 3,
        NpcGrade = 4,
        MateKind = 5,
    }
}

raw_enum! {
    /// `WearableFormulaType` (`wearable_formulas.kind_id`).
    pub enum WearableFormulaType: u8 via from_u8 {
        MaxBaseArmor = 0,
        MaxBaseMagicResistance = 1,
    }
}

raw_enum! {
    /// `FormulaKind`: ids of rows in the `formulas` table (`FormulaManager.GetFormula((uint)kind)`).
    /// Values from 28 on only exist in versions after 1.2.
    pub enum FormulaKind: u32 via from_u32 {
        CastingCancelPercent = 2,
        CastingDelayTime = 3,
        DetectStealthRangeFront = 4,
        NpcPerceptionRangeMod = 5,
        MoneyForRecoverExp = 6,
        LaborPowerForRecoverExp = 7,
        PenaltyExp = 8,
        RecoverableExp = 9,
        DetectStealthRangeBack = 10,
        DamageMultiplierByHeight = 11,
        DamageMultiplierByRange = 12,
        ArmorGradeBuffABLevel = 13,
        NPCFlyPassDist = 14,
        NPCFlyPassHeight = 15,
        NPCFlyDiveDist = 16,
        NPCFlyDiveHeight = 17,
        NPCFlyQuickTurnDist = 18,
        ExpByLaborPower = 19,
        DamageBonusMultiplierByLevel = 20,
        DamagePenaltyMultiplierByLevel = 21,
        GradeEnchantCost = 22,
        DamageReduceRadioByBattleResist = 23,
        FacetsForBullsEye = 24,
        FlexibilityRatio = 25,
        FlexibilityBonus = 26,
        ExpBySkillEffect = 27,
        PhysicsCollisionDamage = 28,
        SlaveEquipmentGradeEnchantCost = 29,
        GearScoreWeaponArmorAcc = 30,
        GearScoreSocket = 31,
        GearScoreEnchantingGem = 32,
        ConquestBonusScore = 33,
        ConquestPenaltyScore = 34,
        AAPointMaxLimitByLevel = 35,
        LeadershipPointDecreaseForNationChange = 36,
        MateEquipmentEchantCost = 37,
        ItemSocketingCost = 38,
        UnbindEquipBindItem = 39,
        ResetSkillCost = 40,
        SwapAbilityCost = 41,
        SwapAbilitySetCost = 42,
        EloRatingCalculation = 43,
        BlessUthstinConsumeItemNum = 44,
        BlessUthstinExtendMaxStat = 45,
    }
}

// ---------------------------------------------------------------------------------------------
// Expression engine (Jace 1.0.0)
// ---------------------------------------------------------------------------------------------

/// A Jace `ParseException` (or one of the raw exceptions Jace's parser lets escape) raised by
/// `CalculationEngine.Build`.
#[derive(Debug, Clone, PartialEq, thiserror::Error)]
pub enum ParseError {
    #[error("Invalid token \"{token}\" detected at position {position}.")]
    InvalidToken { token: char, position: usize },
    /// Jace indexes past the end of the text for a trailing exponent marker (`"2e"`).
    #[error("Incomplete exponent in the number at position {position}.")]
    IncompleteExponent { position: usize },
    /// A binary `-` with no preceding token (only reachable after a dropped invalid number).
    #[error("Unexpected \"-\" at position {position}.")]
    UnexpectedMinus { position: usize },
    #[error(
        "There is a syntax issue for the operation \"{operation}\" at position {position}. \
         The number of arguments does not match with what is expected."
    )]
    OperationArguments { operation: char, position: usize },
    #[error(
        "There is a syntax issue for the function \"{function}\" at position {position}. \
         The number of arguments does not match with what is expected."
    )]
    FunctionArguments { function: String, position: usize },
    #[error("No matching left bracket found for the right bracket at position {position}.")]
    NoMatchingLeftBracket { position: usize },
    #[error("No matching right bracket found for the left bracket at position {position}.")]
    NoMatchingRightBracket { position: usize },
    /// An argument separator outside of any function call (Jace: "Stack empty.").
    #[error("Unexpected argument separator at position {position}.")]
    UnexpectedArgumentSeparator { position: usize },
    #[error("Unexpected integer constant \"{0}\" found.")]
    UnexpectedIntegerConstant(f64),
    #[error("Unexpected floating point constant \"{0}\" found.")]
    UnexpectedFloatingPointConstant(f64),
    #[error("The syntax of the provided formula is not valid.")]
    InvalidSyntax,
    /// Nothing to evaluate (Jace: "Sequence contains no elements").
    #[error("The formula is empty.")]
    Empty,
}

/// A failure while evaluating a parsed [`Expression`].
#[derive(Debug, Clone, PartialEq, Eq, thiserror::Error)]
pub enum EvalError {
    /// Jace `VariableNotDefinedException`.
    #[error("The variable \"{0}\" used is not defined.")]
    VariableNotDefined(String),
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum BinOp {
    Add,
    Sub,
    Mul,
    Div,
    Mod,
    Pow,
    Lt,
    Le,
    Gt,
    Ge,
    Eq,
    Ne,
    And,
    Or,
}

impl BinOp {
    fn from_char(c: char) -> Option<Self> {
        Some(match c {
            '+' => Self::Add,
            '-' => Self::Sub,
            '*' => Self::Mul,
            '/' => Self::Div,
            '%' => Self::Mod,
            '^' => Self::Pow,
            '<' => Self::Lt,
            '≤' => Self::Le,
            '>' => Self::Gt,
            '≥' => Self::Ge,
            '=' => Self::Eq,
            '≠' => Self::Ne,
            '&' => Self::And,
            '|' => Self::Or,
            _ => return None,
        })
    }

    fn apply(self, a: f64, b: f64) -> f64 {
        let bool_value = |v: bool| if v { 1.0 } else { 0.0 };
        match self {
            Self::Add => a + b,
            Self::Sub => a - b,
            Self::Mul => a * b,
            Self::Div => a / b,
            Self::Mod => a % b,
            Self::Pow => a.powf(b),
            Self::Lt => bool_value(a < b),
            Self::Le => bool_value(a <= b),
            Self::Gt => bool_value(a > b),
            Self::Ge => bool_value(a >= b),
            Self::Eq => bool_value(a == b),
            Self::Ne => bool_value(a != b),
            Self::And => bool_value(a != 0.0 && b != 0.0),
            Self::Or => bool_value(a != 0.0 || b != 0.0),
        }
    }
}

/// Jace `AstBuilder.operationPrecedence` (`'_'` is Jace's internal unary minus).
fn precedence(op: char) -> u8 {
    match op {
        '&' | '|' => 1,
        '<' | '>' | '≤' | '≥' | '≠' | '=' => 2,
        '+' | '-' => 3,
        '*' | '/' | '%' => 4,
        '^' => 5,
        _ => 6, // '_'
    }
}

/// Jace `AstBuilder.IsLeftAssociativeOperation`.
fn is_left_associative(op: char) -> bool {
    matches!(op, '*' | '+' | '-' | '/')
}

/// The functions known to the engine: Jace's built-ins plus the four registered by
/// `FormulaManager.Load`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum Func {
    Sin,
    Cos,
    Csc,
    Sec,
    Asin,
    Acos,
    Tan,
    Cot,
    Atan,
    Acot,
    Loge,
    Log10,
    Logn,
    Sqrt,
    Abs,
    If,
    IfLess,
    IfMore,
    IfEqual,
    Ceiling,
    Floor,
    Truncate,
    Round,
    Random,
    Max,
    Min,
    Avg,
    Median,
    Clamp,
    IfNegative,
    IfPositive,
    IfZero,
}

impl Func {
    const ALL: [(&'static str, Func); 32] = [
        ("sin", Func::Sin),
        ("cos", Func::Cos),
        ("csc", Func::Csc),
        ("sec", Func::Sec),
        ("asin", Func::Asin),
        ("acos", Func::Acos),
        ("tan", Func::Tan),
        ("cot", Func::Cot),
        ("atan", Func::Atan),
        ("acot", Func::Acot),
        ("loge", Func::Loge),
        ("log10", Func::Log10),
        ("logn", Func::Logn),
        ("sqrt", Func::Sqrt),
        ("abs", Func::Abs),
        ("if", Func::If),
        ("ifless", Func::IfLess),
        ("ifmore", Func::IfMore),
        ("ifequal", Func::IfEqual),
        ("ceiling", Func::Ceiling),
        ("floor", Func::Floor),
        ("truncate", Func::Truncate),
        ("round", Func::Round),
        ("random", Func::Random),
        ("max", Func::Max),
        ("min", Func::Min),
        ("avg", Func::Avg),
        ("median", Func::Median),
        ("clamp", Func::Clamp),
        ("if_negative", Func::IfNegative),
        ("if_positive", Func::IfPositive),
        ("if_zero", Func::IfZero),
    ];

    /// Jace's function registry lookup, which ignores case.
    fn lookup(name: &str) -> Option<Func> {
        Self::ALL
            .iter()
            .find(|(n, _)| n.eq_ignore_ascii_case(name))
            .map(|&(_, f)| f)
    }

    /// The fixed parameter count, or `None` for Jace's dynamic (`params double[]`) functions.
    fn arity(self) -> Option<usize> {
        Some(match self {
            Self::Random => 0,
            Self::Sin
            | Self::Cos
            | Self::Csc
            | Self::Sec
            | Self::Asin
            | Self::Acos
            | Self::Tan
            | Self::Cot
            | Self::Atan
            | Self::Acot
            | Self::Loge
            | Self::Log10
            | Self::Sqrt
            | Self::Abs
            | Self::Ceiling
            | Self::Floor
            | Self::Truncate
            | Self::Round => 1,
            Self::Logn => 2,
            Self::If | Self::Clamp | Self::IfNegative | Self::IfPositive | Self::IfZero => 3,
            Self::IfLess | Self::IfMore | Self::IfEqual => 4,
            Self::Max | Self::Min | Self::Avg | Self::Median => return None,
        })
    }

    fn apply(self, a: &[f64]) -> f64 {
        match self {
            Self::Sin => a[0].sin(),
            Self::Cos => a[0].cos(),
            Self::Csc => 1.0 / a[0].sin(),
            Self::Sec => 1.0 / a[0].cos(),
            Self::Asin => a[0].asin(),
            Self::Acos => a[0].acos(),
            Self::Tan => a[0].tan(),
            Self::Cot => 1.0 / a[0].tan(),
            Self::Atan => a[0].atan(),
            Self::Acot => (1.0 / a[0]).atan(),
            Self::Loge => a[0].ln(),
            Self::Log10 => a[0].log10(),
            Self::Logn => dotnet_log(a[0], a[1]),
            Self::Sqrt => a[0].sqrt(),
            Self::Abs => a[0].abs(),
            Self::If => {
                if a[0] != 0.0 {
                    a[1]
                } else {
                    a[2]
                }
            }
            Self::IfLess => {
                if a[0] < a[1] {
                    a[2]
                } else {
                    a[3]
                }
            }
            Self::IfMore => {
                if a[0] > a[1] {
                    a[2]
                } else {
                    a[3]
                }
            }
            Self::IfEqual => {
                if a[0] == a[1] {
                    a[2]
                } else {
                    a[3]
                }
            }
            Self::Ceiling => a[0].ceil(),
            Self::Floor => a[0].floor(),
            Self::Truncate => a[0].trunc(),
            Self::Round => a[0].round_ties_even(),
            Self::Random => next_random(),
            Self::Max => linq_max(a),
            Self::Min => linq_min(a),
            // `Enumerable.Average`: a sequential sum starting at +0.0.
            Self::Avg => a.iter().fold(0.0, |sum, x| sum + x) / a.len() as f64,
            Self::Median => median(a),
            // FormulaManager.Load: (a, b, c) => a < b ? b : a > c ? c : a
            Self::Clamp => {
                if a[0] < a[1] {
                    a[1]
                } else if a[0] > a[2] {
                    a[2]
                } else {
                    a[0]
                }
            }
            // (a, b, c) => a < 0 ? b : c
            Self::IfNegative => {
                if a[0] < 0.0 {
                    a[1]
                } else {
                    a[2]
                }
            }
            // (a, b, c) => a > 0 ? b : c
            Self::IfPositive => {
                if a[0] > 0.0 {
                    a[1]
                } else {
                    a[2]
                }
            }
            // (a, b, c) => a == 0 ? b : c
            Self::IfZero => {
                if a[0] == 0.0 {
                    a[1]
                } else {
                    a[2]
                }
            }
        }
    }
}

/// `System.Math.Log(a, newBase)`.
fn dotnet_log(a: f64, new_base: f64) -> f64 {
    if a.is_nan() {
        return a;
    }
    if new_base.is_nan() {
        return new_base;
    }
    if new_base == 1.0 {
        return f64::NAN;
    }
    if a != 1.0 && (new_base == 0.0 || new_base == f64::INFINITY) {
        return f64::NAN;
    }
    a.ln() / new_base.ln()
}

/// `Enumerable.Max(IEnumerable<double>)`: NaN only wins when every value is NaN.
fn linq_max(values: &[f64]) -> f64 {
    let mut iter = values.iter().copied();
    let Some(mut value) = iter.next() else {
        return f64::NAN;
    };
    while value.is_nan() {
        match iter.next() {
            Some(next) => value = next,
            None => return value,
        }
    }
    for x in iter {
        if x > value {
            value = x;
        }
    }
    value
}

/// `Enumerable.Min(IEnumerable<double>)`: any NaN wins.
fn linq_min(values: &[f64]) -> f64 {
    let mut iter = values.iter().copied();
    let Some(mut value) = iter.next() else {
        return f64::NAN;
    };
    if value.is_nan() {
        return value;
    }
    for x in iter {
        if x < value {
            value = x;
        } else if x.is_nan() {
            return x;
        }
    }
    value
}

/// .NET `Double.CompareTo`: NaN sorts first, `-0.0 == 0.0`.
fn dotnet_compare(a: f64, b: f64) -> std::cmp::Ordering {
    use std::cmp::Ordering;
    if a < b {
        Ordering::Less
    } else if a > b {
        Ordering::Greater
    } else if a == b {
        Ordering::Equal
    } else if a.is_nan() {
        if b.is_nan() {
            Ordering::Equal
        } else {
            Ordering::Less
        }
    } else {
        Ordering::Greater
    }
}

/// Jace `MathExtended.Median`: `NthOrderStatistic(list, (count - 1) / 2)`, a quickselect with a
/// Lomuto partition around the last element (no random pivot). Reproduced exactly because it
/// decides which of several equal-comparing values (`-0.0` / `0.0`) is returned.
fn median(values: &[f64]) -> f64 {
    let mut list = values.to_vec();
    if list.is_empty() {
        return f64::NAN;
    }
    let n = (list.len() as isize - 1) / 2;
    let (mut start, mut end) = (0isize, list.len() as isize - 1);
    loop {
        // MathExtended.Partition(list, start, end, rnd: null)
        let pivot = list[end as usize];
        let mut last_low = start - 1;
        for i in start..end {
            if dotnet_compare(list[i as usize], pivot) != std::cmp::Ordering::Greater {
                last_low += 1;
                list.swap(i as usize, last_low as usize);
            }
        }
        last_low += 1;
        list.swap(end as usize, last_low as usize);

        let pivot_index = last_low;
        if pivot_index == n {
            return list[pivot_index as usize];
        }
        if n < pivot_index {
            end = pivot_index - 1;
        } else {
            start = pivot_index + 1;
        }
    }
}

thread_local! {
    static RANDOM_STATE: Cell<u64> = Cell::new({
        let mut hasher = RandomState::new().build_hasher();
        hasher.write_u64(0x9E37_79B9_7F4A_7C15);
        hasher.finish()
    });
}

/// `random()`: uniform in `[0, 1)` (SplitMix64; Jace uses `System.Random.NextDouble`).
fn next_random() -> f64 {
    RANDOM_STATE.with(|state| {
        let s = state.get().wrapping_add(0x9E37_79B9_7F4A_7C15);
        state.set(s);
        let mut z = s;
        z = (z ^ (z >> 30)).wrapping_mul(0xBF58_476D_1CE4_E5B9);
        z = (z ^ (z >> 27)).wrapping_mul(0x94D0_49BB_1331_11EB);
        z ^= z >> 31;
        (z >> 11) as f64 * (1.0 / (1u64 << 53) as f64)
    })
}

/// Jace's constant registry (`e`, `pi`), consulted case-insensitively when a variable is not in
/// the parameter map.
fn constant(name: &str) -> Option<f64> {
    if name.eq_ignore_ascii_case("e") {
        Some(std::f64::consts::E)
    } else if name.eq_ignore_ascii_case("pi") {
        Some(std::f64::consts::PI)
    } else {
        None
    }
}

/// Jace AST node (`Operation` subclasses).
#[derive(Debug, Clone, PartialEq)]
enum Node {
    /// `IntegerConstant` (`integer == true`) or `FloatingPointConstant`; the distinction matters
    /// to the optimizer's `x * 0.0` rule.
    Const {
        value: f64,
        integer: bool,
    },
    Var(Box<str>),
    Neg(Box<Node>),
    Binary(BinOp, Box<Node>, Box<Node>),
    Call(Func, Vec<Node>),
}

impl Node {
    fn float(value: f64) -> Node {
        Node::Const {
            value,
            integer: false,
        }
    }

    /// `Operation.DependsOnVariables`.
    fn depends_on_variables(&self) -> bool {
        match self {
            Node::Const { .. } => false,
            Node::Var(_) => true,
            Node::Neg(a) => a.depends_on_variables(),
            Node::Binary(_, a, b) => a.depends_on_variables() || b.depends_on_variables(),
            Node::Call(_, args) => args.iter().any(Node::depends_on_variables),
        }
    }

    /// `Operation.IsIdempotent` (only `random` is not).
    fn is_idempotent(&self) -> bool {
        match self {
            Node::Const { .. } | Node::Var(_) => true,
            Node::Neg(a) => a.is_idempotent(),
            Node::Binary(_, a, b) => a.is_idempotent() && b.is_idempotent(),
            Node::Call(f, args) => *f != Func::Random && args.iter().all(Node::is_idempotent),
        }
    }

    fn is_float_zero(&self) -> bool {
        matches!(self, Node::Const { value, integer: false } if *value == 0.0)
    }

    fn eval(&self, lookup: &dyn Fn(&str) -> Option<f64>) -> Result<f64, EvalError> {
        Ok(match self {
            Node::Const { value, .. } => *value,
            Node::Var(name) => lookup(name)
                .or_else(|| constant(name))
                .ok_or_else(|| EvalError::VariableNotDefined(name.to_string()))?,
            Node::Neg(a) => -a.eval(lookup)?,
            Node::Binary(op, a, b) => {
                let a = a.eval(lookup)?;
                let b = b.eval(lookup)?;
                op.apply(a, b)
            }
            Node::Call(f, args) => {
                // Every argument is evaluated, like the C# delegate invocation.
                let mut values = [0.0f64; 4];
                if args.len() <= values.len() {
                    for (slot, arg) in values.iter_mut().zip(args) {
                        *slot = arg.eval(lookup)?;
                    }
                    f.apply(&values[..args.len()])
                } else {
                    let values = args
                        .iter()
                        .map(|arg| arg.eval(lookup))
                        .collect::<Result<Vec<_>, _>>()?;
                    f.apply(&values)
                }
            }
        })
    }
}

/// Jace `Optimizer.Optimize`.
fn optimize(node: Node) -> Node {
    if !matches!(node, Node::Const { .. }) && !node.depends_on_variables() && node.is_idempotent() {
        let value = node
            .eval(&|_| None)
            .expect("variable-free expressions always evaluate");
        return Node::float(value);
    }
    match node {
        Node::Binary(BinOp::Mul, a, b) => {
            let (a, b) = (optimize(*a), optimize(*b));
            if a.is_float_zero() || b.is_float_zero() {
                Node::float(0.0)
            } else {
                Node::Binary(BinOp::Mul, Box::new(a), Box::new(b))
            }
        }
        Node::Binary(op @ (BinOp::Add | BinOp::Sub | BinOp::Div | BinOp::Pow), a, b) => {
            Node::Binary(op, Box::new(optimize(*a)), Box::new(optimize(*b)))
        }
        Node::Call(f, args) => Node::Call(f, args.into_iter().map(optimize).collect()),
        other => other,
    }
}

#[derive(Debug, Clone, PartialEq)]
enum TokenKind {
    Integer(f64),
    FloatingPoint(f64),
    Text(String),
    /// Binary operator, or `'_'` for unary minus.
    Operation(char),
    LeftBracket,
    RightBracket,
    ArgumentSeparator,
}

#[derive(Debug, Clone, PartialEq)]
struct Token {
    kind: TokenKind,
    position: usize,
}

/// Jace `TokenReader.IsPartOfNumeric`.
fn is_part_of_numeric(c: char, first: bool, formula_sub_part: bool) -> bool {
    c == '.'
        || c.is_ascii_digit()
        || (formula_sub_part && first && c == '-')
        || (!first && (c == 'e' || c == 'E'))
}

/// Jace `TokenReader.IsPartOfVariable`.
fn is_part_of_variable(c: char, first: bool) -> bool {
    c.is_ascii_alphabetic() || (!first && (c.is_ascii_digit() || c == '_'))
}

/// Jace `TokenReader.Read` (`argumentSeparator = ','`, invariant culture).
fn tokenize(text: &str) -> Result<Vec<Token>, ParseError> {
    let chars: Vec<char> = text.chars().collect();
    let n = chars.len();
    let mut tokens: Vec<Token> = Vec::new();
    let mut formula_sub_part = true;
    // A field of the reader in Jace: only reset by a successfully parsed floating point number.
    let mut is_scientific = false;
    let mut i = 0;
    while i < n {
        if is_part_of_numeric(chars[i], true, formula_sub_part) {
            let start = i;
            let mut buffer = String::new();
            buffer.push(chars[i]);
            i += 1;
            while i < n && is_part_of_numeric(chars[i], false, formula_sub_part) {
                let c = chars[i];
                let scientific = c == 'e' || c == 'E';
                if is_scientific && scientific {
                    return Err(ParseError::InvalidToken {
                        token: c,
                        position: i,
                    });
                }
                if scientific {
                    is_scientific = true;
                    match chars.get(i + 1) {
                        None => return Err(ParseError::IncompleteExponent { position: start }),
                        Some('-') => {
                            buffer.push(c);
                            i += 1;
                        }
                        Some(_) => {}
                    }
                }
                buffer.push(chars[i]);
                i += 1;
            }

            if let Ok(value) = buffer.parse::<i32>() {
                tokens.push(Token {
                    kind: TokenKind::Integer(f64::from(value)),
                    position: start,
                });
                formula_sub_part = false;
            } else if let Ok(value) = buffer.parse::<f64>() {
                tokens.push(Token {
                    kind: TokenKind::FloatingPoint(value),
                    position: start,
                });
                is_scientific = false;
                formula_sub_part = false;
            } else if buffer == "-" {
                tokens.push(Token {
                    kind: TokenKind::Operation('_'),
                    position: start,
                });
            }
            // Anything else is silently skipped, exactly like Jace.

            if i == n {
                break;
            }
        }

        // Jace falls through: the character that ended a number is examined in the same pass.
        if is_part_of_variable(chars[i], true) {
            let start = i;
            let mut buffer = String::new();
            buffer.push(chars[i]);
            i += 1;
            while i < n && is_part_of_variable(chars[i], false) {
                buffer.push(chars[i]);
                i += 1;
            }
            tokens.push(Token {
                kind: TokenKind::Text(buffer),
                position: start,
            });
            formula_sub_part = false;
            if i == n {
                break;
            }
        }

        let c = chars[i];
        let next = chars.get(i + 1).copied();
        let push_op = |op: char, tokens: &mut Vec<Token>| {
            tokens.push(Token {
                kind: TokenKind::Operation(op),
                position: i,
            });
        };
        match c {
            ',' => {
                tokens.push(Token {
                    kind: TokenKind::ArgumentSeparator,
                    position: i,
                });
                formula_sub_part = false;
            }
            ' ' => {}
            '+' | '-' | '*' | '/' | '^' | '%' | '≤' | '≥' | '≠' => {
                let op = if c == '-' {
                    let previous = tokens
                        .last()
                        .ok_or(ParseError::UnexpectedMinus { position: i })?;
                    let binary = matches!(
                        previous.kind,
                        TokenKind::Integer(_)
                            | TokenKind::FloatingPoint(_)
                            | TokenKind::Text(_)
                            | TokenKind::RightBracket
                    );
                    if binary {
                        '-'
                    } else {
                        '_'
                    }
                } else {
                    c
                };
                push_op(op, &mut tokens);
                formula_sub_part = true;
            }
            '(' => {
                tokens.push(Token {
                    kind: TokenKind::LeftBracket,
                    position: i,
                });
                formula_sub_part = true;
            }
            ')' => {
                tokens.push(Token {
                    kind: TokenKind::RightBracket,
                    position: i,
                });
                formula_sub_part = false;
            }
            '<' | '>' => {
                if next == Some('=') {
                    push_op(if c == '<' { '≤' } else { '≥' }, &mut tokens);
                    i += 1;
                } else {
                    push_op(c, &mut tokens);
                }
                // Unlike the arithmetic operators, Jace resets the flag here, so a following
                // `-` is an operator token, never part of a negative literal.
                formula_sub_part = false;
            }
            '!' | '=' | '&' | '|' => {
                let (second, op) = match c {
                    '!' => ('=', '≠'),
                    '=' => ('=', '='),
                    _ => (c, c),
                };
                if next != Some(second) {
                    return Err(ParseError::InvalidToken {
                        token: c,
                        position: i,
                    });
                }
                push_op(op, &mut tokens);
                i += 1;
                formula_sub_part = false;
            }
            _ => {
                return Err(ParseError::InvalidToken {
                    token: c,
                    position: i,
                })
            }
        }
        i += 1;
    }
    Ok(tokens)
}

#[derive(Debug)]
enum StackEntry {
    Operation(char, usize),
    Function(Func, String, usize),
    LeftBracket(usize),
}

/// Jace `AstBuilder` (shunting-yard, including its leniencies).
struct AstBuilder {
    result: Vec<Node>,
    operators: Vec<StackEntry>,
    parameter_count: Vec<usize>,
}

impl AstBuilder {
    fn build(tokens: Vec<Token>) -> Result<Node, ParseError> {
        let mut builder = AstBuilder {
            result: Vec::new(),
            operators: Vec::new(),
            parameter_count: Vec::new(),
        };
        for token in tokens {
            let position = token.position;
            match token.kind {
                TokenKind::Integer(value) => builder.result.push(Node::Const {
                    value,
                    integer: true,
                }),
                TokenKind::FloatingPoint(value) => builder.result.push(Node::float(value)),
                TokenKind::Text(name) => match Func::lookup(&name) {
                    Some(f) => {
                        builder
                            .operators
                            .push(StackEntry::Function(f, name, position));
                        builder.parameter_count.push(1);
                    }
                    None => builder.result.push(Node::Var(name.into_boxed_str())),
                },
                TokenKind::LeftBracket => builder.operators.push(StackEntry::LeftBracket(position)),
                TokenKind::RightBracket => builder.pop_operations(Some(&token.kind), position)?,
                TokenKind::ArgumentSeparator => {
                    builder.pop_operations(Some(&token.kind), position)?;
                    let count = builder
                        .parameter_count
                        .pop()
                        .ok_or(ParseError::UnexpectedArgumentSeparator { position })?;
                    builder.parameter_count.push(count + 1);
                }
                TokenKind::Operation(op) => {
                    while let Some(top) = builder.operators.last() {
                        match *top {
                            StackEntry::Operation(top_op, _) => {
                                let pop = (is_left_associative(op)
                                    && precedence(op) <= precedence(top_op))
                                    || precedence(op) < precedence(top_op);
                                if !pop {
                                    break;
                                }
                                builder.pop_one()?;
                            }
                            // A function on top of the stack is always reduced first.
                            StackEntry::Function(..) => builder.pop_one()?,
                            StackEntry::LeftBracket(_) => break,
                        }
                    }
                    builder.operators.push(StackEntry::Operation(op, position));
                }
            }
        }
        builder.pop_operations(None, 0)?;
        builder.verify_result()
    }

    /// Reduces the operator / function on top of the operator stack.
    fn pop_one(&mut self) -> Result<(), ParseError> {
        match self.operators.pop() {
            Some(StackEntry::Operation(op, position)) => {
                let error = ParseError::OperationArguments {
                    operation: op,
                    position,
                };
                let node = if op == '_' {
                    Node::Neg(Box::new(self.result.pop().ok_or(error)?))
                } else {
                    if self.result.len() < 2 {
                        return Err(error);
                    }
                    let b = self.result.pop().expect("checked length");
                    let a = self.result.pop().expect("checked length");
                    let op = BinOp::from_char(op).expect("tokenizer only emits known operators");
                    Node::Binary(op, Box::new(a), Box::new(b))
                };
                self.result.push(node);
            }
            Some(StackEntry::Function(f, name, position)) => {
                let count = self.parameter_count.pop().unwrap_or(0);
                // Fixed-arity functions ignore the counted arguments (`sin(1,2)` reduces as
                // `1 sin(2)` and then fails on the leftover constant).
                let wanted = f.arity().unwrap_or(count);
                if self.result.len() < wanted {
                    return Err(ParseError::FunctionArguments {
                        function: name,
                        position,
                    });
                }
                let args = self.result.split_off(self.result.len() - wanted);
                self.result.push(Node::Call(f, args));
            }
            Some(StackEntry::LeftBracket(_)) | None => {
                unreachable!("pop_one is only called for operations and functions")
            }
        }
        Ok(())
    }

    /// Jace `AstBuilder.PopOperations`: `current` is the right bracket / argument separator being
    /// processed, `None` at the end of the formula.
    fn pop_operations(
        &mut self,
        current: Option<&TokenKind>,
        position: usize,
    ) -> Result<(), ParseError> {
        while matches!(
            self.operators.last(),
            Some(StackEntry::Operation(..) | StackEntry::Function(..))
        ) {
            self.pop_one()?;
        }
        let until_left_bracket = current == Some(&TokenKind::RightBracket);
        match self.operators.last() {
            Some(StackEntry::LeftBracket(_)) if until_left_bracket => {
                self.operators.pop();
            }
            _ if until_left_bracket => {
                return Err(ParseError::NoMatchingLeftBracket { position });
            }
            Some(StackEntry::LeftBracket(left)) if current.is_none() => {
                return Err(ParseError::NoMatchingRightBracket { position: *left });
            }
            _ => {}
        }
        Ok(())
    }

    /// Jace `AstBuilder.VerifyResultStack`.
    fn verify_result(mut self) -> Result<Node, ParseError> {
        if self.result.len() > 1 {
            // Stack.ToArray() is top-first; Jace skips the top element.
            for node in self.result.iter().rev().skip(1) {
                if let Node::Const { value, integer } = *node {
                    return Err(if integer {
                        ParseError::UnexpectedIntegerConstant(value)
                    } else {
                        ParseError::UnexpectedFloatingPointConstant(value)
                    });
                }
            }
            return Err(ParseError::InvalidSyntax);
        }
        self.result.pop().ok_or(ParseError::Empty)
    }
}

/// A formula compiled once by [`Expression::parse`] (Jace `CalculationEngine.Build`) and
/// evaluated many times with [`Expression::evaluate`].
#[derive(Debug, Clone, PartialEq)]
pub struct Expression {
    root: Node,
}

impl Expression {
    /// `CalculationEngine.Build(formulaText)` with the `FormulaManager.Load` options
    /// (optimizer on, case-sensitive variables, invariant culture).
    pub fn parse(text: &str) -> Result<Expression, ParseError> {
        let tokens = tokenize(text)?;
        let root = optimize(AstBuilder::build(tokens)?);
        Ok(Expression { root })
    }

    /// Runs the compiled formula. Fails only when a variable is neither in `params` (looked up
    /// case-sensitively) nor one of the constants `e` / `pi`.
    pub fn evaluate<K, S>(&self, params: &HashMap<K, f64, S>) -> Result<f64, EvalError>
    where
        K: Borrow<str> + Eq + Hash,
        S: BuildHasher,
    {
        self.root.eval(&|name| params.get(name).copied())
    }
}

// ---------------------------------------------------------------------------------------------
// Formula models
// ---------------------------------------------------------------------------------------------

/// `Formula`: a database formula plus its compiled expression.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct Formula {
    /// `Formula.Id` (0 for formulas built with [`Formula::new`]).
    pub id: u32,
    /// `Formula.TextFormula`.
    pub text_formula: String,
    /// `Formula.Expression` (`None` until [`Formula::prepare`] succeeds).
    expression: Option<Expression>,
}

impl Formula {
    /// `new Formula(string formula)`: sets the text and calls `Prepare()` (whose failure is only
    /// logged, as in the C#).
    pub fn new(text_formula: impl Into<String>) -> Formula {
        let mut formula = Formula::unprepared(0, text_formula);
        formula.prepare();
        formula
    }

    /// `new Formula { Id = id, TextFormula = text }` without calling `Prepare()`.
    pub fn unprepared(id: u32, text_formula: impl Into<String>) -> Formula {
        Formula {
            id,
            text_formula: text_formula.into(),
            expression: None,
        }
    }

    /// `Formula.Prepare()`: compiles [`Formula::text_formula`]; on a syntax error logs it and
    /// returns `false`.
    pub fn prepare(&mut self) -> bool {
        match Expression::parse(&self.text_formula) {
            Ok(expression) => {
                self.expression = Some(expression);
                true
            }
            Err(e) => {
                tracing::error!(
                    "Formula {} : {} has syntax errors: ",
                    self.id,
                    self.text_formula
                );
                tracing::error!("{e}");
                false
            }
        }
    }

    /// Whether [`Formula::prepare`] has succeeded.
    pub fn is_prepared(&self) -> bool {
        self.expression.is_some()
    }

    /// The compiled expression, if prepared.
    pub fn expression(&self) -> Option<&Expression> {
        self.expression.as_ref()
    }

    /// `Formula.Evaluate(parameters)`: on any evaluation error (in practice a variable missing
    /// from `params`) logs the formula, the parameters and the error, and returns `0`.
    ///
    /// Deviation: the C# would throw `ArgumentNullException` out of `lock (Expression)` for a
    /// formula whose `Prepare()` failed (only possible for `new Formula(text)`, e.g. item
    /// templates' `formula_dps`); here that is logged and `0` is returned.
    pub fn evaluate<K, S>(&self, params: &HashMap<K, f64, S>) -> f64
    where
        K: Borrow<str> + Eq + Hash,
        S: BuildHasher,
    {
        let Some(expression) = &self.expression else {
            tracing::error!(
                "Formula {} : {} was never successfully prepared",
                self.id,
                self.text_formula
            );
            return 0.0;
        };
        match expression.evaluate(params) {
            Ok(value) => value,
            Err(e) => {
                let mut sb = String::new();
                for (key, value) in params {
                    let _ = writeln!(sb, "{}: {}", key.borrow(), value);
                }
                tracing::error!("Error in formula {}:\n{}", self.id, self.text_formula);
                tracing::error!("Parameters:\n{}", sb);
                tracing::error!("{e}");
                0.0
            }
        }
    }
}

/// `UnitFormula : Formula` (`unit_formulas` row).
#[derive(Debug, Clone, PartialEq)]
pub struct UnitFormula {
    pub formula: Formula,
    /// `UnitFormula.Kind`, raw (`kind_id`); see [`UnitFormula::kind`].
    pub kind_id: u8,
    /// `UnitFormula.Owner`, raw (`owner_type_id`); see [`UnitFormula::owner`].
    pub owner_type_id: u8,
}

impl UnitFormula {
    /// `UnitFormula.Kind` (`None` for a kind the C# enum does not name).
    pub fn kind(&self) -> Option<UnitFormulaKind> {
        UnitFormulaKind::from_u8(self.kind_id)
    }

    /// `UnitFormula.Owner`.
    pub fn owner(&self) -> Option<FormulaOwnerType> {
        FormulaOwnerType::from_u8(self.owner_type_id)
    }
}

impl Deref for UnitFormula {
    type Target = Formula;

    fn deref(&self) -> &Formula {
        &self.formula
    }
}

/// `UnitFormulaVariable` (`unit_formula_variables` row).
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct UnitFormulaVariable {
    pub formula_id: u32,
    /// `UnitFormulaVariable.Type`, raw (`variable_kind_id`); see [`UnitFormulaVariable::kind`].
    pub variable_kind_id: u8,
    pub key: u32,
    pub value: f32,
}

impl UnitFormulaVariable {
    /// `UnitFormulaVariable.Type`.
    pub fn kind(&self) -> Option<UnitFormulaVariableType> {
        UnitFormulaVariableType::from_u8(self.variable_kind_id)
    }
}

/// `WearableFormula : Formula` (`wearable_formulas` row).
#[derive(Debug, Clone, PartialEq)]
pub struct WearableFormula {
    pub formula: Formula,
    /// `WearableFormula.Type`, raw (`kind_id`); see [`WearableFormula::kind`].
    pub kind_id: u8,
}

impl WearableFormula {
    /// `WearableFormula.Type`.
    pub fn kind(&self) -> Option<WearableFormulaType> {
        WearableFormulaType::from_u8(self.kind_id)
    }
}

impl Deref for WearableFormula {
    type Target = Formula;

    fn deref(&self) -> &Formula {
        &self.formula
    }
}

// ---------------------------------------------------------------------------------------------
// FormulaManager
// ---------------------------------------------------------------------------------------------

/// Failure of [`FormulaManager::load`]: the exceptions `FormulaManager.Load` lets escape.
#[derive(Debug, thiserror::Error)]
pub enum FormulaLoadError {
    #[error(transparent)]
    Sqlite(#[from] rusqlite::Error),
    /// `SQLiteWrapperReader` getters on a NULL column.
    #[error("{table}.{column} is NULL")]
    NullValue {
        table: &'static str,
        column: &'static str,
    },
    /// `GetByte` / `GetInt32` overflow (`OverflowException`).
    #[error("{table}.{column} value {value} does not fit in {target}")]
    Overflow {
        table: &'static str,
        column: &'static str,
        value: i64,
        target: &'static str,
    },
    /// A column holding a BLOB where a number or text is expected.
    #[error("{table}.{column} has an unsupported storage class")]
    InvalidType {
        table: &'static str,
        column: &'static str,
    },
    /// `_unitFormulas[formula.Owner]` (`KeyNotFoundException`): the outer dictionary only holds
    /// the defined `FormulaOwnerType` values.
    #[error("unit formula {id}: owner_type_id {owner} is not a FormulaOwnerType")]
    UnknownOwner { id: u32, owner: u8 },
    /// `Dictionary.Add` of a duplicate `(owner, kind)` (`ArgumentException`).
    #[error("unit formula {id}: duplicate formula for owner {owner}, kind {kind}")]
    DuplicateUnitFormula { id: u32, owner: u8, kind: u8 },
    /// `Dictionary.Add` of a duplicate `(unit_formula_id, variable_kind_id, key)`.
    #[error("duplicate unit formula variable (formula {formula_id}, type {kind}, key {key})")]
    DuplicateUnitVariable { formula_id: u32, kind: u8, key: u32 },
    /// `Dictionary.Add` of a duplicate wearable formula type.
    #[error("wearable formula {id}: duplicate formula for type {kind}")]
    DuplicateWearableFormula { id: u32, kind: u8 },
    /// `Dictionary.Add` of a duplicate formula id.
    #[error("duplicate formula id {0}")]
    DuplicateFormula(u32),
}

/// `FormulaManager`: unit / wearable / generic formulas and the per-NPC/mate unit variables.
///
/// Maps are keyed by the raw database values so that kinds the C# enums do not name are still
/// stored (the C# casts any byte into its enums); the typed getters convert.
#[derive(Debug, Clone, PartialEq)]
pub struct FormulaManager {
    /// `_unitFormulas`: owner -> kind -> formula.
    unit_formulas: HashMap<u8, HashMap<u8, UnitFormula>>,
    /// `_wearableFormulas`.
    wearable_formulas: HashMap<u8, WearableFormula>,
    /// `_formulas`.
    formulas: HashMap<u32, Formula>,
    /// `_unitVariables`: formula id -> variable type -> key -> variable.
    unit_variables: HashMap<u32, HashMap<u8, HashMap<u32, UnitFormulaVariable>>>,
}

impl Default for FormulaManager {
    fn default() -> Self {
        Self::empty()
    }
}

impl FormulaManager {
    /// A manager with no formulas (unit tests); like the C# after `Load` on empty tables, every
    /// `FormulaOwnerType` has an (empty) unit-formula table.
    pub fn empty() -> FormulaManager {
        FormulaManager {
            unit_formulas: FormulaOwnerType::ALL
                .iter()
                .map(|owner| (owner.raw(), HashMap::new()))
                .collect(),
            wearable_formulas: HashMap::new(),
            formulas: HashMap::new(),
            unit_variables: HashMap::new(),
        }
    }

    /// `FormulaManager.Load()`: reads `unit_formulas`, `unit_formula_variables`,
    /// `wearable_formulas` and `formulas` (`SELECT *`). A formula that fails to compile is logged
    /// and skipped; duplicate keys and unknown owners abort like the C# exceptions.
    pub fn load(conn: &Connection) -> Result<FormulaManager, FormulaLoadError> {
        let mut manager = FormulaManager::empty();
        tracing::info!("Loading formulas...");

        {
            const TABLE: &str = "unit_formulas";
            let mut stmt = conn.prepare("SELECT * from unit_formulas")?;
            let mut rows = stmt.query([])?;
            while let Some(row) = rows.next()? {
                let mut formula = UnitFormula {
                    formula: Formula::unprepared(
                        read_u32(row, TABLE, "id")?,
                        read_string(row, TABLE, "formula")?,
                    ),
                    kind_id: read_u8(row, TABLE, "kind_id")?,
                    owner_type_id: read_u8(row, TABLE, "owner_type_id")?,
                };
                if !formula.formula.prepare() {
                    continue;
                }
                let id = formula.id;
                let (owner, kind) = (formula.owner_type_id, formula.kind_id);
                let by_kind = manager
                    .unit_formulas
                    .get_mut(&owner)
                    .ok_or(FormulaLoadError::UnknownOwner { id, owner })?;
                match by_kind.entry(kind) {
                    Entry::Occupied(_) => {
                        return Err(FormulaLoadError::DuplicateUnitFormula { id, owner, kind })
                    }
                    Entry::Vacant(slot) => {
                        slot.insert(formula);
                    }
                }
            }
        }

        {
            const TABLE: &str = "unit_formula_variables";
            let mut stmt = conn.prepare("SELECT * from unit_formula_variables")?;
            let mut rows = stmt.query([])?;
            while let Some(row) = rows.next()? {
                let variable = UnitFormulaVariable {
                    formula_id: read_u32(row, TABLE, "unit_formula_id")?,
                    variable_kind_id: read_u8(row, TABLE, "variable_kind_id")?,
                    key: read_u32(row, TABLE, "key")?,
                    value: read_f32(row, TABLE, "value")?,
                };
                let by_key = manager
                    .unit_variables
                    .entry(variable.formula_id)
                    .or_default()
                    .entry(variable.variable_kind_id)
                    .or_default();
                match by_key.entry(variable.key) {
                    Entry::Occupied(_) => {
                        return Err(FormulaLoadError::DuplicateUnitVariable {
                            formula_id: variable.formula_id,
                            kind: variable.variable_kind_id,
                            key: variable.key,
                        })
                    }
                    Entry::Vacant(slot) => {
                        slot.insert(variable);
                    }
                }
            }
        }

        {
            const TABLE: &str = "wearable_formulas";
            let mut stmt = conn.prepare("SELECT * from wearable_formulas")?;
            let mut rows = stmt.query([])?;
            while let Some(row) = rows.next()? {
                let id = read_u32(row, TABLE, "id")?;
                let kind = read_u8(row, TABLE, "kind_id")?;
                let mut formula = WearableFormula {
                    formula: Formula::unprepared(id, read_string(row, TABLE, "formula")?),
                    kind_id: kind,
                };
                if !formula.formula.prepare() {
                    continue;
                }
                match manager.wearable_formulas.entry(kind) {
                    Entry::Occupied(_) => {
                        return Err(FormulaLoadError::DuplicateWearableFormula { id, kind })
                    }
                    Entry::Vacant(slot) => {
                        slot.insert(formula);
                    }
                }
            }
        }

        {
            const TABLE: &str = "formulas";
            let mut stmt = conn.prepare("SELECT * from formulas")?;
            let mut rows = stmt.query([])?;
            while let Some(row) = rows.next()? {
                let mut formula = Formula::unprepared(
                    read_u32(row, TABLE, "id")?,
                    read_string(row, TABLE, "formula")?,
                );
                if !formula.prepare() {
                    continue;
                }
                match manager.formulas.entry(formula.id) {
                    Entry::Occupied(_) => {
                        return Err(FormulaLoadError::DuplicateFormula(formula.id))
                    }
                    Entry::Vacant(slot) => {
                        slot.insert(formula);
                    }
                }
            }
        }

        tracing::info!("Formulas loaded");
        Ok(manager)
    }

    /// `GetUnitFormula(owner, kind)`.
    pub fn get_unit_formula(
        &self,
        owner: FormulaOwnerType,
        kind: UnitFormulaKind,
    ) -> Option<&UnitFormula> {
        self.get_unit_formula_raw(owner.raw(), kind.raw())
    }

    /// `GetUnitFormula` with raw enum values.
    pub fn get_unit_formula_raw(&self, owner: u8, kind: u8) -> Option<&UnitFormula> {
        self.unit_formulas.get(&owner)?.get(&kind)
    }

    /// `GetUnitVariable(formulaId, type, key)`: the variable's value, `0` when missing.
    pub fn get_unit_variable(
        &self,
        formula_id: u32,
        kind: UnitFormulaVariableType,
        key: u32,
    ) -> f32 {
        self.get_unit_variable_raw(formula_id, kind.raw(), key)
    }

    /// `GetUnitVariable` with a raw variable type.
    pub fn get_unit_variable_raw(&self, formula_id: u32, kind: u8, key: u32) -> f32 {
        self.unit_variables
            .get(&formula_id)
            .and_then(|by_kind| by_kind.get(&kind))
            .and_then(|by_key| by_key.get(&key))
            .map_or(0.0, |variable| variable.value)
    }

    /// `GetWearableFormula(type)`.
    pub fn get_wearable_formula(&self, kind: WearableFormulaType) -> Option<&WearableFormula> {
        self.get_wearable_formula_raw(kind.raw())
    }

    /// `GetWearableFormula` with a raw type.
    pub fn get_wearable_formula_raw(&self, kind: u8) -> Option<&WearableFormula> {
        self.wearable_formulas.get(&kind)
    }

    /// `GetFormula(id)`.
    pub fn get_formula(&self, id: u32) -> Option<&Formula> {
        self.formulas.get(&id)
    }

    /// `GetFormula((uint)kind)`.
    pub fn get_formula_by_kind(&self, kind: FormulaKind) -> Option<&Formula> {
        self.get_formula(kind.raw())
    }
}

/// `SqliteDataReader.GetInt64` (`sqlite3_column_int64` conversions).
fn read_i64(
    row: &Row<'_>,
    table: &'static str,
    column: &'static str,
) -> Result<i64, FormulaLoadError> {
    match row.get_ref(column)? {
        ValueRef::Integer(i) => Ok(i),
        ValueRef::Real(r) => Ok(r as i64),
        ValueRef::Text(t) => {
            let text = String::from_utf8_lossy(t);
            let text = text.trim();
            Ok(text
                .parse::<i64>()
                .ok()
                .or_else(|| text.parse::<f64>().ok().map(|r| r as i64))
                .unwrap_or(0))
        }
        ValueRef::Null => Err(FormulaLoadError::NullValue { table, column }),
        ValueRef::Blob(_) => Err(FormulaLoadError::InvalidType { table, column }),
    }
}

/// `SQLiteWrapperReader.GetUInt32` = `(uint)GetInt32(column)` (checked `int` conversion first).
fn read_u32(
    row: &Row<'_>,
    table: &'static str,
    column: &'static str,
) -> Result<u32, FormulaLoadError> {
    let value = read_i64(row, table, column)?;
    let value = i32::try_from(value).map_err(|_| FormulaLoadError::Overflow {
        table,
        column,
        value,
        target: "Int32",
    })?;
    Ok(value as u32)
}

/// `SQLiteWrapperReader.GetByte` (checked `byte` conversion).
fn read_u8(
    row: &Row<'_>,
    table: &'static str,
    column: &'static str,
) -> Result<u8, FormulaLoadError> {
    let value = read_i64(row, table, column)?;
    u8::try_from(value).map_err(|_| FormulaLoadError::Overflow {
        table,
        column,
        value,
        target: "Byte",
    })
}

/// `SQLiteWrapperReader.GetFloat` = `(float)GetDouble`.
fn read_f32(
    row: &Row<'_>,
    table: &'static str,
    column: &'static str,
) -> Result<f32, FormulaLoadError> {
    match row.get_ref(column)? {
        ValueRef::Real(r) => Ok(r as f32),
        ValueRef::Integer(i) => Ok(i as f64 as f32),
        ValueRef::Text(t) => Ok(String::from_utf8_lossy(t)
            .trim()
            .parse::<f64>()
            .unwrap_or(0.0) as f32),
        ValueRef::Null => Err(FormulaLoadError::NullValue { table, column }),
        ValueRef::Blob(_) => Err(FormulaLoadError::InvalidType { table, column }),
    }
}

/// `SQLiteWrapperReader.GetString`.
fn read_string(
    row: &Row<'_>,
    table: &'static str,
    column: &'static str,
) -> Result<String, FormulaLoadError> {
    match row.get_ref(column)? {
        ValueRef::Text(t) | ValueRef::Blob(t) => Ok(String::from_utf8_lossy(t).into_owned()),
        ValueRef::Integer(i) => Ok(i.to_string()),
        ValueRef::Real(r) => Ok(r.to_string()),
        ValueRef::Null => Err(FormulaLoadError::NullValue { table, column }),
    }
}

// ---------------------------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------------------------

#[cfg(test)]
mod tests {
    use super::*;

    fn eval_with(text: &str, params: &[(&str, f64)]) -> Result<f64, EvalError> {
        let params: HashMap<&str, f64> = params.iter().copied().collect();
        Expression::parse(text)
            .unwrap_or_else(|e| panic!("{text:?} failed to parse: {e}"))
            .evaluate(&params)
    }

    fn eval(text: &str) -> f64 {
        eval_with(text, &[]).unwrap()
    }

    fn parses(text: &str) -> bool {
        Expression::parse(text).is_ok()
    }

    #[test]
    fn precedence_and_associativity() {
        assert_eq!(eval("1+2*3"), 7.0);
        assert_eq!(eval("10-4-3"), 3.0);
        assert_eq!(eval("100/10/5"), 2.0);
        assert_eq!(eval("2/2*3"), 3.0);
        assert_eq!(eval("2^3^2"), 512.0); // right-associative
        assert_eq!(eval("2^3^2^0"), 8.0);
        assert_eq!(eval("2^2*3"), 12.0);
        assert_eq!(eval("2 * (3 + 4) ^ 2"), 98.0);
        // `%` is right-associative and the incoming operator decides ties.
        assert_eq!(eval("2*3%4"), 6.0);
        assert_eq!(eval("8%3*2"), 4.0);
        assert_eq!(eval("10 / 2 % 3"), 5.0);
        assert_eq!(eval("10 % 4 / 2"), 1.0);
        assert_eq!(eval("2%5%3"), 0.0);
        assert_eq!(eval("2 ^ 3 % 5"), 3.0);
        assert_eq!(eval("2 % 3 ^ 2"), 2.0);
        assert_eq!(eval("10 - 2 % 3"), 8.0);
        assert_eq!(
            eval("1 + 2 + 3 + 4 * 5 ^ 2 ^ 0.5 - 6 / 3 % 4"),
            42.954070969341686
        );
    }

    #[test]
    fn unary_minus() {
        assert_eq!(eval("-2^2"), 4.0); // unary minus binds tighter than ^
        assert_eq!(eval("0-2^2"), -4.0);
        assert_eq!(eval_with("-x^2", &[("x", 2.0)]), Ok(4.0));
        assert_eq!(eval("2^-2"), 0.25);
        assert_eq!(eval("2^-3^2"), 512.0);
        assert_eq!(eval("2*-3"), -6.0);
        assert_eq!(eval("2--3"), 5.0);
        assert_eq!(eval("--2"), 2.0);
        assert_eq!(eval("- -2"), 2.0);
        assert_eq!(eval("2+-3*4"), -10.0);
        assert_eq!(eval("-(2+3)"), -5.0);
        assert_eq!(eval("-sin(1)"), -(1f64.sin()));
        assert_eq!(eval_with("3*-x^2", &[("x", 2.0)]), Ok(12.0));
        assert_eq!(eval("-2-2"), -4.0);
        assert!(eval("(-2)^0.5").is_nan());
        // `-0` is an integer literal (+0), `- 0` a unary minus (-0).
        assert!(eval("-0").is_sign_positive());
        assert!(eval("- 0").is_sign_negative());
        assert!(eval("--0").is_sign_positive());
        // No unary plus.
        assert!(!parses("+3"));
        assert!(!parses("2-+3"));
    }

    #[test]
    fn numbers() {
        assert_eq!(eval("2.5e3"), 2500.0);
        assert_eq!(eval("1e-2"), 0.01);
        assert_eq!(eval("2E-1"), 0.2);
        assert_eq!(eval(".5"), 0.5);
        assert_eq!(eval("5."), 5.0);
        assert_eq!(eval("-.5"), -0.5);
        assert_eq!(eval("99999999999"), 99999999999.0);
        assert_eq!(eval("1e400"), f64::INFINITY);
        assert!(!parses("1.5E+2"));
        assert!(!parses("1,5"));
        assert!(!parses("2e"));
        assert!(!parses("1e2e3"));
        // Jace silently drops malformed numbers.
        assert_eq!(eval_with("1.2.3 x", &[("x", 7.0)]), Ok(7.0));
        assert!(!parses("1.2.3"));
    }

    #[test]
    fn division_and_ieee() {
        assert_eq!(eval("1/0"), f64::INFINITY);
        assert_eq!(eval("-1/0"), f64::NEG_INFINITY);
        assert!(eval("0/0").is_nan());
        assert!(eval("10 % 0").is_nan());
        assert_eq!(eval("-7%3"), -1.0);
        assert_eq!(eval("7.5%2"), 1.5);
        assert_eq!(eval("0.1+0.2"), 0.30000000000000004);
    }

    #[test]
    fn comparisons_and_logic() {
        assert_eq!(eval("1<2"), 1.0);
        assert_eq!(eval("2<1"), 0.0);
        assert_eq!(eval("1<=1"), 1.0);
        assert_eq!(eval("1>=2"), 0.0);
        assert_eq!(eval("1==1"), 1.0);
        assert_eq!(eval("1!=1"), 0.0);
        assert_eq!(eval("1≤2"), 1.0);
        assert_eq!(eval("1≥2"), 0.0);
        assert_eq!(eval("1≠2"), 1.0);
        assert_eq!(eval("1<2&&2<3"), 1.0);
        assert_eq!(eval("2&&3"), 1.0);
        assert_eq!(eval("1&&0"), 0.0);
        assert_eq!(eval("0||5"), 1.0);
        assert_eq!(eval("0||0"), 0.0);
        assert_eq!(eval("1+1<3"), 1.0);
        // Right-associative comparisons / logic.
        assert_eq!(eval("1<2==1"), 0.0);
        assert_eq!(eval("3 > 2 > 1"), 1.0);
        assert_eq!(eval("1 < 2 < 3"), 0.0);
        assert_eq!(eval("1 || 0 && 0"), 1.0);
        assert_eq!(eval("0||1&&0"), 0.0);
        assert_eq!(eval_with("x&&1", &[("x", f64::NAN)]), Ok(1.0));
        assert_eq!(eval_with("x!=x", &[("x", f64::NAN)]), Ok(1.0));
        for bad in ["1=1", "1&0", "1|0", "!1", "1<>2", "1 = = 1", "1&&&0"] {
            assert!(!parses(bad), "{bad}");
        }
    }

    #[test]
    fn builtin_functions() {
        let close = |text: &str, expected: f64| {
            let v = eval(text);
            assert!(
                (v - expected).abs() < 1e-12,
                "{text} = {v}, expected {expected}"
            );
        };
        close("sin(0)", 0.0);
        close("cos(0)", 1.0);
        close("csc(1)", 1.1883951057781212);
        close("sec(1)", 1.8508157176809255);
        close("asin(1)", std::f64::consts::FRAC_PI_2);
        close("acos(1)", 0.0);
        close("tan(1)", 1.5574077246549023);
        close("cot(1)", 0.6420926159343306);
        close("atan(1)", std::f64::consts::FRAC_PI_4);
        close("acot(1)", std::f64::consts::FRAC_PI_4);
        close("acot(0)", std::f64::consts::FRAC_PI_2);
        close("loge(e)", 1.0);
        close("log10(100)", 2.0);
        close("logn(8,2)", 3.0);
        close("sqrt(16)", 4.0);
        close("abs(-3)", 3.0);
        assert_eq!(eval("if(1,2,3)"), 2.0);
        assert_eq!(eval("if(0,2,3)"), 3.0);
        assert_eq!(eval("if(1<2,3,4)"), 3.0);
        assert_eq!(eval("ifless(1,2,3,4)"), 3.0);
        assert_eq!(eval("ifmore(1,2,3,4)"), 4.0);
        assert_eq!(eval("ifequal(1,1,3,4)"), 3.0);
        assert_eq!(eval("ceiling(1.2)"), 2.0);
        assert_eq!(eval("floor(-1.2)"), -2.0);
        assert_eq!(eval("truncate(-1.7)"), -1.0);
        assert_eq!(eval("round(2.5)"), 2.0); // banker's rounding
        assert_eq!(eval("round(3.5)"), 4.0);
        assert_eq!(eval("round(-2.5)"), -2.0);
        assert_eq!(eval("max(1,5,3)"), 5.0);
        assert_eq!(eval("min(4,2,8)"), 2.0);
        assert_eq!(eval("max(1)"), 1.0);
        assert_eq!(eval("min(1,2,3,4,5,6,7,8,9,10,0.5)"), 0.5);
        assert_eq!(eval("avg(1,2,3,4)"), 2.5);
        assert_eq!(eval("median(1,2,3,4)"), 2.0);
        assert_eq!(eval("median(9,8,7,6,5)"), 7.0);
        assert_eq!(eval_with("max(x,1)", &[("x", f64::NAN)]), Ok(1.0));
        assert!(eval_with("min(1,x)", &[("x", f64::NAN)]).unwrap().is_nan());
        assert!(eval("logn(1,1)").is_nan());
        let r = eval("random()");
        assert!((0.0..1.0).contains(&r));
        assert_eq!(eval("e"), std::f64::consts::E);
        assert_eq!(eval("pi"), std::f64::consts::PI);
        assert_eq!(eval("PI"), std::f64::consts::PI);
        // Function names are case-insensitive.
        assert_eq!(eval("SIN(0)"), 0.0);
        assert_eq!(eval("MAX(1,2)"), 2.0);
        assert_eq!(eval("CLAMP(5,0,3)"), 3.0);
        // Arity errors and unknown functions.
        for bad in [
            "sin()",
            "sin(1,2)",
            "clamp(1,2)",
            "max()",
            "max(1,)",
            "if(1,2,3,4)",
            "logn(8)",
            "log(100)",
            "pow(2,3)",
            "exp(1)",
            "abs",
            "(1,2)",
        ] {
            assert!(!parses(bad), "{bad}");
        }
    }

    #[test]
    fn minus_after_comparisons_and_separators() {
        // After a comparison / logical operator or `,` a `-` is an operator token, so `-e` is the
        // negated constant rather than a (malformed) number.
        assert_eq!(eval("-1||-e"), 1.0);
        assert_eq!(eval("1<-e"), 0.0);
        assert_eq!(eval("max(1,-e)"), 1.0);
        assert!(!parses("1+-e")); // after `+`, "-e" is lexed as a broken number
        assert!(!parses("-e"));
        assert!(eval("max(-1,-0)").is_sign_negative());
        assert!(eval("max(-1,(-0))").is_sign_positive());
        assert!(eval("ifequal(1,2,10,--0)").is_sign_negative());
        assert!(eval("avg(--0)").is_sign_positive());
    }

    #[test]
    fn median_quickselect() {
        assert_eq!(eval("median(3,1,2)"), 2.0);
        assert_eq!(eval("median(4,1)"), 1.0);
        assert_eq!(eval("median(1,2,3,10)"), 2.0);
        assert_eq!(eval_with("median(x,1,2)", &[("x", f64::NAN)]), Ok(1.0));
        // Which zero wins depends on Jace's partitioning.
        let zeros = [("a", 0.0), ("b", -0.0), ("c", 0.0)];
        assert!(eval_with("median(-1,a,b,c,1)", &zeros)
            .unwrap()
            .is_sign_negative());
        assert!(eval_with("median(-1,b,a,c,1)", &zeros)
            .unwrap()
            .is_sign_positive());
    }

    #[test]
    fn custom_functions() {
        assert_eq!(eval("clamp(5,0,3)"), 3.0);
        assert_eq!(eval("clamp(-1,0,3)"), 0.0);
        assert_eq!(eval("clamp(2,0,3)"), 2.0);
        assert_eq!(eval("if_negative(-1,10,20)"), 10.0);
        assert_eq!(eval("if_negative(0,10,20)"), 20.0);
        assert_eq!(eval("if_positive(1,10,20)"), 10.0);
        assert_eq!(eval("if_positive(0,10,20)"), 20.0);
        assert_eq!(eval("if_zero(0,10,20)"), 10.0);
        assert_eq!(eval("if_zero(1,10,20)"), 20.0);
    }

    #[test]
    fn variables_are_case_sensitive() {
        assert_eq!(eval_with("X", &[("X", 3.0)]), Ok(3.0));
        assert_eq!(
            eval_with("x", &[("X", 3.0)]),
            Err(EvalError::VariableNotDefined("x".into()))
        );
        assert_eq!(eval_with("x+X", &[("x", 1.0), ("X", 10.0)]), Ok(11.0));
        assert_eq!(eval_with("x1_2", &[("x1_2", 7.0)]), Ok(7.0));
        // Parameters override the constants (exact name only).
        assert_eq!(eval_with("e", &[("e", 5.0)]), Ok(5.0));
        assert_eq!(eval_with("PI", &[("pi", 2.0)]), Ok(std::f64::consts::PI));
        assert!(!parses("_x"));
        assert!(!parses("2x"));
        assert!(!parses("x y"));
        assert!(!parses("1\t+\t2"));
    }

    #[test]
    fn optimizer_float_zero_multiplication() {
        // `0.0 * x` is folded to 0 even when x is missing; `0 * x` (integer) is not.
        assert_eq!(eval_with("0.0*x", &[]), Ok(0.0));
        assert_eq!(eval_with("(1-1)*x", &[]), Ok(0.0));
        assert_eq!(eval_with("x*0.0", &[("x", f64::INFINITY)]), Ok(0.0));
        assert!(eval_with("0*x", &[("x", f64::INFINITY)]).unwrap().is_nan());
        assert!(eval_with("0*x", &[]).is_err());
        assert!(eval_with("-(0.0*x)", &[]).is_err());
        assert!(eval_with("(0.0*x)%1", &[]).is_err());
        assert_eq!(eval_with("max(0.0*x,1)", &[]), Ok(1.0));
        assert!(eval("0.0*(-1)").is_sign_negative()); // fully constant: folded, not rewritten
    }

    #[test]
    fn jace_leniencies() {
        assert_eq!(eval("1 2 +"), 3.0);
        assert_eq!(eval("sin 1"), 1f64.sin());
        assert_eq!(eval("sin 1 + 2"), 1f64.sin() + 2.0);
        assert_eq!(eval("2 max()"), 2.0);
        assert_eq!(eval("max((1,2),3)"), 3.0);
        for bad in [
            "", " ", "()", "(1", "1)", "1+", "-", "2**3", "1+()", "abs -2",
        ] {
            assert!(!parses(bad), "{bad:?}");
        }
    }

    #[test]
    fn realistic_formulas() {
        let params = [("level", 50.0), ("sta", 100.0)];
        assert_eq!(
            eval_with("(level * 7 + 50) * (1 + sta / 100)", &params),
            Ok(800.0)
        );
        let f = Formula::new("clamp((level - 10) * 2.5, 0, 100) + if_zero(sta, 1, sta ^ 0.5)");
        let params: HashMap<&str, f64> = params.into_iter().collect();
        assert_eq!(f.evaluate(&params), 100.0 + 10.0);
        let f = Formula::new("floor(level / 10) * 3 + max(1, level - 54)");
        assert_eq!(f.evaluate(&params), 16.0);
    }

    #[test]
    fn formula_evaluate_returns_zero_on_errors() {
        let f = Formula::new("level * 2 + missing");
        assert!(f.is_prepared());
        let params: HashMap<&str, f64> = [("level", 3.0)].into_iter().collect();
        assert_eq!(f.evaluate(&params), 0.0);
        // String keys work too.
        let params: HashMap<String, f64> =
            [("level".to_string(), 3.0), ("missing".to_string(), 1.0)]
                .into_iter()
                .collect();
        assert_eq!(f.evaluate(&params), 7.0);

        let broken = Formula::new("level * (2");
        assert!(!broken.is_prepared());
        assert_eq!(broken.evaluate(&params), 0.0);
    }

    #[test]
    fn enums_keep_exact_values() {
        assert_eq!(UnitFormulaKind::from_u8(46), Some(UnitFormulaKind::Dodge));
        assert_eq!(UnitFormulaKind::from_u8(3), None);
        assert_eq!(UnitFormulaKind::MeleeParry.raw(), 5);
        assert_eq!(FormulaOwnerType::Shipyard.raw(), 6);
        assert_eq!(FormulaOwnerType::ALL.len(), 7);
        assert_eq!(
            UnitFormulaVariableType::try_from(5),
            Ok(UnitFormulaVariableType::MateKind)
        );
        assert_eq!(WearableFormulaType::from_u8(2), None);
        assert_eq!(
            FormulaKind::from_u32(45),
            Some(FormulaKind::BlessUthstinExtendMaxStat)
        );
        assert_eq!(u32::from(FormulaKind::CastingCancelPercent), 2);
    }

    fn test_db() -> Connection {
        let conn = Connection::open_in_memory().unwrap();
        conn.execute_batch(
            "CREATE TABLE unit_formulas (id INTEGER, formula TEXT, kind_id INTEGER, owner_type_id INTEGER);
             CREATE TABLE unit_formula_variables (id INTEGER, unit_formula_id INTEGER, variable_kind_id INTEGER, key INTEGER, value REAL);
             CREATE TABLE wearable_formulas (id INTEGER, kind_id INTEGER, formula TEXT);
             CREATE TABLE formulas (id INTEGER, formula TEXT);
             INSERT INTO unit_formulas VALUES
                 (1, '(level * 7 + 50) * (1 + sta / 100)', 14, 0),
                 (2, 'level * 2', 22, 0),
                 (3, 'level * (', 23, 0),
                 (4, 'level * 10', 14, 1),
                 (5, 'level + 1', 3, 1);
             INSERT INTO unit_formula_variables VALUES
                 (1, 4, 2, 1000, 1.5),
                 (2, 4, 4, 3, 0.25),
                 (3, 4, 9, 1, 2.0);
             INSERT INTO wearable_formulas VALUES
                 (1, 0, 'slot_factor * 10'),
                 (2, 1, 'slot_factor * 5');
             INSERT INTO formulas VALUES
                 (2, 'clamp(a, 0, 1)'),
                 (22, 'grade * 100'),
                 (99, '1 +');",
        )
        .unwrap();
        conn
    }

    #[test]
    fn load_from_sqlite() {
        let manager = FormulaManager::load(&test_db()).unwrap();

        let max_health = manager
            .get_unit_formula(FormulaOwnerType::Character, UnitFormulaKind::MaxHealth)
            .unwrap();
        assert_eq!(max_health.id, 1);
        assert_eq!(max_health.owner(), Some(FormulaOwnerType::Character));
        assert_eq!(max_health.kind(), Some(UnitFormulaKind::MaxHealth));
        let params: HashMap<&str, f64> = [("level", 50.0), ("sta", 100.0)].into_iter().collect();
        assert_eq!(max_health.evaluate(&params), 800.0);

        // The formula with a syntax error is skipped.
        assert!(manager
            .get_unit_formula(FormulaOwnerType::Character, UnitFormulaKind::Dex)
            .is_none());
        // Kinds the enum does not name are still stored.
        assert_eq!(manager.get_unit_formula_raw(1, 3).unwrap().id, 5);
        assert!(manager.get_unit_formula_raw(1, 3).unwrap().kind().is_none());
        assert!(manager
            .get_unit_formula(FormulaOwnerType::Mate, UnitFormulaKind::MaxHealth)
            .is_none());

        assert_eq!(
            manager.get_unit_variable(4, UnitFormulaVariableType::NpcTemplate, 1000),
            1.5
        );
        assert_eq!(
            manager.get_unit_variable(4, UnitFormulaVariableType::NpcGrade, 3),
            0.25
        );
        assert_eq!(manager.get_unit_variable_raw(4, 9, 1), 2.0);
        assert_eq!(
            manager.get_unit_variable(4, UnitFormulaVariableType::NpcGrade, 4),
            0.0
        );
        assert_eq!(
            manager.get_unit_variable(5, UnitFormulaVariableType::NpcKind, 1),
            0.0
        );

        let armor = manager
            .get_wearable_formula(WearableFormulaType::MaxBaseArmor)
            .unwrap();
        let params: HashMap<&str, f64> = [("slot_factor", 2.0)].into_iter().collect();
        assert_eq!(armor.evaluate(&params), 20.0);
        assert_eq!(armor.kind(), Some(WearableFormulaType::MaxBaseArmor));

        assert_eq!(
            manager
                .get_formula_by_kind(FormulaKind::GradeEnchantCost)
                .unwrap()
                .text_formula,
            "grade * 100"
        );
        assert!(manager.get_formula(2).is_some());
        assert!(manager.get_formula(99).is_none()); // failed to prepare
        assert!(manager.get_formula(3).is_none());
    }

    #[test]
    fn load_rejects_what_the_csharp_throws_on() {
        let conn = test_db();
        conn.execute("INSERT INTO unit_formulas VALUES (6, 'level', 14, 0)", [])
            .unwrap();
        assert!(matches!(
            FormulaManager::load(&conn),
            Err(FormulaLoadError::DuplicateUnitFormula {
                id: 6,
                owner: 0,
                kind: 14
            })
        ));

        let conn = test_db();
        conn.execute("INSERT INTO unit_formulas VALUES (7, 'level', 14, 7)", [])
            .unwrap();
        assert!(matches!(
            FormulaManager::load(&conn),
            Err(FormulaLoadError::UnknownOwner { id: 7, owner: 7 })
        ));
        // ...but only for formulas that compiled.
        let conn = test_db();
        conn.execute("INSERT INTO unit_formulas VALUES (7, '(', 14, 7)", [])
            .unwrap();
        assert!(FormulaManager::load(&conn).is_ok());

        let conn = test_db();
        conn.execute(
            "INSERT INTO unit_formula_variables VALUES (9, 4, 2, 1000, 3.0)",
            [],
        )
        .unwrap();
        assert!(matches!(
            FormulaManager::load(&conn),
            Err(FormulaLoadError::DuplicateUnitVariable {
                formula_id: 4,
                kind: 2,
                key: 1000
            })
        ));

        let conn = test_db();
        conn.execute("INSERT INTO wearable_formulas VALUES (3, 1, 'x')", [])
            .unwrap();
        assert!(matches!(
            FormulaManager::load(&conn),
            Err(FormulaLoadError::DuplicateWearableFormula { id: 3, kind: 1 })
        ));

        let conn = test_db();
        conn.execute("INSERT INTO formulas VALUES (22, 'x')", [])
            .unwrap();
        assert!(matches!(
            FormulaManager::load(&conn),
            Err(FormulaLoadError::DuplicateFormula(22))
        ));

        let conn = test_db();
        conn.execute("INSERT INTO unit_formulas VALUES (8, 'level', 300, 0)", [])
            .unwrap();
        assert!(matches!(
            FormulaManager::load(&conn),
            Err(FormulaLoadError::Overflow {
                column: "kind_id",
                value: 300,
                ..
            })
        ));

        let conn = Connection::open_in_memory().unwrap();
        assert!(matches!(
            FormulaManager::load(&conn),
            Err(FormulaLoadError::Sqlite(_))
        ));
    }

    #[test]
    fn empty_manager() {
        let manager = FormulaManager::empty();
        assert!(manager
            .get_unit_formula(FormulaOwnerType::Npc, UnitFormulaKind::Armor)
            .is_none());
        assert_eq!(
            manager.get_unit_variable(1, UnitFormulaVariableType::Unknown, 1),
            0.0
        );
        assert!(manager.get_formula(1).is_none());
        assert!(manager
            .get_wearable_formula(WearableFormulaType::MaxBaseMagicResistance)
            .is_none());
    }
}
