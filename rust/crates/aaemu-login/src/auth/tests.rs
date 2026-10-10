//! Ports of `AAEmu.UnitTests/Login/Core/Authentication/KoreaAuthFlowTests.cs` and
//! `PasswordAuthFlowTests.cs`. The mocked `ILoginController` / `ITwoFactorService` /
//! `IOtpService` / `IPcCertService` / `IArsService` are a [`MockBackend`], the mocked
//! `ILoginClient` is a [`RecordingClient`].

use std::net::{IpAddr, Ipv4Addr};
use std::sync::atomic::Ordering;
use std::sync::Arc;

use aes::cipher::block_padding::NoPadding;
use aes::cipher::{BlockDecryptMut, BlockEncryptMut, KeyIvInit};

use super::*;
use crate::models::{KoreaAuthInfo, LoginResult};
use crate::services::sha256_crypt;
use crate::test_support::{
    test_config, test_server, ClientCall, LoginBehavior, MockBackend, RecordingClient,
};

const LOOPBACK: IpAddr = IpAddr::V4(Ipv4Addr::LOCALHOST);

// ---- KoreaAuthFlowTests -------------------------------------------------------------------

struct Fixture {
    backend: Arc<MockBackend>,
    server: Arc<LoginServer>,
    client: RecordingClient,
}

/// `CreateFlow(username, options)`: `options` are the `KoreaAuth` section (C#
/// `new KoreaAuthOptions { ... }`); `None` means `new KoreaAuthOptions { Enabled = true }`.
fn fixture(options: Option<serde_json::Value>) -> Fixture {
    let options = options.unwrap_or_else(|| serde_json::json!({ "Enabled": true }));
    let backend = Arc::new(MockBackend::default());
    let server = test_server(
        test_config(serde_json::json!({ "KoreaAuth": options })),
        backend.clone(),
    );
    Fixture {
        backend,
        server,
        client: RecordingClient::default(),
    }
}

fn create_flow(username: &str) -> KoreaAuthFlow {
    KoreaAuthFlow::new(username, LOOPBACK)
}

/// `MakeKoreaAuthInfo`: a real sha256_crypt hash for `password` with fast (1000) rounds.
fn make_korea_auth_info(account_id: AccountId, password: &str) -> KoreaAuthInfo {
    let salt = "unitsalttestABCD";
    let rounds = 1000;
    KoreaAuthInfo {
        account_id,
        challenge_key_hash: sha256_crypt::compute_raw(password, salt, rounds),
        challenge_salt: salt.to_string(),
        challenge_rounds: rounds,
    }
}

/// `ComputeExpectedV2Response`: mirrors the client's computation (independently of
/// `services::korea`): `t = AES-CBC-decrypt(key, 0-IV, hc)`, each u32 += 1,
/// `ch = AES-CBC-encrypt(key, 0-IV, t)`.
fn compute_expected_v2_response(key: &[u8; 32], hc: &[u32]) -> [u32; 8] {
    let iv = [0u8; 16];
    let mut buf: Vec<u8> = hc.iter().flat_map(|v| v.to_le_bytes()).collect();
    cbc::Decryptor::<aes::Aes256>::new(key.into(), &iv.into())
        .decrypt_padded_mut::<NoPadding>(&mut buf)
        .unwrap();
    for chunk in buf.as_chunks_mut::<4>().0 {
        *chunk = u32::from_le_bytes(*chunk).wrapping_add(1).to_le_bytes();
    }
    let len = buf.len();
    cbc::Encryptor::<aes::Aes256>::new(key.into(), &iv.into())
        .encrypt_padded_mut::<NoPadding>(&mut buf, len)
        .unwrap();
    let mut out = [0u32; 8];
    for (o, c) in out.iter_mut().zip(buf.as_chunks::<4>().0) {
        *o = u32::from_le_bytes(*c);
    }
    out
}

fn count_challenge2(client: &RecordingClient) -> usize {
    client
        .calls()
        .iter()
        .filter(|c| matches!(c, ClientCall::Challenge2 { .. }))
        .count()
}

#[tokio::test]
async fn start_async_with_valid_korea_hash_sends_challenge2_and_returns_pending() {
    let f = fixture(None);
    f.backend
        .set_korea_info(Some(make_korea_auth_info(AccountId(42), "password")));
    let mut flow = create_flow("testuser");

    let result = flow.start(&f.server, &f.client).await.unwrap();

    assert_eq!(result, AuthFlowResult::Pending);
    assert_eq!(count_challenge2(&f.client), 1);
}

#[tokio::test]
async fn start_async_account_not_found_returns_denied() {
    let f = fixture(None);
    f.backend.set_korea_info(None);
    let mut flow = create_flow("unknownuser");

    let result = flow.start(&f.server, &f.client).await.unwrap();

    assert_eq!(
        result,
        AuthFlowResult::Denied(LoginDeniedReason::BAD_ACCOUNT)
    );
}

#[tokio::test]
async fn start_async_no_korea_challenge_hash_returns_denied() {
    // null return = account exists but no korea_challenge_hash stored
    let f = fixture(None);
    f.backend.set_korea_info(None);
    let mut flow = create_flow("nokoreauser");

    let result = flow.start(&f.server, &f.client).await.unwrap();

    assert!(matches!(result, AuthFlowResult::Denied(_)));
}

#[tokio::test]
async fn continue2_async_correct_response_returns_success() {
    let f = fixture(None);
    let account_id = AccountId(100);
    let info = make_korea_auth_info(account_id, "correctpassword");
    f.backend.set_korea_info(Some(info.clone()));
    f.backend.set_ban(false, LoginDeniedReason::default());
    let mut flow = create_flow("testuser");
    flow.start(&f.server, &f.client).await.unwrap();

    let correct_ch =
        compute_expected_v2_response(&info.challenge_key_hash, &f.client.captured_hc());
    let result = flow
        .step(
            &f.server,
            &f.client,
            KoreaStep::ChallengeV2 { ch: correct_ch },
        )
        .await
        .unwrap();

    assert_eq!(
        result,
        AuthFlowResult::Success {
            account_id,
            username: Some("testuser".to_string()),
        }
    );
}

#[tokio::test]
async fn continue2_async_wrong_response_returns_denied() {
    let f = fixture(None);
    f.backend
        .set_korea_info(Some(make_korea_auth_info(AccountId(100), "password")));
    let mut flow = create_flow("testuser");
    flow.start(&f.server, &f.client).await.unwrap();

    // all zeros — wrong response
    let result = flow
        .step(&f.server, &f.client, KoreaStep::ChallengeV2 { ch: [0; 8] })
        .await
        .unwrap();

    assert_eq!(
        result,
        AuthFlowResult::Denied(LoginDeniedReason::BAD_ACCOUNT)
    );
}

#[tokio::test]
async fn continue2_async_when_not_awaiting_challenge_response_returns_denied() {
    let f = fixture(None);
    let mut flow = create_flow("testuser");
    flow.state = KoreaState::Completed;

    let result = flow
        .step(&f.server, &f.client, KoreaStep::ChallengeV2 { ch: [0; 8] })
        .await
        .unwrap();

    assert_eq!(
        result,
        AuthFlowResult::Denied(LoginDeniedReason::BAD_RESPONSE)
    );
}

#[tokio::test]
async fn continue2_async_banned_account_returns_denied() {
    let f = fixture(None);
    let info = make_korea_auth_info(AccountId(200), "password");
    f.backend.set_korea_info(Some(info.clone()));
    f.backend.set_ban(true, LoginDeniedReason::BAD_ACCOUNT);
    let mut flow = create_flow("testuser");
    flow.start(&f.server, &f.client).await.unwrap();

    let correct_ch =
        compute_expected_v2_response(&info.challenge_key_hash, &f.client.captured_hc());
    let result = flow
        .step(
            &f.server,
            &f.client,
            KoreaStep::ChallengeV2 { ch: correct_ch },
        )
        .await
        .unwrap();

    assert!(matches!(result, AuthFlowResult::Denied(_)));
}

#[tokio::test]
async fn continue_async_v1_always_returns_denied() {
    let f = fixture(None);
    let mut flow = create_flow("testuser");

    // V1 ContinueAsync should always deny regardless of state
    let result = flow
        .step(
            &f.server,
            &f.client,
            KoreaStep::ChallengeV1 {
                ch: [0; 4],
                pw: vec![0; 32],
            },
        )
        .await
        .unwrap();

    assert_eq!(
        result,
        AuthFlowResult::Denied(LoginDeniedReason::BAD_RESPONSE)
    );
}

#[tokio::test]
async fn submit_otp_async_when_not_awaiting_otp_returns_denied() {
    // flow is in AwaitingChallengeResponse state (not AwaitingOtp)
    let f = fixture(None);
    let mut flow = create_flow("testuser");

    let result = flow
        .step(&f.server, &f.client, KoreaStep::Otp("123456".into()))
        .await
        .unwrap();

    assert_eq!(
        result,
        AuthFlowResult::Denied(LoginDeniedReason::BAD_RESPONSE)
    );
}

#[tokio::test]
async fn submit_otp_async_invalid_otp_sends_retry_packet_returns_pending() {
    let f = fixture(Some(serde_json::json!({ "MaxOtpAttempts": 3 })));
    f.backend.otp_valid.store(false, Ordering::SeqCst);
    let mut flow = create_flow("testuser");
    flow.state = KoreaState::AwaitingOtp;

    let result = flow
        .step(&f.server, &f.client, KoreaStep::Otp("000000".into()))
        .await
        .unwrap();

    assert_eq!(result, AuthFlowResult::Pending);
    assert_eq!(f.client.calls(), vec![ClientCall::OtpPrompt(3, 2)]);
}

#[tokio::test]
async fn submit_otp_async_max_attempts_exceeded_returns_denied() {
    let f = fixture(Some(serde_json::json!({ "MaxOtpAttempts": 2 })));
    f.backend.otp_valid.store(false, Ordering::SeqCst);
    let mut flow = create_flow("testuser");
    flow.state = KoreaState::AwaitingOtp;

    // First attempt
    flow.step(&f.server, &f.client, KoreaStep::Otp("000000".into()))
        .await
        .unwrap();
    // Second attempt (max reached)
    let result = flow
        .step(&f.server, &f.client, KoreaStep::Otp("000000".into()))
        .await
        .unwrap();

    assert_eq!(
        result,
        AuthFlowResult::Denied(LoginDeniedReason::BAD_RESPONSE)
    );
}

#[tokio::test]
async fn submit_cert_async_when_not_awaiting_cert_returns_denied() {
    let f = fixture(None);
    let mut flow = create_flow("testuser");

    let result = flow
        .step(&f.server, &f.client, KoreaStep::Cert("12345678".into()))
        .await
        .unwrap();

    assert_eq!(
        result,
        AuthFlowResult::Denied(LoginDeniedReason::BAD_RESPONSE)
    );
}

#[tokio::test]
async fn submit_cert_async_invalid_cert_sends_retry_packet_returns_pending() {
    let f = fixture(Some(serde_json::json!({ "MaxCertAttempts": 3 })));
    f.backend.cert_valid.store(false, Ordering::SeqCst);
    let mut flow = create_flow("testuser");
    flow.state = KoreaState::AwaitingCert;

    let result = flow
        .step(&f.server, &f.client, KoreaStep::Cert("00000000".into()))
        .await
        .unwrap();

    assert_eq!(result, AuthFlowResult::Pending);
    assert_eq!(f.client.calls(), vec![ClientCall::CertPrompt(3, 2)]);
}

#[tokio::test]
async fn submit_cert_async_max_attempts_exceeded_returns_denied() {
    let f = fixture(Some(serde_json::json!({ "MaxCertAttempts": 2 })));
    f.backend.cert_valid.store(false, Ordering::SeqCst);
    let mut flow = create_flow("testuser");
    flow.state = KoreaState::AwaitingCert;

    flow.step(&f.server, &f.client, KoreaStep::Cert("00000000".into()))
        .await
        .unwrap();
    let result = flow
        .step(&f.server, &f.client, KoreaStep::Cert("00000000".into()))
        .await
        .unwrap();

    assert_eq!(
        result,
        AuthFlowResult::Denied(LoginDeniedReason::BAD_RESPONSE)
    );
}

#[tokio::test]
async fn complete_ars_async_when_not_awaiting_ars_returns_denied() {
    let f = fixture(None);
    let mut flow = create_flow("testuser");

    let result = flow
        .step(&f.server, &f.client, KoreaStep::Ars(true))
        .await
        .unwrap();

    assert_eq!(
        result,
        AuthFlowResult::Denied(LoginDeniedReason::BAD_RESPONSE)
    );
}

#[tokio::test]
async fn complete_ars_async_failure_returns_denied() {
    let f = fixture(None);
    let mut flow = create_flow("testuser");
    flow.state = KoreaState::AwaitingArs;

    let result = flow
        .step(&f.server, &f.client, KoreaStep::Ars(false))
        .await
        .unwrap();

    assert_eq!(
        result,
        AuthFlowResult::Denied(LoginDeniedReason::BAD_RESPONSE)
    );
}

#[tokio::test]
async fn complete_ars_async_success_returns_success() {
    let f = fixture(None);
    let account_id = AccountId(999);
    let mut flow = create_flow("testuser");
    flow.state = KoreaState::AwaitingArs;
    flow.account_id = account_id;
    f.backend.set_ban(false, LoginDeniedReason::default());

    let result = flow
        .step(&f.server, &f.client, KoreaStep::Ars(true))
        .await
        .unwrap();

    match result {
        AuthFlowResult::Success { account_id: a, .. } => assert_eq!(a, account_id),
        other => panic!("expected Success, got {other:?}"),
    }
}

// ---- PasswordAuthFlowTests ----------------------------------------------------------------

fn password_flow(username: &str, password: &str) -> AuthFlow {
    AuthFlow::Password(PasswordAuthFlow {
        username: Some(username.to_string()),
        password: Some(Password::from_sha256_hex(password)),
        client_ip: LOOPBACK,
    })
}

#[tokio::test]
async fn password_start_async_successful_login_returns_success_with_username() {
    let f = fixture(None);
    let account_id = AccountId(123);
    f.backend
        .set_login(LoginBehavior::Result(LoginResult::ok(account_id)));
    let mut flow = password_flow("testuser", "testpass");

    let result = flow.start(&f.server, &f.client).await.unwrap();

    assert_eq!(
        result,
        AuthFlowResult::Success {
            account_id,
            username: Some("testuser".to_string()),
        }
    );
}

#[tokio::test]
async fn password_start_async_failed_login_returns_denied() {
    let f = fixture(None);
    let reason = LoginDeniedReason::BAD_RESPONSE;
    f.backend
        .set_login(LoginBehavior::Result(LoginResult::denied(reason)));
    let mut flow = password_flow("testuser", "wrongpass");

    let result = flow.start(&f.server, &f.client).await.unwrap();

    assert_eq!(result, AuthFlowResult::Denied(reason));
}

#[tokio::test]
async fn password_start_async_banned_account_returns_denied_with_ban_reason() {
    let f = fixture(None);
    let reason = LoginDeniedReason::TRY_TRADE_CASH_TEMPORAL;
    f.backend
        .set_login(LoginBehavior::Result(LoginResult::denied(reason)));
    let mut flow = password_flow("banneduser", "password");

    let result = flow.start(&f.server, &f.client).await.unwrap();

    assert_eq!(result, AuthFlowResult::Denied(reason));
}
