use std::io::Cursor;

use chrono::TimeZone;

use super::*;
use crate::test_util::TempDir;

fn t(y: i32, mo: u32, d: u32) -> DateTime<Utc> {
    Utc.with_ymd_and_hms(y, mo, d, 12, 34, 56).unwrap()
}

/// The same operation sequence run through the C# `AAPak` (net10.0) produced files with these
/// MD5s; the Rust writer must be byte-identical.
#[test]
fn write_matches_csharp_byte_for_byte() {
    fn data(n: usize, seed: usize) -> Cursor<Vec<u8>> {
        Cursor::new((0..n).map(|i| ((i * 7 + seed) % 251) as u8).collect())
    }
    let md5_file = |p: &Path| hex_upper(&Md5::digest(std::fs::read(p).unwrap()));
    let dir = TempDir::new("aapak_cs");
    let path = dir.path().join("cs.pak");
    let t = t(2020, 1, 2);
    let mut pak = AAPak::create(&path).unwrap();
    pak.add_file_from_stream("dir/a.bin", &mut data(1000, 1), t, t, false)
        .unwrap();
    pak.add_file_from_stream("dir/sub/b.txt", &mut data(11, 2), t, t, true)
        .unwrap();
    pak.add_file_from_stream("big.dat", &mut data(5000, 3), t, t, true)
        .unwrap();
    pak.close().unwrap();
    assert_eq!(md5_file(&path), "D63D5F1C77C0E5F5C3E2A8550DE068E0");
    let mut pak = AAPak::open(&path, false).unwrap();
    pak.add_file_from_stream(
        "dir/a.bin",
        &mut data(900, 4),
        t,
        t + chrono::Duration::days(1),
        false,
    )
    .unwrap();
    pak.add_file_from_stream("dir/sub/b.txt", &mut data(2000, 5), t, t, false)
        .unwrap();
    pak.add_file_from_stream("c.txt", &mut data(3, 6), t, t, false)
        .unwrap();
    pak.delete_file("big.dat");
    pak.add_file_from_stream("d.txt", &mut data(700, 7), t, t, false)
        .unwrap();
    drop(pak); // Drop saves like the C# finalizer / ClosePak
    assert_eq!(md5_file(&path), "95AE9555FC57B44064427559DA251825");
    let check = AAPak::open(&path, true).unwrap();
    assert_eq!(
        check.file_names().collect::<Vec<_>>(),
        ["dir/a.bin", "dir/sub/b.txt", "d.txt", "c.txt"]
    );
}

#[test]
fn aes_cbc_nist_vector() {
    let key = hex_bytes("2b7e151628aed2a6abf7158809cf4f3c");
    let iv = hex_bytes("000102030405060708090a0b0c0d0e0f");
    let plain = hex_bytes("6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e51");
    let enc = encrypt_aes_using_iv(&plain, &key, &iv, true).unwrap();
    assert_eq!(
        enc,
        hex_bytes("7649abac8119b246cee98e9b12e9197d5086cb9b507219ee95db113a917678b2")
    );
    assert_eq!(encrypt_aes_using_iv(&enc, &key, &iv, false).unwrap(), plain);
    // Zero-IV helper and stream variants agree.
    let e2 = encrypt_aes(&plain, &XL_GAMES_KEY, true).unwrap();
    let mut out = Vec::new();
    encrypt_stream_aes(&mut Cursor::new(&e2), &mut out, &XL_GAMES_KEY, false).unwrap();
    assert_eq!(out, plain);
    // Incomplete block / bad key size are errors (C#: null + LastAESError).
    assert!(matches!(
        encrypt_aes(&[0; 15], &XL_GAMES_KEY, true),
        Err(PakError::Aes(_))
    ));
    assert!(matches!(
        encrypt_aes(&[0; 16], &[0; 15], true),
        Err(PakError::Aes(_))
    ));
    // AES-256 keys are accepted like in .NET.
    assert!(encrypt_aes(&[0; 16], &[1; 32], true).is_ok());
}

fn hex_bytes(s: &str) -> Vec<u8> {
    string_to_byte_array(s).unwrap()
}

#[test]
fn entry_layouts_roundtrip() {
    let pfi = PakFileInfo {
        name: "game/worlds/main_world/world.xml".into(),
        offset: 0x0012_3456_789A,
        size: 1000,
        size_duplicate: 1000,
        padding_size: 24,
        md5: [7; 16],
        dummy1: 0x8000_0000,
        create_time: 111,
        modify_time: 222,
        dummy2: 333,
        ..Default::default()
    };
    for ty in [PakFileType::TypeA, PakFileType::TypeB, PakFileType::TypeF] {
        let raw = pfi.to_decrypted(ty).unwrap();
        let mut back = PakFileInfo::from_decrypted(&raw, ty);
        if ty == PakFileType::TypeF {
            // TypeF has no room for dummy2 at the end... it is stored first; all fields survive.
            assert_eq!(back.dummy2, 333);
        }
        back.entry_index_number = -1;
        assert_eq!(back, pfi, "{ty:?}");
    }
    let a = pfi.to_decrypted(PakFileType::TypeA).unwrap();
    assert_eq!(&a[..4], b"game");
    assert_eq!(&a[0x108..0x110], &0x0012_3456_789Ai64.to_le_bytes());
    let b = pfi.to_decrypted(PakFileType::TypeB).unwrap();
    assert_eq!(&b[0..4], &24i32.to_le_bytes());
    assert_eq!(&b[0x20..0x24], b"game");
    let f = pfi.to_decrypted(PakFileType::TypeF).unwrap();
    assert_eq!(&f[0..8], &333u64.to_le_bytes());
    assert_eq!(&f[8..12], b"game");
    assert!(pfi.to_decrypted(PakFileType::Csv).is_none());
}

#[test]
fn header_types_detected() {
    for ty in [PakFileType::TypeA, PakFileType::TypeB, PakFileType::TypeF] {
        let mut h = PakHeader {
            file_count: 5,
            extra_file_count: 2,
            ..Default::default()
        };
        h.encrypt_header_data(ty).unwrap();
        let mut h2 = PakHeader {
            raw_data: h.raw_data,
            ..Default::default()
        };
        assert_eq!(h2.decrypt_header_data(false).unwrap(), Some(ty));
        assert!(h2.is_valid);
        assert_eq!((h2.file_count, h2.extra_file_count), (5, 2));
    }
    let mut bad = PakHeader::default();
    assert_eq!(bad.decrypt_header_data(false).unwrap(), None);
    assert!(!bad.is_valid);
}

#[test]
fn create_add_read_replace_delete_roundtrip() {
    let dir = TempDir::new("aapak_rt");
    let path = dir.path().join("test.pak");
    let data1: Vec<u8> = (0..1000u32).map(|i| (i % 251) as u8).collect();
    let data2 = b"hello world".to_vec();
    {
        let mut pak = AAPak::create(&path).unwrap();
        assert_eq!(pak.pak_type(), PakFileType::TypeA);
        assert_eq!(pak.file_count(), 0);
        let f1 = pak
            .add_file_from_stream(
                "dir/a.bin",
                &mut Cursor::new(&data1),
                t(2020, 1, 2),
                t(2021, 3, 4),
                false,
            )
            .unwrap();
        assert_eq!(f1.offset, 0);
        assert_eq!(f1.padding_size, 1024 - 1000);
        assert_eq!(f1.md5, <[u8; 16]>::from(Md5::digest(&data1)));
        let f2 = pak
            .add_file_from_stream(
                "dir/sub/b.txt",
                &mut Cursor::new(&data2),
                t(2020, 1, 2),
                t(2021, 3, 4),
                true,
            )
            .unwrap();
        assert_eq!(f2.offset, 1024);
        assert_eq!(f2.padding_size, 512 - 11);
        pak.close().unwrap();
    }
    {
        let pak = AAPak::open(&path, true).unwrap();
        assert!(pak.read_only());
        assert_eq!(pak.file_count(), 2);
        assert_eq!(pak.read_file("dir/a.bin").unwrap().unwrap(), data1);
        assert_eq!(pak.read_file("dir\\sub\\b.txt").unwrap().unwrap(), data2);
        assert!(pak.read_file("missing").unwrap().is_none());
        let a = pak.get_file_by_name("dir/a.bin").unwrap();
        assert_eq!(a.create_time, date_time_to_filetime(t(2020, 1, 2)));
        assert_eq!(a.entry_index_number, 0);
        assert_eq!(pak.get_file_by_index(1).unwrap().name, "dir/sub/b.txt");
        assert_eq!(pak.find_file_by_offset(1500).unwrap().name, "dir/sub/b.txt");
        assert_eq!(pak.header.add_file_offset, 1536);
        assert_eq!(pak.header.first_file_info_offset, 1536);
        assert_eq!(pak.get_files_in_directory("DIR").len(), 1);
        // File length: data (1536) + 2 entries (0x2A0) padded to 0x400 + header.
        assert_eq!(
            std::fs::metadata(&path).unwrap().len(),
            1536 + 0x400 + 0x200
        );
    }
    {
        let mut pak = AAPak::open(&path, false).unwrap();
        // Fits in place -> replaced.
        let small = vec![9u8; 900];
        let f = pak
            .add_file_from_stream(
                "dir/a.bin",
                &mut Cursor::new(&small),
                t(2022, 1, 1),
                t(2022, 1, 1),
                false,
            )
            .unwrap();
        assert_eq!((f.offset, f.size, f.padding_size), (0, 900, 124));
        assert_eq!(f.md5, <[u8; 16]>::from(Md5::digest(&small)));
        // Too big -> old one deleted (becomes an extra entry) and appended at the end.
        let big = vec![3u8; 2000];
        let f = pak
            .add_file_from_stream(
                "dir/sub/b.txt",
                &mut Cursor::new(&big),
                t(2022, 1, 1),
                t(2022, 1, 1),
                false,
            )
            .unwrap();
        assert_eq!(f.offset, 1536);
        assert_eq!(pak.extra_files.len(), 1);
        assert_eq!(pak.extra_files[0].name, UNUSED_FILE_NAME);
        assert_eq!(
            (pak.extra_files[0].offset, pak.extra_files[0].size),
            (1024, 512)
        );
        // A new small file reuses the free entry.
        let f = pak
            .add_as_new_file(
                "c.txt",
                &mut Cursor::new(b"abc"),
                t(2022, 1, 1),
                t(2022, 1, 1),
                false,
            )
            .unwrap();
        assert_eq!((f.offset, f.padding_size), (1024, 509));
        assert!(pak.extra_files.is_empty());
        assert!(pak.delete_file("c.txt"));
        assert!(pak.delete_file("does/not/exist"));
        pak.generate_folder_list(true);
        assert_eq!(pak.folders, vec!["dir".to_string(), "dir/sub".to_string()]);
        assert!(pak.set_md5("dir/a.bin", &[1; 16]));
        assert!(!pak.set_md5("dir/a.bin", &[1; 15]));
        assert_eq!(
            pak.update_md5("dir/a.bin").unwrap(),
            hex_upper(&Md5::digest(&small))
        );
        pak.close().unwrap();
    }
    {
        let pak = AAPak::open(&path, true).unwrap();
        assert_eq!(pak.file_count(), 2);
        assert_eq!(pak.extra_files.len(), 1);
        assert_eq!(pak.extra_files[0].deleted_index_number, 0);
        assert_eq!(pak.read_file("dir/a.bin").unwrap().unwrap(), vec![9u8; 900]);
        assert_eq!(
            pak.read_file("dir/sub/b.txt").unwrap().unwrap(),
            vec![3u8; 2000]
        );
        // Read-only paks refuse changes.
        let mut pak = pak;
        assert!(!pak.delete_file("dir/a.bin"));
        assert!(matches!(
            pak.add_as_new_file("x", &mut Cursor::new(b"x"), Utc::now(), Utc::now(), false),
            Err(PakError::ReadOnly)
        ));
    }
}

fn hex_upper(b: &[u8]) -> String {
    b.iter().map(|b| format!("{b:02X}")).collect()
}

#[test]
fn padding_delete_mode_grows_previous_file() {
    let dir = TempDir::new("aapak_pdm");
    let path = dir.path().join("p.pak");
    let mut pak = AAPak::create(&path).unwrap();
    pak.add_as_new_file(
        "a",
        &mut Cursor::new(vec![1u8; 100]),
        Utc::now(),
        Utc::now(),
        false,
    )
    .unwrap();
    pak.add_as_new_file(
        "b",
        &mut Cursor::new(vec![2u8; 600]),
        Utc::now(),
        Utc::now(),
        false,
    )
    .unwrap();
    pak.padding_delete_mode = true;
    assert!(pak.delete_file("b"));
    let a = pak.get_file_by_name("a").unwrap();
    assert_eq!(a.padding_size, 412 + 600 + 424);
    assert!(pak.extra_files.is_empty());
}

#[test]
fn custom_key_and_invalid_header() {
    let dir = TempDir::new("aapak_key");
    let path = dir.path().join("k.pak");
    std::fs::write(&path, vec![0u8; 0x400]).unwrap();
    assert!(matches!(
        AAPak::open(&path, true),
        Err(PakError::InvalidHeader)
    ));
    assert!(matches!(
        AAPak::open(dir.path().join("nope.pak"), true),
        Err(PakError::NotFound(_))
    ));

    // Build a pak with a custom key by hand.
    let key = [0x42u8; 16];
    let mut h = PakHeader::default();
    h.set_custom_key(&key);
    h.encrypt_header_data(PakFileType::TypeF).unwrap();
    std::fs::write(&path, h.raw_data).unwrap();
    assert!(AAPak::open(&path, true).is_err());
    let pak = AAPak::open_with(
        &path,
        PakOpenOptions {
            key: Some(key.to_vec()),
            ..Default::default()
        },
    )
    .unwrap();
    assert_eq!(pak.pak_type(), PakFileType::TypeF);
    assert_eq!(pak.header.key(), key);
}

#[test]
fn csv_virtual_pak() {
    let dir = TempDir::new("aapak_csv");
    let path = dir.path().join("list.CSV");
    std::fs::write(
        &path,
        format!(
            "{CSV_HEADER}\r\ngame/a.xml;10;512;00112233445566778899AABBCCDDEEFF;2014-10-14 01:02:03;bad;10;502;0;0\r\nshort;line\r\n"
        ),
    )
    .unwrap();
    let pak = AAPak::open(&path, false).unwrap();
    assert!(pak.is_virtual());
    assert!(pak.read_only());
    assert_eq!(pak.pak_type(), PakFileType::Csv);
    assert_eq!(pak.file_count(), 1);
    let f = pak.get_file_by_name("game/a.xml").unwrap();
    assert_eq!((f.size, f.offset, f.padding_size), (10, 512, 502));
    assert_eq!(f.md5[15], 0xFF);
    assert_eq!(
        f.create_time,
        date_time_to_filetime(Utc.with_ymd_and_hms(2014, 10, 14, 1, 2, 3).unwrap())
    );
    assert_eq!(f.modify_time, 0);
    assert!(pak.read_file("game/a.xml").is_err());

    std::fs::write(&path, "wrong header\r\n").unwrap();
    assert!(matches!(
        AAPak::open(&path, true),
        Err(PakError::InvalidCsv(_))
    ));
}

#[test]
fn string_helpers() {
    assert_eq!(string_to_byte_array("0aFF").unwrap(), vec![0x0A, 0xFF]);
    assert!(string_to_byte_array("abc").is_err());
    assert!(string_to_byte_array("zz").is_err());
    assert_eq!(
        date_time_to_date_time_str(t(2014, 10, 14)),
        "20141014-123456"
    );
    assert_eq!(date_time_str_to_filetime("short"), 0);
    assert_eq!(date_time_str_to_filetime("2014-13-14 00:00:00"), 0);
    assert_eq!(date_time_str_to_filetime("1601-01-01 00:00:00"), 0);
    assert_eq!(date_time_str_to_filetime("1601-01-01 00:00:01"), 10_000_000);
    assert_eq!(
        filetime_to_date_time(date_time_to_filetime(t(2000, 2, 29))),
        Some(t(2000, 2, 29))
    );
    assert_eq!(
        system_time_to_filetime(UNIX_EPOCH),
        date_time_to_filetime(DateTime::UNIX_EPOCH)
    );
    assert_eq!(
        byte_array_to_hex_string(&(0u8..17).collect::<Vec<_>>(), " ", "\n"),
        "00 01 02 03  04 05 06 07   08 09 0A 0B  0C 0D 0E 0F \n10 "
    );
    assert_eq!(to_pak_slashes("a\\b/c"), "a/b/c");
    assert_eq!(directory_name("a/b/c.txt"), "a/b");
    assert_eq!(directory_name("a/b/"), "a/b");
    assert_eq!(directory_name("c.txt"), "");
    assert_eq!(
        stream_to_string(&mut Cursor::new(b"\xEF\xBB\xBFhi")).unwrap(),
        "hi"
    );
}

/// The C# `Dictionary` reuses the slot of a removed key for the next added key; the file
/// table must be written in that order.
#[test]
fn dictionary_order_emulated() {
    let mut t = FileTable::default();
    for n in ["a", "b", "c"] {
        t.insert(PakFileInfo {
            name: n.into(),
            ..Default::default()
        })
        .unwrap();
    }
    t.remove("b");
    t.insert(PakFileInfo {
        name: "d".into(),
        ..Default::default()
    })
    .unwrap();
    t.insert(PakFileInfo {
        name: "e".into(),
        ..Default::default()
    })
    .unwrap();
    let names: Vec<_> = t.iter().map(|f| f.name.as_str()).collect();
    assert_eq!(names, ["a", "d", "c", "e"]);
    assert!(matches!(
        t.insert(PakFileInfo {
            name: "a".into(),
            ..Default::default()
        }),
        Err(PakError::DuplicateFileName(_))
    ));
    assert_eq!(t.len(), 4);
}
