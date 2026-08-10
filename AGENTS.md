# AAEmu 전체 작동 구조 및 디버깅 가이드

> 프로젝트: `F:\테스팅\AAEmu-client_version-zone-10.0.2_r575`
> 마지막 갱신: 2026-08-09
> 용도: 다음 작업에서 되풀이되는 실수 방지, 빠른 원인 추적

---

## 1. 실행 파일 및 프로젝트

| 프로젝트 | 출력 | 역할 |
|---|---|---|
| `AAEmu.Game` | `AAEmu.Game.exe` | Zone Server (AI, NPC, 전투, 스킬, 퀘스트). `AAEmu.World`는 `AAEmu.Game.dll` 참조 |
| `AAEmu.WorldServer/AAEmu.World` | `AAEmu.World.exe` | World Server (클라이언트 CS/SC, Zone 중계 ZW/WZ, 플레이어/미러 관리) |
| `AAEmu.Login` | `AAEmu.Login.exe` | Login Server (Kestrel, 인증, 캐릭터, 월드 목록) |
| `AAEmu.Aspire.AppHost` | Aspire orchestration | MySQL/Login/Game 배포, health check, OTLP (사용자는 주로 사용 안 함) |
| `AAEmu.Commons` | `AAEmu.Commons.dll` | 공용 네트워크/IO/DB/모델 |
| `Tools/WorldConverter` | 변환 툴 | 월드 데이터 변환 |
| `Tools/UpdatesForTransform` | 변환 툴 | transform 업데이트 |

### 포트
- `1239`: client ↔ World (CS/SC)
- `1240`: Zone ↔ World (ZW/WZ)
- `1241`: legacy Game → World bridge (선택, `GameBridge`)
- `1250`: Stream network

---

## 2. AAEmu.Game (Zone Server) 기동 흐름

```
Program.Main
  ↓ Config.json + Config.Local.json 로드
  ↓ MySQL + compact.sqlite3 연결 테스트
  ↓ HostBuilder DI 등록
  ↓ GameService.StartAsync
    ├─ MySqlDatabaseUpdater.Run (DB 마이그레이션)
    ├─ ClientFileManager.Initialize (클라이언트 게임 데이터 로드)
    ├─ ManagerOrchestrator.RunLoadAsync (ILoadable managers 병렬 Load)
    ├─ GameDataManager.PostLoadGameData
    ├─ ScriptCompiler.Compile / ScriptReflector.Reflect
    ├─ TaskManager.Start
    ├─ ManagerOrchestrator.RunInitializeAsync (IInitializable managers 병렬 Initialize)
    ├─ WorldManager.CreateStaticInstances
    ├─ WorldManager.Initialize
    ├─ CharacterManager.CheckForDeletedCharacters
    ├─ GameNetwork.Start (1239)
    ├─ StreamNetwork.Start (1250)
    ├─ LoginNetwork.Start (Login 서버 연결)
    └─ MirrorSpawnStreamTask (AOI 관리, pending UnitState 플러시)
```

### 핵심 매니저
- **WorldManager**: world/zone 인스턴스, 좌표 변환
- **ZoneManager**: zone 정의, 좌표계
- **NpcManager**: NPC 템플릿/스폰/상점
- **CharacterManager**: 플레이어 캐릭터
- **SkillManager**: 스킬/버프/효과
- **QuestManager**: 퀘스트 템플릿, 평가
- **ItemManager**: 아이템/인벤토리/장비
- **HousingManager**: 하우징/세금/건설
- **AuctionManager**: 경매장
- **IndunManager**: 던전/인던
- **ChatManager**, **FamilyManager**, **ExpeditionManager**, **CraftManager**, etc.

---

## 3. AAEmu.World (World Server) 기동 흐름

```
Program.Main
  ↓ NLog.config, Config.json/Config.Local.json
  ↓ ZoneNetwork.Start (1240)
  ↓ GameBridgeNetwork.Start (1241, optional)
  ↓ PlayerEnterService, MovementRelay
  ↓ WorldIntegration 콜백 등록
    ├─ TryEnterZone → PlayerEnterService.EnterZone
    ├─ IsZoneLoaded → ZoneSession
    ├─ OnZoneNpcSpawn/Remove/Killed → MirrorZoneNpcSpawn/Remove/Killed
    ├─ RelayUnitStateToZone → WZUnitStatePacket
    ├─ OnPlayerLeave → LeaveZone
    ├─ RelaySkillStarted/FiredToZone → WZSkillStarted/FiredPacket
    └─ OnMainWorldReady → NpcSpawnRelay.RemirrorAllZones
```

### 핵심 컴포넌트
- **ZoneNetwork**: Zone(TCP) 연결 리스너
- **ZoneProtocolHandler**: ZW 패킷 분배
- **PlayerEnterService**: 플레이어 존 입장/이탈/핸드오프
- **NpcSpawnRelay**: `ZWSpawnNpc` → 미러 `Npc` 생성 → `SCUnitState`
- **MovementRelay**: `ZWUnitMovements` → `SCUnitMovements`
- **ZoneSimRelay**: `ZWUnitModelPostureChanged`, `EnterArea`, `LeaveArea`, gimmick, housing
- **CombatRelay**: 전투 이벤트 ZW → SC

---

## 4. 전체 연결 및 패킷 방향

```
Client (ArcheAge)
  ↑↓ CS/SC :1239
AAEmu.World.exe
  ↑↓ ZW/WZ :1240
  ├─ AAEmu.Game.dll (in-process zone 로직, 사용자가 Game.exe 대신 ZoneHost 사용 시 덜 탐)
  └─ AAEmu.ZoneHost.exe (native zone sim, ZOneManager(Full) 사용 시)
```

### CS/SC (Client ↔ World)
- **CS**: `CSEnterWorld`, `CSSelectCharacter`, `CSStartSkill`, `CSMoveUnit` …
  - 처리: `AAEmu.Game/Core/Network/Game/GameProtocolHandler` (World가 Game.dll 내 핸들러 사용)
- **SC**: `SCUnitState`(0x97), `SCUnitMovements`(0x99), `SCUnitModelPostureChanged`(0x142) …
  - `GameNetwork` → `GameConnection.SendPacket`

### ZW/WZ (Zone ↔ World)
- **ZW (Zone→World)**: `ZWJoin`(0x00), `ZWSpawnNpc`(0x02), `ZWUnitMovements`(0x08), `ZWUnitModelPostureChanged`(0x0F) …
  - 처리: `AAEmu.World/Core/Network/ZoneProtocolHandler`
- **WZ (World→Zone)**: `WZUnitState`(0x07), `WZNpcState`, `WZActivateNpcSpawnersInArea`, `WZSkillStarted` …
  - `ZoneConnection.SendPacket`

---

## 5. NPC / Unit 라이프사이클

### 스폰
```
ZoneHost / Game zone
  ↓ ZWSpawnNpc (0x02) → World
  ↓ ZoneProtocolHandler
  ↓ NpcSpawnRelay.OnSpawn
    ├─ NpcScheduleGate 체크 (시간 기반)
    ├─ WorldIntegration.MirrorZoneNpcSpawn
    │   ├─ NpcManager.Create(world, bcId, templateId)
    │   ├─ npc.IsZoneMirror = true
    │   ├─ 좌표 변환 (zone local → world)
    │   ├─ npc.Spawn() → SCUnitState 전송
    │   └─ (필요 시) npc.AnimActionId 오버라이드
    └─ WZNpcStatePacket → Zone 응답
```

### 이동
```
ZoneHost sim
  ↓ ZWUnitMovements (0x08)
  ↓ MovementRelay.RelayZoneMoveToClient
    ├─ Stance, ActorFlags, DeltaY, 위치 파싱
    ├─ 좌표 변환
    └─ SCUnitMovementsPacket → 관심 클라이언트
```

### 자세 변경
```
ZoneHost
  ↓ ZWUnitModelPostureChanged (0x0F)
  ↓ ZoneSimRelay.RelayIdenticalUnitPacket
  ↓ SCUnitModelPostureChangedPacket
```

### 제거
```
ZoneHost
  ↓ ZWRemoveNpc
  ↓ NpcSpawnRelay.HandleRemoveNpc
    ├─ WorldIntegration.MirrorZoneNpcRemove
    └─ SCUnitsRemovedPacket
```

---

## 6. 설정 및 데이터베이스

### 설정 파일
- `AAEmu.Game/Config.json` / `Config.Local.json`
- `AAEmu.WorldServer/AAEmu.World/Config.json` / `Config.Local.json`
- `AAEmu.Login/Config.json` / `Config.Local.json`
- `NLog.config`: 로그 레벨/대상

### 데이터 소스
- **MySQL**: 계정, 캐릭터, 우편, 경매, 길드, 동맹 등 영속 데이터
- **compact.sqlite3**: 클라이언트 sqlite (아이템, NPC, 스킬, 애니메이션, zone 등)
- **ClientData**: `AAEmu.Game/ClientData`, `AAEmu.Game/Data` (게임 파일/JSON)
- **Scripts**: `AAEmu.Game/Scripts` (게임 스크립트, 컴파일/리플렉션 로드)

### 주요 DB 테이블
- `npcs`, `npc_postures`, `npc_spawns`, `npc_skills`
- `skills`, `skill_effects`
- `items`, `item_containers`
- `characters`, `characters_deleted`
- `houses`, `auction_lots`, `mails`
- `doodads`, `spawners`

---

## 7. 주요 서브시스템

- **Skills**: `Models/Game/Skills/Skill.cs`, `SkillManager`, `SkillController`, `Buff`, `Effect`
- **Quests**: `Models/Game/Quests/Quest.cs`, `QuestManager`, `QuestActs`
- **Housing**: `HousingManager`, `Models/Game/Housing`
- **Auction**: `AuctionManager`, `Models/Game/Auction`
- **Indun**: `IndunManager`, `Models/Game/Indun`
- **Craft**: `CraftManager`, `Models/Game/Crafts`
- **Chat**: `ChatManager`, `Models/Game/Chat`
- **Family/Expedition**: `FamilyManager`, `ExpeditionManager`
- **Items**: `ItemManager`, `Models/Game/Items`
- **Doodads**: `DoodadManager`, `Models/Game/DoodadObj`
- **Slaves/Transfers**: `SlaveManager`, `TransferManager` (탈것/이동체)

---

## 8. 빌드 및 실행

### 주요 스크립트
- `Scripts/BuildGameServer.bat`: `AAEmu.Game` 빌드
- `Scripts/BuildWorldServer.bat`: `AAEmu.World` 빌드 후 `dotnet run`
- `Scripts/BuildLoginServer.bat`: `AAEmu.Login` 빌드

### 수동 빌드
```powershell
# Game
 dotnet build AAEmu.Game\AAEmu.Game.csproj -c Debug

# World (Game.dll도 같이 빌드/복사됨)
 dotnet build AAEmu.WorldServer\AAEmu.World\AAEmu.World.csproj -c Debug

# Login
 dotnet build AAEmu.Login\AAEmu.Login.csproj -c Debug
```

### 실행 시 주의
1. `AAEmu.World` + `AAEmu.ZoneHost` 완전 종료
2. 빌드 (DLL 잠금 해제)
3. `AAEmu.World` 시작
4. `ZOneManager(Full)`로 `AAEmu.ZoneHost` 배포/시작
5. 클라이언트 접속

---

## 9. Gweonid 경비병 사례 (복습)

- **관찰**: `Stance=1`, `ActorFlags=5`, `DeltaY≈94`로 이미 walk 상태
- **원인**: `SCUnitState`의 `AnimActionId=109` (`fist_pos_soldier_attention_idle`)가 full-body로 이동 덮음
- **해결**: `Npc.cs`에 `_overrideAnimActionId` override 추가, `WorldIntegration.MirrorZoneNpcSpawn`에서 `AnimActionId=0`으로 막음

---

## 10. 디버깅 체크리스트

- [ ] 어느 프로세스가 실제로 동작하는지 확인 (`AAEmu.Game.exe` vs `AAEmu.ZoneHost.exe`)
- [ ] 로그 레벨 확인 (`NLog.config`)
- [ ] raw 패킷 값 확인 (`MovementRelay`, `ZoneSimRelay` 등 `Logger`)
- [ ] `SCUnitState` 직렬화 경로 확인 (`UnitModelPostureSerializer`, `Npc.AnimActionId`)
- [ ] DB `npc_postures`의 `anim_action_id` 확인
- [ ] build 전 서버 종료
- [ ] 새 DLL이 실제로 복사됐는지 확인

---

## 11. Client SQLite DB (`game_decrypted.sqlite3`)

> 경로 예: `E:\games\archeage-10.0.2.13r575-cn\game\db\game_decrypted.sqlite3`

### 핵심 테이블

- **`npc_postures`** — NPC 스폰/자세 정의
  - `id`, `npc_posture_set_id`, `anim_action_id`, `talk_anim`, `start_tod_time`
  - 예시 (`anim_action_id=109`):
    - `id=78`, `npc_posture_set_id=75`, `anim_action_id=109`, `talk_anim='fist_pos_soldier_attention_talk'`

- **`anim_actions`** — 실제 애니메이션 액션 정의
  - `id`, `name`, `action_state_id`, `anim_name`, `model_*`, `no_rotate`, `mount_pose_id`, ...
  - 예시 (`id=109`):
    - `name='fist_pos_soldier_attention_idle(elf)'`
    - `anim_name='fist_pos_soldier_attention_idle'`
    - `action_state_id=2`, `model_physic=t`
    - 이게 full-body 자세로 캐릭터 고정 → 걷기 블렌딩 막음

- **`anims`** — 애니메이션 클립 정보 (anim_actions와 별개)
  - `id`, `name`, `loop`, `category_id`, `ride_ub`, `hang_ub`, `swim_ub`, `move_ub`, `relaxed_ub`, `swim_move_ub`
  - 주의: `anims.id=109`은 `all_co_sk_buff_cast_defense`이며, `anim_actions.id=109`와 다름
  - `loop=t`이면 반복 재생

- **`const_anim_actions`** — `id`와 `name` 매핑 (이 DB에는 109번 데이터 없음)
  - `id`, `name`, `anim_action_id`

### 사용법

```bash
dotnet run --project "F:\테스팅\_sqlite_query\SqliteQuery.csproj"
```

### 디버깅 팁

- `npc_postures`에서 `npc_posture_set_id`로 NPC 템플릿 자세 찾기
- `anim_actions`에서 `id`로 실제 애니메션 이름/`action_state_id`/전용 동작 여부 확인
- `anims`와 `anim_actions`의 `id`가 같은 값을 가리킬 수도 있지만 의미는 다름
- `stances`/`moods` 테이블은 이 DB에 없음 → `Stance`/`Mood` enum 값은 서버 코드/별도 DB 참고

---

## 11. Client SQLite DB (`game_decrypted.sqlite3`)

> 경로: `E:\games\archeage-10.0.2.13r575-cn\game\db\game_decrypted.sqlite3`

### 11-1. 전체 테이블 목록
- `ability_preview_packs` (136 rows)
- `accept_quest_effects` (78 rows)
- `account_attendance_rewards` (2700 rows)
- `account_attribute_effects` (23 rows)
- `account_buffs` (10 rows)
- `achievement_categories` (9 rows)
- `achievement_objectives` (12750 rows)
- `achievement_sub_categories` (44 rows)
- `achievements` (5122 rows)
- `actability_groups` (37 rows)
- `actability_view_group_elems` (30 rows)
- `actability_view_groups` (4 rows)
- `actor_models` (1707 rows)
- `aggro_effects` (464 rows)
- `aggro_links` (180 rows)
- `ai_command_sets` (454 rows)
- `ai_commands` (1972 rows)
- `ai_events` (2495 rows)
- `ai_files` (15 rows)
- `allow_to_equip_slaves` (120 rows)
- `allow_to_equip_slots` (202 rows)
- `allowed_name_chars` (0 rows)
- `anim_actions` (434 rows)
- `anim_rules` (17 rows)
- `anims` (1111 rows)
- `aoe_diminishings` (10 rows)
- `aoe_shapes` (19928 rows)
- `appellation_levels` (8 rows)
- `appellation_merits` (114 rows)
- `appellations` (1098 rows)
- `arche_pass_categories` (14 rows)
- `arche_pass_tiers` (3028 rows)
- `arche_passes` (97 rows)
- `armor_assets` (3527 rows)
- `armor_element_resists` (15 rows)
- `armor_grade_buffs` (33 rows)
- `attach_anims` (360 rows)
- `attach_point_icons` (16 rows)
- `attachment_anims` (2 rows)
- `attachments` (1 rows)
- `auction_a_categories` (15 rows)
- `auction_b_categories` (58 rows)
- `auction_c_categories` (80 rows)
- `backpack_transform_offsets` (346 rows)
- `bag_expands` (20 rows)
- `ban_slash_commands` (71 rows)
- `battle_field_killstreak_skills` (36 rows)
- `battle_field_pick_buffs` (21 rows)
- `battle_field_random_zones` (0 rows)
- `battle_fields` (26 rows)
- `blocked_child_doodads` (11 rows)
- `blocked_texts` (84658 rows)
- `body_diffuse_maps` (2 rows)
- `body_normal_maps` (27 rows)
- `book_contents` (46 rows)
- `book_elems` (2243 rows)
- `book_page_contents` (4751 rows)
- `book_pages` (2641 rows)
- `books` (206 rows)
- `bubble_chats` (369 rows)
- `bubble_effects` (6239 rows)
- `bubbles` (185 rows)
- `buff_breakers` (2478 rows)
- `buff_effects` (27105 rows)
- `buff_groups` (272 rows)
- `buff_modifiers` (1057 rows)
- `buff_mount_skills` (849 rows)
- `buff_passive_buffs` (35 rows)
- `buff_skills` (2316 rows)
- `buff_swap_skills` (71 rows)
- `buff_tick_effects` (3150 rows)
- `buff_tolerance_steps` (83 rows)
- `buff_tolerances` (31 rows)
- `buff_triggers` (11341 rows)
- `buff_unit_modifiers` (160 rows)
- `buff_visual_changes` (40 rows)
- `buffs` (30654 rows)
- `bundle_feature_elems` (0 rows)
- `bundle_versions` (3 rows)
- `butler_equip_items` (62 rows)
- `butler_equip_slots` (5 rows)
- `butler_func_garden_expand_slots` (5 rows)
- `butler_func_trade_expand_slots` (6 rows)
- `butler_harvest_grades` (2 rows)
- `butler_harvests` (159 rows)
- `butler_levels` (41 rows)
- `butler_look_items` (43 rows)
- `butler_specialty_trades` (163 rows)
- `butlers` (1 rows)
- `change_equipment_buffs` (56 rows)
- `char_records` (9639 rows)
- `char_transform_effects` (7 rows)
- `character_default_skills` (24 rows)
- `character_equip_packs` (14 rows)
- `character_idle_buffs` (2 rows)
- `character_preview_cloths` (85 rows)
- `character_supplies` (4 rows)
- `characters` (17 rows)
- `chat_commands` (20 rows)
- `chat_icon_infos` (11 rows)
- `chat_spam_rule_details` (0 rows)
- `chat_spam_rules` (0 rows)
- `cinema_captions` (224 rows)
- `cinema_effects` (82 rows)
- `cinema_subtitles` (3 rows)
- `cinemas` (320 rows)
- `cleanup_ucc_effects` (1 rows)
- `climates` (6 rows)
- `coffer_item_categories` (98 rows)
- `combat_buffs` (57 rows)
- `combat_resource_effects` (484 rows)
- `combat_resource_groups` (13 rows)
- `combat_resources` (22 rows)
- `combat_sounds` (392 rows)
- `comments` (283 rows)
- `common_farms` (46 rows)
- `competition_pves` (1 rows)
- `competition_pvps` (7 rows)
- `competition_tower_defs` (23 rows)
- `conflict_zone_npc_kills` (286 rows)
- `conflict_zone_npc_spawners` (53 rows)
- `conflict_zone_quest_completions` (34 rows)
- `conflict_zone_realtime_schedules` (205 rows)
- `conflict_zones` (33 rows)
- `const_achievement_sub_category_types` (1 rows)
- `const_anim_actions` (40 rows)
- `const_anim_types` (17 rows)
- `const_buff_types` (113 rows)
- `const_char_record_types` (8 rows)
- `const_craft_a_category_types` (1 rows)
- `const_doodad_types` (43 rows)
- `const_fx_group_refs` (9 rows)
- `const_holdable_types` (5 rows)
- `const_icons` (20 rows)
- `const_instance_types` (2 rows)
- `const_item_category_types` (2 rows)
- `const_item_types` (44 rows)
- `const_model_refs` (23 rows)
- `const_mount_poses` (5 rows)
- `const_npc_types` (9 rows)
- `const_projectile_types` (1 rows)
- `const_quest_cameras` (1 rows)
- `const_quest_context_group_types` (3 rows)
- `const_return_points` (1 rows)
- `const_skill_types` (214 rows)
- `const_slash_command_types` (1 rows)
- `const_slave_types` (3 rows)
- `const_sound_pack_types` (4 rows)
- `const_system_faction_types` (19 rows)
- `const_tags` (59 rows)
- `const_ulc_types` (1 rows)
- `const_world_group_types` (4 rows)
- `constants` (30 rows)
- `content_configs` (335 rows)
- `conversion_effects` (68 rows)
- `craft_a_categories` (9 rows)
- `craft_b_categories` (61 rows)
- `craft_c_categories` (433 rows)
- `craft_d_categories` (792 rows)
- `craft_effects` (868 rows)
- `craft_line_components` (961 rows)
- `craft_lines` (247 rows)
- `craft_materials` (36560 rows)
- `craft_order_coupons` (4 rows)
- `craft_pack_crafts` (12373 rows)
- `craft_packs` (486 rows)
- `craft_products` (12160 rows)
- `crafts` (12402 rows)
- `currency_configs` (13 rows)
- `custom_dual_materials` (110 rows)
- `custom_face_presets` (449 rows)
- `custom_font_colors` (14 rows)
- `custom_hair_textures` (5 rows)
- `customizing_item_asset_colors` (12740 rows)
- `customizing_item_assets` (478 rows)
- `damage_effects` (11001 rows)
- `db_stored_pks` (88 rows)
- `deco_actability_groups` (12 rows)
- `default_action_bar_actions` (3 rows)
- `default_inventory_tab_groups` (10 rows)
- `default_inventory_tabs` (4 rows)
- `default_skills` (155 rows)
- `dispel_effects` (4180 rows)
- `district_return_points` (4078 rows)
- `districts` (449 rows)
- `dominion_housings` (8 rows)
- `doodad_almighties` (16735 rows)
- `doodad_bundle_doodads` (12 rows)
- `doodad_bundles` (12 rows)
- `doodad_func_activate_spheres` (8 rows)
- `doodad_func_animates` (6561 rows)
- `doodad_func_area_triggers` (72 rows)
- `doodad_func_attachments` (737 rows)
- `doodad_func_auction_uis` (2 rows)
- `doodad_func_bank_uis` (21 rows)
- `doodad_func_bind_butlers` (19 rows)
- `doodad_func_bindings` (214 rows)
- `doodad_func_bubbles` (143 rows)
- `doodad_func_buffs` (45 rows)
- `doodad_func_build_condition_infos` (231 rows)
- `doodad_func_build_condition_ui_opens` (231 rows)
- `doodad_func_buy_fish_items` (142 rows)
- `doodad_func_buy_fish_model_items` (141 rows)
- `doodad_func_buy_fish_models` (6 rows)
- `doodad_func_buy_fishes` (7 rows)
- `doodad_func_change_other_doodad_phases` (1010 rows)
- `doodad_func_cleanup_logic_links` (16 rows)
- `doodad_func_climate_reacts` (146 rows)
- `doodad_func_climbs` (188 rows)
- `doodad_func_clout_effects` (840 rows)
- `doodad_func_clouts` (3891 rows)
- `doodad_func_coffer_perms` (37 rows)
- `doodad_func_coffers` (38 rows)
- `doodad_func_competition_points` (10 rows)
- `doodad_func_conditional_uses` (12 rows)
- `doodad_func_consume_changer_items` (10780 rows)
- `doodad_func_consume_changer_model_items` (10632 rows)
- `doodad_func_consume_changer_models` (53 rows)
- `doodad_func_consume_changers` (55 rows)
- `doodad_func_convert_fish_items` (103 rows)
- `doodad_func_convert_fishes` (6 rows)
- `doodad_func_craft_acts` (9 rows)
- `doodad_func_craft_cancels` (13 rows)
- `doodad_func_craft_directs` (353 rows)
- `doodad_func_craft_get_items` (11 rows)
- `doodad_func_craft_infos` (6 rows)
- `doodad_func_craft_order_board_ui_opens` (1 rows)
- `doodad_func_craft_packs` (1039 rows)
- `doodad_func_craft_start_crafts` (1379 rows)
- `doodad_func_craft_starts` (10 rows)
- `doodad_func_cutdownings` (112 rows)
- `doodad_func_declare_sieges` (2 rows)
- `doodad_func_devotes` (596 rows)
- `doodad_func_dig_terrains` (2 rows)
- `doodad_func_dominion_count_reacts` (4 rows)
- `doodad_func_dominion_tax_in_kinds` (27 rows)
- `doodad_func_enter_instances` (40 rows)
- `doodad_func_enter_sys_instances` (16 rows)
- `doodad_func_evidence_item_loots` (19 rows)
- `doodad_func_exchange_items` (1 rows)
- `doodad_func_exchanges` (0 rows)
- `doodad_func_exit_global_induns` (26 rows)
- `doodad_func_exit_induns` (43 rows)
- `doodad_func_expedition_portal_ui_opens` (1 rows)
- `doodad_func_expedition_ui_opens` (1 rows)
- `doodad_func_fake_uses` (2825 rows)
- `doodad_func_finals` (4500 rows)
- `doodad_func_fish_schools` (32 rows)
- `doodad_func_fx_group_callbacks` (214 rows)
- `doodad_func_goods_values` (4 rows)
- `doodad_func_groups` (45513 rows)
- `doodad_func_growths` (2459 rows)
- `doodad_func_hero_elections` (2 rows)
- `doodad_func_hide_map_icons` (63 rows)
- `doodad_func_house_farms` (601 rows)
- `doodad_func_insert_counters` (60 rows)
- `doodad_func_instance_difficult_ui_opens` (2 rows)
- `doodad_func_instant_ui_opens` (21 rows)
- `doodad_func_issuance_of_mobilization_order_ui_opens` (10 rows)
- `doodad_func_item_changer_ui_opens` (34 rows)
- `doodad_func_item_changers` (241 rows)
- `doodad_func_livestock_growths` (0 rows)
- `doodad_func_local_development_board_ui_opens` (111 rows)
- `doodad_func_logic_family_providers` (628 rows)
- `doodad_func_logic_family_subscribers` (156 rows)
- `doodad_func_logics` (71 rows)
- `doodad_func_loot_items` (2063 rows)
- `doodad_func_loot_packs` (1565 rows)
- `doodad_func_model_changes` (250 rows)
- `doodad_func_navi_mark_pos_to_maps` (3 rows)
- `doodad_func_navi_namings` (4 rows)
- `doodad_func_navi_open_mailboxes` (30 rows)
- `doodad_func_navi_open_portals` (2 rows)
- `doodad_func_navi_remove_timers` (3 rows)
- `doodad_func_navi_removes` (2 rows)
- `doodad_func_navi_teleports` (3 rows)
- `doodad_func_npc_interaction_relays` (1 rows)
- `doodad_func_nuons_arrow_ui_opens` (2 rows)
- `doodad_func_open_farm_infos` (4 rows)
- `doodad_func_open_papers` (231 rows)
- `doodad_func_origin_item_time_checks` (47 rows)
- `doodad_func_ownership_times` (2 rows)
- `doodad_func_parent_infos` (60 rows)
- `doodad_func_parrots` (18 rows)
- `doodad_func_play_flow_graphs` (479 rows)
- `doodad_func_private_coffers` (19 rows)
- `doodad_func_pulse_triggers` (367 rows)
- `doodad_func_pulses` (529 rows)
- `doodad_func_purchases` (729 rows)
- `doodad_func_puzzle_ins` (10 rows)
- `doodad_func_puzzle_outs` (112 rows)
- `doodad_func_puzzle_rolls` (1 rows)
- `doodad_func_quest_reacts` (3046 rows)
- `doodad_func_quests` (1832 rows)
- `doodad_func_random_store_uis` (1 rows)
- `doodad_func_ratio_changes` (2888 rows)
- `doodad_func_ratio_respawns` (365 rows)
- `doodad_func_react_devotes` (22 rows)
- `doodad_func_recover_items` (3423 rows)
- `doodad_func_remove_instances` (36 rows)
- `doodad_func_remove_items` (103 rows)
- `doodad_func_require_items` (50 rows)
- `doodad_func_require_quests` (1014 rows)
- `doodad_func_resident_balances` (35 rows)
- `doodad_func_resident_charges` (48 rows)
- `doodad_func_resident_townhall_ui_opens` (350 rows)
- `doodad_func_respawns` (0 rows)
- `doodad_func_siege_periods` (525 rows)
- `doodad_func_siege_raids` (1 rows)
- `doodad_func_signs` (512 rows)
- `doodad_func_skill_hits` (5441 rows)
- `doodad_func_skip_war_states` (6 rows)
- `doodad_func_spawn_gimmicks` (16 rows)
- `doodad_func_spawn_mgmts` (6 rows)
- `doodad_func_spawn_slave_after_get_items` (1 rows)
- `doodad_func_spawns` (24 rows)
- `doodad_func_stamp_makers` (4 rows)
- `doodad_func_store_uis` (84 rows)
- `doodad_func_timers` (15474 rows)
- `doodad_func_tod_reacts` (59 rows)
- `doodad_func_tods` (737 rows)
- `doodad_func_ucc_imprints` (1 rows)
- `doodad_func_uses` (9925 rows)
- `doodad_func_vegetation_growths` (0 rows)
- `doodad_func_water_volumes` (34 rows)
- `doodad_func_worldmap_texts` (32 rows)
- `doodad_func_zone_buffs` (60 rows)
- `doodad_func_zone_overlaps` (3 rows)
- `doodad_func_zone_reacts` (768 rows)
- `doodad_funcs` (32823 rows)
- `doodad_groups` (111 rows)
- `doodad_item_change_effects` (160 rows)
- `doodad_modifiers` (4 rows)
- `doodad_phase_funcs` (48787 rows)
- `doodad_place_skins` (3846 rows)
- `drop_rule_loot_packs` (1094 rows)
- `drop_rules` (351 rows)
- `dyeable_items` (343 rows)
- `dynamic_funcs` (12 rows)
- `dynamic_unit_modifiers` (713 rows)
- `effects` (65061 rows)
- `emblem_patterns` (110 rows)
- `enchant_scale_ratios` (32 rows)
- `enhanced_item_material_armor_defaults` (22 rows)
- `enhanced_item_material_weapon_defaults` (97 rows)
- `enhanced_item_materials` (20 rows)
- `enum_abilities` (30 rows)
- `enum_account_attribute_kinds` (3 rows)
- `enum_achievement_kinds` (3 rows)
- `enum_active_weapon` (5 rows)
- `enum_aggro_link_special_rule_kinds` (5 rows)
- `enum_ai_anchors` (27 rows)
- `enum_ai_command_categories` (5 rows)
- `enum_anim_action_states` (9 rows)
- `enum_anim_categories` (11 rows)
- `enum_anim_poses` (6 rows)
- `enum_anim_rule_operators` (6 rows)
- `enum_aoe_shape_kinds` (3 rows)
- `enum_arche_pass_statuses` (5 rows)
- `enum_area_groups` (18 rows)
- `enum_area_sphere_trigger_conditions` (3 rows)
- `enum_armor_types` (6 rows)
- `enum_attach_point` (88 rows)
- `enum_attachment_timing_kinds` (10 rows)
- `enum_backpack_types` (8 rows)
- `enum_ban_reason_types` (5 rows)
- `enum_ban_slash_command_kinds` (3 rows)
- `enum_battle_field_ending_reasons` (12 rows)
- `enum_battle_field_kinds` (3 rows)
- `enum_bless_uthstin_functions` (2 rows)
- `enum_bond_kinds` (10 rows)
- `enum_buff_attributes` (11 rows)
- `enum_buff_conditions` (4 rows)
- `enum_buff_kinds` (3 rows)
- `enum_buff_save_rules` (3 rows)
- `enum_buff_stack_rule` (7 rows)
- `enum_buff_trigger_agents` (4 rows)
- `enum_buff_trigger_events` (36 rows)
- `enum_buff_visual_apply_type` (2 rows)
- `enum_buff_visual_type` (2 rows)
- `enum_char_gender` (3 rows)
- `enum_char_race` (10 rows)
- `enum_char_record_kinds` (84 rows)
- `enum_chat_bubble_kinds` (4 rows)
- `enum_chat_icon_kinds` (11 rows)
- `enum_chat_types` (26 rows)
- `enum_climates` (5 rows)
- `enum_combat_anim_action` (148 rows)
- `enum_combat_dice_kinds` (8 rows)
- `enum_combat_dice_results` (8 rows)
- `enum_combat_resource_active_types` (3 rows)
- `enum_combat_resource_send_types` (2 rows)
- `enum_combat_resource_ui_types` (4 rows)
- `enum_comparison_operators` (5 rows)
- `enum_competition_tower_defs` (5 rows)
- `enum_conflict_zone_state_kinds` (3 rows)
- `enum_constant_kinds` (30 rows)
- `enum_content_configs` (335 rows)
- `enum_content_currencies` (16 rows)
- `enum_content_kinds` (49 rows)
- `enum_conversion_categories` (2 rows)
- `enum_conversion_source_categories` (3 rows)
- `enum_conversion_target_categories` (4 rows)
- `enum_corpse_status` (3 rows)
- `enum_crime_effect_kinds` (6 rows)
- `enum_currencies` (7 rows)
- `enum_custom_asset_owners` (3 rows)
- `enum_customizing_item_asset_categories` (3 rows)
- `enum_damage_absorption_type` (3 rows)
- `enum_damage_type` (5 rows)
- `enum_day_of_weeks` (8 rows)
- `enum_district_kinds` (2 rows)
- `enum_dominion_housing_groups` (5 rows)
- `enum_doodad_attributes` (2 rows)
- `enum_doodad_basic_category` (3 rows)
- `enum_doodad_func_climb_kinds` (10 rows)
- `enum_doodad_func_group_kinds` (3 rows)
- `enum_doodad_func_quest_kinds` (2 rows)
- `enum_doodad_fx_group_callbacks` (2 rows)
- `enum_doodad_logic_delays` (8 rows)
- `enum_doodad_logic_operations` (7 rows)
- `enum_doodad_model_kinds` (7 rows)
- `enum_doodad_perms` (16 rows)
- `enum_doodad_place_area_kinds` (3 rows)
- `enum_doodad_place_skin_kinds` (5 rows)
- `enum_doodad_zone_overlap_kinds` (1 rows)
- `enum_effect_bone` (34 rows)
- `enum_elo_rating_grades` (10 rows)
- `enum_emblem_pattern_kinds` (4 rows)
- `enum_equip_slot` (35 rows)
- `enum_equip_slot_reinforce_attributes` (3 rows)
- `enum_equip_slot_types` (33 rows)
- `enum_error_messages` (1244 rows)
- `enum_event_ignore_category` (2 rows)
- `enum_face_decal_category` (6 rows)
- `enum_face_morph_type` (8 rows)
- `enum_faction_competition_reset_state_kinds` (3 rows)
- `enum_faction_power_records` (5 rows)
- `enum_faction_scopes` (2 rows)
- `enum_flow_graph_events` (2 rows)
- `enum_formula_kinds` (70 rows)
- `enum_fx_events` (8 rows)
- `enum_fx_locations` (3 rows)
- `enum_fx_scale_types` (3 rows)
- `enum_game_actions` (15 rows)
- `enum_game_events` (30 rows)
- `enum_game_rank_rules` (8 rows)
- `enum_game_rule_modes` (5 rows)
- `enum_game_safe_area_events` (3 rows)
- `enum_game_score_events` (9 rows)
- `enum_game_stances` (9 rows)
- `enum_game_types` (7 rows)
- `enum_grammar_kinds` (6 rows)
- `enum_hair_types` (6 rows)
- `enum_hero_schedule_events` (4 rows)
- `enum_hit_anim_timings` (3 rows)
- `enum_holdable_formula_kinds` (5 rows)
- `enum_honor_point_war_states` (8 rows)
- `enum_hotkey_action_types` (4 rows)
- `enum_hotkey_actions` (120 rows)
- `enum_hotkey_categories` (6 rows)
- `enum_hotkey_modes` (5 rows)
- `enum_house_demolish_types` (11 rows)
- `enum_house_permissions` (4 rows)
- `enum_housing_category` (36 rows)
- `enum_housing_deco_gardens` (3 rows)
- `enum_housing_ucc_kinds` (4 rows)
- `enum_housing_ucc_positions` (5 rows)
- `enum_indun_doodad_check_statuses` (3 rows)
- `enum_indun_npc_info_broadcasting_types` (2 rows)
- `enum_indun_round_alarm_kinds` (2 rows)
- `enum_instance_action_without_params` (2 rows)
- `enum_instance_gain_ranges` (3 rows)
- `enum_instance_permission_tag_kinds` (3 rows)
- `enum_instance_reward_kinds` (7 rows)
- `enum_instance_reward_mail_kinds` (1 rows)
- `enum_instance_round_ending_conditions` (4 rows)
- `enum_instance_round_start_conditions` (2 rows)
- `enum_instance_score_events` (8 rows)
- `enum_instrument_sound_kinds` (2 rows)
- `enum_item_asset_attachment_type` (2 rows)
- `enum_item_asset_categories` (9 rows)
- `enum_item_asset_cloth_waist_size` (6 rows)
- `enum_item_asset_elbow_size` (4 rows)
- `enum_item_asset_equip_pos` (5 rows)
- `enum_item_asset_hide_at_sheath` (2 rows)
- `enum_item_asset_sheath_pos` (12 rows)
- `enum_item_asset_underwear_waist_size` (4 rows)
- `enum_item_asset_wrist_size` (5 rows)
- `enum_item_bind_types` (6 rows)
- `enum_item_enchant_ratio_kinds` (3 rows)
- `enum_item_guide_loot_boss_categories` (5 rows)
- `enum_item_guide_loot_craft_categories` (3 rows)
- `enum_item_guide_loot_etc_categories` (3 rows)
- `enum_item_guide_loot_event_categories` (1 rows)
- `enum_item_guide_loot_indun_categories` (3 rows)
- `enum_item_guide_loot_ingame_shop_categories` (6 rows)
- `enum_item_guide_loot_main_categories` (10 rows)
- `enum_item_guide_loot_other_craft_categories` (4 rows)
- `enum_item_guide_loot_rebuilding_categories` (1 rows)
- `enum_item_guide_loot_shop_categories` (1 rows)
- `enum_item_guide_loot_socket_change_categories` (1 rows)
- `enum_item_impls` (36 rows)
- `enum_item_location_types` (4 rows)
- `enum_item_processed_states` (3 rows)
- `enum_item_set_kinds` (3 rows)
- `enum_item_skin_kinds` (3 rows)
- `enum_item_usages` (8 rows)
- `enum_kill_assist_types` (4 rows)
- `enum_language_type` (11 rows)
- `enum_locales` (11 rows)
- `enum_map_filter_types` (4 rows)
- `enum_map_layers` (22 rows)
- `enum_map_symbol_types` (158 rows)
- `enum_matching_conditions` (3 rows)
- `enum_matching_intergration_levels` (4 rows)
- `enum_matching_invitation_types` (2 rows)
- `enum_mate_kinds` (20 rows)
- `enum_mate_state` (3 rows)
- `enum_mate_types` (3 rows)
- `enum_member_weights` (6 rows)
- `enum_merchandise_kinds` (8 rows)
- `enum_mini_scoreboard_module_kinds` (1 rows)
- `enum_model_file_type` (7 rows)
- `enum_model_states` (9 rows)
- `enum_movement_type` (4 rows)
- `enum_name_rule_targets` (10 rows)
- `enum_npc_control_category` (6 rows)
- `enum_npc_grade` (8 rows)
- `enum_npc_group_aggro_rule_kinds` (3 rows)
- `enum_npc_kind` (14 rows)
- `enum_npc_spawn_type` (4 rows)
- `enum_npc_spawner_category` (2 rows)
- `enum_npc_templates` (9 rows)
- `enum_offset_axes` (5 rows)
- `enum_option_item_save_level_types` (3 rows)
- `enum_option_item_types` (135 rows)
- `enum_path_type` (4 rows)
- `enum_pcbang_buff_kinds` (7 rows)
- `enum_percent_damage_resource_types` (4 rows)
- `enum_plot_area_target_kinds` (5 rows)
- `enum_plot_condition_kinds` (20 rows)
- `enum_plot_effect_sources` (4 rows)
- `enum_plot_effect_targets` (5 rows)
- `enum_plot_source_update_methods` (4 rows)
- `enum_plot_target_update_methods` (7 rows)
- `enum_plot_variable_kinds` (12 rows)
- `enum_political_systems` (4 rows)
- `enum_premium_grade_kinds` (12 rows)
- `enum_proc_chance_type` (20 rows)
- `enum_projectile_physics` (5 rows)
- `enum_provider_types` (7 rows)
- `enum_purchase_types` (4 rows)
- `enum_quest_act_obj_invite_types` (1 rows)
- `enum_quest_component_kinds` (8 rows)
- `enum_quest_component_text_kinds` (4 rows)
- `enum_quest_condition_objs` (4 rows)
- `enum_quest_context_statuses` (6 rows)
- `enum_quest_context_text_kinds` (5 rows)
- `enum_quest_details` (15 rows)
- `enum_quest_name_kinds` (3 rows)
- `enum_quest_npc_ai_names` (6 rows)
- `enum_quest_patterns` (2 rows)
- `enum_quest_triggers` (5 rows)
- `enum_rank_alarm_kinds` (2 rows)
- `enum_rank_data_works` (3 rows)
- `enum_rank_kinds` (12 rows)
- `enum_recommend_ability_categories` (3 rows)
- `enum_recovery_states` (3 rows)
- `enum_regions` (8 rows)
- `enum_rename_faction_types` (4 rows)
- `enum_reset_interval_kinds` (2 rows)
- `enum_schedule_item_kinds` (5 rows)
- `enum_server_transfer_restricted_reasons` (4 rows)
- `enum_siege_periods` (5 rows)
- `enum_skill_active_types` (5 rows)
- `enum_skill_anim_type` (11 rows)
- `enum_skill_attribute` (17 rows)
- `enum_skill_book_categories` (7 rows)
- `enum_skill_category` (8 rows)
- `enum_skill_controller_kinds` (11 rows)
- `enum_skill_coordinate` (4 rows)
- `enum_skill_effect_application_methods` (4 rows)
- `enum_skill_effect_special_type` (171 rows)
- `enum_skill_fx_type` (11 rows)
- `enum_skill_hit_type` (18 rows)
- `enum_skill_model_type` (4 rows)
- `enum_skill_move_type` (3 rows)
- `enum_skill_target_relation` (11 rows)
- `enum_skill_target_selection` (4 rows)
- `enum_skill_target_type` (27 rows)
- `enum_skill_timing` (2 rows)
- `enum_skill_use_condition` (9 rows)
- `enum_skill_use_condition_kinds` (16 rows)
- `enum_slash_functions` (52 rows)
- `enum_slave_equipment_category` (5 rows)
- `enum_slave_kind` (13 rows)
- `enum_sound_categories` (17 rows)
- `enum_sound_end_methods` (2 rows)
- `enum_sound_levels` (1 rows)
- `enum_sound_materials` (28 rows)
- `enum_sound_pack_categories` (6 rows)
- `enum_spawn_dirs` (3 rows)
- `enum_specialty_event_sorts` (4 rows)
- `enum_specialty_event_trigger_msgs` (2 rows)
- `enum_specialty_event_trigger_sorts` (4 rows)
- `enum_survey_form_question_kinds` (2 rows)
- `enum_today_quest_sorts` (6 rows)
- `enum_ucc_applicable_kinds` (3 rows)
- `enum_ui_content_types` (125 rows)
- `enum_ui_text_categories` (122 rows)
- `enum_unit_appellation_merit_size_types` (2 rows)
- `enum_unit_appellation_routes` (5 rows)
- `enum_unit_attribute` (255 rows)
- `enum_unit_formula_kinds` (60 rows)
- `enum_unit_formula_variable_kinds` (5 rows)
- `enum_unit_modifier_type` (2 rows)
- `enum_unit_owner_types` (8 rows)
- `enum_unit_relationship_codes` (3 rows)
- `enum_unit_relationship_reasons` (14 rows)
- `enum_unit_req_kinds` (131 rows)
- `enum_unit_selections` (2 rows)
- `enum_unit_texts` (1 rows)
- `enum_victory_states` (3 rows)
- `enum_weapon_anim_pose` (10 rows)
- `enum_weapon_equip_statuses` (5 rows)
- `enum_wearable_formula_kinds` (2 rows)
- `enum_world_access_ranges` (3 rows)
- `enum_world_interactions` (105 rows)
- `enum_zone_damage_multiplier_kinds` (4 rows)
- `enum_zone_group_state_types` (1 rows)
- `equip_item_attr_modifiers` (120 rows)
- `equip_item_set_bonuses` (959 rows)
- `equip_item_sets` (501 rows)
- `equip_pack_cloths` (2426 rows)
- `equip_pack_weapons` (672 rows)
- `equip_slot_enchanting_costs` (21 rows)
- `equip_slot_group_maps` (138 rows)
- `equip_slot_groups` (47 rows)
- `equip_slot_reinforce_bundle_effects` (3 rows)
- `equip_slot_reinforce_level_effects` (38 rows)
- `equip_slot_reinforce_materials` (2235 rows)
- `equip_slot_reinforce_set_effects` (6 rows)
- `equip_slot_reinforce_unit_modifiers` (179 rows)
- `equip_slot_reinforces` (124 rows)
- `expand_expert_limits` (14 rows)
- `expedition_buff_grades` (93 rows)
- `expedition_buffs` (14 rows)
- `expedition_levels` (100000 rows)
- `expert_limits` (32 rows)
- `express_texts` (181 rows)
- `extend_charge_effects` (23 rows)
- `face_decal_assets` (1787 rows)
- `face_diffuse_maps` (0 rows)
- `face_eyelash_maps` (0 rows)
- `face_normal_maps` (138 rows)
- `faction_change_limit_nums` (10 rows)
- `faction_change_npc_spawners` (6 rows)
- `faction_chat_regions` (4 rows)
- `faction_competition_npc_infos` (108 rows)
- `faction_competition_quest_infos` (14 rows)
- `faction_competitions` (8 rows)
- `faction_power_grades` (10 rows)
- `faction_power_records` (43 rows)
- `family_levels` (3 rows)
- `family_member_limits` (4 rows)
- `family_roles` (7 rows)
- `farm_group_doodads` (80 rows)
- `farm_groups` (2 rows)
- `feature_sets` (0 rows)
- `festival_zones` (20 rows)
- `fish_details` (66 rows)
- `flying_state_change_effects` (16 rows)
- `formula_funcs` (233 rows)
- `formulas` (70 rows)
- `freshness_group_items` (63 rows)
- `freshness_groups` (13 rows)
- `fx_cam_fovs` (5 rows)
- `fx_cgfs` (16 rows)
- `fx_chrs` (63 rows)
- `fx_group_fx_items` (9230 rows)
- `fx_groups` (5051 rows)
- `fx_items` (8181 rows)
- `fx_materials` (12 rows)
- `fx_motion_blurs` (2 rows)
- `fx_particles` (5498 rows)
- `fx_ropes` (4 rows)
- `fx_shake_cameras` (60 rows)
- `fx_sounds` (2361 rows)
- `fx_voices` (160 rows)
- `gacha_advanced_loot_packs` (30 rows)
- `gacha_loot_pack_items` (24 rows)
- `gacha_loot_packs` (11 rows)
- `gain_loot_pack_item_effects` (5229 rows)
- `gain_merchant_reopen_pack_item_effects` (9 rows)
- `game_activities` (4 rows)
- `game_activity_stages` (5 rows)
- `game_activity_tasks` (41 rows)
- `game_condition_types` (88 rows)
- `game_point_rank_details` (6 rows)
- `game_rank_rules` (207 rows)
- `game_rule_events` (398 rows)
- `game_rule_sets` (33 rows)
- `game_safe_area_buffs` (24 rows)
- `game_safe_area_rules` (15 rows)
- `game_schedule_beautyshops` (7 rows)
- `game_schedule_doodads` (2443 rows)
- `game_schedule_housings` (3 rows)
- `game_schedule_quests` (1233 rows)
- `game_schedule_spawners` (1240 rows)
- `game_schedules` (922 rows)
- `game_score_rules` (97 rows)
- `game_stances` (15363 rows)
- `gameplay_participation_configs` (121 rows)
- `gear_rank_details` (1 rows)
- `gem_visual_effects` (26 rows)
- `gimmicks` (211 rows)
- `glider_transform_offsets` (385 rows)
- `global_region_names` (6 rows)
- `global_world_names` (76 rows)
- `grammar_tag_none_types` (6 rows)
- `grammar_tags` (29 rows)
- `guard_tower_settings` (12 rows)
- `guard_tower_steps` (40 rows)
- `hand_ik_offsets` (25 rows)
- `hash_checkers` (72 rows)
- `heal_effects` (954 rows)
- `heavy_taxes` (10 rows)
- `heir_levels` (71 rows)
- `heir_skill_details` (166 rows)
- `heir_skills` (80 rows)
- `hero_bonus_today_assignments` (9 rows)
- `hero_bonuses` (3 rows)
- `hero_conditions` (1 rows)
- `hero_grades` (4 rows)
- `hero_rewards` (18 rows)
- `hero_schedules` (25 rows)
- `heros` (120 rows)
- `holdable_features` (13 rows)
- `holdable_kinds` (15 rows)
- `holdable_shapes` (13 rows)
- `holdables` (32 rows)
- `honor_point_war_state_texts` (8 rows)
- `hotkeys` (120 rows)
- `housing_areas` (847 rows)
- `housing_binding_doodads` (4646 rows)
- `housing_build_steps` (1034 rows)
- `housing_deco_limit_elems` (95 rows)
- `housing_deco_limits` (18 rows)
- `housing_decorations` (2230 rows)
- `housing_group_categories` (98 rows)
- `housing_groups` (25 rows)
- `housing_pack_members` (5 rows)
- `housing_packs` (4 rows)
- `housing_rebuilding_materials` (1486 rows)
- `housing_rebuilding_pack_rebuildings` (2958 rows)
- `housing_rebuilding_packs` (183 rows)
- `housing_rebuildings` (223 rows)
- `housing_sizes` (17 rows)
- `housing_ucc_packs` (0 rows)
- `housing_ucc_slots` (0 rows)
- `housing_view_sizes` (5 rows)
- `housings` (837 rows)
- `icons` (19039 rows)
- `ignore_texts` (8 rows)
- `imprint_ucc_effects` (2 rows)
- `impulse_effects` (414 rows)
- `indestructible_items` (59 rows)
- `indun_action_change_doodad_phases` (244 rows)
- `indun_action_next_rounds` (8 rows)
- `indun_action_remove_tagged_npcs` (40 rows)
- `indun_action_round_alarms` (8 rows)
- `indun_action_send_mail_rewards` (2 rows)
- `indun_action_set_room_cleareds` (34 rows)
- `indun_actions` (357 rows)
- `indun_event_difficult_changeds` (1 rows)
- `indun_event_doodad_phase_changeds` (14 rows)
- `indun_event_doodad_spawneds` (54 rows)
- `indun_event_no_alive_ch_in_rooms` (35 rows)
- `indun_event_no_in_aggro_lists` (10 rows)
- `indun_event_npc_combat_endeds` (3 rows)
- `indun_event_npc_combat_starteds` (23 rows)
- `indun_event_npc_info_broadcastings` (13 rows)
- `indun_event_npc_killeds` (63 rows)
- `indun_event_npc_spawneds` (44 rows)
- `indun_event_zone_score_level_changeds` (3 rows)
- `indun_events` (263 rows)
- `indun_room_spheres` (42 rows)
- `indun_rooms` (42 rows)
- `indun_rounds` (77 rows)
- `indun_zones` (50 rows)
- `ingameshop_goods_sort_orders` (9 rows)
- `ingameshop_modelview_offsets` (51 rows)
- `instance_action_change_doodad_phases` (0 rows)
- `instance_action_create_buff_to_alls` (0 rows)
- `instance_action_create_buff_to_selves` (0 rows)
- `instance_action_destroy_buff_tag_to_alls` (0 rows)
- `instance_action_destroy_buff_tag_to_selves` (0 rows)
- `instance_action_destroy_buff_to_alls` (0 rows)
- `instance_action_destroy_buff_to_selves` (0 rows)
- `instance_action_modify_buff_durations` (0 rows)
- `instance_action_remove_tagged_npcs` (0 rows)
- `instance_action_reset_all_health_and_cooldowns` (0 rows)
- `instance_action_reset_self_health_and_cooldowns` (0 rows)
- `instance_action_round_alarms` (0 rows)
- `instance_action_set_room_cleareds` (0 rows)
- `instance_action_teleport_all_start_points` (0 rows)
- `instance_action_teleport_self_start_points` (0 rows)
- `instance_actions` (0 rows)
- `instance_difficult_infos` (12 rows)
- `instance_ending_compare_kill_counts` (0 rows)
- `instance_ending_compare_round_win_counts` (0 rows)
- `instance_ending_conditions` (0 rows)
- `instance_ending_npc_killeds` (0 rows)
- `instance_entrance_times` (407 rows)
- `instance_event_doodad_spawneds` (0 rows)
- `instance_faction_presets` (21 rows)
- `instance_factions` (69 rows)
- `instance_gain_rules` (18 rows)
- `instance_kill_streak_skills` (0 rows)
- `instance_matching_condition_details` (0 rows)
- `instance_mini_scoreboards` (18 rows)
- `instance_permission_tags` (139 rows)
- `instance_pick_buffs` (0 rows)
- `instance_point_doodad_phase_changes` (18 rows)
- `instance_rank_details` (10 rows)
- `instance_reward_bonus_counts` (431 rows)
- `instance_reward_mail_texts` (5 rows)
- `instance_rewards` (241 rows)
- `instance_room_spheres` (0 rows)
- `instance_rooms` (0 rows)
- `instance_round_ending_compare_ranks` (0 rows)
- `instance_round_ending_compare_scores` (0 rows)
- `instance_round_start_conditions` (0 rows)
- `instance_rounds` (0 rows)
- `instance_ui_kinds` (5 rows)
- `instances` (76 rows)
- `instrument_sounds` (295 rows)
- `integration_distribution_times` (3 rows)
- `integration_distributions` (1 rows)
- `intensified_expert_limits` (8 rows)
- `interaction_effects` (7281 rows)
- `item_accept_quests` (814 rows)
- `item_accessories` (655 rows)
- `item_armor_assets` (34707 rows)
- `item_armors` (11997 rows)
- `item_asset_transforms` (73 rows)
- `item_assets` (38073 rows)
- `item_backpacks` (1220 rows)
- `item_bags` (38 rows)
- `item_bless_uthstins` (45 rows)
- `item_body_parts` (763 rows)
- `item_cap_scale_forbids` (3057 rows)
- `item_categories` (162 rows)
- `item_change_mapping_groups` (302 rows)
- `item_change_mappings` (9172 rows)
- `item_configs` (1 rows)
- `item_conv_epacks` (2 rows)
- `item_conv_exception_filters` (2 rows)
- `item_conv_ppack_members` (5869 rows)
- `item_conv_ppacks` (5657 rows)
- `item_conv_products` (5654 rows)
- `item_conv_reagent_filters` (124 rows)
- `item_conv_reagents` (34900 rows)
- `item_conv_rpack_members` (5988 rows)
- `item_conv_rpacks` (5801 rows)
- `item_conv_sets` (13 rows)
- `item_convs` (6409 rows)
- `item_elements` (5 rows)
- `item_enchant_ratio_groups` (8 rows)
- `item_enchant_ratio_items` (2121 rows)
- `item_enchant_ratios` (104 rows)
- `item_enchanting_gems` (485 rows)
- `item_evolving_materials` (82 rows)
- `item_expedition_attrs` (34 rows)
- `item_grade_buffs` (8686 rows)
- `item_grade_distributions` (54 rows)
- `item_grade_enchant_fail_break_reward_categories` (212 rows)
- `item_grade_enchant_fail_break_rewards` (35 rows)
- `item_grade_enchanting_supports` (100 rows)
- `item_grade_skills` (8 rows)
- `item_grades` (13 rows)
- `item_groups` (32 rows)
- `item_guide_a_categories` (25 rows)
- `item_guide_b_categories` (64 rows)
- `item_guide_elems` (4385 rows)
- `item_guide_icons` (942 rows)
- `item_guide_impls` (7 rows)
- `item_guides` (467 rows)
- `item_housing_decorations` (2305 rows)
- `item_housings` (556 rows)
- `item_look_convert_holdables` (29 rows)
- `item_look_convert_required_items` (31 rows)
- `item_look_convert_wearables` (11 rows)
- `item_look_converts` (30 rows)
- `item_look_revert_required_items` (29 rows)
- `item_open_papers` (708 rows)
- `item_prices` (41202 rows)
- `item_proc_bindings` (186 rows)
- `item_procs` (202 rows)
- `item_rank_assignments` (27 rows)
- `item_rank_details` (3 rows)
- `item_recipes` (2822 rows)
- `item_rnd_attr_categories` (817 rows)
- `item_rnd_attr_category_elements` (428 rows)
- `item_rnd_attr_category_groups` (41 rows)
- `item_rnd_attr_category_properties` (10621 rows)
- `item_rnd_attr_category_relations` (54 rows)
- `item_rnd_attr_unit_modifier_group_sets` (569 rows)
- `item_rnd_attr_unit_modifier_groups` (4399 rows)
- `item_rnd_attr_unit_modifiers` (57187 rows)
- `item_secure_exceptions` (803 rows)
- `item_set_items` (745 rows)
- `item_sets` (250 rows)
- `item_shipyards` (14 rows)
- `item_slave_equipment_grade_spawns` (1207 rows)
- `item_slave_equipment_slave_equipslot_packs` (288 rows)
- `item_slave_equipments` (317 rows)
- `item_smelting_items` (96 rows)
- `item_smelting_probs` (6 rows)
- `item_smeltings` (32 rows)
- `item_socket_chances` (8 rows)
- `item_socket_changes` (4815 rows)
- `item_socket_level_limits` (762 rows)
- `item_socket_num_limits` (403 rows)
- `item_sockets` (783 rows)
- `item_spawn_doodads` (542 rows)
- `item_summon_mates` (552 rows)
- `item_summon_slaves` (283 rows)
- `item_tools` (208 rows)
- `item_weapons` (6846 rows)
- `items` (51010 rows)
- `kill_npc_without_corpse_effects` (1849 rows)
- `level_up_effects` (4 rows)
- `levels` (101 rows)
- `linear_funcs` (466 rows)
- `local_development_board_types` (7 rows)
- `local_development_boards` (78 rows)
- `local_developments` (34 rows)
- `localized_texts` (661919 rows)
- `login_stage_abilities` (8 rows)
- `loot_actability_groups` (266 rows)
- `loot_groups` (11421 rows)
- `loot_pack_dropping_npcs` (13835 rows)
- `loot_packs` (8728 rows)
- `loots` (32971 rows)
- `mana_burn_effects` (100 rows)
- `manual_funcs` (2 rows)
- `map_icons` (156 rows)
- `map_resources` (198 rows)
- `matcher_impl_operators` (2 rows)
- `matcher_impl_sql_wheres` (370 rows)
- `matchers` (372 rows)
- `mate_equip_pack_groups` (598 rows)
- `mate_equip_pack_items` (786 rows)
- `mate_equip_packs` (67 rows)
- `mate_equip_slot_packs` (7 rows)
- `member_weights` (30 rows)
- `merchant_goods` (3654 rows)
- `merchant_packs` (351 rows)
- `merchant_random_goods` (818 rows)
- `merchant_random_groups` (414 rows)
- `merchant_random_packs` (7 rows)
- `merchant_reopen_goods` (254 rows)
- `merchant_reopen_groups` (48 rows)
- `merchant_reopen_packs` (10 rows)
- `merchants` (2645 rows)
- `milestones` (133 rows)
- `mini_scoreboard_condition_npcs` (12 rows)
- `mini_scoreboard_conditions` (12 rows)
- `mini_scoreboard_row_npc_hps` (19 rows)
- `mini_scoreboard_rows` (12 rows)
- `mini_scoreboards` (4 rows)
- `model_attach_point_strings` (87 rows)
- `model_bindings` (5371 rows)
- `model_mutations` (919 rows)
- `model_quest_cameras` (139 rows)
- `models` (3080 rows)
- `monitor_npcs` (3 rows)
- `mount_attached_skills` (1666 rows)
- `mount_poses` (33 rows)
- `mount_skills` (2137 rows)
- `move_to_location_effects` (3 rows)
- `move_to_rez_point_effects` (5 rows)
- `music_note_limits` (12 rows)
- `name_rules` (110 rows)
- `np_passive_buffs` (1470 rows)
- `np_skills` (17010 rows)
- `npc_aggro_links` (973 rows)
- `npc_ai_client_params` (47 rows)
- `npc_ai_params` (3459 rows)
- `npc_binding_unit_buffs` (31 rows)
- `npc_chat_bubbles` (3574 rows)
- `npc_control_effects` (626 rows)
- `npc_doodad_bindings` (56 rows)
- `npc_grade_configs` (8 rows)
- `npc_group_members` (1568 rows)
- `npc_groups` (382 rows)
- `npc_hp_split_configs` (8 rows)
- `npc_initial_buffs` (7647 rows)
- `npc_instance_recruiters` (3 rows)
- `npc_interaction_sets` (214 rows)
- `npc_interactions` (245 rows)
- `npc_mount_skills` (2914 rows)
- `npc_move_to_zone_effect_items` (0 rows)
- `npc_move_to_zone_effects` (0 rows)
- `npc_nick_buffs` (65 rows)
- `npc_nicknames` (170 rows)
- `npc_posture_sets` (309 rows)
- `npc_postures` (324 rows)
- `npc_spawner_despawn_effects` (2788 rows)
- `npc_spawner_npcs` (23394 rows)
- `npc_spawner_spawn_effects` (7161 rows)
- `npc_spawners` (22982 rows)
- `npc_strafe_params` (4 rows)
- `npc_tendencies` (10 rows)
- `npcs` (19522 rows)
- `open_portal_effects` (11 rows)
- `open_portal_inland_reagents` (20 rows)
- `open_portal_outland_reagents` (20 rows)
- `options` (135 rows)
- `overrider_groups` (6 rows)
- `overriders` (73 rows)
- `passive_buffs` (279 rows)
- `pcbang_benefit_lists` (5 rows)
- `pcbang_buffs` (3 rows)
- `physical_explosion_effects` (135 rows)
- `play_log_effects` (118 rows)
- `plot_aoe_conditions` (3532 rows)
- `plot_auction_config` (1 rows)
- `plot_conditions` (19119 rows)
- `plot_effects` (64312 rows)
- `plot_event_conditions` (15587 rows)
- `plot_events` (51178 rows)
- `plot_next_events` (52871 rows)
- `plots` (6559 rows)
- `pre_completed_achievements` (1533 rows)
- `prefab_elements` (4319 rows)
- `prefab_models` (1007 rows)
- `premium_benefit_lists` (17 rows)
- `premium_configs` (1 rows)
- `premium_grades` (6 rows)
- `priest_buffs` (1 rows)
- `projectile_params` (143 rows)
- `projectiles` (1546 rows)
- `put_down_backpack_effects` (793 rows)
- `quest_act_check_complete_components` (95 rows)
- `quest_act_check_distances` (0 rows)
- `quest_act_check_guards` (22 rows)
- `quest_act_check_spheres` (3 rows)
- `quest_act_check_timers` (69 rows)
- `quest_act_con_accept_buffs` (25 rows)
- `quest_act_con_accept_components` (475 rows)
- `quest_act_con_accept_doodads` (923 rows)
- `quest_act_con_accept_item_equips` (0 rows)
- `quest_act_con_accept_item_gains` (123 rows)
- `quest_act_con_accept_items` (737 rows)
- `quest_act_con_accept_level_ranges` (10 rows)
- `quest_act_con_accept_level_ups` (8 rows)
- `quest_act_con_accept_npc_emotions` (2 rows)
- `quest_act_con_accept_npc_groups` (145 rows)
- `quest_act_con_accept_npc_kills` (968 rows)
- `quest_act_con_accept_npcs` (4004 rows)
- `quest_act_con_accept_skills` (0 rows)
- `quest_act_con_accept_spheres` (751 rows)
- `quest_act_con_accept_uis` (777 rows)
- `quest_act_con_auto_completes` (3177 rows)
- `quest_act_con_fails` (0 rows)
- `quest_act_con_report_doodads` (433 rows)
- `quest_act_con_report_journals` (475 rows)
- `quest_act_con_report_npc_groups` (267 rows)
- `quest_act_con_report_npcs` (4771 rows)
- `quest_act_etc_item_obtains` (79 rows)
- `quest_act_obj_ability_levels` (14 rows)
- `quest_act_obj_aggros` (107 rows)
- `quest_act_obj_aliases` (5191 rows)
- `quest_act_obj_cinemas` (24 rows)
- `quest_act_obj_complete_quest_groups` (32 rows)
- `quest_act_obj_complete_quests` (194 rows)
- `quest_act_obj_conditions` (1 rows)
- `quest_act_obj_conquest_wars` (5 rows)
- `quest_act_obj_consume_evolving_materials` (8 rows)
- `quest_act_obj_crafts` (386 rows)
- `quest_act_obj_distances` (1 rows)
- `quest_act_obj_doodad_phase_checks` (14 rows)
- `quest_act_obj_effect_fires` (154 rows)
- `quest_act_obj_enchant_scale_counts` (5 rows)
- `quest_act_obj_express_fires` (110 rows)
- `quest_act_obj_faction_competitions` (6 rows)
- `quest_act_obj_gain_exp_points` (13 rows)
- `quest_act_obj_gain_honor_points` (37 rows)
- `quest_act_obj_gain_living_points` (38 rows)
- `quest_act_obj_interactions` (712 rows)
- `quest_act_obj_invite_team_factions` (4 rows)
- `quest_act_obj_item_gathers` (2677 rows)
- `quest_act_obj_item_group_gathers` (56 rows)
- `quest_act_obj_item_group_uses` (25 rows)
- `quest_act_obj_item_uses` (416 rows)
- `quest_act_obj_labor_powers` (205 rows)
- `quest_act_obj_levels` (58 rows)
- `quest_act_obj_mate_levels` (18 rows)
- `quest_act_obj_monster_contr_group_hunts` (21 rows)
- `quest_act_obj_monster_contr_hunts` (8 rows)
- `quest_act_obj_monster_group_hunts` (833 rows)
- `quest_act_obj_monster_hunts` (857 rows)
- `quest_act_obj_npc_kills` (25 rows)
- `quest_act_obj_pc_kills` (9 rows)
- `quest_act_obj_sell_backpack_goods` (9 rows)
- `quest_act_obj_send_mails` (4 rows)
- `quest_act_obj_spheres` (269 rows)
- `quest_act_obj_talk_npc_groups` (16 rows)
- `quest_act_obj_talks` (404 rows)
- `quest_act_obj_zone_kills` (445 rows)
- `quest_act_obj_zone_monster_hunts` (0 rows)
- `quest_act_obj_zone_npc_talks` (0 rows)
- `quest_act_obj_zone_quest_completes` (0 rows)
- `quest_act_supply_aa_points` (0 rows)
- `quest_act_supply_actabilities` (633 rows)
- `quest_act_supply_appellations` (323 rows)
- `quest_act_supply_arche_pass_points` (325 rows)
- `quest_act_supply_contribution_points` (113 rows)
- `quest_act_supply_coppers` (4135 rows)
- `quest_act_supply_crime_points` (6 rows)
- `quest_act_supply_expedition_exps` (82 rows)
- `quest_act_supply_exps` (4312 rows)
- `quest_act_supply_faction_changes` (27 rows)
- `quest_act_supply_family_exps` (10 rows)
- `quest_act_supply_honor_points` (359 rows)
- `quest_act_supply_interactions` (0 rows)
- `quest_act_supply_items` (5856 rows)
- `quest_act_supply_jury_points` (4 rows)
- `quest_act_supply_leadership_points` (127 rows)
- `quest_act_supply_living_points` (237 rows)
- `quest_act_supply_local_lps` (2 rows)
- `quest_act_supply_lps` (18 rows)
- `quest_act_supply_ranked_items` (23 rows)
- `quest_act_supply_remove_items` (52 rows)
- `quest_act_supply_reset_quests` (0 rows)
- `quest_act_supply_resident_charges` (1 rows)
- `quest_act_supply_resident_points` (41 rows)
- `quest_act_supply_result_ranked_items` (13 rows)
- `quest_act_supply_selective_items` (546 rows)
- `quest_act_supply_skills` (2 rows)
- `quest_acts` (43795 rows)
- `quest_cameras` (104 rows)
- `quest_categories` (210 rows)
- `quest_chat_bubbles` (26920 rows)
- `quest_component_texts` (14193 rows)
- `quest_components` (33126 rows)
- `quest_context_group_members` (493 rows)
- `quest_context_groups` (41 rows)
- `quest_context_texts` (930 rows)
- `quest_contexts` (9011 rows)
- `quest_doodad_groups` (20 rows)
- `quest_doodads` (155 rows)
- `quest_item_group_items` (981 rows)
- `quest_item_groups` (93 rows)
- `quest_mail_attachment_items` (0 rows)
- `quest_mail_attachments` (5 rows)
- `quest_mail_sends` (1 rows)
- `quest_mails` (3 rows)
- `quest_monster_groups` (1050 rows)
- `quest_monster_npcs` (5324 rows)
- `quest_names` (1701 rows)
- `quest_supplies` (126 rows)
- `raid_recruit_headcounts` (5 rows)
- `raid_recruit_sub_types` (16 rows)
- `raid_recruit_time_and_expenses` (4 rows)
- `raid_recruit_types` (4 rows)
- `rank_details` (22 rows)
- `rank_events` (2 rows)
- `rank_resets` (21 rows)
- `rank_tiers` (293 rows)
- `ranking_rewards` (140 rows)
- `ranking_tabs` (4 rows)
- `rankings` (14 rows)
- `ranks` (28 rows)
- `recommend_abilities` (30 rows)
- `recover_exp_effects` (2 rows)
- `repair_slave_effects` (16 rows)
- `replace_chat_keys` (37 rows)
- `replace_chat_texts` (67 rows)
- `replace_chats` (17 rows)
- `report_crime_effects` (5 rows)
- `reputation_resets` (1 rows)
- `reputation_rewards` (9 rows)
- `reset_aoe_diminishing_effects` (199 rows)
- `resident_conditions` (10 rows)
- `resident_rewards` (7 rows)
- `restore_mana_effects` (261 rows)
- `resurrection_waiting_times` (10 rows)
- `return_points` (1075 rows)
- `saga_quest_groups` (9 rows)
- `saga_quests` (195 rows)
- `schedule_items` (292 rows)
- `schema_migrations` (6292 rows)
- `scoped_f_effects` (5 rows)
- `selective_item_effect_elems` (4255 rows)
- `selective_item_effects` (704 rows)
- `server_configs` (2 rows)
- `server_infos` (11 rows)
- `server_transfer_restricted_items` (25 rows)
- `ship_models` (93 rows)
- `shipyard_rewards` (10 rows)
- `shipyard_steps` (94 rows)
- `shipyards` (8 rows)
- `siege_extortion_ratios` (9 rows)
- `siege_faction_dominions` (17 rows)
- `siege_faction_doodads` (101 rows)
- `siege_faction_troops` (5 rows)
- `siege_factions` (3 rows)
- `siege_items` (32 rows)
- `siege_plans` (184 rows)
- `siege_skills` (35 rows)
- `siege_zones` (4 rows)
- `skill_alert_conditions` (101 rows)
- `skill_controllers` (3354 rows)
- `skill_effects` (48745 rows)
- `skill_map_effects` (112 rows)
- `skill_modifiers` (1794 rows)
- `skill_products` (1127 rows)
- `skill_reagents` (2804 rows)
- `skill_req_skill_tags` (339 rows)
- `skill_req_skills` (1813 rows)
- `skill_reqs` (338 rows)
- `skill_synergy_buff_tags` (320 rows)
- `skill_synergy_icons` (699 rows)
- `skill_visual_groups` (86 rows)
- `skills` (38043 rows)
- `skin_colors` (142 rows)
- `slash_commands` (251 rows)
- `slash_functions` (49 rows)
- `slave_bindings` (331 rows)
- `slave_collision_damages` (16 rows)
- `slave_customizing_equip_slots` (232 rows)
- `slave_customizings` (17 rows)
- `slave_doodad_bindings` (1622 rows)
- `slave_drop_doodads` (76 rows)
- `slave_equip_kind_lists` (1774 rows)
- `slave_equip_kinds` (71 rows)
- `slave_equip_packs` (20 rows)
- `slave_equip_slots` (771 rows)
- `slave_equipment_equip_slot_packs` (61 rows)
- `slave_extend_model_links` (44 rows)
- `slave_extend_models` (6 rows)
- `slave_healing_point_doodads` (257 rows)
- `slave_initial_buffs` (1270 rows)
- `slave_initial_item_packs` (29 rows)
- `slave_initial_items` (403 rows)
- `slave_interaction_skills` (1183 rows)
- `slave_mount_skills` (2997 rows)
- `slave_passive_buffs` (968 rows)
- `slaves` (1605 rows)
- `sound_pack_items` (22031 rows)
- `sound_packs` (846 rows)
- `sounds` (8283 rows)
- `spawn_effects` (2803 rows)
- `spawn_fish_effects` (24 rows)
- `spawn_gimmick_effects` (353 rows)
- `special_effects` (44423 rows)
- `specialties` (81 rows)
- `specialty_bundle_items` (4256 rows)
- `specialty_bundles` (59 rows)
- `specialty_event_triggers` (18 rows)
- `specialty_events` (12 rows)
- `specialty_npcs` (38 rows)
- `sphere_accept_quest_quests` (14 rows)
- `sphere_accept_quests` (17 rows)
- `sphere_bubbles` (598 rows)
- `sphere_buffs` (108 rows)
- `sphere_chat_bubbles` (1268 rows)
- `sphere_doodad_interacts` (115 rows)
- `sphere_quest_mails` (1 rows)
- `sphere_quests` (1470 rows)
- `sphere_skills` (286 rows)
- `sphere_sounds` (7 rows)
- `spheres` (2602 rows)
- `sqlite_sequence` (1329 rows)
- `sub_zones` (1356 rows)
- `survey_form_question_options` (6 rows)
- `survey_form_questions` (26 rows)
- `survey_forms` (6 rows)
- `system_doodads` (9 rows)
- `system_faction_relations` (2883 rows)
- `system_factions` (124 rows)
- `system_feature_controls` (2 rows)
- `system_mail_template` (9 rows)
- `tagged_buffs` (52542 rows)
- `tagged_immune_buffs` (2645 rows)
- `tagged_items` (33226 rows)
- `tagged_npcs` (2682 rows)
- `tagged_require_buffs` (309 rows)
- `tagged_skills` (30852 rows)
- `tags` (5683 rows)
- `target_history_clear_effects` (8 rows)
- `target_unit_modifiers` (160 rows)
- `taxations` (26 rows)
- `tip_of_day_groups` (20 rows)
- `tip_of_days` (53 rows)
- `today_quest_goal_items` (15 rows)
- `today_quest_goals` (7 rows)
- `today_quest_group_quests` (1421 rows)
- `today_quest_groups` (128 rows)
- `today_quest_steps` (25 rows)
- `tooltip_skill_effects` (981 rows)
- `total_character_customs` (1589 rows)
- `tower_def_map_events` (3 rows)
- `tower_def_prog_alive_targets` (5 rows)
- `tower_def_prog_kill_targets` (349 rows)
- `tower_def_prog_spawn_targets` (569 rows)
- `tower_def_progs` (584 rows)
- `tower_defs` (177 rows)
- `tradegood_categories` (3 rows)
- `tradegood_materials` (9 rows)
- `tradegood_priceindices` (5 rows)
- `tradegoods` (3 rows)
- `transfer_binding_doodads` (55 rows)
- `transfer_bindings` (69 rows)
- `transfer_paths` (613 rows)
- `transfers` (156 rows)
- `translation_grossaries` (0 rows)
- `ucc_applicables` (359 rows)
- `ucc_categories` (1 rows)
- `ucc_emblems` (55 rows)
- `ucc_sub_categories` (1 rows)
- `ui_avi_subs` (10 rows)
- `ui_avis` (1 rows)
- `ui_content_info_feature_sets` (24 rows)
- `ui_content_infos` (33 rows)
- `ui_esc_menu_categories` (5 rows)
- `ui_esc_menus` (28 rows)
- `ui_hud_right_icon_menus` (7 rows)
- `ui_options` (150 rows)
- `ui_permissions` (3 rows)
- `ui_texts` (9861 rows)
- `ulc_guides` (6 rows)
- `ulcs` (3 rows)
- `unit_attribute_limits` (49 rows)
- `unit_formula_variables` (3660 rows)
- `unit_formulas` (480 rows)
- `unit_modifiers` (51900 rows)
- `unit_reqs` (29559 rows)
- `unit_status_buff_tags` (21 rows)
- `url_whitelists` (12 rows)
- `users` (282 rows)
- `vehicle_models` (272 rows)
- `wearable_formulas` (2 rows)
- `wearable_kinds` (5 rows)
- `wearable_slots` (17 rows)
- `wearables` (76 rows)
- `wi_details` (60 rows)
- `wi_group_wis` (57 rows)
- `wi_groups` (3 rows)
- `world_contents` (7405 rows)
- `world_divisions` (35 rows)
- `world_groups` (7 rows)
- `world_level_configs` (1 rows)
- `world_level_exp_modifiers` (8 rows)
- `world_level_hard_caps` (9 rows)
- `world_message_effects` (945 rows)
- `world_specific_content_configs` (48 rows)
- `world_var_defaults` (1 rows)
- `zone_climate_elems` (13 rows)
- `zone_climates` (10 rows)
- `zone_group_banned_tags` (1562 rows)
- `zone_group_dummy_factions` (12 rows)
- `zone_groups` (154 rows)
- `zone_score_contents` (9 rows)
- `zone_score_kind_rank_details` (2 rows)
- `zone_score_kinds` (16 rows)
- `zone_score_levels` (82 rows)
- `zone_waiting_factions` (64 rows)
- `zone_waitings` (25 rows)
- `zones` (319 rows)

### `npc_postures`
**스키마**
- `id` (INTEGER) PK
- `npc_posture_set_id` (INTEGER)
- `anim_action_id` (INTEGER)
- `talk_anim` (varchar(255))
- `start_tod_time` (INTEGER)

**샘플 (최대 3행)**
```
id: 1
npc_posture_set_id: 1
anim_action_id: 55
talk_anim: fist_pos_stn_stabler_talk
start_tod_time: 0
```
```
id: 3
npc_posture_set_id: 3
anim_action_id: 92
talk_anim: fist_pos_sit_chair_weaponshop_dealer_talk
start_tod_time: 0
```
```
id: 4
npc_posture_set_id: 4
anim_action_id: 54
talk_anim: 
start_tod_time: 0
```

### `npc_posture_sets`
**스키마**
- `id` (INTEGER) PK
- `name` (varchar(255))
- `quest_anim_action_id` (INTEGER)
- `comment` (varchar(255))

**샘플 (최대 3행)**
```
id: 1
name: stn_act_stabler_male
quest_anim_action_id: 0
comment: 마구간지기
```
```
id: 3
name: chair_weapon_male
quest_anim_action_id: 0
comment: 무기 상인
```
```
id: 4
name: stn_thinking_all_notalk
quest_anim_action_id: 97
comment: 
```

### `npcs`
**스키마**
- `id` (INTEGER) PK
- `name` (varchar(128))
- `char_race_id` (INTEGER)
- `npc_grade_id` (INTEGER)
- `npc_kind_id` (INTEGER)
- `level` (INTEGER)
- `npc_template_id` (INTEGER)
- `equip_cloths_id` (INTEGER)
- `equip_weapons_id` (INTEGER)
- `model_id` (INTEGER)
- `faction_id` (INTEGER)
- `skill_trainer` (boolean)
- `ai_file_id` (INTEGER)
- `merchant` (boolean)
- `npc_nickname_id` (INTEGER)
- `auctioneer` (boolean)
- `show_name_tag` (boolean)
- `visible_to_creator_only` (boolean)
- `no_exp` (boolean)
- `pet_item_id` (INTEGER)
- `base_skill_id` (INTEGER)
- `track_friendship` (boolean)
- `priest` (boolean)
- `comment1` (varchar(256))
- `npc_tendency_id` (INTEGER)
- `blacksmith` (boolean)
- `teleporter` (boolean)
- `opacity` (float)
- `ability_changer` (boolean)
- `scale` (float)
- `comment2` (varchar(255))
- `comment3` (varchar(255))
- `sight_range_scale` (float)
- `sight_fov_scale` (float)
- `milestone_id` (INTEGER)
- `attack_start_range_scale` (float)
- `aggression` (boolean)
- `exp_multiplier` (float)
- `exp_adder` (INTEGER)
- `stabler` (boolean)
- `accept_aggro_link` (boolean)
- `return_distance` (float)
- `npc_ai_param_id` (INTEGER)
- `non_pushable_by_actor` (boolean)
- `banker` (boolean)
- `aggro_link_special_rule_id` (INTEGER)
- `aggro_link_help_dist` (float)
- `aggro_link_sight_check` (boolean)
- `expedition` (boolean)
- `honor_point` (INTEGER)
- `trader` (boolean)
- `aggro_link_special_guard` (boolean)
- `aggro_link_special_ignore_npc_attacker` (boolean)
- `comment_wear` (varchar(256))
- `absolute_return_distance` (float)
- `repairman` (boolean)
- `activate_ai_always` (boolean)
- `so_state` (varchar(255))
- `specialty` (boolean)
- `sound_pack_id` (INTEGER)
- `specialty_coin_id` (INTEGER)
- `use_range_mod` (boolean)
- `npc_posture_set_id` (INTEGER)
- `mate_equip_slot_pack_id` (INTEGER)
- `mate_kind_id` (INTEGER)
- `engage_combat_give_quest_id` (INTEGER)
- `total_custom_id` (INTEGER)
- `no_apply_total_custom` (boolean)
- `base_skill_strafe` (boolean)
- `base_skill_delay` (float)
- `npc_interaction_set_id` (INTEGER)
- `use_abuser_list` (boolean)
- `return_when_enter_housing_area` (boolean)
- `look_converter` (boolean)
- `use_ddcms_mount_skill` (boolean)
- `crowd_effect` (boolean)
- `translate` (boolean)
- `no_penalty` (boolean)
- `show_faction_tag` (boolean)
- `check_target_under_terrain` (boolean)
- `decaying_sec_after_looted` (INTEGER)
- `show_on_boss_telescope` (boolean)
- `npc_strafe_param_id` (INTEGER)
- `force_target_me_on_attack` (boolean)
- `dont_pushable_like_ghost` (boolean)
- `mate_revive_delay` (INTEGER)
- `mate_revive_hp_percent` (INTEGER)
- `mate_revive_mp_percent` (INTEGER)
- `use_model_camera_distance` (boolean)
- `check_backpack` (boolean)
- `heir_level` (INTEGER)
- `tradegood_buy` (boolean)
- `npc_ai_client_param_id` (INTEGER)
- `run_away_threshold` (float)
- `friendly_near_quest_id` (INTEGER)
- `ragdoll_after_death_anim` (boolean)
- `multi_jump` (INTEGER)
- `multi_jump_pow_y` (float)
- `multi_jump_pow_z` (float)
- `party_flag` (boolean)
- `weapon_element_id` (INTEGER)
- `weapon_element_level` (INTEGER)
- `armor_type_id` (INTEGER)
- `armor_element_level` (INTEGER)
- `engage_combat_bgm_id` (INTEGER)
- `use_hp_bar_split` (boolean)
- `merchant_random_pack_id` (INTEGER)
- `prior_visibility` (boolean)

**샘플 (최대 3행)**
```
id: 1
name: Test Warrior11
char_race_id: 1
npc_grade_id: 1
npc_kind_id: 1
level: 20
npc_template_id: 9
equip_cloths_id: 1
equip_weapons_id: 
model_id: 10
faction_id: 1
skill_trainer: t
ai_file_id: 15
merchant: t
npc_nickname_id: 18
auctioneer: t
show_name_tag: f
visible_to_creator_only: f
no_exp: t
pet_item_id: 
base_skill_id: 2
track_friendship: t
priest: f
comment1: 
npc_tendency_id: 1
blacksmith: t
teleporter: f
opacity: 1
ability_changer: t
scale: 1
comment2: test1
comment3: 
sight_range_scale: 0
sight_fov_scale: 0
milestone_id: 35
attack_start_range_scale: 0
aggression: f
exp_multiplier: 1
exp_adder: 0
stabler: t
accept_aggro_link: t
return_distance: 50
npc_ai_param_id: 0
non_pushable_by_actor: f
banker: t
aggro_link_special_rule_id: 0
aggro_link_help_dist: 6
aggro_link_sight_check: f
expedition: f
honor_point: 0
trader: t
aggro_link_special_guard: f
aggro_link_special_ignore_npc_attacker: f
comment_wear: 
absolute_return_distance: 200
repairman: f
activate_ai_always: f
so_state: 
specialty: f
sound_pack_id: 
specialty_coin_id: 
use_range_mod: t
npc_posture_set_id: 306
mate_equip_slot_pack_id: 
mate_kind_id: 
engage_combat_give_quest_id: 
total_custom_id: 
no_apply_total_custom: f
base_skill_strafe: t
base_skill_delay: 0
npc_interaction_set_id: 114
use_abuser_list: t
return_when_enter_housing_area: f
look_converter: t
use_ddcms_mount_skill: f
crowd_effect: t
translate: f
no_penalty: f
show_faction_tag: f
check_target_under_terrain: f
decaying_sec_after_looted: 0
show_on_boss_telescope: f
npc_strafe_param_id: 
force_target_me_on_attack: f
dont_pushable_like_ghost: f
mate_revive_delay: 0
mate_revive_hp_percent: 0
mate_revive_mp_percent: 0
use_model_camera_distance: f
check_backpack: f
heir_level: 0
tradegood_buy: f
npc_ai_client_param_id: 0
run_away_threshold: 0
friendly_near_quest_id: 
ragdoll_after_death_anim: f
multi_jump: 1
multi_jump_pow_y: 1
multi_jump_pow_z: 1
party_flag: f
weapon_element_id: 0
weapon_element_level: 0
armor_type_id: 0
armor_element_level: 0
engage_combat_bgm_id: 0
use_hp_bar_split: t
merchant_random_pack_id: 0
prior_visibility: f
```
```
id: 2
name: Castle Guard
char_race_id: 1
npc_grade_id: 1
npc_kind_id: 1
level: 55
npc_template_id: 9
equip_cloths_id: 
equip_weapons_id: 
model_id: 1641
faction_id: 115
skill_trainer: f
ai_file_id: 15
merchant: f
npc_nickname_id: 0
auctioneer: f
show_name_tag: t
visible_to_creator_only: f
no_exp: t
pet_item_id: 
base_skill_id: 43759
track_friendship: f
priest: f
comment1: 
npc_tendency_id: 2
blacksmith: f
teleporter: f
opacity: 1
ability_changer: f
scale: 1
comment2: test
comment3: 
sight_range_scale: 1
sight_fov_scale: 1
milestone_id: 10
attack_start_range_scale: 1
aggression: t
exp_multiplier: 1
exp_adder: 0
stabler: f
accept_aggro_link: t
return_distance: 50
npc_ai_param_id: 1
non_pushable_by_actor: t
banker: f
aggro_link_special_rule_id: 0
aggro_link_help_dist: 6
aggro_link_sight_check: f
expedition: f
honor_point: 0
trader: f
aggro_link_special_guard: f
aggro_link_special_ignore_npc_attacker: f
comment_wear: 
absolute_return_distance: 200
repairman: f
activate_ai_always: f
so_state: 
specialty: f
sound_pack_id: 
specialty_coin_id: 
use_range_mod: t
npc_posture_set_id: 110
mate_equip_slot_pack_id: 
mate_kind_id: 
engage_combat_give_quest_id: 
total_custom_id: 
no_apply_total_custom: f
base_skill_strafe: t
base_skill_delay: 0
npc_interaction_set_id: 115
use_abuser_list: t
return_when_enter_housing_area: f
look_converter: f
use_ddcms_mount_skill: f
crowd_effect: t
translate: f
no_penalty: f
show_faction_tag: f
check_target_under_terrain: f
decaying_sec_after_looted: 0
show_on_boss_telescope: f
npc_strafe_param_id: 
force_target_me_on_attack: f
dont_pushable_like_ghost: f
mate_revive_delay: 0
mate_revive_hp_percent: 0
mate_revive_mp_percent: 0
use_model_camera_distance: f
check_backpack: f
heir_level: 28
tradegood_buy: f
npc_ai_client_param_id: 0
run_away_threshold: 0
friendly_near_quest_id: 
ragdoll_after_death_anim: f
multi_jump: 1
multi_jump_pow_y: 1
multi_jump_pow_z: 1
party_flag: f
weapon_element_id: 0
weapon_element_level: 0
armor_type_id: 0
armor_element_level: 0
engage_combat_bgm_id: 
use_hp_bar_split: t
merchant_random_pack_id: 0
prior_visibility: f
```
```
id: 3
name: Siege Mercenary
char_race_id: 1
npc_grade_id: 1
npc_kind_id: 1
level: 50
npc_template_id: 9
equip_cloths_id: 1
equip_weapons_id: 
model_id: 11
faction_id: 103
skill_trainer: f
ai_file_id: 15
merchant: f
npc_nickname_id: 0
auctioneer: f
show_name_tag: t
visible_to_creator_only: f
no_exp: f
pet_item_id: 
base_skill_id: 2
track_friendship: f
priest: f
comment1: 
npc_tendency_id: 
blacksmith: f
teleporter: f
opacity: 1
ability_changer: f
scale: 1
comment2: test
comment3: 
sight_range_scale: 1
sight_fov_scale: 1
milestone_id: 5
attack_start_range_scale: 1
aggression: t
exp_multiplier: 1
exp_adder: 0
stabler: f
accept_aggro_link: t
return_distance: 50
npc_ai_param_id: 0
non_pushable_by_actor: t
banker: f
aggro_link_special_rule_id: 0
aggro_link_help_dist: 6
aggro_link_sight_check: f
expedition: f
honor_point: 0
trader: f
aggro_link_special_guard: f
aggro_link_special_ignore_npc_attacker: f
comment_wear: 
absolute_return_distance: 200
repairman: f
activate_ai_always: f
so_state: 
specialty: f
sound_pack_id: 
specialty_coin_id: 
use_range_mod: t
npc_posture_set_id: 25
mate_equip_slot_pack_id: 
mate_kind_id: 
engage_combat_give_quest_id: 
total_custom_id: 
no_apply_total_custom: f
base_skill_strafe: t
base_skill_delay: 0
npc_interaction_set_id: 
use_abuser_list: t
return_when_enter_housing_area: f
look_converter: f
use_ddcms_mount_skill: f
crowd_effect: t
translate: f
no_penalty: f
show_faction_tag: f
check_target_under_terrain: f
decaying_sec_after_looted: 0
show_on_boss_telescope: f
npc_strafe_param_id: 
force_target_me_on_attack: f
dont_pushable_like_ghost: f
mate_revive_delay: 0
mate_revive_hp_percent: 0
mate_revive_mp_percent: 0
use_model_camera_distance: f
check_backpack: f
heir_level: 0
tradegood_buy: f
npc_ai_client_param_id: 0
run_away_threshold: 0
friendly_near_quest_id: 0
ragdoll_after_death_anim: f
multi_jump: 1
multi_jump_pow_y: 1
multi_jump_pow_z: 1
party_flag: f
weapon_element_id: 0
weapon_element_level: 0
armor_type_id: 0
armor_element_level: 0
engage_combat_bgm_id: 0
use_hp_bar_split: t
merchant_random_pack_id: 0
prior_visibility: f
```

### `anim_actions`
**스키마**
- `id` (INTEGER) PK
- `name` (varchar(255))
- `action_state_id` (INTEGER)
- `mainhand_tool_id` (INTEGER)
- `offhand_tool_id` (INTEGER)
- `anim_name` (varchar(255))
- `no_rotate` (boolean)
- `model_path` (varchar(255))
- `model_pos_x` (float)
- `model_pos_y` (float)
- `model_pos_z` (float)
- `model_angle` (float)
- `model_physic` (boolean)
- `mount_pose_id` (INTEGER)

**샘플 (최대 3행)**
```
id: 0
name: none
action_state_id: 0
mainhand_tool_id: 
offhand_tool_id: 
anim_name: none
no_rotate: f
model_path: 
model_pos_x: 0
model_pos_y: 0
model_pos_z: 0
model_angle: 0
model_physic: t
mount_pose_id: 0
```
```
id: 1
name: other
action_state_id: 0
mainhand_tool_id: 0
offhand_tool_id: 0
anim_name: other
no_rotate: f
model_path: 
model_pos_x: 0
model_pos_y: 0
model_pos_z: 0
model_angle: 0
model_physic: t
mount_pose_id: 0
```
```
id: 2
name: idle
action_state_id: 2
mainhand_tool_id: 0
offhand_tool_id: 0
anim_name: idle
no_rotate: f
model_path: 
model_pos_x: 0
model_pos_y: 0
model_pos_z: 0
model_angle: 0
model_physic: t
mount_pose_id: 0
```

### `const_anim_actions`
**스키마**
- `id` (INTEGER) PK
- `name` (varchar(255))
- `anim_action_id` (INTEGER)

**샘플 (최대 3행)**
```
id: 1
name: none
anim_action_id: 0
```
```
id: 2
name: other
anim_action_id: 1
```
```
id: 3
name: idle
anim_action_id: 2
```

### `anims`
**스키마**
- `id` (INTEGER) PK
- `name` (varchar(255))
- `loop` (boolean)
- `category_id` (INTEGER)
- `ride_ub` (varchar(255))
- `hang_ub` (varchar(255))
- `swim_ub` (varchar(255))
- `move_ub` (varchar(255))
- `relaxed_ub` (varchar(255))
- `swim_move_ub` (varchar(255))

**샘플 (최대 3행)**
```
id: 1
name: fist_co_attack_r
loop: f
category_id: 2
ride_ub: 
hang_ub: 
swim_ub: fist_co_swim_attack_r
move_ub: fist_co_attack_r_ub
relaxed_ub: 
swim_move_ub: fist_co_swim_attack_r
```
```
id: 2
name: fist_co_attack_r_2
loop: f
category_id: 2
ride_ub: 
hang_ub: fist_co_attack_r_2_ub
swim_ub: fist_co_swim_attack_r
move_ub: fist_co_attack_r_2_ub
relaxed_ub: 
swim_move_ub: fist_co_swim_attack_r
```
```
id: 3
name: onehand_co_attack_r_slash
loop: f
category_id: 1
ride_ub: 
hang_ub: onehand_co_attack_r_slash_ub
swim_ub: onehand_co_swim_attack_r_slash_ub
move_ub: onehand_co_attack_r_slash_ub
relaxed_ub: 
swim_move_ub: onehand_co_swim_attack_r_slash_ub
```

### `skills`
**스키마**
- `id` (INTEGER) PK
- `name` (varchar(255))
- `desc` (varchar(2000))
- `cost` (INTEGER)
- `icon_id` (INTEGER)
- `show` (boolean)
- `start_anim_id` (INTEGER)
- `fire_anim_id` (INTEGER)
- `ability_id` (INTEGER)
- `mana_cost` (INTEGER)
- `timing_id` (INTEGER)
- `weapon_slot_for_autoattack_id` (INTEGER)
- `cooldown_time` (integer(4))
- `casting_time` (INTEGER)
- `ignore_global_cooldown` (boolean)
- `effect_delay` (INTEGER)
- `effect_speed` (float)
- `effect_repeat_count` (INTEGER)
- `effect_repeat_tick` (INTEGER)
- `category_id` (INTEGER)
- `active_weapon_id` (INTEGER)
- `target_type_id` (INTEGER)
- `target_selection_id` (INTEGER)
- `target_relation_id` (INTEGER)
- `target_area_count` (INTEGER)
- `target_area_radius` (INTEGER)
- `weapon_slot_for_angle_id` (INTEGER)
- `target_angle` (INTEGER)
- `weapon_slot_for_range_id` (INTEGER)
- `min_range` (INTEGER)
- `max_range` (INTEGER)
- `keep_stealth` (boolean)
- `stop_autoattack` (boolean)
- `aggro` (INTEGER)
- `fx_group_id` (INTEGER)
- `projectile_id` (INTEGER)
- `check_obstacle` (boolean)
- `channeling_time` (INTEGER)
- `channeling_tick` (INTEGER)
- `channeling_mana` (INTEGER)
- `channeling_anim_id` (INTEGER)
- `channeling_target_buff_id` (INTEGER)
- `target_area_angle` (INTEGER)
- `ability_level` (INTEGER)
- `channeling_doodad_id` (INTEGER)
- `cooldown_tag_id` (INTEGER)
- `skill_controller_id` (INTEGER)
- `repeat_count` (INTEGER)
- `repeat_tick` (INTEGER)
- `toggle_buff_id` (INTEGER)
- `target_dead` (boolean)
- `channeling_buff_id` (INTEGER)
- `reagent_corpse_status_id` (INTEGER)
- `level_step` (INTEGER)
- `valid_height` (float)
- `target_valid_height` (float)
- `stop_casting_on_big_hit` (boolean)
- `stop_channeling_on_big_hit` (boolean)
- `auto_learn` (boolean)
- `mainhand_tool_id` (INTEGER)
- `offhand_tool_id` (INTEGER)
- `front_angle` (INTEGER)
- `mana_level_md` (float)
- `twohand_fire_anim_id` (INTEGER)
- `unmount` (boolean)
- `damage_type_id` (INTEGER)
- `milestone_id` (INTEGER)
- `match_animation` (boolean)
- `plot_id` (INTEGER)
- `use_anim_time` (boolean)
- `start_autoattack` (boolean)
- `consume_lp` (INTEGER)
- `target_alive` (boolean)
- `web_desc` (varchar(800))
- `target_water` (boolean)
- `use_skill_camera` (boolean)
- `controller_camera` (boolean)
- `camera_speed` (float)
- `controller_camera_speed` (INTEGER)
- `camera_max_distance` (float)
- `camera_duration` (float)
- `camera_acceleration` (float)
- `camera_slow_down_distance` (float)
- `camera_hold_z` (boolean)
- `casting_inc` (INTEGER)
- `casting_cancelable` (boolean)
- `casting_delayable` (boolean)
- `channeling_cancelable` (boolean)
- `target_offset_angle` (float)
- `target_offset_distance` (float)
- `actability_group_id` (INTEGER)
- `plot_only` (boolean)
- `pitch_angle` (float)
- `skill_controller_at_end` (boolean)
- `end_skill_controller` (boolean)
- `string_instrument_start_anim_id` (INTEGER)
- `percussion_instrument_start_anim_id` (INTEGER)
- `tube_instrument_start_anim_id` (INTEGER)
- `string_instrument_fire_anim_id` (INTEGER)
- `percussion_instrument_fire_anim_id` (INTEGER)
- `tube_instrument_fire_anim_id` (INTEGER)
- `or_unit_reqs` (boolean)
- `default_gcd` (boolean)
- `show_target_casting_time` (boolean)
- `valid_height_edge_to_edge` (boolean)
- `link_equip_slot_id` (INTEGER)
- `keep_mana_regen` (boolean)
- `crime_point` (INTEGER)
- `level_rule_no_consideration` (boolean)
- `use_weapon_cooldown_time` (boolean)
- `synergy_icon1_buffkind` (boolean)
- `synergy_icon1_id` (INTEGER)
- `synergy_icon2_buffkind` (boolean)
- `synergy_icon2_id` (INTEGER)
- `combat_dice_id` (INTEGER)
- `can_active_weapon_without_anim` (boolean)
- `custom_gcd` (INTEGER)
- `cancel_ongoing_buffs` (boolean)
- `cancel_ongoing_buff_exception_tag_id` (INTEGER)
- `match_animation_count` (boolean)
- `dual_wield_fire_anim_id` (INTEGER)
- `auto_fire` (boolean)
- `check_terrain` (boolean)
- `target_only_water` (boolean)
- `target_preoccupied` (boolean)
- `stop_channeling_on_start_skill` (boolean)
- `stop_casting_by_turn` (boolean)
- `target_my_npc` (boolean)
- `gain_life_point` (INTEGER)
- `target_fishing` (boolean)
- `auto_reuse` (boolean)
- `auto_reuse_delay` (INTEGER)
- `skill_points` (INTEGER)
- `doodad_hit_family` (INTEGER)
- `sensitive_operation` (boolean)
- `name_tr` (boolean)
- `desc_tr` (boolean)
- `web_desc_tr` (boolean)
- `first_reagent_only` (boolean)
- `target_decal_radius` (INTEGER)
- `doodad_bundle_id` (INTEGER)
- `skip_quest_apply_use_item` (boolean)
- `calc_user_level` (boolean)
- `casting_useable` (boolean)
- `skip_validate_source` (boolean)
- `char_race_id` (INTEGER)
- `max_combat_resource` (INTEGER)
- `min_combat_resource` (INTEGER)
- `account_cooldown` (boolean)
- `switch_to_skill_cooldown` (boolean)
- `second_cooldown_tag_id` (INTEGER)
- `third_cooldown_tag_id` (INTEGER)
- `is_dropable_backpack` (boolean)
- `charge_count` (INTEGER)
- `charge_cooldown_time` (INTEGER)
- `precedence_skill_id` (INTEGER)
- `comments` (varchar(255))
- `req_points` (INTEGER)
- `weapon_gcd_id` (INTEGER)
- `random_unit_targeting` (boolean)
- `targetable_stealth` (boolean)
- `target_unit_param` (INTEGER)
- `shot_gun_start_anim_id` (INTEGER)
- `shot_gun_fire_anim_id` (INTEGER)
- `combat_resource_id` (INTEGER)
- `use_input_direction` (boolean)
- `use_condition_bits` (integer(8))
- `skill_learn_item_id` (INTEGER)
- `skill_learn_item_amount` (INTEGER)

**샘플 (최대 3행)**
```
id: 2
name: 근접 공격
desc: 근접 무기로 공격 속도 마다 대상을 자동으로 공격합니다
cost: 10
icon_id: 1456
show: 1
start_anim_id: 
fire_anim_id: 
ability_id: 0
mana_cost: 0
timing_id: 0
weapon_slot_for_autoattack_id: 15
cooldown_time: 300
casting_time: 0
ignore_global_cooldown: f
effect_delay: 0
effect_speed: 0
effect_repeat_count: 1
effect_repeat_tick: 0
category_id: 1
active_weapon_id: 1
target_type_id: 4
target_selection_id: 2
target_relation_id: 0
target_area_count: 1
target_area_radius: 0
weapon_slot_for_angle_id: 15
target_angle: 180
weapon_slot_for_range_id: 15
min_range: 0
max_range: 25
keep_stealth: f
stop_autoattack: f
aggro: 0
fx_group_id: 46
projectile_id: 
check_obstacle: t
channeling_time: 0
channeling_tick: 0
channeling_mana: 0
channeling_anim_id: 
channeling_target_buff_id: 
target_area_angle: 360
ability_level: 1
channeling_doodad_id: 
cooldown_tag_id: 
skill_controller_id: 0
repeat_count: 1
repeat_tick: 100
toggle_buff_id: 
target_dead: f
channeling_buff_id: 
reagent_corpse_status_id: 0
level_step: 0
valid_height: 0
target_valid_height: 0
stop_casting_on_big_hit: f
stop_channeling_on_big_hit: f
auto_learn: t
mainhand_tool_id: 
offhand_tool_id: 
front_angle: 0
mana_level_md: 0
twohand_fire_anim_id: 
unmount: f
damage_type_id: 1
milestone_id: 5
match_animation: f
plot_id: 
use_anim_time: t
start_autoattack: f
consume_lp: 0
target_alive: t
web_desc: 근접 무기로 공격 속도 마다 대상을 자동으로 공격합니다
target_water: t
use_skill_camera: f
controller_camera: f
camera_speed: 20
controller_camera_speed: 80
camera_max_distance: 5
camera_duration: 1
camera_acceleration: 0.2
camera_slow_down_distance: 2
camera_hold_z: f
casting_inc: 0
casting_cancelable: f
casting_delayable: f
channeling_cancelable: f
target_offset_angle: 0
target_offset_distance: 0
actability_group_id: 
plot_only: f
pitch_angle: 0
skill_controller_at_end: f
end_skill_controller: f
string_instrument_start_anim_id: 
percussion_instrument_start_anim_id: 
tube_instrument_start_anim_id: 
string_instrument_fire_anim_id: 
percussion_instrument_fire_anim_id: 
tube_instrument_fire_anim_id: 
or_unit_reqs: f
default_gcd: t
show_target_casting_time: t
valid_height_edge_to_edge: t
link_equip_slot_id: -1
keep_mana_regen: f
crime_point: 0
level_rule_no_consideration: f
use_weapon_cooldown_time: f
synergy_icon1_buffkind: t
synergy_icon1_id: 0
synergy_icon2_buffkind: t
synergy_icon2_id: 0
combat_dice_id: 1
can_active_weapon_without_anim: f
custom_gcd: 0
cancel_ongoing_buffs: t
cancel_ongoing_buff_exception_tag_id: 
match_animation_count: f
dual_wield_fire_anim_id: 
auto_fire: f
check_terrain: f
target_only_water: f
target_preoccupied: f
stop_channeling_on_start_skill: f
stop_casting_by_turn: f
target_my_npc: f
gain_life_point: 0
target_fishing: f
auto_reuse: f
auto_reuse_delay: 0
skill_points: 1
doodad_hit_family: 0
sensitive_operation: f
name_tr: t
desc_tr: t
web_desc_tr: f
first_reagent_only: f
target_decal_radius: 0
doodad_bundle_id: 
skip_quest_apply_use_item: f
calc_user_level: f
casting_useable: f
skip_validate_source: f
char_race_id: 0
max_combat_resource: 0
min_combat_resource: 0
account_cooldown: f
switch_to_skill_cooldown: f
second_cooldown_tag_id: 
third_cooldown_tag_id: 
is_dropable_backpack: f
charge_count: 0
charge_cooldown_time: 0
precedence_skill_id: 
comments: 
req_points: 0
weapon_gcd_id: -1
random_unit_targeting: f
targetable_stealth: f
target_unit_param: 127
shot_gun_start_anim_id: 
shot_gun_fire_anim_id: 
combat_resource_id: 
use_input_direction: f
use_condition_bits: 1
skill_learn_item_id: 0
skill_learn_item_amount: 0
```
```
id: 3
name: Offhand (1레벨)
desc: 보조무기 공격을 자동으로 실행합니다.
cost: 0
icon_id: 375
show: 0
start_anim_id: 
fire_anim_id: 4
ability_id: 0
mana_cost: 0
timing_id: 0
weapon_slot_for_autoattack_id: 16
cooldown_time: 0
casting_time: 0
ignore_global_cooldown: t
effect_delay: 0
effect_speed: 0
effect_repeat_count: 1
effect_repeat_tick: 0
category_id: 1
active_weapon_id: 1
target_type_id: 4
target_selection_id: 2
target_relation_id: 0
target_area_count: 1
target_area_radius: 0
weapon_slot_for_angle_id: 16
target_angle: 180
weapon_slot_for_range_id: 16
min_range: 0
max_range: 4
keep_stealth: f
stop_autoattack: f
aggro: 0
fx_group_id: 184
projectile_id: 
check_obstacle: t
channeling_time: 0
channeling_tick: 0
channeling_mana: 0
channeling_anim_id: 
channeling_target_buff_id: 0
target_area_angle: 360
ability_level: 1
channeling_doodad_id: 
cooldown_tag_id: 
skill_controller_id: 0
repeat_count: 1
repeat_tick: 100
toggle_buff_id: 
target_dead: f
channeling_buff_id: 
reagent_corpse_status_id: 0
level_step: 0
valid_height: 2
target_valid_height: 0
stop_casting_on_big_hit: f
stop_channeling_on_big_hit: f
auto_learn: t
mainhand_tool_id: 
offhand_tool_id: 
front_angle: 0
mana_level_md: 0
twohand_fire_anim_id: 
unmount: f
damage_type_id: 1
milestone_id: 5
match_animation: f
plot_id: 
use_anim_time: t
start_autoattack: f
consume_lp: 0
target_alive: t
web_desc: 
target_water: t
use_skill_camera: f
controller_camera: f
camera_speed: 20
controller_camera_speed: 80
camera_max_distance: 5
camera_duration: 1
camera_acceleration: 0.2
camera_slow_down_distance: 2
camera_hold_z: f
casting_inc: 0
casting_cancelable: f
casting_delayable: f
channeling_cancelable: f
target_offset_angle: 0
target_offset_distance: 0
actability_group_id: 
plot_only: t
pitch_angle: 0
skill_controller_at_end: f
end_skill_controller: f
string_instrument_start_anim_id: 
percussion_instrument_start_anim_id: 
tube_instrument_start_anim_id: 
string_instrument_fire_anim_id: 
percussion_instrument_fire_anim_id: 
tube_instrument_fire_anim_id: 
or_unit_reqs: f
default_gcd: t
show_target_casting_time: t
valid_height_edge_to_edge: t
link_equip_slot_id: -1
keep_mana_regen: f
crime_point: 0
level_rule_no_consideration: f
use_weapon_cooldown_time: f
synergy_icon1_buffkind: t
synergy_icon1_id: 0
synergy_icon2_buffkind: t
synergy_icon2_id: 0
combat_dice_id: 1
can_active_weapon_without_anim: f
custom_gcd: 0
cancel_ongoing_buffs: t
cancel_ongoing_buff_exception_tag_id: 
match_animation_count: f
dual_wield_fire_anim_id: 
auto_fire: f
check_terrain: f
target_only_water: f
target_preoccupied: f
stop_channeling_on_start_skill: f
stop_casting_by_turn: f
target_my_npc: f
gain_life_point: 0
target_fishing: f
auto_reuse: f
auto_reuse_delay: 0
skill_points: 1
doodad_hit_family: 0
sensitive_operation: f
name_tr: f
desc_tr: f
web_desc_tr: f
first_reagent_only: f
target_decal_radius: 0
doodad_bundle_id: 0
skip_quest_apply_use_item: f
calc_user_level: f
casting_useable: f
skip_validate_source: f
char_race_id: 0
max_combat_resource: 0
min_combat_resource: 0
account_cooldown: f
switch_to_skill_cooldown: f
second_cooldown_tag_id: 
third_cooldown_tag_id: 
is_dropable_backpack: f
charge_count: 0
charge_cooldown_time: 0
precedence_skill_id: 0
comments: 
req_points: 0
weapon_gcd_id: -1
random_unit_targeting: f
targetable_stealth: f
target_unit_param: 127
shot_gun_start_anim_id: 
shot_gun_fire_anim_id: 
combat_resource_id: 
use_input_direction: f
use_condition_bits: 1
skill_learn_item_id: 0
skill_learn_item_amount: 0
```
```
id: 4
name: 원거리 공격
desc: 원거리 공격을 자동으로 실행합니다.
cost: 10
icon_id: 1457
show: f
start_anim_id: 
fire_anim_id: 9
ability_id: 0
mana_cost: 0
timing_id: 0
weapon_slot_for_autoattack_id: 17
cooldown_time: 500
casting_time: 0
ignore_global_cooldown: t
effect_delay: 0
effect_speed: 40
effect_repeat_count: 1
effect_repeat_tick: 0
category_id: 1
active_weapon_id: 2
target_type_id: 4
target_selection_id: 2
target_relation_id: 0
target_area_count: 1
target_area_radius: 0
weapon_slot_for_angle_id: 17
target_angle: 90
weapon_slot_for_range_id: 17
min_range: 4
max_range: 25
keep_stealth: f
stop_autoattack: f
aggro: 0
fx_group_id: 116
projectile_id: 9
check_obstacle: t
channeling_time: 0
channeling_tick: 0
channeling_mana: 0
channeling_anim_id: 
channeling_target_buff_id: 
target_area_angle: 360
ability_level: 1
channeling_doodad_id: 
cooldown_tag_id: 
skill_controller_id: 0
repeat_count: 1
repeat_tick: 100
toggle_buff_id: 
target_dead: f
channeling_buff_id: 
reagent_corpse_status_id: 0
level_step: 0
valid_height: 0
target_valid_height: 0
stop_casting_on_big_hit: f
stop_channeling_on_big_hit: f
auto_learn: t
mainhand_tool_id: 
offhand_tool_id: 
front_angle: 0
mana_level_md: 1
twohand_fire_anim_id: 
unmount: f
damage_type_id: 4
milestone_id: 5
match_animation: f
plot_id: 
use_anim_time: t
start_autoattack: f
consume_lp: 0
target_alive: t
web_desc: 
target_water: t
use_skill_camera: f
controller_camera: f
camera_speed: 20
controller_camera_speed: 80
camera_max_distance: 5
camera_duration: 1
camera_acceleration: 0.2
camera_slow_down_distance: 2
camera_hold_z: f
casting_inc: 0
casting_cancelable: f
casting_delayable: f
channeling_cancelable: f
target_offset_angle: 0
target_offset_distance: 0
actability_group_id: 
plot_only: f
pitch_angle: 0
skill_controller_at_end: f
end_skill_controller: f
string_instrument_start_anim_id: 
percussion_instrument_start_anim_id: 
tube_instrument_start_anim_id: 
string_instrument_fire_anim_id: 
percussion_instrument_fire_anim_id: 
tube_instrument_fire_anim_id: 
or_unit_reqs: f
default_gcd: t
show_target_casting_time: t
valid_height_edge_to_edge: t
link_equip_slot_id: -1
keep_mana_regen: f
crime_point: 0
level_rule_no_consideration: f
use_weapon_cooldown_time: f
synergy_icon1_buffkind: t
synergy_icon1_id: 0
synergy_icon2_buffkind: t
synergy_icon2_id: 0
combat_dice_id: 2
can_active_weapon_without_anim: f
custom_gcd: 0
cancel_ongoing_buffs: t
cancel_ongoing_buff_exception_tag_id: 
match_animation_count: f
dual_wield_fire_anim_id: 
auto_fire: f
check_terrain: f
target_only_water: f
target_preoccupied: f
stop_channeling_on_start_skill: f
stop_casting_by_turn: f
target_my_npc: f
gain_life_point: 0
target_fishing: f
auto_reuse: f
auto_reuse_delay: 0
skill_points: 1
doodad_hit_family: 0
sensitive_operation: f
name_tr: t
desc_tr: t
web_desc_tr: f
first_reagent_only: f
target_decal_radius: 0
doodad_bundle_id: 
skip_quest_apply_use_item: f
calc_user_level: f
casting_useable: f
skip_validate_source: f
char_race_id: 0
max_combat_resource: 0
min_combat_resource: 0
account_cooldown: f
switch_to_skill_cooldown: f
second_cooldown_tag_id: 
third_cooldown_tag_id: 
is_dropable_backpack: f
charge_count: 0
charge_cooldown_time: 0
precedence_skill_id: 
comments: 
req_points: 0
weapon_gcd_id: -1
random_unit_targeting: f
targetable_stealth: f
target_unit_param: 127
shot_gun_start_anim_id: 
shot_gun_fire_anim_id: 1090
combat_resource_id: 
use_input_direction: f
use_condition_bits: 1
skill_learn_item_id: 0
skill_learn_item_amount: 0
```

### `skill_effects`
**스키마**
- `id` (INTEGER) PK
- `skill_id` (INTEGER)
- `effect_id` (INTEGER)
- `weight` (INTEGER)
- `start_level` (INTEGER)
- `end_level` (INTEGER)
- `friendly` (boolean)
- `non_friendly` (boolean)
- `target_buff_tag_id` (INTEGER)
- `target_nobuff_tag_id` (INTEGER)
- `source_buff_tag_id` (INTEGER)
- `source_nobuff_tag_id` (INTEGER)
- `chance` (INTEGER)
- `front` (boolean)
- `back` (boolean)
- `target_npc_tag_id` (INTEGER)
- `application_method_id` (INTEGER)
- `synergy_text` (boolean)
- `consume_source_item` (boolean)
- `consume_item_id` (INTEGER)
- `consume_item_count` (INTEGER)
- `always_hit` (boolean)
- `item_set_id` (INTEGER)
- `interaction_success_hit` (boolean)
- `enable` (boolean)
- `start_casting_use_chance` (INTEGER)
- `end_casting_use_chance` (INTEGER)
- `start_combat_resource` (INTEGER)
- `end_combat_resource` (INTEGER)
- `check_target_tag_src` (boolean)
- `check_no_target_tag_src` (boolean)
- `check_source_tag_src` (boolean)
- `check_no_source_tag_src` (boolean)
- `excute_effect_on_fire` (boolean)
- `source_buff_stack_count_min` (INTEGER)
- `source_buff_stack_count_max` (INTEGER)
- `target_buff_stack_count_min` (INTEGER)
- `target_buff_stack_count_max` (INTEGER)
- `source_except_buff_stack_count_min` (INTEGER)
- `source_except_buff_stack_count_max` (INTEGER)
- `target_except_buff_stack_count_min` (INTEGER)
- `target_except_buff_stack_count_max` (INTEGER)
- `target_combat_resource_id` (INTEGER)

**샘플 (최대 3행)**
```
id: 1
skill_id: 2
effect_id: 1
weight: 0
start_level: 1
end_level: 99
friendly: t
non_friendly: t
target_buff_tag_id: 
target_nobuff_tag_id: 
source_buff_tag_id: 
source_nobuff_tag_id: 
chance: 100
front: t
back: f
target_npc_tag_id: 
application_method_id: 1
synergy_text: f
consume_source_item: f
consume_item_id: 
consume_item_count: 1
always_hit: f
item_set_id: 0
interaction_success_hit: f
enable: t
start_casting_use_chance: 1
end_casting_use_chance: 100
start_combat_resource: 0
end_combat_resource: 0
check_target_tag_src: f
check_no_target_tag_src: f
check_source_tag_src: f
check_no_source_tag_src: f
excute_effect_on_fire: f
source_buff_stack_count_min: 0
source_buff_stack_count_max: 0
target_buff_stack_count_min: 0
target_buff_stack_count_max: 0
source_except_buff_stack_count_min: 0
source_except_buff_stack_count_max: 0
target_except_buff_stack_count_min: 0
target_except_buff_stack_count_max: 0
target_combat_resource_id: 
```
```
id: 2
skill_id: 3
effect_id: 2
weight: 0
start_level: 1
end_level: 99
friendly: t
non_friendly: t
target_buff_tag_id: 
target_nobuff_tag_id: 
source_buff_tag_id: 
source_nobuff_tag_id: 
chance: 100
front: t
back: f
target_npc_tag_id: 
application_method_id: 1
synergy_text: f
consume_source_item: f
consume_item_id: 0
consume_item_count: 1
always_hit: f
item_set_id: 0
interaction_success_hit: f
enable: t
start_casting_use_chance: 1
end_casting_use_chance: 100
start_combat_resource: 0
end_combat_resource: 0
check_target_tag_src: f
check_no_target_tag_src: f
check_source_tag_src: f
check_no_source_tag_src: f
excute_effect_on_fire: f
source_buff_stack_count_min: 0
source_buff_stack_count_max: 0
target_buff_stack_count_min: 0
target_buff_stack_count_max: 0
source_except_buff_stack_count_min: 0
source_except_buff_stack_count_max: 0
target_except_buff_stack_count_min: 0
target_except_buff_stack_count_max: 0
target_combat_resource_id: 
```
```
id: 48
skill_id: 7001
effect_id: 33
weight: 0
start_level: 1
end_level: 99
friendly: t
non_friendly: t
target_buff_tag_id: 
target_nobuff_tag_id: 
source_buff_tag_id: 
source_nobuff_tag_id: 
chance: 100
front: t
back: t
target_npc_tag_id: 
application_method_id: 1
synergy_text: f
consume_source_item: f
consume_item_id: 0
consume_item_count: 1
always_hit: f
item_set_id: 0
interaction_success_hit: f
enable: t
start_casting_use_chance: 1
end_casting_use_chance: 100
start_combat_resource: 0
end_combat_resource: 0
check_target_tag_src: f
check_no_target_tag_src: f
check_source_tag_src: f
check_no_source_tag_src: f
excute_effect_on_fire: f
source_buff_stack_count_min: 0
source_buff_stack_count_max: 0
target_buff_stack_count_min: 0
target_buff_stack_count_max: 0
source_except_buff_stack_count_min: 0
source_except_buff_stack_count_max: 0
target_except_buff_stack_count_min: 0
target_except_buff_stack_count_max: 0
target_combat_resource_id: 
```

### `items`
**스키마**
- `id` (INTEGER) PK
- `name` (varchar(255))
- `category_id` (INTEGER)
- `level` (integer(2))
- `description` (varchar(255))
- `bind_id` (INTEGER)
- `pickup_limit` (integer(1))
- `max_stack_size` (INTEGER)
- `icon_id` (INTEGER)
- `sellable` (boolean)
- `use_skill_id` (INTEGER)
- `use_skill_as_reagent` (boolean)
- `impl_id` (INTEGER)
- `pickup_sound_id` (INTEGER)
- `milestone_id` (INTEGER)
- `buff_id` (INTEGER)
- `gradable` (boolean)
- `loot_multi` (boolean)
- `loot_quest_id` (INTEGER)
- `notify_ui` (boolean)
- `use_or_equipment_sound_id` (INTEGER)
- `exp_abs_lifetime` (INTEGER)
- `exp_online_lifetime` (INTEGER)
- `exp_date` (datetime)
- `specialty_zone_id` (INTEGER)
- `level_requirement` (INTEGER)
- `comment` (varchar(255))
- `auction_a_category_id` (integer(1))
- `auction_b_category_id` (integer(1))
- `auction_c_category_id` (integer(1))
- `level_limit` (INTEGER)
- `fixed_grade` (integer(1))
- `disenchantable` (boolean)
- `actability_group_id` (INTEGER)
- `actability_requirement` (INTEGER)
- `char_gender_id` (integer(1))
- `one_time_sale` (boolean)
- `limited_sale_count` (INTEGER)
- `male_icon_id` (INTEGER)
- `over_icon_id` (INTEGER)
- `translate` (boolean)
- `auto_register_to_actionbar` (boolean)
- `use_skill_lifetime` (INTEGER)
- `use_skill_recharge_restrict_item_id` (INTEGER)
- `craft_id` (INTEGER)
- `side_effect` (boolean)
- `ingameshop_main_category` (integer(1))
- `ingameshop_sub_category` (integer(1))
- `auction_charge_default` (boolean)
- `auction_charge` (INTEGER)
- `expedition_level` (INTEGER)
- `max_enchantable_grade` (integer(1))
- `cash_item` (boolean)
- `auction_only` (boolean)
- `auto_complete` (boolean)
- `uid` (integer(8))
- `proc_lifetime` (INTEGER)
- `proc_recharge_restrict_item_id` (INTEGER)
- `max_enchant_scale_id` (INTEGER)
- `auto_loot` (boolean)
- `exp_day_of_week_id` (integer(1))
- `exp_day_of_week_min` (INTEGER)
- `period_base_date` (datetime)

**샘플 (최대 3행)**
```
id: 3
name: Default Bag
category_id: 5
level: 1
description: 
bind_id: 1
pickup_limit: 0
max_stack_size: 1
icon_id: 1592
sellable: t
use_skill_id: 0
use_skill_as_reagent: f
impl_id: 4
pickup_sound_id: 50
milestone_id: 10
buff_id: 0
gradable: f
loot_multi: f
loot_quest_id: 0
notify_ui: t
use_or_equipment_sound_id: 323
exp_abs_lifetime: 0
exp_online_lifetime: 0
exp_date: 
specialty_zone_id: 0
level_requirement: 1
comment: 
auction_a_category_id: 
auction_b_category_id: 
auction_c_category_id: 
level_limit: 0
fixed_grade: -1
disenchantable: t
actability_group_id: 
actability_requirement: 0
char_gender_id: 0
one_time_sale: f
limited_sale_count: 0
male_icon_id: 
over_icon_id: 
translate: f
auto_register_to_actionbar: f
use_skill_lifetime: 0
use_skill_recharge_restrict_item_id: 0
craft_id: 
side_effect: f
ingameshop_main_category: 0
ingameshop_sub_category: 0
auction_charge_default: t
auction_charge: 0
expedition_level: 0
max_enchantable_grade: -1
cash_item: f
auction_only: f
auto_complete: t
uid: 3375310485
proc_lifetime: 0
proc_recharge_restrict_item_id: 0
max_enchant_scale_id: 0
auto_loot: f
exp_day_of_week_id: 8
exp_day_of_week_min: 0
period_base_date: 
```
```
id: 5
name: 테스트용
category_id: 100
level: 1
description: 종군 기자
bind_id: 1
pickup_limit: 1
max_stack_size: 1
icon_id: 5876
sellable: t
use_skill_id: 0
use_skill_as_reagent: t
impl_id: 1
pickup_sound_id: 201
milestone_id: 10
buff_id: 0
gradable: t
loot_multi: f
loot_quest_id: 0
notify_ui: t
use_or_equipment_sound_id: 328
exp_abs_lifetime: 0
exp_online_lifetime: 0
exp_date: 
specialty_zone_id: 
level_requirement: 1
comment: 
auction_a_category_id: 
auction_b_category_id: 
auction_c_category_id: 
level_limit: 0
fixed_grade: -1
disenchantable: t
actability_group_id: 
actability_requirement: 0
char_gender_id: 0
one_time_sale: f
limited_sale_count: 0
male_icon_id: 
over_icon_id: 
translate: f
auto_register_to_actionbar: f
use_skill_lifetime: 0
use_skill_recharge_restrict_item_id: 0
craft_id: 0
side_effect: f
ingameshop_main_category: 0
ingameshop_sub_category: 0
auction_charge_default: t
auction_charge: 0
expedition_level: 0
max_enchantable_grade: -1
cash_item: f
auction_only: f
auto_complete: t
uid: 4285597272
proc_lifetime: 0
proc_recharge_restrict_item_id: 0
max_enchant_scale_id: 0
auto_loot: f
exp_day_of_week_id: 8
exp_day_of_week_min: 0
period_base_date: 
```
```
id: 7
name: 테스트_칼A_메탈
category_id: 100
level: 10
description: 
bind_id: 1
pickup_limit: 0
max_stack_size: 1
icon_id: 6409
sellable: t
use_skill_id: 0
use_skill_as_reagent: t
impl_id: 1
pickup_sound_id: 201
milestone_id: 10
buff_id: 0
gradable: t
loot_multi: f
loot_quest_id: 0
notify_ui: t
use_or_equipment_sound_id: 328
exp_abs_lifetime: 0
exp_online_lifetime: 0
exp_date: 
specialty_zone_id: 
level_requirement: 30
comment: 
auction_a_category_id: 
auction_b_category_id: 
auction_c_category_id: 
level_limit: 0
fixed_grade: -1
disenchantable: t
actability_group_id: 
actability_requirement: 0
char_gender_id: 0
one_time_sale: f
limited_sale_count: 0
male_icon_id: 
over_icon_id: 
translate: f
auto_register_to_actionbar: f
use_skill_lifetime: 0
use_skill_recharge_restrict_item_id: 0
craft_id: 0
side_effect: f
ingameshop_main_category: 0
ingameshop_sub_category: 0
auction_charge_default: t
auction_charge: 0
expedition_level: 0
max_enchantable_grade: -1
cash_item: f
auction_only: f
auto_complete: t
uid: 1048009278
proc_lifetime: 0
proc_recharge_restrict_item_id: 0
max_enchant_scale_id: 0
auto_loot: f
exp_day_of_week_id: 8
exp_day_of_week_min: 0
period_base_date: 
```

### `item_grades`
**스키마**
- `id` (INTEGER) PK
- `name` (varchar(255))
- `grade_order` (INTEGER)
- `var_holdable_dps` (float)
- `var_holdable_armor` (float)
- `var_holdable_magic_dps` (float)
- `var_wearable_armor` (float)
- `var_wearable_magic_resistance` (float)
- `color_argb` (varchar(8))
- `comments` (varchar(255))
- `durability_value` (float)
- `icon_id` (INTEGER)
- `upgrade_ratio` (INTEGER)
- `stat_multiplier` (INTEGER)
- `refund_multiplier` (INTEGER)
- `var_holdable_heal_dps` (float)
- `var_holdable_magic_resist` (float)

**샘플 (최대 3행)**
```
id: 0
name: Lv.1 일반
grade_order: 1
var_holdable_dps: 1
var_holdable_armor: 1
var_holdable_magic_dps: 1
var_wearable_armor: 1
var_wearable_magic_resistance: 1
color_argb: FFBA976D
comments: common
durability_value: 1
icon_id: 5767
upgrade_ratio: 100000
stat_multiplier: 100
refund_multiplier: 100
var_holdable_heal_dps: 1
var_holdable_magic_resist: 1
```
```
id: 1
name: Lv.0 저급
grade_order: 0
var_holdable_dps: 0.8
var_holdable_armor: 0.8
var_holdable_magic_dps: 0.8
var_wearable_armor: 0.8
var_wearable_magic_resistance: 0.8
color_argb: FF949293
comments: poor
durability_value: 0.5
icon_id: 5768
upgrade_ratio: 100000
stat_multiplier: 80
refund_multiplier: 50
var_holdable_heal_dps: 0.8
var_holdable_magic_resist: 0.8
```
```
id: 2
name: Lv.2 고급
grade_order: 2
var_holdable_dps: 1.05
var_holdable_armor: 1.05
var_holdable_magic_dps: 1.05
var_wearable_armor: 1.05
var_wearable_magic_resistance: 1.05
color_argb: FF77b064
comments: uncommon
durability_value: 1.05
icon_id: 5769
upgrade_ratio: 100000
stat_multiplier: 108
refund_multiplier: 150
var_holdable_heal_dps: 1.05
var_holdable_magic_resist: 1.05
```

### `quest_acts`
**스키마**
- `id` (INTEGER) PK
- `quest_component_id` (INTEGER)
- `act_detail_id` (INTEGER)
- `act_detail_type` (varchar(255))
- `enable` (boolean)

**샘플 (최대 3행)**
```
id: 31
quest_component_id: 23
act_detail_id: 6
act_detail_type: QuestActConAcceptNpc
enable: t
```
```
id: 36
quest_component_id: 27
act_detail_id: 7
act_detail_type: QuestActConAcceptNpc
enable: t
```
```
id: 40
quest_component_id: 31
act_detail_id: 3
act_detail_type: QuestActObjItemGather
enable: t
```

### `quest_components`
**스키마**
- `id` (INTEGER) PK
- `quest_context_id` (INTEGER)
- `component_kind_id` (INTEGER)
- `next_component` (INTEGER)
- `npc_ai_id` (INTEGER)
- `npc_id` (INTEGER)
- `skill_id` (INTEGER)
- `skill_self` (boolean)
- `ai_path_name` (varchar(255))
- `ai_path_type_id` (INTEGER)
- `sound_id` (INTEGER)
- `npc_spawner_id` (INTEGER)
- `play_cinema_before_bubble` (boolean)
- `ai_command_set_id` (INTEGER)
- `or_unit_reqs` (boolean)
- `cinema_id` (INTEGER)
- `summary_voice_id` (INTEGER)
- `hide_quest_marker` (boolean)
- `buff_id` (INTEGER)

**샘플 (최대 3행)**
```
id: 23
quest_context_id: 13
component_kind_id: 2
next_component: 0
npc_ai_id: 1
npc_id: 
skill_id: 
skill_self: f
ai_path_name: 
ai_path_type_id: 0
sound_id: 
npc_spawner_id: 743
play_cinema_before_bubble: t
ai_command_set_id: 
or_unit_reqs: f
cinema_id: 
summary_voice_id: 
hide_quest_marker: f
buff_id: 
```
```
id: 24
quest_context_id: 13
component_kind_id: 4
next_component: 0
npc_ai_id: 1
npc_id: 
skill_id: 
skill_self: t
ai_path_name: 
ai_path_type_id: 0
sound_id: 
npc_spawner_id: 0
play_cinema_before_bubble: t
ai_command_set_id: 
or_unit_reqs: f
cinema_id: 
summary_voice_id: 
hide_quest_marker: f
buff_id: 
```
```
id: 27
quest_context_id: 44
component_kind_id: 2
next_component: 0
npc_ai_id: 1
npc_id: 
skill_id: 
skill_self: f
ai_path_name: 
ai_path_type_id: 0
sound_id: 
npc_spawner_id: 0
play_cinema_before_bubble: t
ai_command_set_id: 
or_unit_reqs: f
cinema_id: 
summary_voice_id: 
hide_quest_marker: f
buff_id: 
```

### `zones`
**스키마**
- `id` (INTEGER) PK
- `name` (varchar(255))
- `zone_key` (INTEGER)
- `group_id` (INTEGER)
- `closed` (boolean)
- `display_text` (varchar(255))
- `faction_id` (INTEGER)
- `zone_climate_id` (INTEGER)
- `abox_show` (boolean)
- `integration` (boolean)
- `closed_to_foreigner` (boolean)

**샘플 (최대 3행)**
```
id: 1
name: w_gweonid_forest_1
zone_key: 129
group_id: 1
closed: f
display_text: 그위오니드 숲
faction_id: 148
zone_climate_id: 2
abox_show: f
integration: f
closed_to_foreigner: t
```
```
id: 2
name: w_marianople_1
zone_key: 133
group_id: 2
closed: f
display_text: 마리아노플
faction_id: 148
zone_climate_id: 2
abox_show: f
integration: f
closed_to_foreigner: t
```
```
id: 3
name: e_steppe_belt_1
zone_key: 136
group_id: 14
closed: f
display_text: 초원의 띠
faction_id: 
zone_climate_id: 5
abox_show: f
integration: f
closed_to_foreigner: t
```

### `zone_groups`
**스키마**
- `id` (INTEGER) PK
- `name` (varchar(255))
- `x` (float)
- `y` (float)
- `w` (float)
- `h` (float)
- `sound_id` (INTEGER)
- `target_id` (INTEGER)
- `display_text` (varchar(255))
- `faction_chat_region_id` (INTEGER)
- `sound_pack_id` (INTEGER)
- `pirate_desperado` (boolean)
- `fishing_sea_loot_pack_id` (INTEGER)
- `fishing_land_loot_pack_id` (INTEGER)
- `buff_id` (INTEGER)
- `enable_physics_collision_damage` (boolean)
- `faction_id` (INTEGER)
- `ocean_simulate` (boolean)
- `dp_lv_min` (INTEGER)
- `dp_lv_max` (INTEGER)
- `hide_world_pos` (boolean)
- `enable_special_resurrection_district` (boolean)

**샘플 (최대 3행)**
```
id: 1
name: w_gweonid_forest
x: 8888
y: 14196
w: 3984
h: 2387
sound_id: 1
target_id: 3
display_text: 그위오니드 숲
faction_chat_region_id: 2
sound_pack_id: 101
pirate_desperado: f
fishing_sea_loot_pack_id: 
fishing_land_loot_pack_id: 11947
buff_id: 
enable_physics_collision_damage: f
faction_id: 148
ocean_simulate: f
dp_lv_min: 1
dp_lv_max: 10
hide_world_pos: f
enable_special_resurrection_district: f
```
```
id: 2
name: w_marianople
x: 9677.473684
y: 10910.315789
w: 3174.175439
h: 1904.280702
sound_id: 1
target_id: 3
display_text: 마리아노플
faction_chat_region_id: 2
sound_pack_id: 102
pirate_desperado: f
fishing_sea_loot_pack_id: 11946
fishing_land_loot_pack_id: 11947
buff_id: 
enable_physics_collision_damage: f
faction_id: 148
ocean_simulate: f
dp_lv_min: 24
dp_lv_max: 26
hide_world_pos: f
enable_special_resurrection_district: f
```
```
id: 3
name: w_garangdol_plains
x: 9934.628571
y: 12160
w: 5185.828571
h: 3112.228571
sound_id: 1
target_id: 3
display_text: 가랑돌 평원
faction_chat_region_id: 2
sound_pack_id: 103
pirate_desperado: f
fishing_sea_loot_pack_id: 11946
fishing_land_loot_pack_id: 11947
buff_id: 
enable_physics_collision_damage: f
faction_id: 148
ocean_simulate: f
dp_lv_min: 15
dp_lv_max: 19
hide_world_pos: f
enable_special_resurrection_district: f
```

### `characters`
**스키마**
- `id` (INTEGER) PK
- `char_race_id` (INTEGER)
- `char_gender_id` (INTEGER)
- `model_id` (INTEGER)
- `faction_id` (INTEGER)
- `starting_zone_id` (INTEGER)
- `preview_cloth_pack_id` (INTEGER)
- `default_return_district_id` (INTEGER)
- `default_resurrection_district_id` (INTEGER)
- `default_system_voice_sound_pack_id` (INTEGER)
- `default_fx_voice_sound_pack_id` (INTEGER)
- `default_custom_id` (INTEGER)
- `face_item_id` (INTEGER)
- `milestone_id` (INTEGER)
- `creatable` (boolean)

**샘플 (최대 3행)**
```
id: 1
char_race_id: 1
char_gender_id: 1
model_id: 10
faction_id: 101
starting_zone_id: 179
preview_cloth_pack_id: 183
default_return_district_id: 342
default_resurrection_district_id: 343
default_system_voice_sound_pack_id: 86
default_fx_voice_sound_pack_id: 
default_custom_id: 301
face_item_id: 19838
milestone_id: 5
creatable: t
```
```
id: 2
char_race_id: 1
char_gender_id: 2
model_id: 11
faction_id: 101
starting_zone_id: 179
preview_cloth_pack_id: 183
default_return_district_id: 342
default_resurrection_district_id: 343
default_system_voice_sound_pack_id: 87
default_fx_voice_sound_pack_id: 
default_custom_id: 328
face_item_id: 19839
milestone_id: 5
creatable: t
```
```
id: 3
char_race_id: 3
char_gender_id: 1
model_id: 14
faction_id: 104
starting_zone_id: 328
preview_cloth_pack_id: 6
default_return_district_id: 191
default_resurrection_district_id: 190
default_system_voice_sound_pack_id: 88
default_fx_voice_sound_pack_id: 
default_custom_id: 1264
face_item_id: 401
milestone_id: 5
creatable: t
```

### 참고: Gweonid guard posture

`npc_postures` where `anim_action_id=109`:
```
id: 78
npc_posture_set_id: 75
anim_action_id: 109
talk_anim: fist_pos_soldier_attention_talk
start_tod_time: 0
```
`npc_posture_sets` where `id=75`:
```
id: 75
name: stn_soldier_elf_all
quest_anim_action_id: 0
comment: 엘프 경비병, 감시병
```

---

## 12. 퀘스트 2396/2401 (Doodad 퀘스트를 NPC로 우회 처리) — 성공 케이스

### 문제
- 2396(`첫 번째 시합`)과 2401(`두 번째 시합을 위해`)은 원래 Doodad 14178에서 수락/보고해야 함.
- 해당 Doodad가 `doodad_spawns`에 없어서 클라이언트에 `!`/`?` 마커가 뜨지 않고, 퀘스트를 받거나 완료할 수 없음.
- 목표: 에오카드 델토킨(NPC 7817) 머리위에 `!`/`?` 마커가 뜨고, 대화창으로 정상 수락/완료.

### 핵심 원인
- `quest_components.npc_id`만 바꾸는 것으로는 클라이언트 마커가 안 뜸.
- 클라이언트가 마커 위치를 결정하는 기준은 **`quest_acts.act_detail_type` + `act_detail_id`**.
- 2396/2401 Start/Ready가 `QuestActConAcceptDoodad` / `QuestActConReportDoodad`로 되어 있어, 클라이언트는 Doodad 기준으로 마커를 찍음.

### 해결 방법
1. **이미 존재하는 NPC act row 재사용**
   - `quest_act_con_accept_npcs`에서 `npc_id=7817`인 row 찾기 → 여기서는 `id=818` (2388 시작에도 사용).
   - `quest_act_con_report_npcs`에서 `npc_id=7817`인 row 찾기 → 여기서는 `id=183` (2388 보고에도 사용).

2. **`quest_acts` 변경 (2396/2401)**
   ```sql
   UPDATE quest_acts SET act_detail_type='QuestActConAcceptNpc', act_detail_id=818 WHERE id=64214; -- 2396 Start
   UPDATE quest_acts SET act_detail_type='QuestActConReportNpc',  act_detail_id=183 WHERE id=64215; -- 2396 Ready
   UPDATE quest_acts SET act_detail_type='QuestActConAcceptNpc', act_detail_id=818 WHERE id=64216; -- 2401 Start
   UPDATE quest_acts SET act_detail_type='QuestActConReportNpc',  act_detail_id=183 WHERE id=14520; -- 2401 Ready
   ```

3. **`quest_components.npc_id` 설정**
   ```sql
   UPDATE quest_components
   SET npc_id = 7817
   WHERE quest_context_id IN (2396,2401)
     AND component_kind_id IN (2,6); -- Start, Ready
   ```

4. **DB 동기화**
   - 클라이언트 원본: `E:\games\archeage-10.0.2.13r575-cn\game\db\compact.sqlite3`
   - 서버 원본: `E:\games\archeage-10.0.2.13r575-cn\game\db\game_decrypted.sqlite3` → `AAEmu.Game\bin\Debug\net10.0\Data\compact.sqlite3` 및 `AAEmu.WorldServer\AAEmu.World\bin\Debug\net10.0\Data\compact.sqlite3`로 복사
   - `game_pak-`에 `game/db/compact.sqlite3` 패치 (`PatchPak` 사용)

5. **코드 원복**
   - `CSStartQuestContextPacket`은 일반 `AddQuestFromNpc` / `AddQuestFromDoodad` 흐름 사용.
   - `CSCompleteQuestContextPacket`은 `DoReportEvents` 호출 후 `quest.Step == Ready`면 `RunCurrentStep()` 두 번(Ready→Reward→Complete) 호출.
   - `CSInteractNPCPacket`에서 자동 수락/보고 로직 제거.

6. **빌드 후 서버 재시작**
   - `dotnet build AAEmu.WorldServer\AAEmu.World\AAEmu.World.csproj -c Debug`
   - **(주의) 서버 종료/재시작은 사용자 허락 후 진행할 것.**
   - 클라이언트도 `game_pak-`이 변경되었으므로 껐다 켜야 새 DB가 로드됨.

### 검증
- `game_pak-`에서 `game/db/compact.sqlite3`을 추출해 다음을 확인:
   ```sql
   SELECT qc.quest_context_id, qc.component_kind_id, qc.npc_id,
          qa.act_detail_type, qa.act_detail_id
   FROM quest_components qc
   JOIN quest_acts qa ON qa.quest_component_id = qc.id
   WHERE qc.quest_context_id IN (2396,2401)
     AND qc.component_kind_id IN (2,6);
   ```
- 결과가 `QuestActConAcceptNpc`/818, `QuestActConReportNpc`/183, `npc_id=7817`이면 정상.

### 결과
- 델토킨(NPC 7817) 머리위에 `!`/`?` 마커가 표시되고, 대화창에서 정상 수락/완료 가능.
- 2396/2401을 비롯한 연계 퀘스트가 NPC 7817 기준으로 진행됨.

---

## 13. 퀘스트 213 (`npctype://` Doodad 마우스 타겟 문제를 NPC 582 스폰으로 우회 처리)

### 문제
- 213(`의원회 조사`)의 최종 보고 대상은 Doodad 14177(의원 벨리온, `model=npctype://582`)임.
- Doodad는 마우스로 잠깐 타겟되지만 `select nothing` 로그와 함께 0.1초 만에 풀림. 키보드 `F` 상호작용은 가능하지만 마우스 타겟이 불안정.
- `npctype://` 모델을 사용하는 Doodad는 클라이언트가 이를 NPC로 인식했다가 서버의 `SCDoodadCreatedPacket`을 받아 다시 풀리는 현상이 원인.

### 목표
- Doodad 14177 대신 실제 NPC 582(의원 벨리온)가 해당 위치에 스폰되고, 마우스 타겟 및 대화가 정상 동작.

### 핵심 원인
- `quest_acts` 213 Ready가 `QuestActConReportDoodad`로 되어 있어 클라이언트/서버 모두 Doodad 기준으로 동작.
- NPC 582가 `npc_spawners`에는 등록되어 있으나 `npc_spawners.g`에 실제 위치(placement)가 없어 월드에 스폰되지 않음.
- Doodad 14177은 `doodad_spawns`에도 없었기에 사용자가 `/doodad spawn`으로 임시 스폰하고 `nwrite` 저장해야만 보였음.

### 해결 방법
1. **`quest_act_con_report_npcs` row 추가**
   ```sql
   INSERT OR REPLACE INTO quest_act_con_report_npcs (id, npc_id, use_alias, quest_act_obj_alias_id)
   VALUES (9000, 582, 't', 6597);
   ```

2. **`quest_acts` 변경 (213 Ready)**
   ```sql
   UPDATE quest_acts
   SET act_detail_type='QuestActConReportNpc', act_detail_id=9000
   WHERE id=64217;
   ```

3. **`quest_components.npc_id` 설정 (213 Ready)**
   ```sql
   UPDATE quest_components
   SET npc_id=582
   WHERE id=312;
   ```

4. **NPC 582 스폰 추가 (`zone 182`, 호숫가 마을)**
   - `/loc` World: `X 10777.2, Y 15318.9, Z 226.5`
   - Zone 182 origin cell: `(10, 14)`, zone-local: `(x 537.2, y 982.9, z 226.5)`
   - `worlds/main_world/level_design/zone/182/zone_server/npc_spawners.g` 맨 끝에 추가:
     ```text
     spawner
         spawnerId 900000
         spawnAreaType point
         spawnerType 561
         points
             point
                 pos ( x 537.2, y 982.9, z 226.5 )
                 zRot 0
     ```
   - `spawnerType 561`은 `npc_spawners.id=561`을 가리키며, 이 row는 `npc_spawner_npcs.member_id=582`(NPC 벨리온)로 연결되어 있음.

5. **Doodad 임시 스폰 제거**
   - `/doodad spawn 14177`로 `nwrite` 저장했던 항목을 `AAEmu.Game\bin\Debug\net10.0\Data\Worlds\main_world\doodad_spawns.json`에서 제거.

6. **DB 동기화 및 `game_pak-` 패치**
   - 클라이언트/서버 DB: `E:\games\archeage-10.0.2.13r575-cn\game\db\{compact,game_decrypted}.sqlite3`
   - 서버 런타임: `AAEmu.Game\bin\Debug\net10.0\Data\compact.sqlite3`
   - `game_pak-`에 `game/db/compact.sqlite3` + `worlds/main_world/level_design/zone/182/zone_server/npc_spawners.g` 패치.

7. **빌드 후 서버 재시작**
   ```powershell
   dotnet build AAEmu.WorldServer\AAEmu.World\AAEmu.World.csproj -c Debug
   # 서버 종료 후
   dotnet run --project AAEmu.WorldServer\AAEmu.World\AAEmu.World.csproj -c Debug --no-build
   ```
   - 클라이언트도 `game_pak-`이 변경되었으므로 재시작 필요.

### 검증
- `game_pak-`에서 `game/db/compact.sqlite3`을 추출해 확인:
  ```sql
  SELECT qc.quest_context_id, qc.component_kind_id, qc.npc_id,
         qa.act_detail_type, qa.act_detail_id
  FROM quest_components qc
  JOIN quest_acts qa ON qa.quest_component_id = qc.id
  WHERE qc.quest_context_id = 213
    AND qc.component_kind_id = 6;
  ```
- 결과가 `npc_id=582`, `QuestActConReportNpc`/9000이면 정상.
- 서버 기동 후 zone 182에 NPC 582가 스폰되고 마우스 타겟/대화가 정상.

### 결과
- 의원 벨리온(NPC 582)이 지정 위치에 스폰되며, 마우스 타겟 및 퀘스트 보고가 정상 동작.
- 213번 퀘스트를 NPC 582 기준으로 진행.
- `npctype://` Doodad 마우스 타겟 문제는 실제 NPC로 우회하는 것이 가장 확실한 해법.

### 후속: 2391/2392 Start/Report도 NPC 582로 우회
- 213 완료 후 바로 이어지는 2391, 2392도 `QuestActConAcceptDoodad(14177)` / `QuestActConReportDoodad(14177)`로 되어 있어 NPC 582에서 `!` 마커가 안 뜸.
- 추가 SQL:
  ```sql
  INSERT OR REPLACE INTO quest_act_con_accept_npcs (id, npc_id, use_alias, quest_act_obj_alias_id) VALUES (9001, 582, 'f', 0);
  INSERT OR REPLACE INTO quest_act_con_accept_npcs (id, npc_id, use_alias, quest_act_obj_alias_id) VALUES (9002, 582, 'f', 0);
  INSERT OR REPLACE INTO quest_act_con_report_npcs (id, npc_id, use_alias, quest_act_obj_alias_id) VALUES (9003, 582, 't', 6657);

  UPDATE quest_acts SET act_detail_type='QuestActConAcceptNpc', act_detail_id=9001 WHERE id=64218; -- 2391 Start
  UPDATE quest_acts SET act_detail_type='QuestActConReportNpc', act_detail_id=9003 WHERE id=64219; -- 2391 Ready
  UPDATE quest_acts SET act_detail_type='QuestActConAcceptNpc', act_detail_id=9002 WHERE id=64220; -- 2392 Start

  UPDATE quest_components SET npc_id=582 WHERE id IN (10277, 10281); -- 2391/2392 Start marker
  ```

### 같이 복원한 것: Gweonid 경비병 애니메이션 (guard posture)
- `WorldIntegration.MirrorZoneNpcSpawn`에서 `npc.AnimActionId = 0;` 추가가 누락(원복)되어 경비병이 걷지 못하고 자세 109에 고정된 상태였음.
- `AAEmu.Game/WorldIntegration.cs` `npc.IsZoneMirror = true;` 직후에 `npc.AnimActionId = 0;` 삽입.

### 2391 퀘스트 아이템 사용 디버깅
- 2391 Progress component (id=20196)는 `QuestActObjItemUse`로 item 24976 (`진실을 보는 눈`) 사용.
- 사용 스킬 14584의 `unit_reqs`에 `AreaSphere=1709` 조건이 있어, 반경 5m 안에서만 사용 가능.
- Sphere 1709의 zone-local 중심: `(544.039, 985.728, 226.272)`.
- quest_sign_sphere.g에도 `qtype 2391 / ctype 20196 / radius 5`로 동일 위치가 등록되어 있음.
- 최초 스폰 추가 시 NPC 582를 `(537.2, 982.9, 226.5)`에 둬서 구 중심에서 약 7.7m 벗어나 있었고, 이 때문에 아이템 사용이 불가능했음.
- 해결: `zone/182/zone_server/npc_spawners.g` spawnerId 900000 의 좌표를 `(544.039, 985.728, 226.272)`로 수정 후 game_pak- 패치.
