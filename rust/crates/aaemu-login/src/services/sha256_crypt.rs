//! Unix SHA-256 crypt (`$5$`) per Drepper's specification (port of `Sha256Crypt.cs`).
//!
//! **Security note:** sha256_crypt at moderate rounds is weaker than PBKDF2@600k for offline
//! attacks (at 100,000 rounds roughly 3x weaker than PBKDF2@310k). It is used only for Korea
//! challenge-response authentication; the hash itself is never sent to clients, only the salt and
//! rounds. Each account has a unique random salt, preventing rainbow table attacks.
//!
//! Differences from the reference implementation that the C# code has (and that are kept here):
//! the salt is not truncated to 16 characters, `rounds=` is always emitted, and rounds are not
//! clamped. See <https://akkadia.org/drepper/SHA-crypt.txt>.

use rand::RngCore;
use sha2::{Digest, Sha256};
use subtle::ConstantTimeEq;

const ALPHABET: &[u8; 64] = b"./0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

/// Byte-reordering groups for SHA-256: `(i2, i1, i0, char_count)` where i2 is the high byte,
/// i1 the mid byte and i0 the low byte. `i2 == None` means the high byte is zero (final group).
const BYTE_GROUPS: [(Option<usize>, usize, usize, usize); 11] = [
    (Some(0), 10, 20, 4),
    (Some(21), 1, 11, 4),
    (Some(12), 22, 2, 4),
    (Some(3), 13, 23, 4),
    (Some(24), 4, 14, 4),
    (Some(15), 25, 5, 4),
    (Some(6), 16, 26, 4),
    (Some(27), 7, 17, 4),
    (Some(18), 28, 8, 4),
    (Some(9), 19, 29, 4),
    (None, 31, 30, 3),
];

/// Default rounds when a `$5$` string has no `rounds=` field.
pub const DEFAULT_ROUNDS: i32 = 5000;

#[derive(Debug, thiserror::Error, PartialEq, Eq)]
pub enum Sha256CryptError {
    #[error("Not a SHA-256 crypt hash ($5$ prefix missing).")]
    MissingPrefix,
    #[error("Malformed $5$ hash: missing '$' after rounds.")]
    MissingRoundsSeparator,
    #[error("Malformed $5$ hash: missing '$' after salt.")]
    MissingSaltSeparator,
    #[error("Malformed $5$ hash: invalid rounds value '{0}'.")]
    InvalidRounds(String),
    #[error("Expected 43-char hash, got {0}.")]
    BadHashLength(usize),
    #[error("Invalid character '{0}' in SHA-256 crypt hash.")]
    InvalidChar(char),
}

/// Computes the raw 32-byte SHA-256 crypt hash.
pub fn compute_raw(password: &str, salt: &str, rounds: i32) -> [u8; 32] {
    let pw = password.as_bytes();
    let salt = salt.as_bytes();

    // Step 1: digest B = SHA256(password + salt + password)
    let sum_b: [u8; 32] = Sha256::new()
        .chain_update(pw)
        .chain_update(salt)
        .chain_update(pw)
        .finalize()
        .into();

    // Step 2: digest A = SHA256(password + salt + (sumB repeated to fill pwLen) + bit-alternation)
    let mut sha = Sha256::new();
    sha.update(pw);
    sha.update(salt);
    let mut rem = pw.len();
    while rem > 0 {
        sha.update(&sum_b[..rem.min(32)]);
        rem = rem.saturating_sub(32);
    }
    // Bit-alternation: process bits of password length from LSB
    let mut bits = pw.len();
    while bits > 0 {
        if bits & 1 != 0 {
            sha.update(sum_b);
        } else {
            sha.update(pw);
        }
        bits >>= 1;
    }
    let mut sum_a: [u8; 32] = sha.finalize().into();

    // Step 3: P-string = SHA256(password x pwLen), expanded to pwLen bytes
    let mut sha = Sha256::new();
    for _ in 0..pw.len() {
        sha.update(pw);
    }
    let sum_p: [u8; 32] = sha.finalize().into();
    let p_string: Vec<u8> = (0..pw.len()).map(|i| sum_p[i % 32]).collect();

    // Step 4: S-string = SHA256(salt x (16 + sumA[0])), expanded to saltLen bytes
    let mut sha = Sha256::new();
    for _ in 0..16 + usize::from(sum_a[0]) {
        sha.update(salt);
    }
    let sum_s: [u8; 32] = sha.finalize().into();
    let s_string: Vec<u8> = (0..salt.len()).map(|i| sum_s[i % 32]).collect();

    // Step 5: stretch for `rounds` iterations
    for c in 0..rounds.max(0) {
        let mut sha = Sha256::new();
        if c & 1 != 0 {
            sha.update(&p_string);
        } else {
            sha.update(sum_a);
        }
        if c % 3 != 0 {
            sha.update(&s_string);
        }
        if c % 7 != 0 {
            sha.update(&p_string);
        }
        if c & 1 != 0 {
            sha.update(sum_a);
        } else {
            sha.update(&p_string);
        }
        sum_a = sha.finalize().into();
    }
    sum_a
}

/// Formats a raw 32-byte hash into `$5$rounds=N$salt$...` (does not recompute).
pub fn format_encoded(raw: &[u8; 32], salt: &str, rounds: i32) -> String {
    let mut out = format!("$5$rounds={rounds}${salt}$");
    for (i2, i1, i0, n) in BYTE_GROUPS {
        let b2 = i2.map_or(0u32, |i| u32::from(raw[i]));
        let mut w = (b2 << 16) | (u32::from(raw[i1]) << 8) | u32::from(raw[i0]);
        for _ in 0..n {
            out.push(ALPHABET[(w & 63) as usize] as char);
            w >>= 6;
        }
    }
    out
}

/// Generates a 16-character random salt using cryptographically secure random bytes.
pub fn generate_salt() -> String {
    const SALT_CHARS: &[u8] = b"abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789./";
    let mut rng = [0u8; 16];
    rand::rngs::OsRng.fill_bytes(&mut rng);
    rng.iter()
        .map(|b| SALT_CHARS[usize::from(*b) % SALT_CHARS.len()] as char)
        .collect()
}

fn parse_int(s: &str) -> Result<i32, Sha256CryptError> {
    // int.Parse allows surrounding whitespace and a leading sign.
    s.trim()
        .parse::<i32>()
        .map_err(|_| Sha256CryptError::InvalidRounds(s.to_string()))
}

/// Parses only the rounds value from a stored `$5$rounds=N$salt$hash` string.
pub fn parse_rounds(stored: &str) -> Result<i32, Sha256CryptError> {
    let rest = stored
        .strip_prefix("$5$")
        .ok_or(Sha256CryptError::MissingPrefix)?;
    let Some(after) = rest.strip_prefix("rounds=") else {
        return Ok(DEFAULT_ROUNDS);
    };
    // The C# code searches for '$' in `rest` (which still starts with "rounds="), same result.
    let sep = after
        .find('$')
        .ok_or(Sha256CryptError::MissingRoundsSeparator)?;
    parse_int(&after[..sep])
}

/// Parses a stored `$5$rounds=N$salt$hash` string. Returns `(rounds, salt, raw_hash)`.
pub fn parse(stored: &str) -> Result<(i32, String, [u8; 32]), Sha256CryptError> {
    let mut rest = stored
        .strip_prefix("$5$")
        .ok_or(Sha256CryptError::MissingPrefix)?;
    let mut rounds = DEFAULT_ROUNDS;
    if let Some(after) = rest.strip_prefix("rounds=") {
        let sep = after
            .find('$')
            .ok_or(Sha256CryptError::MissingRoundsSeparator)?;
        rounds = parse_int(&after[..sep])?;
        rest = &after[sep + 1..];
    }
    let salt_end = rest
        .find('$')
        .ok_or(Sha256CryptError::MissingSaltSeparator)?;
    let salt = rest[..salt_end].to_string();
    let raw = decode(&rest[salt_end + 1..])?;
    Ok((rounds, salt, raw))
}

/// Verifies a plaintext password against a stored `$5$` hash string (constant-time compare).
pub fn verify(plaintext: &str, stored: &str) -> Result<bool, Sha256CryptError> {
    let (rounds, salt, expected) = parse(stored)?;
    let computed = compute_raw(plaintext, &salt, rounds);
    Ok(bool::from(computed.ct_eq(&expected)))
}

/// Decodes the 43-character base64 portion of a `$5$` hash.
fn decode(hash: &str) -> Result<[u8; 32], Sha256CryptError> {
    let chars: Vec<char> = hash.chars().collect();
    if chars.len() != 43 {
        // C# compares the UTF-16 length; identical for the ASCII alphabet.
        return Err(Sha256CryptError::BadHashLength(hash.encode_utf16().count()));
    }
    let mut out = [0u8; 32];
    let mut idx = 0;
    for (i2, i1, i0, n) in BYTE_GROUPS {
        let mut w = 0u32;
        for k in 0..n {
            let c = chars[idx];
            idx += 1;
            let v = ALPHABET
                .iter()
                .position(|&a| a as char == c)
                .ok_or(Sha256CryptError::InvalidChar(c))?;
            w |= (v as u32) << (6 * k);
        }
        if let Some(i) = i2 {
            out[i] = (w >> 16) as u8;
        }
        out[i1] = (w >> 8) as u8;
        out[i0] = w as u8;
    }
    Ok(out)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn enc(pw: &str, salt: &str, rounds: i32) -> String {
        format_encoded(&compute_raw(pw, salt, rounds), salt, rounds)
    }

    #[test]
    fn drepper_vector() {
        assert_eq!(
            enc("Hello world!", "saltstringsaltst", 10000),
            "$5$rounds=10000$saltstringsaltst$3xv.VbSHBb41AL9AvLeujZkZRBAwqFMz2.opqey6IcA"
        );
    }

    #[test]
    fn matches_csharp_outputs() {
        // Produced by the C# Sha256Crypt.ComputeRaw + FormatEncoded.
        assert_eq!(
            enc("KrTesting", "abcdEFGH12345678", 1000),
            "$5$rounds=1000$abcdEFGH12345678$G6P9sA2.z7nziwWaXQ1RT3LEjLPlckoVWmixUb7Q0Y7"
        );
        assert_eq!(
            hex::encode(compute_raw("KrTesting", "abcdEFGH12345678", 1000)),
            "2d437f9a375fc54ca270b238ff2f2341b528f6991200322e7471ef87ece00299"
        );
        assert_eq!(
            enc("", "", 1000),
            "$5$rounds=1000$$jAQ1NZgJkatmWyAsK0NxDcSXTB41aRCrTggwkNbBkXA"
        );
        assert_eq!(
            enc(&"x".repeat(70), "s", 1001),
            "$5$rounds=1001$s$DONpxSaZd6eGkzPQGXVQwiECGTw0SgHk5dKIO3pMr16"
        );
    }

    #[test]
    fn parse_and_verify() {
        let stored = "$5$rounds=1000$abcdEFGH12345678$G6P9sA2.z7nziwWaXQ1RT3LEjLPlckoVWmixUb7Q0Y7";
        let (rounds, salt, raw) = parse(stored).unwrap();
        assert_eq!((rounds, salt.as_str()), (1000, "abcdEFGH12345678"));
        assert_eq!(raw, compute_raw("KrTesting", "abcdEFGH12345678", 1000));
        assert_eq!(parse_rounds(stored), Ok(1000));
        assert!(verify("KrTesting", stored).unwrap());
        assert!(!verify("krtesting", stored).unwrap());
        assert_eq!(parse_rounds("$5$salt$abc"), Ok(DEFAULT_ROUNDS));
        assert_eq!(
            parse("$6$x$y").unwrap_err(),
            Sha256CryptError::MissingPrefix
        );
        assert_eq!(
            parse("$5$salt$short").unwrap_err(),
            Sha256CryptError::BadHashLength(5)
        );
    }

    #[test]
    fn salt_shape() {
        let s = generate_salt();
        assert_eq!(s.len(), 16);
        assert!(s
            .bytes()
            .all(|b| b.is_ascii_alphanumeric() || b == b'.' || b == b'/'));
    }
}
