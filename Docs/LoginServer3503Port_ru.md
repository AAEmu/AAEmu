# Порт логин-сервера 1.2 → 3.5.0.3: что сделано и почему

Документ описывает перенос изменений логин-сервера (`AAEmu.Login`) из проекта 3.5.0.3 (`AAC3500`)
в актуальную ветку 1.2. Источник изменений — материалы побайтового сравнения в
`../diff-3503/` (см. `PORTING-1.2-to-3.5.md`, `files-modified-content.txt`, `files-added.txt`).

## Зачем это нужно

Клиент 3.5.0.3 отличается от 1.2 протоколом логин-сиквенса: другие опкоды, другие структуры
пакетов `ACJoinResponse`/`ACAuthResponse`, новый вариант пакета аутентификации (`CARequestAuth_004`)
и рабочий поток аутентификации Tencent/Korea вместо заглушки. Без этих изменений клиент 3.5
не проходит стадию логин-сервера (auth → список миров → cookie). Перенос выполнен первым шагом
общего порта на 3.5 — осознанно **только логин-сервер**, чтобы изменения были обозримыми
и не ломали Game-сервер.

## Как применялись изменения

Патчи `aaemu-1.2-to-3503-part1-delete-modified.patch` и `aaemu-1.2-to-3503-part2-write-files.patch`
сняты со снимка 1.2 (`9bd66745`, 2026-07-16). Текущий HEAD репозитория новее и уже содержит
нормализацию окончаний строк CRLF→LF (коммит `e8bee625`, `.gitattributes`: `*.cs text eol=lf`),
поэтому патчи применялись выборочно и с игнорированием различий пробельных символов:

```bash
git -c core.autocrlf=false apply --ignore-whitespace --whitespace=nowarn \
    --include='AAEmu.Login/**' ../diff-3503/aaemu-1.2-to-3503-part1-delete-modified.patch
git -c core.autocrlf=false apply --ignore-whitespace --whitespace=nowarn \
    --include='AAEmu.Login/**' ../diff-3503/aaemu-1.2-to-3503-part2-write-files.patch
```

- `--include='AAEmu.Login/**'` — ограничение скоупа строго логин-сервером;
- `--ignore-whitespace` — необходим, т.к. в патчах контент с CRLF (снимок 1.2), а в рабочем
  дереве уже LF; без флага hunk'и не накладываются.

Результат применения в точности совпал со списком изменений логин-сервера из
`files-modified-content.txt` и `files-added.txt` — без «ложных» EOL-only отличий.

## Что изменено (21 файл) и добавлено (2 файла)

### Ядро логина

| Файл | Что изменилось и почему |
|---|---|
| `Core/Network/Connections/LoginClient.cs` | Новые сигнатуры под клиента 3.5: `ACJoinResponsePacket((byte)1, reason, new AfsValue(2, 2, AdditionalData=22, ...))` — добавлены байт `1` и поле `AdditionalData` (ushort 22); слоты персонажей 6→2, доп. слоты 0→2; `ACAuthResponsePacket(accountId.Value, 0)` вместо `(accountId, 6)`. Без этого клиент 3.5 отвергает ответы логин-сервера. |
| `Core/Network/Connections/LoginSession.cs` | Переработанная state-машина логина (~100 строк реальных изменений) под последовательность пакетов 3.5. |
| `Core/Controllers/GameController.cs` | Правки формирования списка миров/персонажей под новые структуры ответов. |

### Аутентификация

| Файл | Что изменилось и почему |
|---|---|
| `Core/PacketHandlers/C2L/CARequestAuthTencentPacketHandler.cs` | Был no-op stub → вызывает `authFlowFactory.Create(packet.Account, ip)` и `session.AuthenticateAsync(flow)` — рабочая аутентификация через `KoreaAuthFlowFactory`. |
| **новый** `Core/Packets/C2L/CARequestAuth_004_Packet.cs` | Новый вариант auth-пакета клиента 3.5. |
| **новый** `Core/PacketHandlers/C2L/CARequestAuth_004_PacketHandler.cs` | Обработчик нового пакета. |

### Пакеты и опкоды

- `Core/Packets/L2C/ACJoinResponsePacket.cs`, `ACAuthResponsePacket.cs`, `ACWorldListPacket.cs` —
  структуры ответов под клиента 3.5.
- `Core/Packets/C2L/CLOffsets.cs`, `Core/Packets/L2C/LCOffsets.cs`,
  `Core/Packets/G2L/GLOffsets.cs`, `Core/Packets/L2G/LGOffsets.cs` — опкоды 3.5.0.3.
- `Core/Packets/C2L/CARequestAuthPacket.cs`, `CARequestAuthGameOnPacket.cs`,
  `CARequestAuthTencentPacket.cs`, `CARequestReconnectPacket.cs`, `CACancelEnterWorldPacket.cs`,
  `CAOtpNumberPacket.cs`, `CAPcCertNumberPacket.cs` — структуры входящих пакетов 3.5.
- `Core/PacketHandlers/C2L/CAEnterWorldPacketHandler.cs`,
  `Core/PacketHandlers/ServiceCollectionExtensions.cs` (регистрация нового handler'а),
  `Models/AccountId.cs`.

## Что сознательно НЕ переносилось

- **`AAEmu.Commons/Models/LoginCharacterInfo.cs`** (`AccountId` uint→ulong) — формально часть
  логин-потока, но тип используется и Game-сервером; точечный перенос сломал бы сборку
  `AAEmu.Game`. Переносить вместе с коммитом «uint→ulong для AccountId» по всему решению
  (шаг 1 рекомендуемого порядка порта в `PORTING-1.2-to-3.5.md`).
- **Тесты из 3.5** (`AAEmu.UnitTests/Login/*`, `AAEmu.Login.IntegrationTests/*`) — вне запрошенного
  скоупа; текущие тесты 1.2 собираются и проходят на новом коде.
- **Документация 3.5** (`Docs/Packets/AAC3500_LoginSequence_Sync.md`) — рекомендуется добавить
  при порте игрового протокола (опкоды логин-сиквенса Game-сервера).
- Game-часть порта (шифрование `EncryptionManager`, `GamePacket` level 5, `X2EnterWorld*` и т.д.) —
  следующие шаги по `PORTING-1.2-to-3.5.md`.

## Проверка

- `dotnet build AAEmu.Login/AAEmu.Login.csproj` — 0 ошибок (1 пред-существующее предупреждение
  CS9113 в `KoreaAuthFlow.cs`, не относится к порту).
- `dotnet build AAEmu.UnitTests` и `AAEmu.Login.IntegrationTests` — 0 ошибок.
- Полный прогон `AAEmu.UnitTests` (TUnit): 1076/1076 успешно, 0 сбоев.
