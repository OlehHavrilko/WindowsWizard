# Движок WizardOS

Консольный движок на .NET 8: читает карточки твиков, строит план, применяет, проверяет и откатывает изменения. Решения: D-023, `EXECUTION-CONTRACT.md`, `REVERT.md`, `VERIFICATION.md`.

## Структура

| Путь | Что |
|---|---|
| `src/WizardOS.Core/Contracts.cs` | контракты: `Tweak`, `Plan`, `Baseline`, `Report`, `IRegistryAccess`, `IServiceAccess`, `IRunStore`, `ITweakEngine` |
| `src/WizardOS.Core/Engine/` | `TweakEngine`, `JsonRunStore`, `TweakCatalog` |
| `src/WizardOS.Core/Windows/` | `RegistryAccess` (Microsoft.Win32.Registry), `ServiceAccess` (чтение из реестра, запись через `sc.exe config`); вне Windows бросают `PlatformNotSupportedException` |
| `src/WizardOS.Cli/` | приложение `wizardos` |
| `catalog/**/*.yml` | карточки твиков |

## Команды

```
wizardos <plan|apply|verify|revert> [--catalog <dir>] [--data <dir>] [--only id,id] [--json]
```

- `--catalog` — каталог карточек, по умолчанию `./catalog`.
- `--data` — каталог прогонов, по умолчанию `%ProgramData%\WizardOS\runs`.
- `--only` — только перечисленные id твиков.
- `--json` — отчёт в JSON; иначе таблица «Статус / Твик / До / После / Ожидалось».
- `plan` только читает систему. `apply` и `revert` требуют прав администратора (иначе код 2).

Код выхода: `0` — `Report.Ok`; `1` — есть проблемы (Failed, Mismatch, Conflict, Unknown…); `2` — ошибка использования, валидации карточек или прав.

## Карточка твика

YAML (ключи camelCase, перечисления строками, регистр не важен) по схеме `record Tweak`; поля — [PARAMETERS](PARAMETERS.md) §2. Файл — одна карточка или список карточек; читаются `*.yml` / `*.yaml` рекурсивно из `catalog/`. Неизвестный ключ — ошибка валидации.

```yaml
id: shell.widgets
title: "…"
verdict: Core            # Core | Standard | Option | Lab
stability: Stable        # Stable | Moderate | Fragile
intent: "что и зачем"
evidence: "измеримая величина эффекта"
actions:
  - id: a1
    registry: { path: 'HKLM\…', name: "…", kind: DWord, data: "0" }
  - id: a2
    service: { name: DiagTrack, start: Disabled }
# необязательные поля:
category: privacy        # реестр / службы / appx / задачи / приватность / удобство / латентность
scope: machine           # machine | user
risk: T1                 # T0..T4
reversibility: "…"
gate: "…"                # option / FeaturePage
rationale: "…"
breaks: "…"
verification: "…"
delay: none              # none | reboot | relogon
source: "…"
status: proposed         # proposed | accepted | verified | rejected
```

В действии ровно одно из `registry` / `service`. Пути реестра в одинарных кавычках (обратные слэши без экранирования). Данные реестра в каноническом виде: DWord/QWord — десятичное число, Binary — hex, MultiString — строки через `\n`.

Запретный список (Defender / Hyper-V / WSL): отклоняются службы из списка, реестровые пути `...\Services\<запрещённая служба>`, пути с `Windows Defender`, `Windows Advanced Threat Protection`, `Hyper-V`, `\Microsoft\Virtualization`, `\Lxss`, `\WSL`.

## Где лежат данные

`<--data>\<runId>\`: снимок исходного состояния (baseline, не перезаписывается), журнал (строка перед и после каждого действия) и отчёт прогона. Откат идёт по снимку в обратном порядке с проверкой «текущее / снимок / ожидаемое»; расхождение даёт `Conflict`.

## Как добавить твик

1. Создать `catalog/<id>.yml` по схеме выше. Без измеримого `evidence` твик не принимается.
2. Действия — только то, что умеют `registry` и `service`; для каждого заранее ясно значение «после».
3. Проверить: `wizardos plan --only <id>`, затем `apply`, `verify`, `revert` на тестовой машине Windows.
4. Отметить в `docs/CHANGELOG.md`.
