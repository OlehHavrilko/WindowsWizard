#!/usr/bin/env python3
"""Сборка .apbx из содержимого Playbook/ + bareos.exe + catalog/ (docs/BUILD.md §2).

Формат взят у рабочего эталона (AtlasOS, tools/build/AtlasBuild: `7z a -tzip -mx1 -pmalte`):
ZIP, шифрование ZipCrypto (штатное для zip в 7-Zip), пароль `malte`, deflate, пути через '/',
файлы лежат в корне архива (playbook.conf, Configuration/, Executables/). Формат 7z AME Beta 0.8.4 не открыл.
Без внешних зависимостей: ZipCrypto пишется вручную.

Использование: python tools/package.py --exe path/to/bareos.exe --toolbox path/to/BareOS-Toolbox.exe [--out releases]
"""
import argparse, hashlib, os, shutil, struct, subprocess, sys, tempfile, time, zlib
import xml.etree.ElementTree as ET
from pathlib import Path

PASSWORD = b"malte"

_CRC = [0] * 256
for _i in range(256):
    _c = _i
    for _ in range(8):
        _c = (_c >> 1) ^ 0xEDB88320 if _c & 1 else _c >> 1
    _CRC[_i] = _c


def _crc32(crc, b):
    return (_CRC[(crc ^ b) & 0xFF] ^ (crc >> 8)) & 0xFFFFFFFF


class ZipCrypto:
    def __init__(self, password):
        self.k = [0x12345678, 0x23456789, 0x34567890]
        for ch in password:
            self._update(ch)

    def _update(self, c):
        k = self.k
        k[0] = _crc32(k[0], c)
        k[1] = (k[1] + (k[0] & 0xFF)) & 0xFFFFFFFF
        k[1] = (k[1] * 134775813 + 1) & 0xFFFFFFFF
        k[2] = _crc32(k[2], k[1] >> 24)

    def encrypt(self, data):
        out = bytearray(len(data))
        for i, p in enumerate(data):
            t = (self.k[2] | 2) & 0xFFFF
            out[i] = p ^ (((t * (t ^ 1)) >> 8) & 0xFF)
            self._update(p)
        return bytes(out)


def write_zip(dest, files):
    """files: список (имя_в_архиве, путь_на_диске)."""
    now = time.localtime()
    dos_time = (now.tm_hour << 11) | (now.tm_min << 5) | (now.tm_sec // 2)
    dos_date = ((now.tm_year - 1980) << 9) | (now.tm_mon << 5) | now.tm_mday
    central, offset = [], 0
    with open(dest, "wb") as out:
        for name, path in files:
            raw = Path(path).read_bytes()
            crc = zlib.crc32(raw) & 0xFFFFFFFF
            co = zlib.compressobj(1, zlib.DEFLATED, -15)
            comp = co.compress(raw) + co.flush()
            header = os.urandom(11) + bytes([crc >> 24])
            enc = ZipCrypto(PASSWORD).encrypt(header + comp)
            nb = name.encode("utf-8")
            flags = 0x0001 | 0x0800                     # шифрование + UTF-8 имена
            local = struct.pack("<IHHHHHIIIHH", 0x04034B50, 20, flags, 8, dos_time, dos_date,
                                crc, len(enc), len(raw), len(nb), 0) + nb
            out.write(local)
            out.write(enc)
            central.append(struct.pack("<IHHHHHHIIIHHHHHII", 0x02014B50, 20, 20, flags, 8, dos_time, dos_date,
                                       crc, len(enc), len(raw), len(nb), 0, 0, 0, 0, 0x20, offset) + nb)
            offset += len(local) + len(enc)
        cd_start = out.tell()
        for c in central:
            out.write(c)
        cd_size = out.tell() - cd_start
        out.write(struct.pack("<IHHHHIIH", 0x06054B50, 0, 0, len(central), len(central), cd_size, cd_start, 0))


def main():
    repo = Path(__file__).resolve().parent.parent
    ap = argparse.ArgumentParser()
    ap.add_argument("--exe", required=True, type=Path)
    ap.add_argument("--toolbox", required=True, type=Path)
    ap.add_argument("--out", type=Path, default=repo / "releases")
    a = ap.parse_args()

    if subprocess.call([sys.executable, str(repo / "tools" / "validate.py")]) != 0:
        sys.exit("валидация не пройдена")
    for f in (a.exe, a.toolbox):
        if not f.is_file():
            sys.exit(f"нет {f}")
    version = ET.parse(repo / "Playbook" / "playbook.conf").getroot().findtext("Version")

    with tempfile.TemporaryDirectory() as tmp:
        stage = Path(tmp) / "pb"
        shutil.copytree(repo / "Playbook", stage)
        shutil.copy2(a.exe, stage / "Executables" / "bareos.exe")
        shutil.copy2(a.toolbox, stage / "Executables" / "BareOS-Toolbox.exe")
        shutil.copytree(repo / "catalog", stage / "Executables" / "catalog", ignore=shutil.ignore_patterns("*.json"))
        files = sorted((p.relative_to(stage).as_posix(), p) for p in stage.rglob("*") if p.is_file())
        a.out.mkdir(parents=True, exist_ok=True)
        apbx = a.out / f"Bare OS-{version}.apbx"
        apbx.unlink(missing_ok=True)
        write_zip(apbx, files)
    sha = hashlib.sha256(apbx.read_bytes()).hexdigest()
    (apbx.parent / (apbx.name + ".sha256")).write_text(f"{sha} *{apbx.name}\n")
    print(apbx, sha)


if __name__ == "__main__":
    main()
