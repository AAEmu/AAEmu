//! Password hashing (`Password.cs`, `IPasswordService.cs`, `PasswordService.cs`) including a
//! port of the ASP.NET Core Identity `PasswordHasher<T>` storage format the C# server uses.

use base64::engine::general_purpose::STANDARD as B64;
use base64::Engine;
use rand::RngCore;
use sha2::{Digest, Sha256, Sha512};
use subtle::ConstantTimeEq;

/// How a password value is encoded.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum PasswordKind {
    /// The password is plaintext.
    Plaintext,
    /// The password is a hex-encoded SHA-256 hash (AAEmu launcher / Trion / Mail.Ru).
    Sha256Hex,
}

/// A password value that carries its encoding.
#[derive(Clone, PartialEq, Eq)]
pub struct Password {
    pub value: String,
    pub kind: PasswordKind,
}

impl Password {
    pub fn from_plaintext(value: impl Into<String>) -> Self {
        Self {
            value: value.into(),
            kind: PasswordKind::Plaintext,
        }
    }

    pub fn from_sha256_hex(hex: impl Into<String>) -> Self {
        Self {
            value: hex.into(),
            kind: PasswordKind::Sha256Hex,
        }
    }
}

impl std::fmt::Debug for Password {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("Password")
            .field("kind", &self.kind)
            .finish_non_exhaustive()
    }
}

/// Result of verifying a password against a stored hash.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum PasswordVerificationResult {
    /// The password did not match.
    Failed,
    /// The password matched and is stored in the current format.
    Success,
    /// The password matched but is stored in a legacy format and should be rehashed.
    SuccessRehashNeeded,
}

#[derive(Debug, thiserror::Error)]
pub enum PasswordError {
    /// `Convert.FromHexString` throws on invalid input.
    #[error("invalid hex password: {0}")]
    InvalidHex(#[from] hex::FromHexError),
}

/// ASP.NET Core Identity `PasswordHasher<TUser>` (V2 and V3 formats).
pub mod identity {
    use super::*;

    /// `KeyDerivationPrf`.
    #[derive(Clone, Copy, Debug, PartialEq, Eq)]
    pub enum Prf {
        HmacSha1 = 0,
        HmacSha256 = 1,
        HmacSha512 = 2,
    }

    const V3_SALT_SIZE: usize = 128 / 8;
    const V3_SUBKEY_SIZE: usize = 256 / 8;
    const V2_SALT_SIZE: usize = 128 / 8;
    const V2_SUBKEY_SIZE: usize = 256 / 8;
    const V2_ITERATIONS: u32 = 1000;

    pub fn pbkdf2(prf: Prf, password: &[u8], salt: &[u8], iterations: u32, out: &mut [u8]) {
        match prf {
            Prf::HmacSha1 => pbkdf2::pbkdf2_hmac::<sha1::Sha1>(password, salt, iterations, out),
            Prf::HmacSha256 => pbkdf2::pbkdf2_hmac::<Sha256>(password, salt, iterations, out),
            Prf::HmacSha512 => pbkdf2::pbkdf2_hmac::<Sha512>(password, salt, iterations, out),
        }
    }

    /// V3 format: `0x01 | prf (u32 BE) | iter (u32 BE) | saltLen (u32 BE) | salt | subkey`,
    /// Base64-encoded. Uses HMAC-SHA512, 128-bit salt, 256-bit subkey (the .NET 8+ defaults).
    pub fn hash_password_with_salt(
        password: &str,
        iterations: u32,
        salt: &[u8; V3_SALT_SIZE],
    ) -> String {
        let mut subkey = [0u8; V3_SUBKEY_SIZE];
        pbkdf2(
            Prf::HmacSha512,
            password.as_bytes(),
            salt,
            iterations,
            &mut subkey,
        );
        let mut out = Vec::with_capacity(13 + V3_SALT_SIZE + V3_SUBKEY_SIZE);
        out.push(0x01);
        out.extend_from_slice(&(Prf::HmacSha512 as u32).to_be_bytes());
        out.extend_from_slice(&iterations.to_be_bytes());
        out.extend_from_slice(&(V3_SALT_SIZE as u32).to_be_bytes());
        out.extend_from_slice(salt);
        out.extend_from_slice(&subkey);
        B64.encode(out)
    }

    pub fn hash_password(password: &str, iterations: u32) -> String {
        let mut salt = [0u8; V3_SALT_SIZE];
        rand::rngs::OsRng.fill_bytes(&mut salt);
        hash_password_with_salt(password, iterations, &salt)
    }

    fn be_u32(b: &[u8], offset: usize) -> Option<u32> {
        Some(u32::from_be_bytes(
            b.get(offset..offset + 4)?.try_into().ok()?,
        ))
    }

    fn verify_v2(decoded: &[u8], password: &str) -> bool {
        if decoded.len() != 1 + V2_SALT_SIZE + V2_SUBKEY_SIZE {
            return false;
        }
        let salt = &decoded[1..1 + V2_SALT_SIZE];
        let expected = &decoded[1 + V2_SALT_SIZE..];
        let mut actual = [0u8; V2_SUBKEY_SIZE];
        pbkdf2(
            Prf::HmacSha1,
            password.as_bytes(),
            salt,
            V2_ITERATIONS,
            &mut actual,
        );
        bool::from(actual.ct_eq(expected))
    }

    /// Returns `Some((iterations, prf))` on a match.
    fn verify_v3(decoded: &[u8], password: &str) -> Option<(u32, Prf)> {
        let prf = match be_u32(decoded, 1)? {
            0 => Prf::HmacSha1,
            1 => Prf::HmacSha256,
            2 => Prf::HmacSha512,
            _ => return None, // KeyDerivation.Pbkdf2 throws -> caught -> false
        };
        let iterations = be_u32(decoded, 5)?;
        // (int)iterCount <= 0 makes Pbkdf2 throw -> false
        if iterations == 0 || iterations > i32::MAX as u32 {
            return None;
        }
        let salt_len = be_u32(decoded, 9)? as usize;
        if salt_len < 128 / 8 {
            return None;
        }
        let salt = decoded.get(13..13 + salt_len)?;
        let expected = &decoded[13 + salt_len..];
        if expected.len() < 128 / 8 {
            return None;
        }
        let mut actual = vec![0u8; expected.len()];
        pbkdf2(prf, password.as_bytes(), salt, iterations, &mut actual);
        bool::from(actual.ct_eq(expected)).then_some((iterations, prf))
    }

    /// `PasswordHasher.VerifyHashedPassword` with compatibility mode V3 and the given
    /// configured iteration count.
    pub fn verify_hashed_password(
        hashed: &str,
        password: &str,
        configured_iterations: u32,
    ) -> PasswordVerificationResult {
        let Ok(decoded) = B64.decode(hashed) else {
            return PasswordVerificationResult::Failed;
        };
        match decoded.first() {
            Some(0x00) => {
                if verify_v2(&decoded, password) {
                    // This is an old password hash format - the caller needs to rehash.
                    PasswordVerificationResult::SuccessRehashNeeded
                } else {
                    PasswordVerificationResult::Failed
                }
            }
            Some(0x01) => match verify_v3(&decoded, password) {
                Some((iterations, prf)) => {
                    if iterations < configured_iterations
                        || matches!(prf, Prf::HmacSha1 | Prf::HmacSha256)
                    {
                        PasswordVerificationResult::SuccessRehashNeeded
                    } else {
                        PasswordVerificationResult::Success
                    }
                }
                None => PasswordVerificationResult::Failed,
            },
            _ => PasswordVerificationResult::Failed, // unknown format marker
        }
    }
}

/// Legacy format: Base64-encoded 32 bytes = exactly 44 characters (with padding).
const LEGACY_BASE64_LENGTH: usize = 44;
const LEGACY_HASH_BYTE_LENGTH: usize = 32;

/// Password hashing and verification with support for migrating from the legacy format.
#[derive(Clone, Debug)]
pub struct PasswordService {
    /// PBKDF2 iterations (600,000 per OWASP recommendations, as configured by the C# server).
    pub iterations: u32,
}

impl Default for PasswordService {
    fn default() -> Self {
        Self {
            iterations: 600_000,
        }
    }
}

impl PasswordService {
    /// A value suitable for storage in the database.
    ///
    /// * Plaintext -> PBKDF2 hash string.
    /// * Sha256Hex -> Base64 of the raw SHA-256 bytes (the legacy DB format). AAEmu Launcher sends
    ///   hex-encoded SHA-256; storing it in the legacy format means a subsequent plaintext login
    ///   can verify it via SHA-256 computation, and SuccessRehashNeeded triggers a PBKDF2 upgrade.
    pub fn hash_for_storage(&self, password: &Password) -> Result<String, PasswordError> {
        match password.kind {
            PasswordKind::Sha256Hex => Ok(B64.encode(hex::decode(&password.value)?)),
            PasswordKind::Plaintext => {
                Ok(identity::hash_password(&password.value, self.iterations))
            }
        }
    }

    pub fn verify_password(
        &self,
        stored_hash: &str,
        password: &Password,
    ) -> PasswordVerificationResult {
        if self.is_legacy_format(stored_hash) {
            let matches = match password.kind {
                PasswordKind::Sha256Hex => verify_legacy_password(stored_hash, &password.value),
                PasswordKind::Plaintext => {
                    verify_legacy_password_from_plaintext(stored_hash, &password.value)
                }
            };
            return if matches {
                PasswordVerificationResult::SuccessRehashNeeded
            } else {
                PasswordVerificationResult::Failed
            };
        }
        identity::verify_hashed_password(stored_hash, &password.value, self.iterations)
    }

    /// Whether a stored hash uses the legacy format (Base64-encoded raw SHA-256 bytes).
    pub fn is_legacy_format(&self, stored_hash: &str) -> bool {
        stored_hash.len() == LEGACY_BASE64_LENGTH && decode_legacy(stored_hash).is_some()
    }
}

fn decode_legacy(stored: &str) -> Option<Vec<u8>> {
    B64.decode(stored)
        .ok()
        .filter(|b| b.len() == LEGACY_HASH_BYTE_LENGTH)
}

fn verify_legacy_password_from_plaintext(stored: &str, plaintext: &str) -> bool {
    let Some(stored) = decode_legacy(stored) else {
        return false;
    };
    let computed = Sha256::digest(plaintext.as_bytes());
    bool::from(computed.as_slice().ct_eq(&stored))
}

fn verify_legacy_password(stored: &str, password_hex: &str) -> bool {
    let Some(stored) = decode_legacy(stored) else {
        return false;
    };
    // Convert.FromHexString(span, dest[32]) must succeed and write exactly 32 bytes.
    match hex::decode(password_hex) {
        Ok(bytes) if bytes.len() == LEGACY_HASH_BYTE_LENGTH => bool::from(bytes.ct_eq(&stored)),
        _ => false,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    // Generated by the C# PasswordHasher<string> / PasswordService (see crate tests notes).
    const V3_600K: &str =
        "AQAAAAIACSfAAAAAEBGwMJh+2oLPb7GuHpnL58VdT6OUuplyvFoUwgJbprA/IBKJPmJ2NrRkGS7PLq5xhQ==";
    const V3_10K: &str =
        "AQAAAAIAACcQAAAAEAZnQeK3R6Vz0eX3/HnOWw7jEOgZT0gIQJQLjL9WaA+Rt4TnZ1dVPiIjW1sNoCVMHA==";
    const V2: &str = "AK8bzRkKhAu2UFYdLooCOMPHHVE0Yi3n6NtPJoNRuJ4VGP+3Fl70vUR+E/8bSVPO9w==";
    const SHA_HEX: &str = "F52FBD32B2B3B86FF88EF6C490628285F482AF15DDCB29541F94BCF526A3F6C7";
    const LEGACY: &str = "9S+9MrKzuG/4jvbEkGKChfSCrxXdyylUH5S89Saj9sc=";

    #[test]
    fn verifies_csharp_identity_hashes() {
        let svc = PasswordService::default();
        let pw = Password::from_plaintext("hunter2");
        assert_eq!(
            svc.verify_password(V3_600K, &pw),
            PasswordVerificationResult::Success
        );
        assert_eq!(
            svc.verify_password(V3_10K, &pw),
            PasswordVerificationResult::SuccessRehashNeeded
        );
        assert_eq!(
            svc.verify_password(V2, &pw),
            PasswordVerificationResult::SuccessRehashNeeded
        );
        let wrong = Password::from_plaintext("hunter3");
        assert_eq!(
            svc.verify_password(V3_10K, &wrong),
            PasswordVerificationResult::Failed
        );
        assert_eq!(
            svc.verify_password(V2, &wrong),
            PasswordVerificationResult::Failed
        );
        assert_eq!(
            svc.verify_password("not base64!", &pw),
            PasswordVerificationResult::Failed
        );
        assert_eq!(
            svc.verify_password("", &pw),
            PasswordVerificationResult::Failed
        );
    }

    #[test]
    fn hash_format_is_identity_v3() {
        let salt = [1u8; 16];
        let h = identity::hash_password_with_salt("hunter2", 1000, &salt);
        let raw = B64.decode(&h).unwrap();
        assert_eq!(&raw[..13], &[1, 0, 0, 0, 2, 0, 0, 3, 0xE8, 0, 0, 0, 16]);
        assert_eq!(raw.len(), 13 + 16 + 32);
        assert_eq!(
            identity::verify_hashed_password(&h, "hunter2", 1000),
            PasswordVerificationResult::Success
        );
        assert_eq!(
            identity::verify_hashed_password(&h, "hunter2", 1001),
            PasswordVerificationResult::SuccessRehashNeeded
        );
    }

    #[test]
    fn legacy_formats() {
        let svc = PasswordService::default();
        assert_eq!(
            svc.hash_for_storage(&Password::from_sha256_hex(SHA_HEX))
                .unwrap(),
            LEGACY
        );
        assert!(svc.is_legacy_format(LEGACY));
        assert!(!svc.is_legacy_format(V2));
        assert_eq!(
            svc.verify_password(LEGACY, &Password::from_plaintext("hunter2")),
            PasswordVerificationResult::SuccessRehashNeeded
        );
        assert_eq!(
            svc.verify_password(LEGACY, &Password::from_sha256_hex(SHA_HEX.to_lowercase())),
            PasswordVerificationResult::SuccessRehashNeeded
        );
        assert_eq!(
            svc.verify_password(LEGACY, &Password::from_sha256_hex("00")),
            PasswordVerificationResult::Failed
        );
        assert_eq!(
            svc.verify_password(LEGACY, &Password::from_plaintext("nope")),
            PasswordVerificationResult::Failed
        );
        assert!(svc
            .hash_for_storage(&Password::from_sha256_hex("zz"))
            .is_err());
    }

    // ---- Port of AAEmu.UnitTests/Login/Core/Services/PasswordServiceTests.cs ----------------
    // `new PasswordService(new PasswordHasher<string>(new PasswordHasherOptions()))`: the default
    // ASP.NET Core Identity options (V3, 100,000 iterations).

    fn sut() -> PasswordService {
        PasswordService {
            iterations: 100_000,
        }
    }

    /// `Convert.ToHexString` (upper-case).
    fn to_hex(bytes: &[u8]) -> String {
        hex::encode_upper(bytes)
    }

    fn bytes_0_to_31() -> Vec<u8> {
        (0..32u8).collect()
    }

    #[test]
    fn is_legacy_format_base64_encoded_32_bytes_returns_true() {
        let legacy_hash = B64.encode([0u8; 32]);
        assert!(sut().is_legacy_format(&legacy_hash));
    }

    #[test]
    fn is_legacy_format_base64_encoded_32_bytes_with_content_returns_true() {
        let legacy_hash = B64.encode(bytes_0_to_31());
        assert!(sut().is_legacy_format(&legacy_hash));
        assert_eq!(legacy_hash.len(), 44);
    }

    #[test]
    fn is_legacy_format_new_format_hash_returns_false() {
        // new format hash from PasswordHasher
        let new_hash = identity::hash_password("test", 100_000);
        assert!(!sut().is_legacy_format(&new_hash));
        assert!(new_hash.len() >= 84);
    }

    #[test]
    fn is_legacy_format_wrong_length_returns_false() {
        assert!(!sut().is_legacy_format("shortstring"));
    }

    #[test]
    fn is_legacy_format_invalid_base64_returns_false() {
        // 44 characters but not valid Base64 that decodes to 32 bytes
        assert!(!sut().is_legacy_format(&"!".repeat(44)));
    }

    #[test]
    fn hash_for_storage_plaintext_returns_new_format_hash() {
        let svc = sut();
        let hash = svc
            .hash_for_storage(&Password::from_plaintext("mypassword123"))
            .unwrap();
        assert!(!svc.is_legacy_format(&hash));
        assert!(hash.len() >= 84);
    }

    #[test]
    fn hash_for_storage_sha256_hex_returns_legacy_base64_format() {
        let svc = sut();
        let bytes = bytes_0_to_31();
        let expected_base64 = B64.encode(&bytes);

        let hash = svc
            .hash_for_storage(&Password::from_sha256_hex(to_hex(&bytes)))
            .unwrap();

        // must be the legacy Base64 format, NOT a PBKDF2 hash
        assert_eq!(hash, expected_base64);
        assert!(svc.is_legacy_format(&hash));
    }

    #[test]
    fn hash_for_storage_sha256_hex_verifiable_by_plaintext_via_legacy() {
        // Regression test for the double-hash bug: an account created from a SHA256-hex password
        // is stored in the legacy Base64 format so a subsequent login can verify it.
        let svc = sut();
        let hex = to_hex(&bytes_0_to_31());
        let stored_hash = svc
            .hash_for_storage(&Password::from_sha256_hex(&hex))
            .unwrap();

        let result = svc.verify_password(&stored_hash, &Password::from_sha256_hex(&hex));
        assert_eq!(result, PasswordVerificationResult::SuccessRehashNeeded);
    }

    #[test]
    fn verify_password_legacy_format_correct_password_returns_success_rehash_needed() {
        let bytes = bytes_0_to_31();
        let legacy_hash = B64.encode(&bytes);

        let result =
            sut().verify_password(&legacy_hash, &Password::from_sha256_hex(to_hex(&bytes)));

        assert_eq!(result, PasswordVerificationResult::SuccessRehashNeeded);
    }

    #[test]
    fn verify_password_legacy_format_wrong_password_returns_failed() {
        let legacy_hash = B64.encode(bytes_0_to_31());
        let wrong: Vec<u8> = (1..=32u8).collect();

        let result =
            sut().verify_password(&legacy_hash, &Password::from_sha256_hex(to_hex(&wrong)));

        assert_eq!(result, PasswordVerificationResult::Failed);
    }

    const PASSWORD_HEX: &str = "000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F";

    #[test]
    fn verify_password_new_format_correct_password_returns_success() {
        let svc = sut();
        let new_hash = svc
            .hash_for_storage(&Password::from_plaintext(PASSWORD_HEX))
            .unwrap();

        let result = svc.verify_password(&new_hash, &Password::from_plaintext(PASSWORD_HEX));

        assert_eq!(result, PasswordVerificationResult::Success);
    }

    #[test]
    fn verify_password_new_format_wrong_password_returns_failed() {
        let svc = sut();
        let new_hash = svc
            .hash_for_storage(&Password::from_plaintext(PASSWORD_HEX))
            .unwrap();
        let wrong = "0102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F20";

        let result = svc.verify_password(&new_hash, &Password::from_plaintext(wrong));

        assert_eq!(result, PasswordVerificationResult::Failed);
    }

    #[test]
    fn verify_password_migration_scenario_pre_hashed_works() {
        let svc = sut();
        let bytes = bytes_0_to_31();
        let legacy_hash = B64.encode(&bytes);
        let password_hex = to_hex(&bytes);

        // first login with legacy format (pre-hashed, returns SuccessRehashNeeded)
        let first = svc.verify_password(&legacy_hash, &Password::from_sha256_hex(&password_hex));
        // Simulate rehashing with plaintext equivalent
        let new_hash = svc
            .hash_for_storage(&Password::from_plaintext(&password_hex))
            .unwrap();
        // Second login with new format
        let second = svc.verify_password(&new_hash, &Password::from_plaintext(&password_hex));

        assert_eq!(first, PasswordVerificationResult::SuccessRehashNeeded);
        assert_eq!(second, PasswordVerificationResult::Success);
    }

    fn legacy_of_plaintext(plaintext: &str) -> String {
        B64.encode(Sha256::digest(plaintext.as_bytes()))
    }

    #[test]
    fn verify_password_legacy_format_plaintext_correct_password_returns_success_rehash_needed() {
        let legacy_hash = legacy_of_plaintext("mypassword123");

        let result =
            sut().verify_password(&legacy_hash, &Password::from_plaintext("mypassword123"));

        assert_eq!(result, PasswordVerificationResult::SuccessRehashNeeded);
    }

    #[test]
    fn verify_password_legacy_format_plaintext_wrong_password_returns_failed() {
        let legacy_hash = legacy_of_plaintext("mypassword123");

        let result =
            sut().verify_password(&legacy_hash, &Password::from_plaintext("wrongpassword"));

        assert_eq!(result, PasswordVerificationResult::Failed);
    }

    #[test]
    fn verify_password_migration_scenario_plaintext_works() {
        let svc = sut();
        let legacy_hash = legacy_of_plaintext("mypassword123");

        let first = svc.verify_password(&legacy_hash, &Password::from_plaintext("mypassword123"));
        let new_hash = svc
            .hash_for_storage(&Password::from_plaintext("mypassword123"))
            .unwrap();
        let second = svc.verify_password(&new_hash, &Password::from_plaintext("mypassword123"));

        assert_eq!(first, PasswordVerificationResult::SuccessRehashNeeded);
        assert_eq!(second, PasswordVerificationResult::Success);
    }
}
