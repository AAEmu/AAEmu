# Changelog

## Unreleased

### Added

- `AAEmu.Login`: новый пакет `CARequestAuth_004_Packet` и его обработчик — вариант аутентификации клиента 3.5.0.3.
- Документация порта логин-сервера: `Docs/LoginServer3503Port_ru.md` — что перенесено из 3.5.0.3 и почему.

### Changed

- Порт логин-сервера (`AAEmu.Login`) под протокол клиента 3.5.0.3 по материалам сравнения 1.2 → 3.5 (`diff-3503`):
  - `LoginClient` — новые сигнатуры `ACJoinResponsePacket` (слоты персонажей 6→2, `AdditionalData=22`) и `ACAuthResponsePacket`;
  - `LoginSession` — переработанная state-машина логина под последовательность пакетов 3.5;
  - `CARequestAuthTencentPacketHandler` — рабочая аутентификация через `KoreaAuthFlowFactory` вместо no-op заглушки;
  - структуры пакетов C2L/L2C и опкоды (`CLOffsets`, `LCOffsets`, `GLOffsets`, `LGOffsets`) синхронизированы с клиентом 3.5.0.3.
