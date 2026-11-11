# Журнал изменений

Формат: версия, дата, состав. Версия совпадает с `Version` в `playbook.conf` начиная с v0.1.

---

## v0.1-dev — движок: CLI, Windows-доступ, первые твики

**Добавлено:**

- `src/WizardOS.Core/Windows/` — `RegistryAccess`, `ServiceAccess`, `TaskAccess` (планировщик через `schtasks.exe`).
- Действие `task` (enabled/disabled) в движке, каталоге и откате; команда `wizardos validate`; `catalog/tweak.schema.json`.
- Каталог: 13 карточек (приватность, поиск, Delivery Optimization, задачи телеметрии); CI публикует self-contained `wizardos.exe` (win-x64) как артефакт.
- `src/WizardOS.Cli/` — приложение `wizardos` (`plan|apply|verify|revert`).
- `catalog/` (YAML, YamlDotNet) — `privacy.telemetry`, `shell.widgets`, `consumer.ads`; JSON-загрузка карточек удалена; запретный список закрывает `Services\<служба>`, Windows Advanced Threat Protection, Microsoft\Virtualization.
- `docs/ENGINE.md` — описание движка.
- Целевая платформа — .NET 10 (D-027); `RunLock` — замок параллельного прогона (apply/revert, код 2 при занятом); команда `report [--run id|latest]`; на Windows ACL каталога прогона — только Administrators и SYSTEM; CI `.github/workflows/ci.yml`.

---

## v0.0 — документация и структура

**Дата:** 2026-11-11

**Добавлено:**

- `README.md` — назначение, границы проекта, навигация, таблица проверенных фактов и открытых вопросов.
- `docs/ARCHITECTURE.md` — механика `.apbx`, этапы Injection/OOBE, порядок фаз, границы ответственности.
- `docs/PLAYBOOK-CONF.md` — справочник свойств `playbook.conf`, требования, `ISO`/`OOBE`, FeaturePages, лимиты формата.
- `docs/ACTIONS.md` — перечень действий, совместимость с этапами, правила применения.
- `docs/DECISIONS.md` — журнал решений D-001…D-012 и открытые вопросы Q-01…Q-05.
- `docs/PARAMETERS.md` — модель параметра, уровни риска, статусы, чек-лист приёма.
- `docs/SAFETY.md` — инварианты, защищённый перечень, запреты, политика allowlist для служб.
- `docs/VERIFICATION.md` — контракт preflight/верификатора/отчёта, критерии приёмки.
- `docs/REVERT.md` — уровни отката, матрица обратимости, порядок и проверка.
- `docs/BUILD.md` — валидация, упаковка, версионирование, чек-лист релиза.
- `docs/TEST-MATRIX.md` — среды, сценарии S-01…S-10, проверки, форма фиксации.
- `docs/ROADMAP.md` — этапы v0.1…v1.0 с критериями выхода.
- `docs/RISKS.md` — реестр рисков R-01…R-25 и их снижение.
- `docs/EXECUTION-CONTRACT.md` — контракт исполнения: план, `run_id`/`baseline_id`, снимок по режимам, контекст пользователя (SID), журнал и остановка, правила отката, хранение и доверие.
- `docs/REVIEW.md` — ревизия каркаса: 14 уточнений и условия начала реализации.
- `docs/SOURCES.md` — источники и границы подтверждения; что осталось проверить.
- Каркас каталогов `Playbook/`, `tools/`, `tests/`, `releases/` с пояснениями.

**Уточнено по ревизии:**

- `TEST-MATRIX`: добавлены сценарии S-11…S-17 (сбой preflight, prior-state политик, повторный/прерванный прогон, контекст пользователя, хранение и публикация отчёта, baseline при injection).
- `PARAMETERS`: необратимое изменение не может быть T2; Appx — T4 до доказанного восстановления регистрации, provisioning и данных.
- `ROADMAP`: минимальный обратимый откат перенесён в v0.1; v0.1 не включает необратимые цели.
- README: в навигацию добавлены `EXECUTION-CONTRACT`, `REVIEW`, `SOURCES`.

**Стратегия отличий:**

- `docs/COMPETITORS.md` — разбор AtlasOS, ReviOS, Privacy+, AME 11: слабые места W-01…W-09 и сильные стороны.
- `docs/DIFFERENTIATORS.md` — функции-отличия F-01…F-10 и линия Lab с тремя условиями допуска.
- `DECISIONS`: добавлены D-014 (позиционирование) и D-015 (линия Lab), оба `proposed`; уточнён Q-02.
- `ROADMAP`: функции-отличия разнесены по этапам.
- `SOURCES`: источники по конкурентам с границами подтверждения.

**Toolbox (замысел владельца):**

- `docs/TOOLBOX.md` — вкладки, пресеты твиков, установка драйверов без браузера, стек, дополнения T-01…T-07, этапы.
- `docs/TOOLS-CATALOG.md` — каталог open source программ: 12 категорий, наборы для установки пачкой.
- `DECISIONS`: D-016 (Toolbox), D-017 (Copilot / Recall / Edge в пресете «Стандарт»), D-018 (драйверы) — `accepted`.

- `DECISIONS`: D-019 (обновляемость как главный принцип) — `accepted`; D-002 подтверждён. `DIFFERENTIATORS` §5: механизмы адаптации к обновлениям F-12a…F-12e.

- `DIFFERENTIATORS` §5.1–5.3: кто обслуживает адаптацию к обновлениям — три уровня без ручного ведения списков; в карточку параметра добавлен класс устойчивости.

**Ответы владельца (2026-11-11):**

- `docs/TWEAKS-BASELINE.md` — сравнение твиков WinUtil, AtlasOS, ReviOS с вердиктом по каждому.
- `docs/REFERENCE-MACHINE.md` — эталонная конфигурация без персональных данных.
- `DECISIONS`: D-020…D-024; D-003, D-014, D-015 приняты; D-005 частично заменён; Q-01, Q-02 закрыты, добавлен Q-06 (название).
- `SAFETY` §4, `TOOLBOX`, `TOOLS-CATALOG`, `DIFFERENTIATORS`, `ROADMAP` приведены в соответствие.

**Название, автономия, роли агентов (2026-11-11):**

- `DECISIONS`: D-025 — продукт называется WizardOS (Q-06 закрыт); D-026 — PR и merge без согласования, заменяет D-013 п. 3; D-024 п. 6 подтверждён владельцем.
- `README`, `BUILD`, `ROADMAP` приведены к новому имени и порядку работ.
- `.claude/agents/`: роли по частям работы — `architect` (Opus, среднее усилие), `engine-coder`, `playbook-author`, `tester` (Sonnet, среднее), `executor` (Haiku, низкое).

**Архитектура под движок (2026-11-11):**

- `ARCHITECTURE` §3–8 переписаны под D-023: слои (каталог → движок → playbook / Toolbox), команды и коды возврата, обработчики, хранение, фазы playbook, адаптация к обновлениям.
- `ROADMAP`: v0.1 — движок, v0.2 — playbook-обёртка, v0.3 — ISO-путь; Toolbox после v0.2.
- `DECISIONS`: D-027 — движок на .NET 10, self-contained; каталог прогонов — `%ProgramData%\WizardOS\`.

**Фирменный стиль (2026-11-11):**

- `docs/BRANDING.md` — механизмы оформления Windows и решение по каждому, концепция «Ясная магия», палитра с проверенным контрастом, типографика, знак, обои, карточки `brand.*`, откат, стиль Toolbox, этапы B-0…B-3.
- `DECISIONS`: D-028 — бренд через карточки движка, только свои ассеты, без замены системных файлов.
- `.claude/agents/designer.md` — агент реализации стиля (Sonnet, medium).

**Инфраструктура:**

- Репозиторий — <https://github.com/OlehHavrilko/WindowsWizard> (public, `main`). Каркас и документация залиты в `main` (11.11.2026); локальная копия — `D:\Projects\codex\winoptimizer` ([DECISIONS](DECISIONS.md) D-013).
- `.gitattributes` — канонизация переводов строк (LF) и пометка бинарных артефактов.

**Не добавлено (намеренно):** YAML-контент, `playbook.conf`, скрипты, реальные параметры.

**Открытые решения:** D-007 (состав Appx), D-011 (порог измеримости), D-012 (публикация).
