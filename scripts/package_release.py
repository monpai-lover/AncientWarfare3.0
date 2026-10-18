#!/usr/bin/env python3
"""Package the NeoModLoader source release using the v1.2.7g layout."""

import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path, PurePosixPath
import subprocess
import time
import zipfile


RUNTIME_DIRECTORIES = {
    "Code", "Assemblies", "ABPackages", "EmbededResources", "GameResources",
    "Locales", "name_generators", "word_libraries", "THIRD_PARTY_NOTICES",
}
ROOT_FILES = {
    "AncientWarfare3.csproj", "default_config.json", "icon.png", "mod.json",
    "NEW_CONTENT_2026-08-21.md", "README.md", "sponsor_qr.jpg", "supporters.csv",
    "THIRD_PARTY_NOTICES.md",
}
PACKAGE_ROOT = "AncientWarfare3.0"


def git(root, *arguments):
    return subprocess.check_output(["git", "-C", str(root), *arguments])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--tag", required=True)
    parser.add_argument("--output", type=Path, default=Path("release"))
    parser.add_argument("--previous", type=Path,
                        help="Optional previous release ZIP for runtime-file comparison")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    metadata = json.loads((root / "mod.json").read_text(encoding="utf-8"))
    if args.tag != "v" + metadata["version"]:
        raise ValueError("Release tag must match mod.json version")
    if git(root, "status", "--porcelain", "--untracked-files=no").strip():
        raise ValueError("Commit tracked changes before creating a release package")

    commit = git(root, "rev-parse", "HEAD").decode().strip()
    object_format = git(root, "rev-parse", "--show-object-format").decode().strip()
    timestamp = int(git(root, "show", "-s", "--format=%ct", "HEAD"))
    date_time = time.gmtime(max(timestamp, 315532800))[:6]
    root_files = ROOT_FILES | {f"CHANGELOG-{args.tag}.md"}
    tracked = {}
    for entry in git(root, "ls-tree", "-r", "-z", "HEAD").split(b"\0"):
        if not entry:
            continue
        properties, raw_name = entry.split(b"\t", 1)
        mode, kind, object_id = properties.decode().split()
        name = raw_name.decode("utf-8")
        parts = PurePosixPath(name).parts
        include = (name in root_files or parts[0] in RUNTIME_DIRECTORIES or
                   name.startswith("docs/api/") and name.endswith(".md"))
        if not include:
            continue
        if kind != "blob" or mode not in {"100644", "100755"}:
            raise ValueError(f"Unsupported release entry: {name}")
        if any(part in {"bin", "obj", ".git", "Tests"} for part in parts):
            raise ValueError(f"Development output in release: {name}")
        if name.lower().endswith((".pdb", ".trx")) or parts[-1] == "AncientWarfare3.dll":
            raise ValueError(f"Build output in source release: {name}")
        tracked[name] = object_id
    if not root_files.issubset(tracked):
        raise ValueError(f"Missing root files: {sorted(root_files - tracked.keys())}")
    for directory in RUNTIME_DIRECTORIES:
        if not any(name.startswith(directory + "/") for name in tracked):
            raise ValueError(f"Missing runtime directory: {directory}")

    if args.previous:
        with zipfile.ZipFile(args.previous) as previous:
            old_runtime = set()
            for entry in previous.infolist():
                name = entry.filename.replace("\\", "/")
                if name.endswith("/"):
                    continue
                parts = PurePosixPath(name).parts
                if len(parts) > 2 and parts[0] == PACKAGE_ROOT and parts[1] in RUNTIME_DIRECTORIES:
                    old_runtime.add("/".join(parts[1:]))
            missing = old_runtime - tracked.keys()
            if missing:
                raise ValueError(f"Previous runtime files missing: {sorted(missing)}")

    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    archive = output / f"AncientWarfare3-{args.tag}.zip"
    manifest = {}
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as package:
        for name, object_id in sorted(tracked.items()):
            path = root / name
            if path.is_symlink() or not path.resolve().is_relative_to(root):
                raise ValueError(f"Unsafe release path: {name}")
            data = path.read_bytes()
            blob = f"blob {len(data)}\0".encode() + data
            if hashlib.new(object_format, blob).hexdigest() != object_id:
                raise ValueError(f"File differs from release commit: {name}")
            info = zipfile.ZipInfo(f"{PACKAGE_ROOT}/{name}", date_time)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.create_system = 3
            info.external_attr = 0o100644 << 16
            package.writestr(info, data, compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)
            manifest[name] = hashlib.sha256(data).hexdigest()

    with zipfile.ZipFile(archive) as package:
        if package.testzip() is not None:
            raise ValueError("ZIP integrity check failed")
        if set(package.namelist()) != {f"{PACKAGE_ROOT}/{name}" for name in tracked}:
            raise ValueError("ZIP file list differs from the release manifest")
        for name, digest in manifest.items():
            if hashlib.sha256(package.read(f"{PACKAGE_ROOT}/{name}")).hexdigest() != digest:
                raise ValueError(f"ZIP content verification failed: {name}")

    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    (output / f"{archive.name}.sha256").write_text(f"{digest}  {archive.name}\n")
    report = {"tag": args.tag, "commit": commit, "archive": archive.name,
              "sha256": digest, "files": len(manifest),
              "directories": dict(Counter(name.split("/")[0] for name in manifest)),
              "manifest": manifest}
    (output / "package-manifest.json").write_text(
        json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    notes = (root / f"CHANGELOG-{args.tag}.md").read_text(encoding="utf-8")
    (output / "RELEASE-NOTES.md").write_text(
        notes + f"\n发布提交：`{commit}`\n\n附件 SHA-256：`{digest}`\n", encoding="utf-8")
    print(json.dumps({key: value for key, value in report.items() if key != "manifest"},
                     ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
