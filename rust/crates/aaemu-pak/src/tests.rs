use crate::*;
use std::fs;
use std::path::PathBuf;

struct Fixture(PathBuf);
impl Fixture {
    fn new() -> Self {
        let base = std::env::var_os("AAEMU_PAK_TEST_DIR")
            .map(PathBuf::from)
            .unwrap_or_else(std::env::temp_dir);
        let path = base.join(format!("aaemu-pak-tests-{:016x}", rand::random::<u64>()));
        fs::create_dir_all(&path).unwrap();
        Self(path)
    }
    fn source(&self, name: &str, data: &[u8]) -> PathBuf {
        let path = self.0.join(name);
        fs::write(&path, data).unwrap();
        path
    }
}
impl Drop for Fixture {
    fn drop(&mut self) {
        let _ = fs::remove_dir_all(&self.0);
    }
}

fn content(length: usize) -> Vec<u8> {
    let mut seed = 0x0120_8022_u32;
    (0..length)
        .map(|i| {
            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            if i < 8192 {
                0
            } else {
                seed as u8
            }
        })
        .collect()
}
fn refresh_body_hash(bytes: &mut [u8]) {
    let sha = hash(&bytes[128..]);
    bytes[80..112].copy_from_slice(&sha);
}

#[test]
fn manifest_exact_wire_layout_and_rejects_corruption() {
    let fixture = Fixture::new();
    let source = fixture.source("raw", &content(12289));
    let path = build(source, fixture.0.join("store"), 4096).unwrap();
    let bytes = fs::read(path).unwrap();
    assert_eq!(&bytes[..8], b"AAEMPK01");
    assert_eq!(&bytes[8..16], &[1, 0, 0, 0, 128, 0, 0, 0]);
    assert_eq!(bytes.len(), 128 + 40 + 4 * 56);
    assert_eq!(Manifest::decode(&bytes).unwrap().encode().unwrap(), bytes);
    for at in [0, 8, 12, 36, 40, 72, 80, 112, 128] {
        let mut changed = bytes.clone();
        changed[at] ^= 1;
        if at == 40 {
            assert!(Manifest::decode(&changed).is_ok());
        } else {
            assert!(Manifest::decode(&changed).is_err(), "offset {at}");
        }
    }
    for length in [0, 7, 127, 128, bytes.len() - 1] {
        assert!(Manifest::decode(&bytes[..length]).is_err());
    }
    let mut extended = bytes.clone();
    extended.push(0);
    assert!(Manifest::decode(&extended).is_err());
    for (at, value) in [
        (128 + 40, u32::MAX),
        (128 + 44, 7),
        (128 + 56, 0),
        (128 + 60, 5000),
    ] {
        let mut changed = bytes.clone();
        changed[at..at + 4].copy_from_slice(&value.to_le_bytes());
        refresh_body_hash(&mut changed);
        assert!(Manifest::decode(&changed).is_err());
    }
    let mut changed = bytes.clone();
    changed[128 + 48..128 + 56].copy_from_slice(&u64::MAX.to_le_bytes());
    refresh_body_hash(&mut changed);
    assert!(Manifest::decode(&changed).is_err());
}

#[test]
fn random_partial_boundary_backward_and_eof_reads_are_exact() {
    let fixture = Fixture::new();
    let data = content(3 * 65536 + 513);
    let source = fixture.source("raw", &data);
    let path = build(source, fixture.0.join("store"), 65536).unwrap();
    let pak = Pak::open(path).unwrap();
    pak.verify().unwrap();
    assert!(pak.manifest.chunks.iter().any(|c| c.codec == 0));
    assert!(pak.manifest.chunks.iter().any(|c| c.codec == 1));
    for (offset, count) in [
        (0, 1),
        (511, 513),
        (65535, 65537),
        (0, 131073),
        (131071, 17),
        (data.len() - 127, 255),
        (data.len(), 1),
        (data.len() + 1, 17),
        (0, 0),
    ] {
        let mut output = vec![0xcd; count + 16];
        let n = pak.read_at(offset as u64, &mut output[..count]).unwrap();
        let expected = count.min(data.len().saturating_sub(offset));
        assert_eq!(n, expected);
        assert_eq!(
            &output[..n],
            &data[offset.min(data.len())..offset.min(data.len()) + n]
        );
        assert!(output[n..].iter().all(|b| *b == 0xcd));
    }
    let mut seed = 42u32;
    for _ in 0..200 {
        seed = seed.wrapping_mul(1664525).wrapping_add(1013904223);
        let offset = seed as usize % data.len();
        let mut b = [0u8; 511];
        let n = pak.read_at(offset as u64, &mut b).unwrap();
        assert_eq!(&b[..n], &data[offset..offset + n]);
    }
    assert_eq!(pak.read_at(u64::MAX, &mut [0; 1]).unwrap(), 0);
    assert!(pak.cached_bytes().unwrap() <= CACHE_BYTES);
}

#[test]
fn empty_stream_and_cache_eviction() {
    let fixture = Fixture::new();
    let empty = fixture.source("empty", &[]);
    let path = build(empty, fixture.0.join("empty-store"), 4096).unwrap();
    let pak = Pak::open(path).unwrap();
    pak.verify().unwrap();
    assert_eq!(pak.read_at(0, &mut [0; 1]).unwrap(), 0);
    assert!(pak.manifest.segments.is_empty());
    let data = vec![7u8; CACHE_BYTES + 1048576];
    let path = build(
        fixture.source("large", &data),
        fixture.0.join("large-store"),
        1048576,
    )
    .unwrap();
    let pak = Pak::open(path).unwrap();
    for offset in (0..data.len()).step_by(1048576) {
        assert_eq!(pak.read_at(offset as u64, &mut [0; 1]).unwrap(), 1);
        assert!(pak.cached_bytes().unwrap() <= CACHE_BYTES);
    }
    let mut byte = [0];
    pak.read_at(0, &mut byte).unwrap();
    assert_eq!(byte, [7]);
    assert_eq!(pak.cached_bytes().unwrap(), CACHE_BYTES);
}

#[test]
fn stored_chunk_and_logical_hash_corruption_are_detected() {
    let fixture = Fixture::new();
    let data = content(12000);
    let path = build(fixture.source("raw", &data), fixture.0.join("store"), 4096).unwrap();
    let manifest = Manifest::load(&path).unwrap();
    let segment = path.parent().unwrap().join(manifest.segments[0].filename());
    let mut packed = fs::read(&segment).unwrap();
    let last = packed.len() - 1;
    packed[last] ^= 1;
    fs::write(&segment, &packed).unwrap();
    let pak = Pak::open(&path).unwrap();
    assert!(pak.verify().is_err());
    assert!(pak.read_at(8192, &mut vec![0; 3808]).is_err());
    packed[last] ^= 1;
    fs::write(&segment, packed).unwrap();
    let mut bytes = fs::read(&path).unwrap();
    bytes[40] ^= 1;
    fs::write(&path, bytes).unwrap();
    assert!(Pak::open(&path).unwrap().verify().is_err());
    fs::write(&segment, [0]).unwrap();
    assert!(Pak::open(&path).is_err());
}

#[test]
fn patch_reuses_unchanged_chunks_replay_and_noop_are_safe() {
    let fixture = Fixture::new();
    let original = content(4 * 4096);
    let installed = build(
        fixture.source("raw", &original),
        fixture.0.join("store"),
        4096,
    )
    .unwrap();
    let old_bytes = fs::read(&installed).unwrap();
    let old = Manifest::decode(&old_bytes).unwrap();
    let mut changed = original.clone();
    changed[9000] ^= 1;
    let package = fixture.0.join("patch");
    let candidate = diff(fixture.source("new", &changed), &installed, &package).unwrap();
    let new = Manifest::load(candidate).unwrap();
    for i in [0, 1, 3] {
        assert_eq!(old.chunks[i].hash, new.chunks[i].hash);
        assert_eq!(
            old.segments[old.chunks[i].segment as usize].hash,
            new.segments[new.chunks[i].segment as usize].hash
        );
    }
    assert_ne!(old.chunks[2].hash, new.chunks[2].hash);
    let receipt: Receipt =
        serde_json::from_slice(&fs::read(package.join("receipt.json")).unwrap()).unwrap();
    assert_eq!(receipt.new_segments.len(), 1);
    assert_eq!(apply(&installed, &package).unwrap(), ApplyResult::Applied);
    assert_eq!(
        apply(&installed, &package).unwrap(),
        ApplyResult::AlreadyApplied
    );
    let pak = Pak::open(&installed).unwrap();
    let mut read = vec![0; changed.len()];
    pak.read_at(0, &mut read).unwrap();
    assert_eq!(read, changed);
    pak.verify().unwrap();
    for segment in &old.segments {
        assert!(installed
            .parent()
            .unwrap()
            .join(segment.filename())
            .exists());
    }
    let noop = fixture.0.join("noop");
    diff(fixture.source("same", &changed), &installed, &noop).unwrap();
    assert_eq!(
        fs::read(noop.join("game.aacm")).unwrap(),
        fs::read(&installed).unwrap()
    );
    assert_eq!(
        apply(&installed, &noop).unwrap(),
        ApplyResult::AlreadyApplied
    );
    // Existing readers retain the previous immutable bytes across a later manifest switch.
    fs::write(&installed, old_bytes).unwrap();
    let old_reader = Pak::open(&installed).unwrap();
    apply(&installed, &package).unwrap();
    let mut read = vec![0; original.len()];
    old_reader.read_at(0, &mut read).unwrap();
    assert_eq!(read, original);
}

#[test]
fn patch_reuses_aligned_moves_and_duplicates_with_exact_applied_bytes() {
    let fixture = Fixture::new();
    let random = content(8192 + 4 * 4096);
    let a = random[8192..12288].to_vec();
    let b = random[12288..16384].to_vec();
    let c = random[16384..20480].to_vec();
    let inserted = random[20480..24576].to_vec();
    let tail = a[..137].to_vec();
    let original = [a.clone(), b.clone(), a.clone(), c.clone(), tail.clone()].concat();
    let cases = [
        (
            "insert",
            [
                a.clone(),
                inserted.clone(),
                b.clone(),
                a.clone(),
                c.clone(),
                tail.clone(),
            ]
            .concat(),
            vec![Some(0), None, Some(1), Some(0), Some(3), Some(4)],
        ),
        (
            "delete",
            [a.clone(), a.clone(), c.clone(), tail.clone()].concat(),
            vec![Some(0), Some(0), Some(3), Some(4)],
        ),
        (
            "reorder",
            [c.clone(), a.clone(), b.clone(), a.clone(), tail.clone()].concat(),
            vec![Some(3), Some(0), Some(1), Some(0), Some(4)],
        ),
        (
            "duplicate",
            [a.clone(), b.clone(), a.clone(), c.clone(), a.clone(), tail].concat(),
            vec![Some(0), Some(1), Some(2), Some(3), Some(0), Some(4)],
        ),
    ];
    for (name, updated, reused) in cases {
        let installed = build(
            fixture.source(&format!("raw-{name}"), &original),
            fixture.0.join(format!("store-{name}")),
            4096,
        )
        .unwrap();
        let old = Manifest::load(&installed).unwrap();
        // The repeated base bytes have distinct stored offsets. This makes both
        // same-offset preference and first-logical-match fallback observable.
        assert_ne!(old.chunks[0].offset, old.chunks[2].offset);
        let package = fixture.0.join(format!("patch-{name}"));
        let candidate = diff(
            fixture.source(&format!("new-{name}"), &updated),
            &installed,
            &package,
        )
        .unwrap();
        let new = Manifest::load(&candidate).unwrap();
        assert_eq!(new.chunks.len(), reused.len());
        for (chunk, old_index) in new.chunks.iter().zip(&reused) {
            if let Some(index) = old_index {
                let previous = &old.chunks[*index];
                assert_eq!(chunk.hash, previous.hash);
                assert_eq!(chunk.decoded, previous.decoded);
                assert_eq!(chunk.codec, previous.codec);
                assert_eq!(chunk.offset, previous.offset);
                assert_eq!(chunk.stored, previous.stored);
                assert_eq!(
                    new.segments[chunk.segment as usize],
                    old.segments[previous.segment as usize]
                );
            }
        }
        let receipt: Receipt =
            serde_json::from_slice(&fs::read(package.join("receipt.json")).unwrap()).unwrap();
        let new_bytes: u64 = receipt
            .new_segments
            .iter()
            .map(|name| fs::metadata(package.join(name)).unwrap().len())
            .sum();
        if name == "insert" {
            assert_eq!(receipt.new_segments.len(), 1);
            assert_eq!(
                new_bytes,
                zstd::bulk::compress(&inserted, 3)
                    .unwrap()
                    .len()
                    .min(inserted.len()) as u64
            );
            assert_eq!(new.chunks[1].hash, hash(&inserted));
        } else {
            assert!(receipt.new_segments.is_empty());
            assert_eq!(new_bytes, 0);
        }
        assert_eq!(apply(&installed, &package).unwrap(), ApplyResult::Applied);
        let pak = Pak::open(&installed).unwrap();
        pak.verify().unwrap();
        let mut actual = vec![0xcd; updated.len() + 1];
        assert_eq!(pak.read_at(0, &mut actual).unwrap(), updated.len());
        assert_eq!(&actual[..updated.len()], updated);
        assert_eq!(actual[updated.len()], 0xcd);
        assert_eq!(
            apply(&installed, &package).unwrap(),
            ApplyResult::AlreadyApplied
        );
    }
}

#[test]
fn profile_partial_and_all_hot_preserve_logical_bytes_and_replay() {
    let fixture = Fixture::new();
    let data = [vec![1; 4096], vec![2; 4096], vec![3; 4096], vec![4; 233]].concat();
    let source = fixture.source("raw", &data);
    let header = "offset,bytes,microseconds,thread\n";
    for (name, rows, raw_indices) in [
        (
            "partial",
            "4095,2,0.125,7\n12300,1,12.000,8\n18446744073709551615,0,0,9\n",
            vec![0, 1, 3],
        ),
        ("all", "0,12521,0.000,7\n", vec![0, 1, 2, 3]),
    ] {
        let installed = build(&source, fixture.0.join(format!("store-{name}")), 4096).unwrap();
        let base_bytes = fs::read(&installed).unwrap();
        let old = Manifest::decode(&base_bytes).unwrap();
        assert!(old.chunks.iter().all(|chunk| chunk.codec == 1));
        let trace = fixture.source(
            &format!("trace-{name}"),
            format!("{header}{rows}").as_bytes(),
        );
        let package = fixture.0.join(format!("patch-{name}"));
        let candidate = profile(&installed, &trace, &package).unwrap();
        assert_eq!(fs::read(&installed).unwrap(), base_bytes);
        assert_eq!(fs::read(&source).unwrap(), data);
        let new = Manifest::load(candidate).unwrap();
        assert_eq!(new.logical_size, old.logical_size);
        assert_eq!(new.logical_hash, old.logical_hash);
        let mut expected_raw = Vec::new();
        for (index, chunk) in new.chunks.iter().enumerate() {
            assert_eq!(chunk.hash, old.chunks[index].hash);
            if raw_indices.contains(&index) {
                assert_eq!(chunk.codec, 0);
                assert_eq!(chunk.stored, chunk.decoded);
                assert_eq!(chunk.offset, expected_raw.len() as u64);
                let start = index * 4096;
                expected_raw.extend_from_slice(&data[start..start + chunk.decoded as usize]);
            } else {
                assert_eq!(chunk.codec, old.chunks[index].codec);
                assert_eq!(chunk.offset, old.chunks[index].offset);
                assert_eq!(
                    new.segments[chunk.segment as usize],
                    old.segments[old.chunks[index].segment as usize]
                );
            }
        }
        let receipt: Receipt =
            serde_json::from_slice(&fs::read(package.join("receipt.json")).unwrap()).unwrap();
        assert_eq!(receipt.new_segments.len(), 1);
        assert_eq!(
            fs::read(package.join(&receipt.new_segments[0])).unwrap(),
            expected_raw
        );
        assert_eq!(new.segments.len(), if name == "all" { 1 } else { 2 });
        assert_eq!(apply(&installed, &package).unwrap(), ApplyResult::Applied);
        let pak = Pak::open(&installed).unwrap();
        pak.verify().unwrap();
        let mut actual = vec![0; data.len()];
        assert_eq!(pak.read_at(0, &mut actual).unwrap(), data.len());
        assert_eq!(actual, data);
        assert_eq!(
            apply(&installed, &package).unwrap(),
            ApplyResult::AlreadyApplied
        );
        let repeat = fixture.0.join(format!("repeat-{name}"));
        profile(&installed, &trace, &repeat).unwrap();
        assert_eq!(
            fs::read(repeat.join("game.aacm")).unwrap(),
            fs::read(&installed).unwrap()
        );
        let receipt: Receipt =
            serde_json::from_slice(&fs::read(repeat.join("receipt.json")).unwrap()).unwrap();
        assert!(receipt.new_segments.is_empty());
        assert_eq!(
            apply(&installed, &repeat).unwrap(),
            ApplyResult::AlreadyApplied
        );
        for segment in &old.segments {
            assert!(installed
                .parent()
                .unwrap()
                .join(segment.filename())
                .exists());
        }
        assert!(profile(&installed, &trace, &package).is_err());
    }
}

#[test]
fn profile_empty_selection_and_already_raw_are_verified_noops() {
    let fixture = Fixture::new();
    let random = content(8192 + 4096);
    for (name, data, rows) in [
        ("empty", vec![7; 4096], ""),
        ("raw", random[8192..].to_vec(), "0,4096,0,1\n"),
    ] {
        let installed = build(
            fixture.source(&format!("raw-{name}"), &data),
            fixture.0.join(format!("store-{name}")),
            4096,
        )
        .unwrap();
        if name == "raw" {
            assert_eq!(Manifest::load(&installed).unwrap().chunks[0].codec, 0);
        }
        let before = fs::read(&installed).unwrap();
        let trace = fixture.source(
            &format!("trace-{name}"),
            format!("offset,bytes,microseconds,thread\r\n{rows}").as_bytes(),
        );
        let package = fixture.0.join(format!("patch-{name}"));
        profile(&installed, &trace, &package).unwrap();
        assert_eq!(fs::read(package.join("game.aacm")).unwrap(), before);
        let receipt: Receipt =
            serde_json::from_slice(&fs::read(package.join("receipt.json")).unwrap()).unwrap();
        assert!(receipt.new_segments.is_empty());
        assert_eq!(
            apply(&installed, &package).unwrap(),
            ApplyResult::AlreadyApplied
        );
        // Even a no-op must reject an unverified base segment.
        let old = Manifest::load(&installed).unwrap();
        let segment = installed.parent().unwrap().join(old.segments[0].filename());
        let mut bytes = fs::read(&segment).unwrap();
        bytes[0] ^= 1;
        fs::write(segment, bytes).unwrap();
        assert!(profile(&installed, &trace, fixture.0.join(format!("bad-{name}"))).is_err());
    }
}

#[test]
fn profile_rejects_malformed_overflow_out_of_range_and_oversized_traces() {
    use crate::store::{MAX_TRACE_BYTES, MAX_TRACE_ROWS};
    let fixture = Fixture::new();
    let installed = build(
        fixture.source("raw", &[7; 4096]),
        fixture.0.join("store"),
        4096,
    )
    .unwrap();
    let before = fs::read(&installed).unwrap();
    let header = "offset,bytes,microseconds,thread\n";
    let invalid_rows = [
        "",
        "offset,bytes\n0,1\n",
        "offset,bytes,microseconds,thread\n\n",
        "0,1,0\n",
        "-1,1,0,1\n",
        "18446744073709551616,1,0,1\n",
        "18446744073709551615,1,0,1\n",
        "0,4294967296,0,1\n",
        "4096,1,0,1\n",
        "4095,2,0,1\n",
        "../game_pak,1,0,1\n",
        "\"0\",1,0,1\n",
        "0,1,NaN,1\n",
        "0,1,inf,1\n",
        "0,1,-1,1\n",
        "0,1,1.2.3,1\n",
        "0,1,0,4294967296\n",
        "0,1,0,1,extra\n",
    ];
    for (index, row) in invalid_rows.iter().enumerate() {
        let text = if index < 3 {
            row.to_string()
        } else {
            format!("{header}{row}")
        };
        let trace = fixture.source(&format!("bad-trace-{index}"), text.as_bytes());
        let package = fixture.0.join(format!("bad-patch-{index}"));
        assert!(profile(&installed, trace, &package).is_err(), "row {index}");
        assert!(!package.join("game.aacm").exists());
    }
    let utf8 = fixture.source("bad-utf8", &[0xff]);
    assert!(profile(&installed, utf8, fixture.0.join("bad-utf8-patch")).is_err());
    let large = fixture.source("large-trace", &[]);
    fs::OpenOptions::new()
        .write(true)
        .open(&large)
        .unwrap()
        .set_len(MAX_TRACE_BYTES + 1)
        .unwrap();
    assert!(profile(&installed, large, fixture.0.join("large-patch")).is_err());
    let rows = format!("{header}{}", "0,0,0,0\n".repeat(MAX_TRACE_ROWS + 1));
    let many = fixture.source("many-trace", rows.as_bytes());
    assert!(profile(&installed, many, fixture.0.join("many-patch")).is_err());
    let long = fixture.source(
        "long-trace",
        format!("{header}{}\n", "1".repeat(257)).as_bytes(),
    );
    assert!(profile(&installed, long, fixture.0.join("long-patch")).is_err());
    assert_eq!(fs::read(&installed).unwrap(), before);
}

#[test]
fn rejected_update_leaves_manifest_unchanged() {
    let fixture = Fixture::new();
    let original = content(16384);
    let installed = build(
        fixture.source("raw", &original),
        fixture.0.join("store"),
        4096,
    )
    .unwrap();
    let old_bytes = fs::read(&installed).unwrap();
    let mut changed = original.clone();
    changed[9000] ^= 1;
    let package = fixture.0.join("patch");
    diff(fixture.source("new", &changed), &installed, &package).unwrap();
    let receipt_path = package.join("receipt.json");
    let receipt_bytes = fs::read(&receipt_path).unwrap();
    let mut receipt: Receipt = serde_json::from_slice(&receipt_bytes).unwrap();
    receipt.base_manifest_sha256 = "0".repeat(64);
    fs::write(&receipt_path, serde_json::to_vec(&receipt).unwrap()).unwrap();
    assert!(apply(&installed, &package).is_err());
    assert_eq!(fs::read(&installed).unwrap(), old_bytes);
    fs::write(&receipt_path, &receipt_bytes).unwrap();
    let receipt: Receipt = serde_json::from_slice(&receipt_bytes).unwrap();
    let blob = package.join(&receipt.new_segments[0]);
    let good = fs::read(&blob).unwrap();
    let mut corrupt = good.clone();
    corrupt[0] ^= 1;
    fs::write(&blob, corrupt).unwrap();
    assert!(apply(&installed, &package).is_err());
    assert_eq!(fs::read(&installed).unwrap(), old_bytes);
    fs::remove_file(&blob).unwrap();
    assert!(apply(&installed, &package).is_err());
    assert_eq!(fs::read(&installed).unwrap(), old_bytes);
    fs::write(&blob, good).unwrap();
    let mut receipt: Receipt = serde_json::from_slice(&receipt_bytes).unwrap();
    receipt.new_segments = vec!["../game_pak.aacb".into()];
    fs::write(&receipt_path, serde_json::to_vec(&receipt).unwrap()).unwrap();
    assert!(apply(&installed, &package).is_err());
    assert_eq!(fs::read(&installed).unwrap(), old_bytes);
}

#[test]
fn chunk_sizes_destination_collisions_and_source_growth_shrink() {
    let fixture = Fixture::new();
    let raw = fixture.source("raw", &content(8193));
    for size in [0, 1, 4095, 4097, 8 * 1048576] {
        assert!(build(&raw, fixture.0.join(format!("invalid-{size}")), size).is_err());
    }
    let installed = build(&raw, fixture.0.join("store"), 4096).unwrap();
    assert!(build(&raw, fixture.0.join("store"), 4096).is_err());
    for (name, data) in [
        ("grow", content(19000)),
        ("shrink", content(1000)),
        ("empty", vec![]),
    ] {
        let package = fixture.0.join(name);
        diff(
            fixture.source(&format!("source-{name}"), &data),
            &installed,
            &package,
        )
        .unwrap();
        apply(&installed, &package).unwrap();
        Pak::open(&installed).unwrap().verify().unwrap();
        assert_eq!(
            Manifest::load(&installed).unwrap().logical_size,
            data.len() as u64
        );
    }
}

#[test]
fn unending_package_readers_stop_at_receipt_and_segment_limits() {
    use crate::store::{check_segment_reader, read_receipt, stage_segment, MAX_RECEIPT_BYTES};
    use std::io::Read;

    #[derive(Default)]
    struct UnendingReader {
        bytes: u64,
    }
    impl Read for UnendingReader {
        fn read(&mut self, output: &mut [u8]) -> std::io::Result<usize> {
            output.fill(0);
            self.bytes += output.len() as u64;
            Ok(output.len())
        }
    }

    let mut receipt_source = UnendingReader::default();
    assert!(read_receipt(&mut receipt_source)
        .unwrap_err()
        .to_string()
        .contains("receipt too large"));
    assert_eq!(receipt_source.bytes, MAX_RECEIPT_BYTES + 1);

    let fixture = Fixture::new();
    let segment = Segment {
        hash: hash(&[0; 4096]),
        stored_length: 4096,
    };
    let mut verification_source = UnendingReader::default();
    assert!(check_segment_reader(&mut verification_source, &segment).is_err());
    assert_eq!(verification_source.bytes, segment.stored_length + 1);

    let mut staging_source = UnendingReader::default();
    assert!(stage_segment(&mut staging_source, &fixture.0, &segment).is_err());
    assert_eq!(staging_source.bytes, segment.stored_length + 1);
    assert_eq!(fs::read_dir(&fixture.0).unwrap().count(), 0);

    let overflowing = Segment {
        stored_length: u64::MAX,
        ..segment.clone()
    };
    let mut source = UnendingReader::default();
    assert!(check_segment_reader(&mut source, &overflowing).is_err());
    assert!(stage_segment(&mut source, &fixture.0, &overflowing).is_err());
    assert_eq!(source.bytes, 0);

    // Correct length alone cannot publish incorrect bytes under the expected hash.
    assert!(stage_segment(&[1u8; 4096][..], &fixture.0, &segment).is_err());
    assert_eq!(fs::read_dir(&fixture.0).unwrap().count(), 0);
    assert_eq!(
        stage_segment(&[0u8; 4096][..], &fixture.0, &segment).unwrap(),
        segment
    );
    assert_eq!(fs::read_dir(&fixture.0).unwrap().count(), 1);
    assert_eq!(
        fs::read(fixture.0.join(segment.filename())).unwrap(),
        vec![0; 4096]
    );
}

#[test]
fn oversized_receipt_and_blob_changes_before_staging_leave_manifest_unchanged() {
    let fixture = Fixture::new();
    let original = content(16384);
    let installed = build(
        fixture.source("raw", &original),
        fixture.0.join("store"),
        4096,
    )
    .unwrap();
    let old_bytes = fs::read(&installed).unwrap();
    let mut changed = original;
    changed[9000] ^= 1;
    let package = fixture.0.join("patch");
    diff(fixture.source("new", &changed), &installed, &package).unwrap();
    let receipt_path = package.join("receipt.json");
    let receipt_bytes = fs::read(&receipt_path).unwrap();
    fs::OpenOptions::new()
        .write(true)
        .open(&receipt_path)
        .unwrap()
        .set_len(crate::store::MAX_RECEIPT_BYTES + 1)
        .unwrap();
    assert!(apply(&installed, &package)
        .unwrap_err()
        .to_string()
        .contains("receipt too large"));
    assert_eq!(fs::read(&installed).unwrap(), old_bytes);
    fs::write(&receipt_path, &receipt_bytes).unwrap();

    let receipt: Receipt = serde_json::from_slice(&receipt_bytes).unwrap();
    let name = &receipt.new_segments[0];
    let blob = package.join(name);
    let good = fs::read(&blob).unwrap();
    let installed_blobs = || {
        fs::read_dir(installed.parent().unwrap())
            .unwrap()
            .filter(|entry| {
                entry.as_ref().unwrap().path().extension() == Some(std::ffi::OsStr::new("aacb"))
            })
            .count()
    };
    let original_blob_count = installed_blobs();
    let mut grown = good.clone();
    grown.extend_from_slice(&[0; 65536]);
    let mut corrupted = good.clone();
    corrupted[0] ^= 1;
    for invalid in [grown, good[..good.len() - 1].to_vec(), corrupted] {
        fs::write(&blob, &good).unwrap();
        // Package verification succeeds first; mutation occurs deterministically at
        // the same boundary where an incomplete download or writer could grow a blob.
        assert!(
            crate::store::apply_before_staging(&installed, &package, || {
                fs::write(&blob, &invalid)?;
                Ok(())
            })
            .is_err()
        );
        assert_eq!(fs::read(&installed).unwrap(), old_bytes);
        assert!(!installed.parent().unwrap().join(name).exists());
        assert_eq!(installed_blobs(), original_blob_count);
        assert!(!fs::read_dir(installed.parent().unwrap())
            .unwrap()
            .any(|entry| entry
                .unwrap()
                .file_name()
                .to_string_lossy()
                .ends_with(".tmp")));
    }
    fs::write(&blob, good).unwrap();
    assert_eq!(apply(&installed, &package).unwrap(), ApplyResult::Applied);
    Pak::open(&installed).unwrap().verify().unwrap();
}

#[cfg(unix)]
#[test]
fn unix_apply_bare_relative_manifest_path() {
    const CHILD_ENV: &str = "AAEMU_PAK_RELATIVE_PATH_TEST_CHILD";
    if std::env::var_os(CHILD_ENV).is_some() {
        assert_eq!(apply("game.aacm", "patch").unwrap(), ApplyResult::Applied);
        assert_eq!(
            apply("game.aacm", "patch").unwrap(),
            ApplyResult::AlreadyApplied
        );
        Pak::open("game.aacm").unwrap().verify().unwrap();
        return;
    }
    let fixture = Fixture::new();
    let original = content(16384);
    let installed = build(
        fixture.source("raw", &original),
        fixture.0.join("store"),
        4096,
    )
    .unwrap();
    let mut changed = original;
    changed[9000] ^= 1;
    let directory = installed.parent().unwrap();
    diff(
        fixture.source("new", &changed),
        &installed,
        directory.join("patch"),
    )
    .unwrap();
    // Change only the subprocess's working directory, so parallel tests are unaffected.
    let output = std::process::Command::new(std::env::current_exe().unwrap())
        .args([
            "--exact",
            "tests::unix_apply_bare_relative_manifest_path",
            "--nocapture",
        ])
        .env(CHILD_ENV, "1")
        .current_dir(directory)
        .output()
        .unwrap();
    assert!(
        output.status.success(),
        "{}\n{}",
        String::from_utf8_lossy(&output.stdout),
        String::from_utf8_lossy(&output.stderr)
    );
    let mut actual = vec![0; changed.len()];
    assert_eq!(
        Pak::open(&installed)
            .unwrap()
            .read_at(0, &mut actual)
            .unwrap(),
        changed.len()
    );
    assert_eq!(actual, changed);
}

#[test]
fn c_bridge_bounds_error_truncation_and_panic_containment() {
    use crate::ffi::*;
    assert_eq!(aaemu_pak_abi(), 1);
    unsafe {
        assert!(aaemu_pak_open(std::ptr::null(), 0).is_null());
        assert_eq!(aaemu_pak_size(std::ptr::null_mut()), -1);
        let length = aaemu_pak_error(std::ptr::null_mut(), 0);
        assert!(length > 0);
        let mut short = [0xcd; 8];
        assert_eq!(aaemu_pak_error(short.as_mut_ptr(), 4), length);
        assert_eq!(short[3], 0);
        assert_eq!(&short[4..], &[0xcd; 4]);
        assert_eq!(panic_probe(), u32::MAX);
        assert!(aaemu_pak_error(std::ptr::null_mut(), 0) > 0);
        let fixture = Fixture::new();
        let data = content(10000);
        let path = build(fixture.source("raw", &data), fixture.0.join("store"), 4096).unwrap();
        let bytes = path.to_string_lossy().as_bytes().to_vec();
        let handle = aaemu_pak_open(bytes.as_ptr(), bytes.len() as u32);
        assert!(!handle.is_null());
        assert_eq!(aaemu_pak_size(handle), 10000);
        assert_eq!(
            aaemu_pak_read_at(handle, 0, std::ptr::null_mut(), 1),
            u32::MAX
        );
        assert_eq!(aaemu_pak_read_at(handle, 0, std::ptr::null_mut(), 0), 0);
        let mut out = [0xcd; 32];
        assert_eq!(
            aaemu_pak_read_at(handle, 9999, out.as_mut_ptr().cast(), 32),
            1
        );
        assert_eq!(out[0], data[9999]);
        assert!(out[1..].iter().all(|b| *b == 0xcd));
        assert_eq!(
            aaemu_pak_read_at(handle, u64::MAX, out.as_mut_ptr().cast(), 32),
            0
        );
        assert_eq!(
            aaemu_pak_read_at(handle, 0, out.as_mut_ptr().cast(), u32::MAX),
            u32::MAX
        );
        aaemu_pak_close(handle);
        aaemu_pak_close(std::ptr::null_mut());
    }
}

#[test]
fn concurrent_shared_reader_and_c_context_preserve_bytes_and_cache_bound() {
    use crate::ffi::*;
    use std::sync::{Arc, Barrier};
    let fixture = Fixture::new();
    let data = Arc::new(content(CACHE_BYTES + 8193));
    let path = build(
        fixture.source("concurrent-raw", &data),
        fixture.0.join("store"),
        262144,
    )
    .unwrap();
    let pak = Arc::new(Pak::open(&path).unwrap());
    let barrier = Arc::new(Barrier::new(8));
    let mut threads = Vec::new();
    for worker in 0..8u32 {
        let pak = Arc::clone(&pak);
        let data = Arc::clone(&data);
        let barrier = Arc::clone(&barrier);
        threads.push(std::thread::spawn(move || {
            barrier.wait();
            let mut seed = worker + 1;
            for i in 0..96 {
                seed = seed.wrapping_mul(1664525).wrapping_add(1013904223);
                let offset = if i < 8 {
                    262143
                } else {
                    seed as usize % data.len()
                };
                let mut output = vec![0xcd; 65537];
                let n = pak.read_at(offset as u64, &mut output).unwrap();
                assert_eq!(&output[..n], &data[offset..offset + n]);
                assert!(output[n..].iter().all(|b| *b == 0xcd));
                assert!(pak.cached_bytes().unwrap() <= CACHE_BYTES);
            }
        }));
    }
    for thread in threads {
        thread.join().unwrap();
    }
    pak.verify().unwrap();
    let bytes = path.to_string_lossy().as_bytes().to_vec();
    let handle = unsafe { aaemu_pak_open(bytes.as_ptr(), bytes.len() as u32) };
    assert!(!handle.is_null());
    // The native caller owns the handle until all concurrent calls have joined.
    let address = handle as usize;
    let mut threads = Vec::new();
    for worker in 0..8 {
        let data = Arc::clone(&data);
        threads.push(std::thread::spawn(move || {
            let handle = address as *mut std::ffi::c_void;
            for i in 0..16 {
                let offset = 262143 + worker * 8192 + i * 4096;
                let mut output = vec![0xcd; 65537];
                let n = unsafe {
                    aaemu_pak_read_at(
                        handle,
                        offset as u64,
                        output.as_mut_ptr().cast(),
                        output.len() as u32,
                    )
                };
                assert_eq!(n, 65537);
                assert_eq!(&output, &data[offset..offset + output.len()]);
            }
            if worker == 0 {
                assert_eq!(
                    unsafe { aaemu_pak_read_at(std::ptr::null_mut(), 0, std::ptr::null_mut(), 1) },
                    u32::MAX
                );
                assert!(unsafe { aaemu_pak_error(std::ptr::null_mut(), 0) } > 0);
            } else {
                assert_eq!(unsafe { aaemu_pak_error(std::ptr::null_mut(), 0) }, 0);
            }
        }));
    }
    for thread in threads {
        thread.join().unwrap();
    }
    unsafe { aaemu_pak_close(handle) };
}

#[test]
fn os_writer_lock_survives_stale_file_and_releases_after_process_exit() {
    use crate::store::UpdateLock;
    if let Some(path) = std::env::var_os("AAEMU_PAK_LOCK_CHILD") {
        let _lock = UpdateLock::acquire(&PathBuf::from(path)).unwrap();
        // Deliberately skip Rust Drop; the operating system must release the lock.
        std::process::exit(0);
    }
    let fixture = Fixture::new();
    let lock_path = fixture.0.join(".aaemu-pak-update.lock");
    fs::write(&lock_path, b"stale marker from an interrupted process").unwrap();
    let lock = UpdateLock::acquire(&fixture.0).unwrap();
    let dir = fixture.0.clone();
    assert!(
        std::thread::spawn(move || UpdateLock::acquire(&dir).is_err())
            .join()
            .unwrap()
    );
    drop(lock);
    assert_eq!(
        fs::read(&lock_path).unwrap(),
        b"stale marker from an interrupted process"
    );
    drop(UpdateLock::acquire(&fixture.0).unwrap());
    let status = std::process::Command::new(std::env::current_exe().unwrap())
        .args([
            "--exact",
            "tests::os_writer_lock_survives_stale_file_and_releases_after_process_exit",
        ])
        .env("AAEMU_PAK_LOCK_CHILD", &fixture.0)
        .status()
        .unwrap();
    assert!(status.success());
    drop(UpdateLock::acquire(&fixture.0).unwrap());
    assert!(lock_path.exists());
}

#[cfg(windows)]
#[test]
fn windows_apply_rejects_live_native_client_and_writer_blocks_new_client() {
    use std::fs::OpenOptions;
    use std::os::windows::fs::OpenOptionsExt;
    let fixture = Fixture::new();
    let original = content(16384);
    let installed = build(
        fixture.source("raw", &original),
        fixture.0.join("store"),
        4096,
    )
    .unwrap();
    let before = fs::read(&installed).unwrap();
    let mut updated = original;
    updated[9000] ^= 1;
    let package = fixture.0.join("patch");
    diff(fixture.source("updated", &updated), &installed, &package).unwrap();
    let client = OpenOptions::new()
        .read(true)
        .share_mode(1)
        .open(&installed)
        .unwrap();
    match apply(&installed, &package) {
        Err(Error::Io(error)) => assert_eq!(error.raw_os_error(), Some(32)),
        other => panic!("expected client sharing violation, got {other:?}"),
    }
    assert_eq!(fs::read(&installed).unwrap(), before);
    drop(client);
    // The updater's held write-access guard also excludes a newly starting native
    // client using FILE_SHARE_READ, even though the guard itself allows sharing.
    let writer = OpenOptions::new()
        .read(true)
        .write(true)
        .open(&installed)
        .unwrap();
    assert_eq!(
        OpenOptions::new()
            .read(true)
            .share_mode(1)
            .open(&installed)
            .unwrap_err()
            .raw_os_error(),
        Some(32)
    );
    drop(writer);
    assert_eq!(apply(&installed, &package).unwrap(), ApplyResult::Applied);
    Pak::open(&installed).unwrap().verify().unwrap();
    assert_ne!(fs::read(&installed).unwrap(), before);
}

#[cfg(windows)]
#[test]
fn windows_publication_client_race_keeps_one_verified_generation() {
    use crate::store::apply_before_publication;
    use std::fs::OpenOptions;
    use std::os::windows::fs::OpenOptionsExt;
    let fixture = Fixture::new();
    let original = content(16384);
    let installed = build(
        fixture.source("raw", &original),
        fixture.0.join("store"),
        4096,
    )
    .unwrap();
    let before = fs::read(&installed).unwrap();
    let mut updated = original.clone();
    updated[9000] ^= 1;
    let package = fixture.0.join("patch");
    diff(fixture.source("updated", &updated), &installed, &package).unwrap();
    let mut client = None;
    let result = apply_before_publication(&installed, &package, || {
        // Force the client to win after the updater releases its write-access guard.
        client = Some(
            OpenOptions::new()
                .read(true)
                .share_mode(1)
                .open(&installed)?,
        );
        Ok(())
    });
    match result {
        Err(Error::Io(error)) => assert!(matches!(error.raw_os_error(), Some(5 | 32))),
        other => panic!("expected rename blocked by the native client, got {other:?}"),
    }
    assert_eq!(fs::read(&installed).unwrap(), before);
    let old = Pak::open(&installed).unwrap();
    let mut data = vec![0; original.len()];
    old.read_at(0, &mut data).unwrap();
    assert_eq!(data, original);
    drop(client);
    // Force publication to win; a subsequently starting client sees only new bytes.
    assert_eq!(
        apply_before_publication(&installed, &package, || Ok(())).unwrap(),
        ApplyResult::Applied
    );
    let _client = OpenOptions::new()
        .read(true)
        .share_mode(1)
        .open(&installed)
        .unwrap();
    let new = Pak::open(&installed).unwrap();
    new.verify().unwrap();
    new.read_at(0, &mut data).unwrap();
    assert_eq!(data, updated);
    // An older immutable reader still owns its old segment generation.
    old.read_at(0, &mut data).unwrap();
    assert_eq!(data, original);
}
