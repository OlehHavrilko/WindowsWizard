#!/usr/bin/env python3
"""Сборка .apbx (7z, пароль malte) из содержимого Playbook/ + bareos.exe + catalog/ (docs/BUILD.md §2).
Использование: python tools/package.py --exe path\\to\\bareos.exe [--out releases]
Требует: pip install py7zr"""
import argparse, hashlib, shutil, subprocess, sys, tempfile
import xml.etree.ElementTree as ET
from pathlib import Path
import py7zr

repo = Path(__file__).resolve().parent.parent
ap = argparse.ArgumentParser()
ap.add_argument("--exe", required=True, type=Path)
ap.add_argument("--out", type=Path, default=repo / "releases")
a = ap.parse_args()

if subprocess.call([sys.executable, str(repo / "tools" / "validate.py")]) != 0: sys.exit("валидация не пройдена")
if not a.exe.is_file(): sys.exit(f"нет {a.exe}")
version = ET.parse(repo / "Playbook" / "playbook.conf").getroot().findtext("Version")

with tempfile.TemporaryDirectory() as tmp:
    stage = Path(tmp) / "pb"
    shutil.copytree(repo / "Playbook", stage)
    shutil.copy2(a.exe, stage / "Executables" / "bareos.exe")
    shutil.copytree(repo / "catalog", stage / "Executables" / "catalog", ignore=shutil.ignore_patterns("*.json"))
    a.out.mkdir(parents=True, exist_ok=True)
    apbx = a.out / f"Bare OS-{version}.apbx"
    apbx.unlink(missing_ok=True)
    with py7zr.SevenZipFile(apbx, "w", password="malte") as z:
        for item in sorted(stage.iterdir()):
            z.writeall(item, arcname=item.name)
sha = hashlib.sha256(apbx.read_bytes()).hexdigest()
(apbx.parent / (apbx.name + ".sha256")).write_text(f"{sha} *{apbx.name}\n")
print(apbx, sha)
