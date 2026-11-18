# Bare OS — AME Playbook для Windows 11 26H2 Pro

Продукт называется Bare OS ([DECISIONS](docs/DECISIONS.md) D-029); репозиторий — WindowsWizard.

**Версия:** 0.2 (`Bare OS-0.2.apbx`).
**Что внутри:** .NET-движок `bareos` (команды `plan`, `apply`, `verify`, `revert`, `measure`, `proof`), каталог из 76 карточек, плейбук-обёртка для AME Wizard и Toolbox (WPF).
**Проверено:** полный прогон на ВМ (Windows 11 26H2, сборка 26300) 18.11.2026 — apply 143 действия, verify 187/187, откат работает. Proof Report: −710 МБ в `WindowsApps`, процессов 159 → 155, служб с автозапуском 73 → 71.
**Репозиторий:** <https://github.com/OlehHavrilko/WindowsWizard> — public, ветка `main`.

---

## Что это и чем не является

**Является:** набором декларативных изменений Windows 11 в формате AME Playbook (`.apbx`) для оригинального ISO Microsoft: убирает рекламу, предустановленный мусор (Copilot, виджеты, погода и новости, партнёрские приложения), лишнюю телеметрию и фоновые службы, настраивает отзывчивость и оформление. Каждое изменение проверяется (`verify`) и откатывается (`revert`).

**Не является:**

- не модифицированная Windows и не готовый ISO от третьей стороны;
- не универсальный твикер и не замена NTLite;
- не «ускоритель на 30 FPS» — эффект должен быть измерим, иначе параметр не попадает в набор;
- не инструмент удаления Defender, WSL2 или Hyper-V.

## Скачать

Релизов на GitHub пока нет. Готовый плейбук собирает CI на каждый коммит в `main`:

1. Откройте [последний успешный запуск CI на main](https://github.com/OlehHavrilko/WindowsWizard/actions/workflows/ci.yml?query=branch%3Amain+is%3Asuccess).
2. В разделе *Artifacts* скачайте `bareos-playbook-apbx` — внутри `Bare OS-0.2.apbx`.
3. Отдельные артефакты: `bareos-win-x64` (`bareos.exe`), `bareos-toolbox-win-x64` (`BareOS-Toolbox.exe`).

Скачивание артефактов требует входа в GitHub; срок хранения артефактов ограничен.

Или соберите сами: `python tools/package.py --exe <bareos.exe> --toolbox <BareOS-Toolbox.exe> --out dist` ([BUILD](docs/BUILD.md)).

## Как запустить

Требования: чистая Windows 11 26H2 Pro x64, права администратора, интернет, нет ожидающих обновлений. **Сначала сделайте снимок ВМ или точку восстановления.**

1. Установите [AME Wizard](https://amelabs.net) (проверено на 0.8.4, Beta).
2. Запустите AME Wizard и перетащите в него `Bare OS-0.2.apbx`. Пароль архива, если спросит: `malte` (формат `.apbx` — ZIP с ZipCrypto, как у AtlasOS).
3. Playbook без `ProductCode` AME помечает как непроверенный — это нормально, подтвердите запуск.
4. Дождитесь завершения и перезагрузитесь. Toolbox появится в Пуске и на рабочем столе, Proof Report — в `%ProgramData%\BareOS\proof\proof.html`.

Запуск без GUI: `TrustedUninstaller.CLI.exe "<распакованный плейбук>" default` ([BUILD](docs/BUILD.md) §7).

Откат: через Toolbox («Откатить всё») или `bareos revert` ([REVERT](docs/REVERT.md)). Удалённые Appx-пакеты откат не восстанавливает — только вручную.

## Целевая система

| Параметр | Значение |
|---|---|
| ОС | Windows 11 26H2 Pro x64, сборка семейства 26300 (проверено на 26300.9818 и 26300.9974) |
| База | оригинальный ISO Microsoft, чистая установка |
| Применение | на живую систему через AME Wizard; внедрение в ISO (OOBE) — позже, см. [ROADMAP](docs/ROADMAP.md) |
| Сценарии | игры (+ античиты), WSL2, Docker Desktop, локальный AI/GPU-ускорение |

## Документация

| Вопрос | Документ |
|---|---|
| Устройство: движок, плейбук, Toolbox | [ARCHITECTURE](docs/ARCHITECTURE.md), [ENGINE](docs/ENGINE.md) |
| План, baseline, журнал, остановка, откат | [EXECUTION-CONTRACT](docs/EXECUTION-CONTRACT.md) |
| Формат карточки и правила приёма параметров | [PARAMETERS](docs/PARAMETERS.md) |
| Какие действия есть и где работают | [ACTIONS](docs/ACTIONS.md) |
| `playbook.conf`, гейты, FeaturePages | [PLAYBOOK-CONF](docs/PLAYBOOK-CONF.md) |
| Что нельзя трогать | [SAFETY](docs/SAFETY.md) |
| Проверка после применения | [VERIFICATION](docs/VERIFICATION.md) |
| Откат и что откатить нельзя | [REVERT](docs/REVERT.md) |
| Сборка, упаковка, запуск через CLI | [BUILD](docs/BUILD.md) |
| Тестирование | [TEST-MATRIX](docs/TEST-MATRIX.md) |
| Журнал изменений | [CHANGELOG](docs/CHANGELOG.md) |
| Решения и открытые вопросы | [DECISIONS](docs/DECISIONS.md) |
| Дорожная карта | [ROADMAP](docs/ROADMAP.md) |
| Риски | [RISKS](docs/RISKS.md) |
| Ревизия и источники | [REVIEW](docs/REVIEW.md), [SOURCES](docs/SOURCES.md) |
| Сравнение с AtlasOS / ReviOS | [COMPETITORS](docs/COMPETITORS.md), [DIFFERENTIATORS](docs/DIFFERENTIATORS.md) |
| Твики из WinUtil, AtlasOS, ReviOS; эталонная машина | [TWEAKS-BASELINE](docs/TWEAKS-BASELINE.md), [REFERENCE-MACHINE](docs/REFERENCE-MACHINE.md) |
| Toolbox | [TOOLBOX](docs/TOOLBOX.md), [TOOLS-CATALOG](docs/TOOLS-CATALOG.md) |
| Фирменный стиль | [BRANDING](docs/BRANDING.md) |

## Структура репозитория

```
WindowsWizard/
├── src/            BareOS.Core, BareOS.Cli (bareos.exe), BareOS.Toolbox (WPF), тесты
├── catalog/        76 карточек твиков (YAML) и tweak.schema.json
├── Playbook/       то, что пакуется в .apbx (playbook.conf, Configuration, Executables)
├── tools/          validate.py, package.py, скрипты брендинга
├── branding/       знак «b_», обои, иконки
├── tests/          тестовая матрица и артефакты прогонов
├── releases/       описание релизов (сами архивы в git не хранятся)
└── docs/           проектная документация
```

Правило: `docs/` — источник истины, `catalog/` и `Playbook/` — реализация. Изменение в них требует записи в [DECISIONS](docs/DECISIONS.md) и [CHANGELOG](docs/CHANGELOG.md).

## Предупреждения

- **AME Wizard — Beta.** Формат и поведение могут меняться; тестировалось на 0.8.4.
- Карточки со статусом `proposed` ещё не проверены на ВМ и по умолчанию не применяются; включаются явно (`--only`).
- Часть изменений требует перезагрузки; отключение защиты UCPD — только флагом `--allow-ucpd-disable`.
- Проект делается для себя и публикуется как есть; применяйте на ВМ или с точкой восстановления.
