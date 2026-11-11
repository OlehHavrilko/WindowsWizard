# Источники и границы подтверждения

Дата сверки: **2026-11-11**. Проверка документации не заменяет запуск конкретной версии AME и Windows. Утверждения прежней документации без ссылки не считаются воспроизведённым тестом.

| Источник | Что подтверждено | Что остаётся проверить |
|---|---|---|
| [Microsoft: Windows 11 release information](https://learn.microsoft.com/en-us/windows/release-health/windows11-release-information) | 26H2 относится к семейству 26300 | фактические build + UBR, Pro x64, индекс и SHA-256 используемого ISO; совместимость playbook |
| [AME: ISO Compatibility](https://docs.amelabs.net/developers/iso.html) | injection и OOBE разделены; скрипты/команды не поддерживаются при injection; `DefenderDisabled` удаляет Defender из образа; опции связываются с выбором при создании ISO | точная матрица действий, вложенные `!task`, offline-baseline и исполнение на целевой версии |
| [AME: Configuration](https://docs.amelabs.net/developers/configuration.html) | обязательные метаданные и наличие `UseKernelDriver` | необязательные поля вне таблицы, правила сравнения SupportedBuilds и новые версии формата |
| [AME: PowerShell](https://docs.amelabs.net/developers/actions/PowerShell.html) | официальный справочник действия | реакция оркестратора на ошибку/exit code и гарантированная остановка дальнейших изменений |
| [Microsoft: BitLocker recovery process](https://learn.microsoft.com/en-us/windows/security/operating-system-security/data-protection/bitlocker/recovery-process) | изменения прошивки могут вызвать recovery; предусмотрены ключ восстановления и временная приостановка защиты | необходимость отключения автошифрования именно в этом проекте остаётся решением D-003 |

## Источники по конкурентам

Использованы в [COMPETITORS](COMPETITORS.md). Получены веб-поиском 2026-11-11; сами playbook не запускались, страницы релизов читались в пересказе поисковой выдачи.

| Источник | Что взято | Что остаётся проверить |
|---|---|---|
| [Atlas-OS/Atlas: releases](https://github.com/Atlas-OS/Atlas/releases) | состав 0.5.0: Toolbox, ISO injection, 25H2, ограниченный путь апгрейда | актуальная версия; состав по Copilot / Recall / Edge |
| Atlas-OS/Atlas: issues [#1556](https://github.com/Atlas-OS/Atlas/issues/1556), [#974](https://github.com/Atlas-OS/Atlas/issues/974) | поломка Store после накопительного обновления; рекомендация перезапускать playbook | воспроизводимость на 26H2 |
| [meetrevision/playbook: releases](https://github.com/meetrevision/playbook/releases) | 26.04: перенос логики твиков в Revision Tool | полный список изменений |
| [AME: Removing / Upgrading](https://docs.amelabs.net/remove_upgrade.html) | у платформы нет стандартного удаления playbook; обратимость заявлена до v1.0 | состояние на закреплённой версии AME |
| [AME: Privacy+ Testing Methodology (PDF)](https://download.ameliorated.io/Privacy%2B%20Testing%20Methodology.pdf) | существование опубликованной методики тестов | содержание методики не изучалось |
| Сторонние обзоры и форумы | оценки прироста FPS на уровне погрешности, сравнение уровней безопасности | не первичные источники; использовать только как наблюдение |

## Маршрутизация AME

В ISO Compatibility указано: при неуказанном `oobe` действие с `iso: true/only` пропускается на OOBE; `oobe: true` включает выполнение там явно. Значения `iso: only` и `oobe: only` исключают обычный live-прогон. Кэширование загрузок/ПО рассматривается отдельно. Нельзя выводить поведение всего дерева из одного свойства на родительском `!task`; это проверяется отдельным прогоном S-17.

В этой же странице выбор опций описан при создании ISO, а часть возможностей UI OOBE — как будущая. Поэтому возможность менять на OOBE опции, уже повлиявшие на injection, не заявляется. Документация не доказывает наличие единого автоматического механизма снимка до injection.

## Что пока не подтверждено

- `BypassNRO`, точный состав генерируемого `unattend` и его зависимость от `DisableBitLocker` — инспекция результата сборки на закреплённой версии AME.
- `EstimatedMinutes`, `UpgradableFrom`, лимиты/атрибуты FeaturePages и псевдонимы `!status`/`!writeStatus` — целевой parser и UI; примера недостаточно.
- Абсолютное утверждение «у AME нет журналов» не используется: проекту нужен собственный журнал prior-state, наличие платформенного лога его не заменяет.
- Состояние GitHub описано в старых документах как наблюдение 11.11.2026; при этой ревизии удалённый репозиторий не проверялся и не изменялся.

Для каждого спорного поля на этапе реализации фиксируются версия AME, URL/commit источника, минимальный воспроизводимый пример и результат. До этого поле не является основанием заявлять поддержку.
