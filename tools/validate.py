#!/usr/bin/env python3
"""Офлайн-проверка Playbook/ без запуска AME (docs/BUILD.md §2). Код 0 — можно упаковывать."""
import re, sys, uuid
import xml.etree.ElementTree as ET
from pathlib import Path

root = Path(__file__).resolve().parent.parent / "Playbook"
errors = []
def err(m): errors.append(m)

conf = root / "playbook.conf"
if not conf.exists():
    sys.exit("нет playbook.conf")
try:
    t = ET.parse(conf).getroot()
except ET.ParseError as e:
    sys.exit(f"playbook.conf: {e}")
for k in ("Name", "Username", "UniqueId", "ShortDescription", "Title", "Description", "Details", "Version"):
    if t.findtext(k, "").strip() == "": err(f"playbook.conf: нет {k}")
try: uuid.UUID(t.findtext("UniqueId", ""))
except ValueError: err("playbook.conf: UniqueId не GUID")
if not re.fullmatch(r"\d+(\.\d+){0,2}", t.findtext("Version", "")): err("playbook.conf: неверный Version")

main = root / "Configuration" / "main.yml"
if not main.exists(): err("нет Configuration/main.yml")
else:
    refs = re.findall(r"!task:\s*\{path:\s*'([^']+)'", main.read_text(encoding="utf-8"))
    names = [Path(r).name for r in refs]
    if names != sorted(names): err("порядок !task не совпадает с числовыми префиксами")
    for r in refs:
        if not (root / "Configuration" / r).exists(): err(f"!task: нет файла {r}")
for f in (root / "Configuration").rglob("*.yml"):
    for m in re.findall(r"exe:\s*'([^']+)'", f.read_text(encoding="utf-8")):
        if not (root / "Executables" / m).exists(): err(f"{f.name}: нет Executables/{m}")
png = root / "playbook.png"
if not png.exists(): err("нет playbook.png")
elif png.read_bytes()[16:24] != b"\x00\x00\x01\x00\x00\x00\x01\x00": err("playbook.png должен быть 256x256")
txt = " ".join(p.read_text(encoding="utf-8", errors="ignore") for p in root.rglob("*") if p.suffix in (".yml", ".conf"))
for bad in ("Windows Defender", "WinDefend", "Hyper-V", "LxssManager"):
    if bad in txt and "не затрагиваются" not in txt: err(f"запрещённая тема: {bad}")

if errors:
    print("\n".join("ОШИБКА: " + e for e in errors)); sys.exit(1)
print("OK: playbook валиден")
