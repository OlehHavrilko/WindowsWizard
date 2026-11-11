# WizardOS — собственный AME Playbook для Windows 11 26H2 Pro

Продукт называется WizardOS ([DECISIONS](docs/DECISIONS.md) D-025); репозиторий — WindowsWizard.

**Статус:** документированный каркас; исполняемого кода, playbook-контента и результатов прогонов нет. Теоретическая ревизия — [REVIEW](docs/REVIEW.md); переход к реализации ограничен условиями [ROADMAP](docs/ROADMAP.md).
**Цель:** один управляемый AME Playbook для чистой установки Windows 11 26H2 Pro x64 — без готовых сборок, без удаления защитных механизмов, с проверяемым результатом и рабочим откатом.
**Репозиторий:** <https://github.com/OlehHavrilko/WindowsWizard> — public, ветка `main`.
**Локальная копия:** `D:\Projects\codex\winoptimizer` (имя каталога историческое; git инициализирован, ветка `main` синхронизирована с origin — см. [DECISIONS](docs/DECISIONS.md) D-013).

---

## Чем это является и чем нет

**Является:** набором декларативных изменений конфигурации Windows в формате AME Playbook (`.apbx`), который применяется к оригинальному ISO Microsoft — либо на живую систему через AME Wizard, либо внедряется в установочный образ и применяется на первом запуске (OOBE).

**Не является:**

- не модифицированная Windows и не готовый ISO от третьей стороны;
- не универсальный твикер и не замена NTLite;
- не «ускоритель на 30 FPS» — эффект должен быть измерим, иначе параметр не попадает в набор;
- не инструмент удаления Defender, WSL2 или Hyper-V.

## Целевая система

| Параметр | Значение |
|---|---|
| ОС | Windows 11 26H2 Pro x64 |
| Билд | семейство 26300 подтверждено Microsoft; конкретный ISO, индекс Pro, архитектура и UBR ещё не проверены ([SOURCES](docs/SOURCES.md)) |
| База | оригинальный ISO Microsoft, чистая установка |
| Сценарии | игры (+ античиты), WSL2, Docker Desktop, локальный AI/GPU-ускорение |
| Носитель | ISO, внедрённый через AME Wizard (основной путь); применение на живую систему — отладочный |

## Быстрая навигация

| Вопрос | Документ |
|---|---|
| Как устроен AME Playbook, какие два этапа исполнения | [ARCHITECTURE](docs/ARCHITECTURE.md) |
| Как фиксировать план, исходное состояние, сбои и повторные запуски | [EXECUTION-CONTRACT](docs/EXECUTION-CONTRACT.md) |
| Что выявила ревизия и какие сведения подтверждены источниками | [REVIEW](docs/REVIEW.md), [SOURCES](docs/SOURCES.md) |
| Что писать в `playbook.conf`: поля, гейты, FeaturePages | [PLAYBOOK-CONF](docs/PLAYBOOK-CONF.md) |
| Какие действия доступны и где они работают | [ACTIONS](docs/ACTIONS.md) |
| Почему включено/исключено то или иное, что ещё не решено | [DECISIONS](docs/DECISIONS.md) |
| Правила приёма параметров в набор | [PARAMETERS](docs/PARAMETERS.md) |
| Что нельзя трогать ни при каких условиях | [SAFETY](docs/SAFETY.md) |
| Контракт проверки после применения | [VERIFICATION](docs/VERIFICATION.md) |
| Как откатывать и что откатить нельзя | [REVERT](docs/REVERT.md) |
| Валидация, упаковка, релизы | [BUILD](docs/BUILD.md) |
| На чём и как тестировать | [TEST-MATRIX](docs/TEST-MATRIX.md) |
| Этапы от документации до релиза | [ROADMAP](docs/ROADMAP.md) |
| Риски и их снижение | [RISKS](docs/RISKS.md) |
| Требования к реализации: план, baseline, журнал, остановка, откат | [EXECUTION-CONTRACT](docs/EXECUTION-CONTRACT.md) |
| Ревизия каркаса: что уточнено и что осталось до начала реализации | [REVIEW](docs/REVIEW.md) |
| Источники и границы подтверждения | [SOURCES](docs/SOURCES.md) |
| Чем проект отличается от AtlasOS / ReviOS и чего им не хватает | [COMPETITORS](docs/COMPETITORS.md), [DIFFERENTIATORS](docs/DIFFERENTIATORS.md) |
| Toolbox: твики, драйверы, каталог программ после установки | [TOOLBOX](docs/TOOLBOX.md), [TOOLS-CATALOG](docs/TOOLS-CATALOG.md) |
| Фирменный стиль: обои, тема, знак, интерфейс Toolbox | [BRANDING](docs/BRANDING.md) |
| Какие твики берём у WinUtil, AtlasOS, ReviOS и на какой машине меряем | [TWEAKS-BASELINE](docs/TWEAKS-BASELINE.md), [REFERENCE-MACHINE](docs/REFERENCE-MACHINE.md) |

## Структура репозитория

```
WindowsWizard/                  (локальный каталог: winoptimizer/)
├── README.md                     ← вы здесь
├── .gitignore
├── docs/                         вся проектная документация
├── Playbook/                     то, что пакуется в .apbx (пока только каркас)
│   ├── playbook.conf             ← будет создан на этапе v0.1
│   ├── Configuration/
│   │   ├── main.yml              ← оркестратор, будет создан на этапе v0.1
│   │   └── Tasks/                фазовые файлы задач
│   ├── Executables/              verify / report / revert (скрипты — позже)
│   └── Images/                   иконки для OOBE и Software
├── tools/                        validate + package (позже)
├── tests/                        тестовая матрица и артефакты прогонов
└── releases/                     готовые .apbx и контрольные суммы
```

Правило репозитория: `docs/` — источник истины, `Playbook/` — реализация. Любое изменение в `Playbook/` обязано иметь строку в [DECISIONS](docs/DECISIONS.md) и запись в `CHANGELOG` (см. [BUILD](docs/BUILD.md)).

## Проверенные факты и открытые вопросы

Формат AME сверен по официальной документации и по открытому рабочему playbook; всё, что не сверено, помечено в тексте как «требует проверки».

| Утверждение | Статус |
|---|---|
| Репозиторий `OlehHavrilko/WindowsWizard`: public, ветка `main` | проверено `gh repo view` 11.11.2026 |
| Документация залита в `main` | 11.11.2026 |
| `.apbx` = 7z с паролем `malte` | проверено по докам AME |
| Структура `playbook.conf` + `Configuration/main.yml` + `Configuration/Tasks/*.yml` + `Executables/` | проверено по докам AME |
| Набор действий (`!registryValue`, `!service`, `!appx`, `!systemPackage`, `!task`, …) | проверено по докам AME |
| Разделение на этапы Injection / OOBE и свойства `iso:` / `oobe:` | проверено по докам AME |
| `!cmd`, `!run`, `!powerShell`, `!taskKill`, `!appx`, `!systemPackage` несовместимы с injection | по докам AME (запрет динамических действий) + комментарии в стороннем playbook |
| `!service` допускает injection только для `delete` / `change` | по докам AME + сторонний playbook |
| Лимиты FeaturePages (4 чекбокса, 3 радио, `TopLine` ≤ 3) | проверено по докам AME |
| `RadioPage` для injection требует `IsRequired` + `DefaultOption` | сторонний playbook, требует проверки в ВМ |
| AME удаляет Defender при `DefenderDisabled` | проверено по докам AME (ISO Compatibility) |
| У AME нет штатного отката | по докам откат не описан; ReviOS компенсирует «reversible deletion method» |
| Windows 11 26H2 = семейство сборок 26300 | подтверждено Microsoft 11.11.2026; совместимость playbook не проверялась |
| `EstimatedMinutes`, `UpgradableFrom` | встречаются в стороннем playbook, в таблицах документации свойств не описаны — проверить |

## Предупреждения

- **AME Wizard — Beta.** Формат и поведение могут меняться между версиями; версия, на которой проводилось тестирование, должна быть зафиксирована (см. [BUILD](docs/BUILD.md)).
- **Штатного отката нет.** Всё, что playbook удалил, возвращается только вручную; поэтому откат проектируется как отдельный артефакт ([REVERT](docs/REVERT.md)).
- Playbook без `ProductCode` отображается в AME как непроверенный. Это нормальное состояние до прохождения ревью AME.
