-- phpMyAdmin SQL Dump
-- version 4.8.3
-- https://www.phpmyadmin.net/
--
-- Хост: 127.0.0.1:3306
-- Время создания: Янв 07 2019 г., 16:35
-- Версия сервера: 8.0.12
-- Версия PHP: 7.2.10

SET SQL_MODE = "NO_AUTO_VALUE_ON_ZERO";
SET AUTOCOMMIT = 0;
START TRANSACTION;
SET time_zone = "+00:00";


/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET @OLD_CHARACTER_SET_RESULTS=@@CHARACTER_SET_RESULTS */;
/*!40101 SET @OLD_COLLATION_CONNECTION=@@COLLATION_CONNECTION */;
/*!40101 SET NAMES utf8mb4 */;

--
-- База данных: `aaemu_game`
--

-- --------------------------------------------------------

--
-- Структура таблицы `abilities`
--

CREATE TABLE `abilities` (
  `id` tinyint(3) UNSIGNED NOT NULL,
  `exp` int(11) NOT NULL,
  `owner` int(11) UNSIGNED NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- --------------------------------------------------------

--
-- Структура таблицы `actabilities`
--

CREATE TABLE `actabilities` (
  `id` int(10) UNSIGNED NOT NULL,
  `point` int(10) UNSIGNED NOT NULL DEFAULT '0',
  `step` tinyint(3) UNSIGNED NOT NULL DEFAULT '0',
  `owner` int(10) UNSIGNED NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- --------------------------------------------------------

--
-- Структура таблицы `appellations`
--

CREATE TABLE `appellations` (
  `id` int(10) UNSIGNED NOT NULL,
  `active` tinyint(1) NOT NULL DEFAULT '0',
  `owner` int(10) UNSIGNED NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- --------------------------------------------------------

--
-- Структура таблицы `characters`
--

CREATE TABLE `characters` (
  `id` int(11) UNSIGNED NOT NULL,
  `account_id` int(11) UNSIGNED NOT NULL,
  `name` varchar(128) NOT NULL,
  `race` tinyint(2) NOT NULL,
  `gender` tinyint(1) NOT NULL,
  `unit_model_params` blob NOT NULL,
  `level` tinyint(4) NOT NULL,
  `expirience` int(11) NOT NULL,
  `recoverable_exp` int(11) NOT NULL,
  `hp` int(11) NOT NULL,
  `mp` int(11) NOT NULL,
  `labor_power` mediumint(9) NOT NULL,
  `labor_power_modified` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `consumed_lp` int(11) NOT NULL,
  `ability1` tinyint(4) NOT NULL,
  `ability2` tinyint(4) NOT NULL,
  `ability3` tinyint(4) NOT NULL,
  `zone_id` int(11) UNSIGNED NOT NULL,
  `x` float NOT NULL,
  `y` float NOT NULL,
  `z` float NOT NULL,
  `rotation_x` tinyint(4) NOT NULL,
  `rotation_y` tinyint(4) NOT NULL,
  `rotation_z` tinyint(4) NOT NULL,
  `faction_id` int(11) UNSIGNED NOT NULL,
  `faction_name` varchar(128) NOT NULL,
  `family` int(11) UNSIGNED NOT NULL,
  `dead_count` mediumint(8) UNSIGNED NOT NULL,
  `dead_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `rez_wait_duration` int(11) NOT NULL,
  `rez_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `rez_penalty_duration` int(11) NOT NULL,
  `leave_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `money` bigint(20) NOT NULL,
  `money2` bigint(20) NOT NULL,
  `crime_point` int(11) NOT NULL,
  `crime_record` int(11) NOT NULL,
  `delete_request_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `transfer_request_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `delete_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `bm_point` int(11) NOT NULL,
  `auto_use_aapoint` tinyint(1) NOT NULL,
  `prev_point` int(11) NOT NULL,
  `point` int(11) NOT NULL,
  `gift` int(11) NOT NULL,
  `num_inv_slot` tinyint(3) UNSIGNED NOT NULL DEFAULT '50',
  `num_bank_slot` smallint(5) UNSIGNED NOT NULL DEFAULT '50',
  `slots` blob NOT NULL,
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` datetime NOT NULL DEFAULT '0001-01-01 00:00:00'
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- --------------------------------------------------------

--
-- Структура таблицы `items`
--

CREATE TABLE `items` (
  `id` bigint(20) UNSIGNED NOT NULL,
  `type` varchar(100) NOT NULL,
  `template_id` int(11) UNSIGNED NOT NULL,
  `slot_type` enum('Equipment','Inventory','Bank') NOT NULL,
  `slot` int(11) NOT NULL,
  `count` int(11) NOT NULL,
  `details` blob,
  `lifespan_mins` int(11) NOT NULL,
  `unsecure_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `unpack_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `owner` int(11) UNSIGNED NOT NULL,
  `created_at` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `grade` tinyint(1) UNSIGNED NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- --------------------------------------------------------

--
-- Структура таблицы `options`
--

CREATE TABLE `options` (
  `key` varchar(100) NOT NULL,
  `value` text NOT NULL,
  `owner` int(11) UNSIGNED NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- --------------------------------------------------------

--
-- Структура таблицы `skills`
--

CREATE TABLE `skills` (
  `id` int(11) UNSIGNED NOT NULL,
  `level` tinyint(4) NOT NULL,
  `type` enum('Skill','Buff') NOT NULL,
  `owner` int(11) UNSIGNED NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

--
-- Индексы сохранённых таблиц
--

--
-- Индексы таблицы `abilities`
--
ALTER TABLE `abilities`
  ADD PRIMARY KEY (`id`,`owner`);

--
-- Индексы таблицы `actabilities`
--
ALTER TABLE `actabilities`
  ADD PRIMARY KEY (`owner`,`id`);

--
-- Индексы таблицы `appellations`
--
ALTER TABLE `appellations`
  ADD PRIMARY KEY (`id`,`owner`);

--
-- Индексы таблицы `characters`
--
ALTER TABLE `characters`
  ADD PRIMARY KEY (`id`,`account_id`);

--
-- Индексы таблицы `items`
--
ALTER TABLE `items`
  ADD PRIMARY KEY (`id`),
  ADD KEY `owner` (`owner`);

--
-- Индексы таблицы `options`
--
ALTER TABLE `options`
  ADD PRIMARY KEY (`key`,`owner`);

--
-- Индексы таблицы `skills`
--
ALTER TABLE `skills`
  ADD PRIMARY KEY (`id`,`owner`);

/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;
