/*
 Navicat Premium Data Transfer

 Source Server         : AA1200server.table
 Source Server Type    : SQLite
 Source Server Version : 3030001
 Source Schema         : main

 Target Server Type    : SQLite
 Target Server Version : 3030001
 File Encoding         : 65001

 Date: 15/01/2026 23:22:16
*/

PRAGMA foreign_keys = false;

-- ----------------------------
-- Table structure for npc_groups
-- ----------------------------
DROP TABLE IF EXISTS "npc_groups";
CREATE TABLE "npc_groups" (
  "id" INT,
  "name" TEXT,
  "aggro_rule_id" INT,
  "enable_respawn" NUM
);

-- ----------------------------
-- Records of npc_groups
-- ----------------------------
INSERT INTO "npc_groups" VALUES (2, '사나운페피', 1, 'f');
INSERT INTO "npc_groups" VALUES (3, '용맹한 사냥꾼들', 2, 'f');
INSERT INTO "npc_groups" VALUES (6, '솔즈리드 우두머리 늑대', 1, 'f');
INSERT INTO "npc_groups" VALUES (8, '그위오니드_나무거인과 졸개', 2, 'f');
INSERT INTO "npc_groups" VALUES (9, '글라도쉬', 1, 't');
INSERT INTO "npc_groups" VALUES (10, '무지개벌판_가젤떼', 0, 'f');
INSERT INTO "npc_groups" VALUES (11, '무지개벌판_가젤떼3마리', 0, 'f');
INSERT INTO "npc_groups" VALUES (12, '매사냥-우두머리들소그룹', 2, 'f');
INSERT INTO "npc_groups" VALUES (13, '호랑이등뼈산맥_총독과비서', 2, 'f');
INSERT INTO "npc_groups" VALUES (14, '무지개벌판_일개미', 1, 'f');
INSERT INTO "npc_groups" VALUES (15, '벌떼', 0, 'f');
INSERT INTO "npc_groups" VALUES (16, '두왕관_흙먼지구릉_바다_해적단', 2, 'f');
INSERT INTO "npc_groups" VALUES (18, '그위오니드_여우그룹', 1, 'f');
INSERT INTO "npc_groups" VALUES (19, '무지개벌판_가젤', 0, 'f');
INSERT INTO "npc_groups" VALUES (20, '무지개벌판_야생마', 0, 'f');
INSERT INTO "npc_groups" VALUES (21, '무지개벌판_바이암습격대', 0, 'f');
INSERT INTO "npc_groups" VALUES (22, '무지개벌판_젊은피상단', 2, 'f');
INSERT INTO "npc_groups" VALUES (23, '두왕관_검붉은눈물_노예관리', 0, 't');
INSERT INTO "npc_groups" VALUES (24, '두왕관_악마전쟁의흉터_아모텍monster', 1, 'f');
INSERT INTO "npc_groups" VALUES (25, '그위오니드 바람총페피', 2, 'f');
INSERT INTO "npc_groups" VALUES (26, '루시와짐말', 2, 'f');
INSERT INTO "npc_groups" VALUES (27, '이니스테르 - 붉은 코트의 스티나와 납치범 오이스텐', 0, 'f');
INSERT INTO "npc_groups" VALUES (28, '노래의땅_뿌리마법사링크안됨', 1, 'f');
INSERT INTO "npc_groups" VALUES (29, '노래의땅_바이암필리리', 1, 'f');
INSERT INTO "npc_groups" VALUES (30, '노래의땅_뿌리마법사와자식', 1, 'f');
INSERT INTO "npc_groups" VALUES (31, '두왕관_흙먼지구릉_협곡_용병npc', 1, 'f');
INSERT INTO "npc_groups" VALUES (32, '저거너트노예감독', 1, 't');
INSERT INTO "npc_groups" VALUES (34, '길을잃고 방황하는자', 0, 'f');
INSERT INTO "npc_groups" VALUES (36, '무지개벌판_황야의카론과졸개', 1, 'f');
INSERT INTO "npc_groups" VALUES (38, '경비병 비나타와 그의 개', 1, 'f');
INSERT INTO "npc_groups" VALUES (40, '두왕관_오색들판_검투사경기장_사자monster', 2, 'f');
INSERT INTO "npc_groups" VALUES (41, '페레 경비병 세트', 2, 'f');
INSERT INTO "npc_groups" VALUES (42, '누이안 경비병 세트', 1, 'f');
INSERT INTO "npc_groups" VALUES (43, '하리하란 경비병 세트', 1, 'f');
INSERT INTO "npc_groups" VALUES (44, '바이암 무리', 1, 'f');
INSERT INTO "npc_groups" VALUES (45, '그위오니드 무서운 언니단', 2, 't');
INSERT INTO "npc_groups" VALUES (47, '중립 경비병 세트 ', 2, 'f');
INSERT INTO "npc_groups" VALUES (48, '눈사자 무리', 1, 'f');
INSERT INTO "npc_groups" VALUES (49, '청동바위산 : 순록떼와 육식동물', 0, 'f');
INSERT INTO "npc_groups" VALUES (50, '고요한 바다 : 어미 바다벌레와 새끼 바다벌레', 1, 'f');
INSERT INTO "npc_groups" VALUES (52, '고대의 숲 흑마법사 감시자와 사냥개', 1, 'f');
INSERT INTO "npc_groups" VALUES (53, '훈련교관 피사', 1, 't');
INSERT INTO "npc_groups" VALUES (54, '피 묻은 손 수송대원', 1, 'f');
INSERT INTO "npc_groups" VALUES (55, '철갑망령 지배자', 1, 'f');
INSERT INTO "npc_groups" VALUES (56, '이니스테르 : 순찰중인 장교와 경비병들', 1, 'f');
INSERT INTO "npc_groups" VALUES (57, '황금평원 - 뛰는들소무리', 0, 'f');
INSERT INTO "npc_groups" VALUES (58, '황금평원 - 곰가족', 1, 'f');
INSERT INTO "npc_groups" VALUES (60, '불탄성 무기고 로머A그룹', 1, 'f');
INSERT INTO "npc_groups" VALUES (61, '불탄성 무기고 로머B그룹', 1, 'f');
INSERT INTO "npc_groups" VALUES (62, '로드러너', 0, 't');
INSERT INTO "npc_groups" VALUES (63, '황금평원-켄타추적자', 2, 'f');
INSERT INTO "npc_groups" VALUES (64, '가랑돌 평원 비명 협곡 : 해골 마부와 해골 말', 1, 'f');
INSERT INTO "npc_groups" VALUES (65, '가랑돌 평원 은둔자의 절벽 : 검은 수염 도적단 로밍 파티', 1, 'f');
INSERT INTO "npc_groups" VALUES (66, '가랑돌 평원_들소몰이 벌판_들소떼', 0, 'f');
INSERT INTO "npc_groups" VALUES (68, '긴모래톱_인어눈물 해안_칼레일 사교도 로밍파티', 1, 'f');
INSERT INTO "npc_groups" VALUES (69, '그위오니드_바람폐허_바람정령', 1, 'f');
INSERT INTO "npc_groups" VALUES (70, '지옥늪지대_오크주술사와오우거', 1, 't');
INSERT INTO "npc_groups" VALUES (71, '지옥늪지대_오크우두머리', 1, 't');
INSERT INTO "npc_groups" VALUES (72, '로카의 장기말들_암흑갈기 사냥꾼 늑대조합', 1, 't');
INSERT INTO "npc_groups" VALUES (73, '어둠과 유령', 1, 'f');
INSERT INTO "npc_groups" VALUES (74, '긴 모래톱_별 모래톱_해안가의 연인_나잡아봐라', 0, 'f');
INSERT INTO "npc_groups" VALUES (75, '긴 모래톱_별 모래톱_해안가의 연인_다정히걷기', 0, 'f');
INSERT INTO "npc_groups" VALUES (76, '로카의 장기말들_고행의 길 수행자', 1, 'f');
INSERT INTO "npc_groups" VALUES (77, '도망가는  얼룩말', 1, 'f');
INSERT INTO "npc_groups" VALUES (79, '살피라 조련사', 1, 'f');
INSERT INTO "npc_groups" VALUES (80, '지옥늪지대_먹깨비와구더기', 1, 't');
INSERT INTO "npc_groups" VALUES (81, '지옥늪지대_비홀더와구더기', 1, 't');
INSERT INTO "npc_groups" VALUES (82, '지옥늪지대_익사체와구더기', 1, 't');
INSERT INTO "npc_groups" VALUES (83, '지옥늪지대_재버워키와구더기', 1, 't');
INSERT INTO "npc_groups" VALUES (85, '페레 중립 경비병 세트', 2, 't');
INSERT INTO "npc_groups" VALUES (86, '긴 모래톱_녹색 늑대의 숲_거만한 밀렵꾼과 표범', 1, 'f');
INSERT INTO "npc_groups" VALUES (87, '공학자와 골렘', 2, 'f');
INSERT INTO "npc_groups" VALUES (89, '가랑돌평원_들쥐떼', 0, 'f');
INSERT INTO "npc_groups" VALUES (91, '노래의 땅_남자 신도와 혼', 0, 'f');
INSERT INTO "npc_groups" VALUES (92, '검은 상아 밀렵단 덫 사냥꾼+추적꾼', 2, 'f');
INSERT INTO "npc_groups" VALUES (93, '검은상아 밀렵단 파수꾼 +추적꾼', 2, 'f');
INSERT INTO "npc_groups" VALUES (94, '고대의 숲 지하_얼굴없는', 2, 'f');
INSERT INTO "npc_groups" VALUES (95, '마하데비 메인퀘', 1, 'f');
INSERT INTO "npc_groups" VALUES (98, '승천하는 가루다', 2, 'f');
INSERT INTO "npc_groups" VALUES (99, '하얀 숲 / 키사라와 그리핀', 1, 'f');
INSERT INTO "npc_groups" VALUES (101, '바라기마을_리포포와린델', 0, 'f');
INSERT INTO "npc_groups" VALUES (105, '매사냥고원_시치미와잿빛의대결_시치미부대', 1, 'f');
INSERT INTO "npc_groups" VALUES (107, '울부짖는 구렁텅이 그룹1', 1, 'f');
INSERT INTO "npc_groups" VALUES (108, '한 방 상단', 0, 'f');
INSERT INTO "npc_groups" VALUES (109, '매사냥고원_동굴박쥐떼', 1, 'f');
INSERT INTO "npc_groups" VALUES (110, '매사냥고원_지배자와피지배자', 1, 'f');
INSERT INTO "npc_groups" VALUES (111, '솔즈리드 경작지의 피 묻은 손 정찰대', 0, 'f');
INSERT INTO "npc_groups" VALUES (112, '황금평원_알버트와똘마니', 1, 'f');
INSERT INTO "npc_groups" VALUES (113, '매사냥고원_옥시언을습격하는시치미', 1, 'f');
INSERT INTO "npc_groups" VALUES (114, '황금평원_덴슬로와똘마니', 1, 'f');
INSERT INTO "npc_groups" VALUES (115, '황금평원_황금의잔경계병과광신도', 1, 'f');
INSERT INTO "npc_groups" VALUES (116, '황금평원_검은늑대기사단원', 1, 'f');
INSERT INTO "npc_groups" VALUES (117, '황금평원_검은늑대로머', 1, 'f');
INSERT INTO "npc_groups" VALUES (118, '그위오니드_바람폐허_바람정령', 1, 't');
INSERT INTO "npc_groups" VALUES (120, '솔즈리드_선원', 2, 't');
INSERT INTO "npc_groups" VALUES (121, '솔즈리드_요정', 2, 't');
INSERT INTO "npc_groups" VALUES (123, '솔즈리드 요정대장', 2, 't');
INSERT INTO "npc_groups" VALUES (124, '솔즈리드_밴시', 2, 't');
INSERT INTO "npc_groups" VALUES (125, '솔즈리드_선장', 2, 't');
INSERT INTO "npc_groups" VALUES (126, '매사냥고원_사슴떼', 0, 't');
INSERT INTO "npc_groups" VALUES (127, '3_지배당한 페레', 1, 't');
INSERT INTO "npc_groups" VALUES (128, '4_지배당한 페레', 1, 't');
INSERT INTO "npc_groups" VALUES (129, '작업반장과 노예', 2, 't');
INSERT INTO "npc_groups" VALUES (130, '천둥소리요새_미노타우르스광전사와검은눈용아병', 1, 't');
INSERT INTO "npc_groups" VALUES (131, '피묻은손 수송대원', 1, 'f');
INSERT INTO "npc_groups" VALUES (132, '물고기떼', 1, 't');
INSERT INTO "npc_groups" VALUES (133, '초원의 띠 - 사나운  얼룩말떼', 1, 't');
INSERT INTO "npc_groups" VALUES (134, '초원의 띠 - 검은 상아 밀렵단 돌격대/호위대', 1, 't');
INSERT INTO "npc_groups" VALUES (135, '초원의 띠 - 무지개 코끼리와 황금바다 밀렵단', 1, 'f');
INSERT INTO "npc_groups" VALUES (136, '초원의 띠 - 칸과 그의 코끼리떼', 1, 't');
INSERT INTO "npc_groups" VALUES (137, '로카-분노한 거인 로밍', 1, 'f');
INSERT INTO "npc_groups" VALUES (138, '바다 : 32 먼바다 장어, 34 먼바다 천둥장어', 1, 't');
INSERT INTO "npc_groups" VALUES (139, '바다 : 36 새끼 해파리 무리', 1, 'f');
INSERT INTO "npc_groups" VALUES (140, '태양의 들녘 : 슬라임 무리', 0, 't');
INSERT INTO "npc_groups" VALUES (141, '태양의 들녘 : 만드라고라 로밍 파티', 0, 't');
INSERT INTO "npc_groups" VALUES (142, '유령군대 일반', 1, 't');
INSERT INTO "npc_groups" VALUES (143, '상단 호위 이벤트 - 마하데비 상단', 0, 'f');
INSERT INTO "npc_groups" VALUES (144, '영원의 섬 - 고대인의 망령 로밍 파티', 1, 't');
INSERT INTO "npc_groups" VALUES (145, '영원의 섬 - 다후타의 권속 로밍 파티', 1, 't');
INSERT INTO "npc_groups" VALUES (146, '영원의 섬 - 니모를 찾아서 패러디 파티', 1, 't');
INSERT INTO "npc_groups" VALUES (148, '나차쉬가르_바르키(일반)', 2, 'f');
INSERT INTO "npc_groups" VALUES (150, '매사냥_여왕벌', 1, 'f');
INSERT INTO "npc_groups" VALUES (152, '광복절 순례객 NPC 서부', 1, 't');
INSERT INTO "npc_groups" VALUES (153, '광복절 순례객 NPC 동부', 1, 't');
INSERT INTO "npc_groups" VALUES (154, '광복절 순례객 NPC 신기루 섬', 1, 't');
INSERT INTO "npc_groups" VALUES (156, '갈대무리땅_적진으로 뛰어드는 대원', 1, 't');
INSERT INTO "npc_groups" VALUES (157, '잊혔던 열람실 : 해골 정찰병과 약탈자 ', 1, 'f');
INSERT INTO "npc_groups" VALUES (158, '잊혔던 열람실 : 수집기계, 해골 정찰병', 1, 'f');
INSERT INTO "npc_groups" VALUES (159, '잊혔던 열람실 : 불타는 서고 정령과 책 정령', 1, 'f');
INSERT INTO "npc_groups" VALUES (160, '잊혔던 열람실 : 해골 정찰병과 탐색자', 1, 'f');
INSERT INTO "npc_groups" VALUES (161, '잊혔던 열람실 : 임프와 불타는 책 정령', 1, 'f');
INSERT INTO "npc_groups" VALUES (162, '잊혔던 열람실 : 서고 관찰자와 산탄화살 미믹', 1, 'f');
INSERT INTO "npc_groups" VALUES (163, '잊혔던 열람실 : 서고 임프와 서큐버스', 1, 'f');
INSERT INTO "npc_groups" VALUES (164, '에아나드 도서관 3층 염소인간 족장 쫓는 양피지 병사', 0, 'f');
INSERT INTO "npc_groups" VALUES (166, '추석 아이들 - 서대륙', 0, 'f');
INSERT INTO "npc_groups" VALUES (167, '추석 아이들 - 동대륙', 0, 'f');
INSERT INTO "npc_groups" VALUES (168, '추석 아이들 - 해적섬', 0, 'f');
INSERT INTO "npc_groups" VALUES (169, '잊혔던 열람실 : 사서의 망령과 등불 정령', 1, 'f');
INSERT INTO "npc_groups" VALUES (170, '잊혔던 열람실 : 수호정령과 순찰정령', 1, 'f');
INSERT INTO "npc_groups" VALUES (171, '잊혔던 열람실 : 순찰정령과 등불정령', 1, 'f');
INSERT INTO "npc_groups" VALUES (172, '잊혔던 열람실 : 사서의 망령과 도제의 망령', 1, 'f');
INSERT INTO "npc_groups" VALUES (173, '에아나드 도서관 3층 부하 쫓는 염소인간 족장', 0, 't');
INSERT INTO "npc_groups" VALUES (175, '에아나드 도서관 3층 양피지 병사 로밍파티', 1, 't');
INSERT INTO "npc_groups" VALUES (176, '에아나드 도서관 3층 양피지 병사 쫓는 염소인간', 0, 't');
INSERT INTO "npc_groups" VALUES (177, '망각의 균열 1차', 1, 'f');
INSERT INTO "npc_groups" VALUES (178, '망각의 균열 2차1', 1, 'f');
INSERT INTO "npc_groups" VALUES (179, '망각의 균열 2차2', 1, 'f');
INSERT INTO "npc_groups" VALUES (180, '망각의 균열 2차3', 1, 'f');
INSERT INTO "npc_groups" VALUES (181, '망각의 균열 3차', 1, 'f');
INSERT INTO "npc_groups" VALUES (182, '기계의 소란 1차', 1, 'f');
INSERT INTO "npc_groups" VALUES (183, '기계의 소란 2차1', 1, 'f');
INSERT INTO "npc_groups" VALUES (184, '기계의 소란 2차2', 1, 'f');
INSERT INTO "npc_groups" VALUES (185, '기계의 소란 2차3', 1, 'f');
INSERT INTO "npc_groups" VALUES (186, '기계의 소란 3차', 1, 'f');
INSERT INTO "npc_groups" VALUES (187, '우정사절단 세트', 1, 't');
INSERT INTO "npc_groups" VALUES (190, '이슬 평원 : 피투성이 군대 지휘관 부대', 1, 'f');
INSERT INTO "npc_groups" VALUES (191, '이슬 평원 : 가려진 달의 지휘관 그룹', 1, 'f');
INSERT INTO "npc_groups" VALUES (192, '이슬 평원 : 가려진 달의 점령병 그룹', 0, 'f');

PRAGMA foreign_keys = true;
