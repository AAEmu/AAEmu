-- phpMyAdmin SQL Dump
-- version 4.8.3
-- https://www.phpmyadmin.net/
--
-- Хост: 127.0.0.1:3306
-- Время создания: Янв 23 2019 г., 23:24
-- Версия сервера: 8.0.12
-- Версия PHP: 7.2.10

SET SQL_MODE = "NO_AUTO_VALUE_ON_ZERO";
SET AUTOCOMMIT = 0;
START TRANSACTION;
SET time_zone = "+00:00";

--
-- База данных: `aaemu`
--

-- --------------------------------------------------------

--
-- Структура таблицы `game_servers`
--

CREATE TABLE `game_servers` (
  `id` tinyint(3) UNSIGNED NOT NULL,
  `name` text NOT NULL,
  `host` varchar(128) NOT NULL,
  `port` int(11) NOT NULL,
  `hidden` tinyint(1) NOT NULL DEFAULT '1'
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- --------------------------------------------------------

--
-- Структура таблицы `users`
--

CREATE TABLE `users` (
  `id` int(11) UNSIGNED NOT NULL,
  `username` varchar(32) NOT NULL,
  `password` text NOT NULL,
  `email` varchar(128) NOT NULL,
  `last_login` bigint(20) UNSIGNED NOT NULL DEFAULT '0',
  `last_ip` varchar(128) NOT NULL,
  `created_at` bigint(20) UNSIGNED NOT NULL DEFAULT '0',
  `updated_at` bigint(20) UNSIGNED NOT NULL DEFAULT '0'
) ENGINE=InnoDB DEFAULT CHARSET=utf8 ROW_FORMAT=COMPACT;

--
-- Индексы сохранённых таблиц
--

--
-- Индексы таблицы `game_servers`
--
ALTER TABLE `game_servers`
  ADD PRIMARY KEY (`id`);

--
-- Индексы таблицы `users`
--
ALTER TABLE `users`
  ADD PRIMARY KEY (`id`),
  ADD KEY `username` (`username`);

--
-- AUTO_INCREMENT для сохранённых таблиц
--

--
-- AUTO_INCREMENT для таблицы `users`
--
ALTER TABLE `users`
  MODIFY `id` int(11) UNSIGNED NOT NULL AUTO_INCREMENT;
COMMIT;
