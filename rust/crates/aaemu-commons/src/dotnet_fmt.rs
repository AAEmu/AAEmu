//! .NET-compatible number formatting, for tools / files that must match the C# output byte for
//! byte.
//!
//! .NET Core 3.0+ formats `double`/`float` with `ToString()` (and `"R"`) using the shortest
//! round-trippable digits, switching to scientific notation (`1E+17`, `1E-05`) when the decimal
//! exponent is larger than `max(digit count, 17)` (`9` for `float`) or the value is below
//! `1e-4`. Rust's `{}` never uses an exponent and `{:?}` uses different thresholds, hence these
//! helpers. Formatting is culture-invariant (`.` decimal separator).

/// `double.ToString(CultureInfo.InvariantCulture)` / `ToString("R")`.
pub fn format_f64(v: f64) -> String {
    if v.is_nan() {
        return "NaN".into();
    }
    if v.is_infinite() {
        return if v > 0.0 { "Infinity" } else { "-Infinity" }.into();
    }
    let sci = ties_to_even(&format!("{v:e}"), &format!("{v:.800e}"), |s| {
        s.parse::<f64>().ok() == Some(v)
    });
    layout(&sci, 17)
}

/// `float.ToString(CultureInfo.InvariantCulture)` / `ToString("R")`.
pub fn format_f32(v: f32) -> String {
    if v.is_nan() {
        return "NaN".into();
    }
    if v.is_infinite() {
        return if v > 0.0 { "Infinity" } else { "-Infinity" }.into();
    }
    let sci = ties_to_even(&format!("{v:e}"), &format!("{:.200e}", f64::from(v)), |s| {
        s.parse::<f32>().ok() == Some(v)
    });
    layout(&sci, 9)
}

/// Splits `-1.2345e-7` into (negative, digits, exponent).
fn split_sci(sci: &str) -> (bool, Vec<u8>, i32) {
    let (mantissa, exp) = sci.split_once('e').unwrap_or((sci, "0"));
    let exp: i32 = exp.parse().unwrap_or(0);
    let (neg, mantissa) = match mantissa.strip_prefix('-') {
        Some(m) => (true, m),
        None => (false, mantissa),
    };
    (
        neg,
        mantissa.bytes().filter(u8::is_ascii_digit).collect(),
        exp,
    )
}

fn join_sci(neg: bool, digits: &[u8], exp: i32) -> String {
    let mut s = String::new();
    if neg {
        s.push('-');
    }
    s.push(char::from(digits[0]));
    if digits.len() > 1 {
        s.push('.');
        digits[1..].iter().for_each(|&d| s.push(char::from(d)));
    }
    s.push_str(&format!("e{exp}"));
    s
}

/// When the exact value lies exactly halfway between two shortest round-trippable candidates,
/// Rust's shortest formatting rounds up while .NET (Dragon4) picks the even last digit
/// (`7293.15625f` → Rust `7293.1563`, .NET `7293.1562`). Returns `shortest` adjusted to .NET.
fn ties_to_even(shortest: &str, exact: &str, round_trips: impl Fn(&str) -> bool) -> String {
    let (neg, d, exp) = split_sci(shortest);
    let (_, mut e, exp_e) = split_sci(exact);
    while e.len() > 1 && e.last() == Some(&b'0') {
        e.pop();
    }
    let n = d.len();
    if exp_e != exp || e.len() != n + 1 || e[n] != b'5' {
        return shortest.to_string();
    }
    // Tie between T (truncated) and U = T + 1 unit in the last place.
    let t = e[..n].to_vec();
    let mut u = t.clone();
    let mut i = n;
    loop {
        if i == 0 {
            return shortest.to_string(); // carry out of the leading digit; leave as is
        }
        i -= 1;
        if u[i] == b'9' {
            u[i] = b'0';
        } else {
            u[i] += 1;
            break;
        }
    }
    let even = if (t[n - 1] - b'0').is_multiple_of(2) {
        t
    } else {
        u
    };
    let mut even_trimmed = even.clone();
    while even_trimmed.len() > 1 && even_trimmed.last() == Some(&b'0') {
        even_trimmed.pop();
    }
    let candidate = join_sci(neg, &even_trimmed, exp);
    if even != d && round_trips(&candidate) {
        candidate
    } else {
        shortest.to_string()
    }
}

/// Newtonsoft.Json's representation of a `double` (`JsonConvert.ToString`): the .NET string
/// with `.0` appended when it has no `.`/exponent.
pub fn json_f64(v: f64) -> String {
    ensure_decimal_place(v.is_finite(), format_f64(v))
}

/// Newtonsoft.Json's representation of a `float`.
pub fn json_f32(v: f32) -> String {
    ensure_decimal_place(v.is_finite(), format_f32(v))
}

fn ensure_decimal_place(finite: bool, text: String) -> String {
    if !finite || text.contains(['.', 'E', 'e']) {
        text
    } else {
        text + ".0"
    }
}

/// Lays out Rust's shortest `{:e}` digits (`-1.2345e-7`) using .NET's `G` rules.
fn layout(sci: &str, max_round_trip_digits: i32) -> String {
    let (neg, digits, exp) = split_sci(sci);
    let mut out = String::new();
    if neg {
        out.push('-');
    }
    if digits.iter().all(|&d| d == b'0') {
        out.push('0');
        return out;
    }
    let n = digits.len() as i32;
    let scale = exp + 1; // value = 0.d1d2... * 10^scale
    let push_digits =
        |out: &mut String, ds: &[u8]| ds.iter().for_each(|&d| out.push(char::from(d)));
    if scale > n.max(max_round_trip_digits) || scale < -3 {
        push_digits(&mut out, &digits[..1]);
        if n > 1 {
            out.push('.');
            push_digits(&mut out, &digits[1..]);
        }
        let e = scale - 1;
        out.push('E');
        out.push(if e < 0 { '-' } else { '+' });
        out.push_str(&format!("{:02}", e.abs()));
    } else if scale > 0 {
        let int_len = scale.min(n) as usize;
        push_digits(&mut out, &digits[..int_len]);
        for _ in n..scale {
            out.push('0');
        }
        if scale < n {
            out.push('.');
            push_digits(&mut out, &digits[scale as usize..]);
        }
    } else {
        out.push_str("0.");
        for _ in 0..-scale {
            out.push('0');
        }
        push_digits(&mut out, &digits);
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Expected strings produced by .NET 10 (`ToString()` with the invariant culture).
    #[test]
    fn doubles_match_dotnet() {
        for (v, s) in [
            (1e14, "100000000000000"),
            (1e15, "1000000000000000"),
            (1e16, "10000000000000000"),
            (1e17, "1E+17"),
            (123456789012345678.0, "1.2345678901234568E+17"),
            (0.0001, "0.0001"),
            (0.00001, "1E-05"),
            (-0.0, "-0"),
            (0.0, "0"),
            (31.99951171875, "31.99951171875"),
            (1.5e-7, "1.5E-07"),
            (12345.678, "12345.678"),
            (100.0, "100"),
            (-60.0, "-60"),
            (f64::MAX, "1.7976931348623157E+308"),
            (f64::NAN, "NaN"),
            (f64::NEG_INFINITY, "-Infinity"),
        ] {
            assert_eq!(format_f64(v), s, "{v:e}");
        }
    }

    #[test]
    #[allow(clippy::excessive_precision)]
    fn floats_match_dotnet() {
        for (v, s) in [
            (1e6f32, "1000000"),
            (1e7, "10000000"),
            (1e8, "100000000"),
            (1e9, "1E+09"),
            (1e10, "1E+10"),
            (16777216.0, "16777216"),
            (0.0001, "0.0001"),
            (0.00001, "1E-05"),
            (4096.0, "4096"),
            (0.1, "0.1"),
            (123456789.0, "123456790"),
            (-0.0, "-0"),
            (3.4028235e38, "3.4028235E+38"),
            // ties between two shortest candidates go to the even digit
            (7293.15625, "7293.1562"),
            (23283.8125, "23283.812"),
            (20915.3125, "20915.312"),
        ] {
            assert_eq!(format_f32(v), s, "{v:e}");
        }
    }

    #[test]
    fn newtonsoft() {
        assert_eq!(json_f32(4096.0), "4096.0");
        assert_eq!(json_f32(-0.0), "-0.0");
        assert_eq!(json_f64(31.99951171875), "31.99951171875");
        assert_eq!(json_f64(1e17), "1E+17");
        assert_eq!(json_f64(f64::NAN), "NaN");
    }
}
