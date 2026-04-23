#!/usr/bin/env python3
"""
i18n parity check for Finances.App client locales.

Exits non-zero if any locale JSON file is missing keys present in another.
Run from the repository root:

    python3 scripts/check-i18n-parity.py

Also verifies every locale is valid JSON.
"""

from __future__ import annotations

import json
import pathlib
import sys
from typing import Dict, Set


LOCALES_DIR = pathlib.Path("Finances.App.Client/wwwroot/locales")


def load_locale(path: pathlib.Path) -> Dict[str, object]:
    try:
        with path.open("r", encoding="utf-8") as fh:
            data = json.load(fh)
    except json.JSONDecodeError as err:
        print(f"[i18n] INVALID JSON in {path}: {err}", file=sys.stderr)
        sys.exit(1)

    if not isinstance(data, dict):
        print(f"[i18n] Locale {path} must be a JSON object at the root.", file=sys.stderr)
        sys.exit(1)

    return data


def main() -> int:
    if not LOCALES_DIR.is_dir():
        print(f"[i18n] Locales directory not found: {LOCALES_DIR}", file=sys.stderr)
        return 1

    locale_files = sorted(LOCALES_DIR.glob("*.json"))
    if len(locale_files) < 2:
        print(f"[i18n] Need at least two locale files to compare; found {len(locale_files)}.")
        return 0

    locales: Dict[str, Set[str]] = {}
    for path in locale_files:
        data = load_locale(path)
        locales[path.name] = set(data.keys())

    baseline_name, baseline_keys = next(iter(locales.items()))
    has_errors = False

    for name, keys in locales.items():
        if name == baseline_name:
            continue
        missing_in_current = sorted(baseline_keys - keys)
        missing_in_baseline = sorted(keys - baseline_keys)

        if missing_in_current:
            has_errors = True
            print(f"[i18n] {name} is missing {len(missing_in_current)} keys present in {baseline_name}:",
                  file=sys.stderr)
            for key in missing_in_current:
                print(f"    - {key}", file=sys.stderr)

        if missing_in_baseline:
            has_errors = True
            print(f"[i18n] {baseline_name} is missing {len(missing_in_baseline)} keys present in {name}:",
                  file=sys.stderr)
            for key in missing_in_baseline:
                print(f"    - {key}", file=sys.stderr)

    if has_errors:
        print("[i18n] Locale parity check FAILED.", file=sys.stderr)
        return 2

    key_count = len(baseline_keys)
    print(f"[i18n] OK — {len(locales)} locales in sync with {key_count} keys each.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
