/*
 Navicat Premium Data Transfer

 Source Server         : AAClientDB
 Source Server Type    : SQLite
 Source Server Version : 3021000
 Source Schema         : main

 Target Server Type    : SQLite
 Target Server Version : 3021000
 File Encoding         : 65001

 Date: 18/04/2019 14:05:45
*/

PRAGMA foreign_keys = false;

-- ----------------------------
-- Table structure for loot_groups
-- ----------------------------
DROP TABLE IF EXISTS "loot_groups";
CREATE TABLE "loot_groups" (
  "id" integer NOT NULL,
  "pack_id" integer,
  "group_no" integer,
  "drop_rate" integer,
  "item_grade_distribution_id" integer,
  PRIMARY KEY ("id")
);

-- ----------------------------
-- Table structure for loots
-- ----------------------------
DROP TABLE IF EXISTS "loots";
CREATE TABLE "loots" (
  "id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
  "group" integer NOT NULL DEFAULT 0,
  "item_id" integer NOT NULL,
  "drop_rate" integer NOT NULL DEFAULT 10000000,
  "min_amount" integer NOT NULL DEFAULT 1,
  "max_amount" integer NOT NULL DEFAULT 1,
  "loot_pack_id" integer NOT NULL DEFAULT 0,
  "grade_id" integer NOT NULL DEFAULT 0,
  "always_drop" boolean NOT NULL DEFAULT 'f'
);

PRAGMA foreign_keys = true;
