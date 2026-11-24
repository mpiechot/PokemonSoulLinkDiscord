#!/usr/bin/env python3
"""Publish a prepared SoulLink Windows package without restarting the service."""

import argparse
import hashlib
import json
import os
import re
import sys
import tempfile
import zipfile
from contextlib import contextmanager
from pathlib import Path

MAXIMUM_BYTES = 512 * 1024 * 1024
VERSION_PATTERN = re.compile(r"(0|[1-9][0-9]{0,9})\.(0|[1-9][0-9]{0,9})\.(0|[1-9][0-9]{0,9})")


def version_tuple(version):
    if not isinstance(version, str) or not VERSION_PATTERN.fullmatch(version):
        raise ValueError("Invalid player release version.")
    numbers = tuple(map(int, version.split(".")))
    if any(part > 2_147_483_647 for part in numbers):
        raise ValueError("Invalid player release version.")
    return numbers


@contextmanager
def publish_lock(directory):
    import fcntl

    with (directory / ".publish.lock").open("a", encoding="ascii") as lock_file:
        fcntl.flock(lock_file, fcntl.LOCK_EX)
        yield


def read_manifest(archive):
    manifest_file = archive.getinfo("manifest.json")
    if manifest_file.file_size > 8192:
        raise ValueError("Release metadata exceeds the allowed size.")
    manifest = json.loads(archive.read(manifest_file))
    if not isinstance(manifest, dict) or manifest.get("format") != 1:
        raise ValueError("Unknown player release format.")
    version_tuple(manifest.get("version"))
    size = manifest.get("size")
    checksum = manifest.get("sha256")
    signature = manifest.get("signature")
    if type(size) is not int or not 0 < size <= MAXIMUM_BYTES or not isinstance(checksum, str) or not re.fullmatch("[0-9A-F]{64}", checksum):
        raise ValueError("Invalid player release size or checksum.")
    if not isinstance(signature, str) or not 300 <= len(signature) <= 2048:
        raise ValueError("The archive is missing a release signature.")
    return manifest


def publish(archive_path, directory):
    directory.mkdir(mode=0o755, parents=True, exist_ok=True)
    with publish_lock(directory), zipfile.ZipFile(archive_path) as archive:
        manifest = read_manifest(archive)
        filename = f"SoulLink-{manifest['version']}.exe"
        if sorted(archive.namelist()) != sorted(["manifest.json", filename]):
            raise ValueError("The archive must contain only the manifest and the versioned Windows package.")
        package = archive.getinfo(filename)
        if package.file_size != manifest["size"]:
            raise ValueError("The package size does not match the manifest.")
        current = directory / "manifest.json"
        previous = json.loads(current.read_text(encoding="utf-8")) if current.exists() else None
        if previous and version_tuple(manifest["version"]) <= version_tuple(previous["version"]):
            if previous == manifest and (directory / filename).is_file():
                return manifest["version"]
            raise ValueError("Publish a version newer than the current release.")

        with tempfile.TemporaryDirectory(prefix=".publish-", dir=directory) as stage:
            staged = Path(stage)
            checksum = hashlib.sha256()
            with archive.open(package) as source, (staged / filename).open("xb") as output:
                remaining = manifest["size"]
                while chunk := source.read(1024 * 1024):
                    remaining -= len(chunk)
                    if remaining < 0:
                        raise ValueError("The package exceeds the signed size.")
                    checksum.update(chunk)
                    output.write(chunk)
                if remaining or checksum.hexdigest().upper() != manifest["sha256"]:
                    raise ValueError("The package checksum does not match the manifest.")
                output.flush()
                os.fsync(output.fileno())
            (staged / "manifest.json").write_text(json.dumps(manifest, separators=(",", ":")), encoding="utf-8")
            os.chmod(staged / filename, 0o644)
            os.chmod(staged / "manifest.json", 0o644)
            os.replace(staged / filename, directory / filename)
            os.replace(staged / "manifest.json", current)
        return manifest["version"]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", type=Path)
    parser.add_argument("--directory", default="/var/lib/soullink/player-updates", type=Path)
    args = parser.parse_args()
    try:
        print(f"SoulLink player release {publish(args.archive, args.directory.resolve())} is available.")
    except (ValueError, OSError, KeyError, zipfile.BadZipFile) as error:
        print(str(error), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
