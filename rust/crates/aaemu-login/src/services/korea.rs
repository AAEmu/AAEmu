//! Korea challenge-response crypto: `KoreaChallengeCrypt.cs` and `KoreanChallengeVerifier.cs`.
//!
//! The client performs the following computation (from binary analysis of x2game-dev.dll,
//! function sub_393C51F0 / AES CBC wrapper sub_395C4F20):
//! ```text
//! K              = sha256_crypt(password, "$5$rounds=N$salt")  // 32-byte raw hash = AES-256 key
//! t              = AES_CBC_decrypt(K, IV=zeros, hc_bytes)      // mode=0 in sub_395C4F20
//! t_uint32[i]   += 1                                           // increment each DWORD
//! ch_response[:] = AES_CBC_encrypt(K, IV=zeros, t)             // mode=1 in sub_395C4F20
//! ```
//! The server recomputes `ch_response` from the stored key material and the `hc` values it sent,
//! then compares with the received `ch`.

use aes::cipher::block_padding::NoPadding;
use aes::cipher::{BlockDecryptMut, BlockEncryptMut, KeyIvInit};
use subtle::ConstantTimeEq;

use super::sha256_crypt::{self, Sha256CryptError};

/// Computes a sha256_crypt hash of `plaintext` with a fresh random salt. Returns the full
/// `$5$rounds=N$salt$...` string (for the `korea_challenge_hash` column) and the raw 32-byte hash
/// (the AES-256 key used by the client).
pub fn compute_with_raw(plaintext: &str, rounds: i32) -> (String, [u8; 32]) {
    let salt = sha256_crypt::generate_salt();
    let raw = sha256_crypt::compute_raw(plaintext, &salt, rounds);
    (sha256_crypt::format_encoded(&raw, &salt, rounds), raw)
}

/// `KoreaChallengeCrypt.Compute(plaintext, rounds)`: the encoded string only.
pub fn compute(plaintext: &str, rounds: i32) -> String {
    compute_with_raw(plaintext, rounds).0
}

/// Verifies a plaintext password against a stored `korea_challenge_hash` (constant-time).
pub fn verify(stored: &str, plaintext: &str) -> Result<bool, Sha256CryptError> {
    sha256_crypt::verify(plaintext, stored)
}

fn u32s_to_bytes(values: &[u32]) -> Vec<u8> {
    values.iter().flat_map(|v| v.to_le_bytes()).collect()
}

fn cbc_decrypt_zero_iv(key: &[u8], data: &mut [u8]) {
    let iv = [0u8; 16];
    match key.len() {
        32 => {
            let _ = cbc::Decryptor::<aes::Aes256>::new_from_slices(key, &iv)
                .expect("valid key/iv length")
                .decrypt_padded_mut::<NoPadding>(data);
        }
        24 => {
            let _ = cbc::Decryptor::<aes::Aes192>::new_from_slices(key, &iv)
                .expect("valid key/iv length")
                .decrypt_padded_mut::<NoPadding>(data);
        }
        n => unreachable!("unsupported AES key length {n}"),
    }
}

fn cbc_encrypt_zero_iv(key: &[u8], data: &mut [u8]) {
    let iv = [0u8; 16];
    let len = data.len();
    match key.len() {
        32 => {
            let _ = cbc::Encryptor::<aes::Aes256>::new_from_slices(key, &iv)
                .expect("valid key/iv length")
                .encrypt_padded_mut::<NoPadding>(data, len);
        }
        24 => {
            let _ = cbc::Encryptor::<aes::Aes192>::new_from_slices(key, &iv)
                .expect("valid key/iv length")
                .encrypt_padded_mut::<NoPadding>(data, len);
        }
        n => unreachable!("unsupported AES key length {n}"),
    }
}

/// Computes the response a correct client sends for `hc` (`ch` in `CAChallengeResponse2`).
pub fn compute_response(key: &[u8], hc: &[u32]) -> Vec<u32> {
    // Step 1: marshal hc uints -> bytes (little-endian)
    let mut t = u32s_to_bytes(hc);
    // Step 2: t = AES_CBC_decrypt(K, IV=zeros, hc_bytes)
    cbc_decrypt_zero_iv(key, &mut t);
    // Step 3: each uint32 in t += 1
    for chunk in t.as_chunks_mut::<4>().0 {
        *chunk = u32::from_le_bytes(*chunk).wrapping_add(1).to_le_bytes();
    }
    // Step 4: ch = AES_CBC_encrypt(K, IV=zeros, t)
    cbc_encrypt_zero_iv(key, &mut t);
    t.as_chunks::<4>()
        .0
        .iter()
        .map(|c| u32::from_le_bytes(*c))
        .collect()
}

fn verify_core(key: &[u8], hc: &[u32], expected_ch: &[u32]) -> bool {
    let computed = u32s_to_bytes(&compute_response(key, hc));
    // Step 5: compare (constant-time)
    bool::from(computed.ct_eq(&u32s_to_bytes(expected_ch)))
}

/// Verifies a V2 Korea challenge response (AES-256, sha256_crypt-based key).
///
/// * `challenge_key` - 32-byte raw sha256_crypt hash used as the AES-256 key.
/// * `hc` - the 8 server-generated values sent in `ACChallenge2Packet`.
/// * `response_ch` - the 8 values from `CAChallengeResponse2Packet`.
pub fn verify_v2(challenge_key: &[u8], hc: &[u32], response_ch: &[u32]) -> bool {
    if challenge_key.len() != 32 || hc.len() != 8 || response_ch.len() != 8 {
        return false;
    }
    verify_core(challenge_key, hc, response_ch)
}

/// V1 Korea challenge verifier (AES-192, key = SHA1(password)[0..20] || salt LE).
///
/// **NOT ENABLED.** SHA-1 without a per-account salt is approximately 17 million times weaker than
/// PBKDF2@600k for offline attacks. Exists for protocol completeness only (the C# method is
/// `[Obsolete(error: true)]`); the server never issues `ACChallengePacket`.
#[deprecated = "V1 Korea challenge is disabled due to SHA-1 security weaknesses. Use V2 only."]
pub fn verify_v1(sha1_hash: &[u8], challenge_salt: u32, hc: &[u32], response_ch: &[u32]) -> bool {
    if sha1_hash.len() != 20 || hc.len() != 4 || response_ch.len() != 4 {
        return false;
    }
    let mut key = [0u8; 24];
    key[..20].copy_from_slice(sha1_hash);
    key[20..].copy_from_slice(&challenge_salt.to_le_bytes());
    verify_core(&key, hc, response_ch)
}

#[cfg(test)]
mod tests {
    use super::*;

    const HC: [u32; 8] = [
        0x11111111, 0x22222222, 0x33333333, 0x44444444, 0x55555555, 0x66666666, 0x77777777,
        0x88888888,
    ];

    #[test]
    fn verifies_csharp_client_simulation() {
        // Key = sha256_crypt("KrTesting", "abcdEFGH12345678", 1000); ch computed by the C# test
        // harness with System.Security.Cryptography.Aes (DecryptCbc / +1 / EncryptCbc).
        let key = sha256_crypt::compute_raw("KrTesting", "abcdEFGH12345678", 1000);
        let ch = [
            2394706929, 2106435918, 1300708160, 4095512849, 1359586479, 2964080146, 963501928,
            458096805,
        ];
        assert_eq!(compute_response(&key, &HC), ch);
        assert!(verify_v2(&key, &HC, &ch));
        let mut bad = ch;
        bad[7] ^= 1;
        assert!(!verify_v2(&key, &HC, &bad));
        assert!(!verify_v2(&key[..31], &HC, &ch));
        assert!(!verify_v2(&key, &HC[..7], &ch));
    }

    #[test]
    #[allow(deprecated)]
    fn v1_roundtrip() {
        let sha1 = [7u8; 20];
        let mut key = [0u8; 24];
        key[..20].copy_from_slice(&sha1);
        key[20..].copy_from_slice(&0xABCDu32.to_le_bytes());
        let hc = [1, 2, 3, 4];
        let ch = compute_response(&key, &hc);
        assert!(verify_v1(&sha1, 0xABCD, &hc, &ch));
        assert!(!verify_v1(&sha1, 0xABCE, &hc, &ch));
    }

    #[test]
    fn compute_and_verify_plaintext() {
        let (stored, raw) = compute_with_raw("pw", 1000);
        assert!(stored.starts_with("$5$rounds=1000$"));
        assert!(verify(&stored, "pw").unwrap());
        assert!(!verify(&stored, "pw2").unwrap());
        let (_, _, parsed_raw) = sha256_crypt::parse(&stored).unwrap();
        assert_eq!(parsed_raw, raw);
    }

    /// Port of `AAEmu.UnitTests/Login/Core/Services/KoreaSeedDataGeneratorTests.cs`. Not a
    /// correctness test — run manually (`cargo test -p aaemu-login -- --ignored
    /// generate_korea_test_account --nocapture`) to print SQL seed rows for Korea auth testing.
    #[test]
    #[ignore = "manual seed-data generator, not a correctness test"]
    fn generate_korea_test_account() {
        use crate::services::password::{Password, PasswordService};

        const USERNAME: &str = "KrTest";
        const TEST_PASSWORD: &str = "KrTesting";
        const ROUNDS: i32 = 100_000;

        // `new PasswordService(new PasswordHasher<string>(new PasswordHasherOptions()))`
        let passwords = PasswordService {
            iterations: 100_000,
        };
        let pbkdf2_hash = passwords
            .hash_for_storage(&Password::from_plaintext(TEST_PASSWORD))
            .unwrap();
        let korea_hash = compute(TEST_PASSWORD, ROUNDS);

        println!("-- Korea test account: username={USERNAME}, password={TEST_PASSWORD}");
        println!("-- PBKDF2:             {pbkdf2_hash}");
        println!("-- korea_challenge_hash: {korea_hash}");
        println!();
        println!(
            "INSERT INTO `users` (username, password, korea_challenge_hash, email, last_ip, last_login, created_at, updated_at) VALUES ('{USERNAME}', '{pbkdf2_hash}', '{korea_hash}', '', '127.0.0.1', 0, 0, 0);"
        );
        assert!(verify(&korea_hash, TEST_PASSWORD).unwrap());
    }
}
