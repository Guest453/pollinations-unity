#!/usr/bin/env python3
"""Generate Unity .meta files for every file in the package (stable GUIDs, deterministic).

Unity auto-generates .meta files on first import; committing them explicitly keeps
GUIDs stable across machines (required for correct git-based upgrades and samples).
Run from the repo root:  python3 tools/generate_meta_files.py
"""
import hashlib
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCAN_DIRS = ["Runtime", "Editor", "Samples~", "Tests"]

def meta_guid(path: str) -> str:
    rel = os.path.relpath(path, ROOT).replace(os.sep, "/")
    return hashlib.md5(("pollinations-unity:" + rel).encode()).hexdigest()[:32]

def meta_for(path: str) -> str:
    rel = os.path.relpath(path, ROOT).replace(os.sep, "/")
    if rel.endswith(".cs"):
        return f"fileFormatVersion: 2\nguid: {meta_guid(path)}\nMonoImporter:\n  externalObjects: {{}}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {{instanceID: 0}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    if rel.endswith(".unity"):
        return f"fileFormatVersion: 2\nguid: {meta_guid(path)}\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    return f"fileFormatVersion: 2\nguid: {meta_guid(path)}\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"

def main():
    created = 0
    for d in SCAN_DIRS:
        base = os.path.join(ROOT, d)
        if not os.path.isdir(base):
            continue
        for dirpath, dirnames, filenames in os.walk(base):
            # never emit metas for build artifacts
            dirnames[:] = [d for d in dirnames if d not in ("bin", "obj")]
            for name in filenames:
                if name.endswith(".meta"):
                    continue
                p = os.path.join(dirpath, name)
                mp = p + ".meta"
                if not os.path.exists(mp):
                    with open(mp, "w") as f:
                        f.write(meta_for(p))
                    created += 1
            # meta for directories too
            dm = dirpath + ".meta"
            if not os.path.exists(dm):
                with open(dm, "w") as f:
                    f.write(f"fileFormatVersion: 2\nguid: {meta_guid(dirpath)}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n")
                created += 1
    print(f"created {created} meta files")

if __name__ == "__main__":
    sys.exit(main())
