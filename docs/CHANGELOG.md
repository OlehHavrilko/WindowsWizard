# Журнал изменений

Формат: версия, дата, состав. Версия совпадает с `Version` в `playbook.conf` начиная с v0.1.

---

## v0.2-dev — Toolbox (D-034)

- `src/BareOS.Toolbox`: WPF-приложение поверх движка: карточки каталога с переключателями (apply/revert/verify), поиск, «Применить рекомендованное», «Откатить всё», Proof Report (замер до/после, HTML), журнал. Стиль Bare OS.
- Плейбук: задача `01-toolbox.yml` и `install-toolbox.cmd/.ps1` ставят Toolbox в `%ProgramFiles%\Bare OS` с ярлыками в Пуске и на рабочем столе; `tools/package.py` принимает `--toolbox`.
- CI собирает Toolbox и готовый `.apbx` (артефакт `bareos-playbook-apbx`).

---

## v0.2-dev — Appx и видимая оптимизация (D-033)

- Действие `appx` (удаление пакетов для всех пользователей и из provisioned), `AppxAccess` через Windows PowerShell, защищённый список (Store/Xbox/Defender/WSL/winget/среды выполнения).
- Каталог 25 карточек: реклама Spotlight, поиск без веба и Cortana, диагностика, фоновые приложения, Edge без фонового режима, лишние службы, партнёрские Appx; `onedrive.remove` как proposed. Отсутствие службы при `Disabled` считается выполненным.
- Каталог 29 карточек: добавлены `shell.start-suggestions`, `perf.gamedvr`, `svc.unused-extra`, `task.background-extra`.
- Каталог 39 карточек: производительность по образцу AtlasOS/ReviOS — планировщик CPU, MMCSS, питание, QoS, быстрое выключение, NTFS; спорные (таймер, DisablePagingExecutive, Prefetch, HAGS) — `proposed`, включаются через `--only`.
- Каталог 45 карточек: добавлены приватность (CEIP, инвентаризация, ввод, Copilot-политика, карты), Edge-приватность, Проводник, `brand.oem-info` («О системе»). Плейбук v0.2, иконка — знак «b_».
- Proof Report (F-01): команды `bareos measure` и `bareos proof` (процессы, память, Appx, службы с автозапуском, автозапуск, размер WindowsApps; текст и HTML), задачи плейбука `05-measure-before` и `95-proof`; отчёт в `%ProgramData%\BareOS\proof\proof.html`.
- Карточки `appx.copilot`, `appx.widgets`, `appx.weather-news`, `appx.consumer-apps`; откат Appx не восстанавливает и говорит об этом явно.

---

## v0.2-dev — playbook-обёртка

- `Playbook/`: `playbook.conf` (UniqueId `ffeab6fe-1e87-4d06-930f-06f0fdb62bd5`, 26300), `main.yml` и задачи `plan → apply → verify` через `bareos-run.cmd`, `playbook.png`.
- `tools/validate.py`, `tools/package.py` (7z, пароль `malte`; кладёт `bareos.exe` и `catalog/` в `Executables/`). Прогон через AME Wizard на ВМ: не проверен.

---

## v0.1-dev — явное отключение UCPD флагом (D-032)

- По прямому указанию владельца (после того как D-031 не обошла защиту на ВМ): `UcpdGuard.DisableProtectionTweak()` — отдельный, не каталожный твик (служба `UCPD` → `Disabled`, задача `UCPD velocity` → выключена), как делает AtlasOS.
- Включается флагом CLI `--allow-ucpd-disable` на `apply`/`revert` (по умолчанию выключен). При apply выполняется первым, при revert — Revert() сам переворачивает порядок, поэтому восстанавливается последним.
- Требует перезагрузки: драйвер уже загружен, отключение вступает в силу только после неё. `shell.widgets` остаётся `proposed`.
- Сообщения CLI/отчёта про `BlockedByOS` обновлены (ссылались только на D-030, теперь — D-031 и D-032).
- Тесты: `UcpdDisableTests`.

---

## v0.1-dev — обход UCPD через групповую политику (D-031)

- По прямому указанию владельца (после обзора AtlasOS/Meridian/Winhance/0Ai) отменена часть D-030 «не обходим»: при `BlockedByOS` движок пробует записать значение через локальную групповую политику (`Registry.pol` + `gpupdate /force`), не отключая саму защиту ОС. Реализация: `IPolicyWriter`/`PolicyRegistryWriter`, чистый формат PReg — `PRegFile` (без Windows, тестируется отдельно).
- Если обход не сработал — прежнее поведение: `BlockedByOS`, код не меняется.
- Отключение самой службы UCPD осталось нереализованным заделом (отдельный флаг понадобится позже, D-031 п.3).
- Тесты: `UcpdTests` (фолбэк applies/fails/not-tried, revert через политику), `PRegFileTests` (round-trip формата).
- **Проверено на ВМ:** гипотеза не подтвердилась — `gpupdate` не смог применить значение к `Dsh` (UCPD блокирует и gpsvc), хотя сам `Registry.pol` записывается. Движок распознаёт это (читает реестр после `gpupdate`) и остаётся на `BlockedByOS`; неудачную запись в `Registry.pol` теперь откатывает сам, чтобы не оставлять мёртвую запись. Сообщение `UcpdGuard.Explain` обновлено (ссылалось на D-030, теперь упоминает и D-031). `shell.widgets` остаётся `proposed`.

---

## v0.1-dev — блокировка UCPD (D-030)

- Новый статус результата `BlockedByOS` (рядом с `Failed`): отказ «доступ запрещён» при записи/удалении в ключе из списка `UcpdGuard.ProtectedKeys` (Dsh, Windows Feeds\EnableFeeds, NandI, Explorer\Advanced TaskbarDa), когда служба UCPD запущена (не читается — решает ключ). Код выхода не меняется: apply/revert/verify дают 0; verify показывает n/a с причиной; пустой ключ, не удалённый из-за UCPD, — предупреждение при статусе `Reverted`.
- CLI и отчёт: «заблокировано ОС (UCPD)» с пояснением.
- Карточка `shell.widgets` переведена в `proposed`: UCPD на 24H2+ закрывает `Dsh\AllowNewsAndInterests`; групповая политика Registry.pol — гипотеза, не проверена.
- Тесты: `UcpdTests`, `FakeRegistry.DenyWriteKeys/DenyDeleteKeys`, проверка каталога на защищённые UCPD цели.

---

## v0.1-dev — переименование и аудит

- Продукт переименован WizardOS → Bare OS (D-029): CLI `bareos`, `BareOS.*`, `%ProgramData%\BareOS\runs`. Ранее описанное ниже и в D-001…D-028 под именем WizardOS — то же.
- Аудит движка (PR #7): откат по истории baseline с маркером, `AutomaticDelayed`, ACL корня прогонов, Roslyn/CodeQL/Semgrep/схема каталога в CI.

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

**Стиль под новое название Bare OS (2026-11-11):**

- `BRANDING` §3–7 переписаны: концепция «Ничего лишнего», графит + костяной + единственный мятный акцент, четыре эскиза знака на основе `b`, обои с изолиниями; имена ассетов `bareos-*`.
- `BRANDING` §3a: изюминка «живой курсор» (строка-квитанция на обоях, курсор на экране блокировки, журнал печатается построчно); открытые BQ-05, BQ-06.
- `docs/branding/wizardos-clear-magic.md` и `preview.html`: прежнее направление «Ясная магия» сохранено целиком как запасное.
- Знак выбран (эскиз D «b_»): `branding/src/` — `mark.svg`, `mark-small.svg`, `mark-mono.svg`, `mark-tile.svg`, `palette.json`; `BRANDING` §6 с геометрией, BQ-04 закрыт, добавлен BQ-07.
- `DECISIONS` D-028 п. 1 обновлён; агент `designer` — под BareOS.

**Инфраструктура:**

- Репозиторий — <https://github.com/OlehHavrilko/WindowsWizard> (public, `main`). Каркас и документация залиты в `main` (11.11.2026); локальная копия — `D:\Projects\codex\winoptimizer` ([DECISIONS](DECISIONS.md) D-013).
- `.gitattributes` — канонизация переводов строк (LF) и пометка бинарных артефактов.

**Не добавлено (намеренно):** YAML-контент, `playbook.conf`, скрипты, реальные параметры.

**Открытые решения:** D-007 (состав Appx), D-011 (порог измеримости), D-012 (публикация).
