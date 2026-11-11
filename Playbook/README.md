# `Playbook/` — содержимое `.apbx`

Здесь лежит ровно то, что пакуется в архив: `playbook.conf`, `Configuration/`, `Executables/`, `Images/`, `playbook.png`.

**Статус:** каркас. YAML-контент и `playbook.conf` появятся на этапе v0.1 (см. [ROADMAP](../docs/ROADMAP.md)).

Правила:

1. Пути внутри playbook считаются от `Configuration/main.yml` для `!task` (`path: 'Tasks/xxx.yml'`) и от `Executables/` для `!run` / `!powerShell`.
2. Порядок фаз задаётся именами файлов с числовыми префиксами: `00-preflight.yml` … `90-report.yml`.
3. `playbook.png` — 256 px, в корне этой папки.
4. Изменение любого файла здесь требует записи в [DECISIONS](../docs/DECISIONS.md) и заполненного `CHANGELOG`.

Не класть сюда: тестовые артефакты, черновики, документацию, скрипты упаковки.
