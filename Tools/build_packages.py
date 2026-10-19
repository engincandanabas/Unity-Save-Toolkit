"""Builds release artifacts for Save Toolkit.

    python Tools/build_packages.py

Outputs into Builds/:
  com.engincandanabas.savetoolkit-<version>.tgz   UPM tarball (Package Manager → Add package from tarball)
  SaveToolkit-<version>.unitypackage              Classic package, imported under Assets/SaveToolkit

The .unitypackage reuses the GUIDs from the package's .meta files, so scenes
and prefabs that reference the toolkit keep working across both formats.
"""

import gzip
import io
import json
import os
import tarfile
import uuid

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PACKAGE_DIR = os.path.join(ROOT, "Packages", "com.engincandanabas.savetoolkit")
OUTPUT_DIR = os.path.join(ROOT, "Builds")

UNITYPACKAGE_ROOT = "Assets/SaveToolkit"
# Package folder -> path inside the .unitypackage (None = skip).
UNITYPACKAGE_MAP = {
    "Runtime": "Runtime",
    "Editor": "Editor",
    "Samples~/Basic": "Samples/Basic",
    "README.md": "README.md",
    "CHANGELOG.md": "CHANGELOG.md",
    "LICENSE.md": "LICENSE.md",
}

FOLDER_META = (
    "fileFormatVersion: 2\nguid: {guid}\nfolderAsset: yes\nDefaultImporter:\n"
    "  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
)


def read_version():
    with open(os.path.join(PACKAGE_DIR, "package.json"), encoding="utf-8") as f:
        return json.load(f)["version"]


def add_bytes(tar, name, data, mode=0o644):
    info = tarfile.TarInfo(name)
    info.size = len(data)
    info.mode = mode
    info.mtime = 0
    tar.addfile(info, io.BytesIO(data))


def build_tarball(version):
    """npm-pack layout: every file under package/, including .meta files."""
    path = os.path.join(OUTPUT_DIR, f"com.engincandanabas.savetoolkit-{version}.tgz")

    with tarfile.open(path, "w:gz") as tar:
        for dirpath, dirnames, filenames in os.walk(PACKAGE_DIR):
            dirnames[:] = sorted(d for d in dirnames if not d.startswith("."))
            for name in sorted(filenames):
                if name.startswith("."):
                    continue
                full = os.path.join(dirpath, name)
                rel = os.path.relpath(full, PACKAGE_DIR).replace(os.sep, "/")
                with open(full, "rb") as f:
                    add_bytes(tar, "package/" + rel, f.read())

    return path


def read_guid(meta_path):
    with open(meta_path, encoding="utf-8") as f:
        for line in f:
            if line.startswith("guid:"):
                return line.split(":", 1)[1].strip()
    raise ValueError(f"No guid in {meta_path}")


def unitypackage_entries():
    """Yields (unity_path, source_file_or_None, meta_text)."""
    seen_folders = set()

    def folder(unity_path, source_dir):
        if unity_path in seen_folders:
            return []
        seen_folders.add(unity_path)
        meta = source_dir + ".meta" if source_dir else None
        if meta and os.path.exists(meta):
            with open(meta, encoding="utf-8") as f:
                text = f.read()
        else:
            # Folders such as Samples~ have no meta; derive a stable GUID.
            guid = uuid.uuid5(uuid.NAMESPACE_URL, "savetoolkit:" + unity_path).hex
            text = FOLDER_META.format(guid=guid)
        return [(unity_path, None, text)]

    entries = []
    entries += folder(UNITYPACKAGE_ROOT, None)

    for source, target in UNITYPACKAGE_MAP.items():
        source_path = os.path.join(PACKAGE_DIR, source.replace("/", os.sep))
        target_path = f"{UNITYPACKAGE_ROOT}/{target}"

        # Parent folders of the target (e.g. Assets/SaveToolkit/Samples).
        parts = target.split("/")[:-1]
        for i in range(len(parts)):
            entries += folder(f"{UNITYPACKAGE_ROOT}/{'/'.join(parts[:i + 1])}", None)

        if os.path.isfile(source_path):
            with open(source_path + ".meta", encoding="utf-8") as f:
                entries.append((target_path, source_path, f.read()))
            continue

        entries += folder(target_path, source_path if not source.endswith("~/Basic") else None)

        for dirpath, dirnames, filenames in os.walk(source_path):
            dirnames[:] = sorted(d for d in dirnames if not d.startswith(".") and not d.endswith("~"))
            rel_dir = os.path.relpath(dirpath, source_path).replace(os.sep, "/")
            unity_dir = target_path if rel_dir == "." else f"{target_path}/{rel_dir}"

            for d in dirnames:
                entries += folder(f"{unity_dir}/{d}", os.path.join(dirpath, d))

            for name in sorted(filenames):
                if name.endswith(".meta") or name.startswith("."):
                    continue
                full = os.path.join(dirpath, name)
                with open(full + ".meta", encoding="utf-8") as f:
                    entries.append((f"{unity_dir}/{name}", full, f.read()))

    return entries


def build_unitypackage(version):
    path = os.path.join(OUTPUT_DIR, f"SaveToolkit-{version}.unitypackage")
    buffer = io.BytesIO()
    guids = set()

    with tarfile.open(fileobj=buffer, mode="w") as tar:
        for unity_path, source, meta in unitypackage_entries():
            guid = next(l.split(":", 1)[1].strip() for l in meta.splitlines() if l.startswith("guid:"))
            if guid in guids:
                raise ValueError(f"Duplicate GUID {guid} at {unity_path}")
            guids.add(guid)

            add_bytes(tar, f"{guid}/pathname", unity_path.encode("utf-8"))
            add_bytes(tar, f"{guid}/asset.meta", meta.encode("utf-8"))
            if source:
                with open(source, "rb") as f:
                    add_bytes(tar, f"{guid}/asset", f.read())

    with open(path, "wb") as f:
        f.write(gzip.compress(buffer.getvalue(), mtime=0))

    return path, len(guids)


def main():
    os.makedirs(OUTPUT_DIR, exist_ok=True)
    version = read_version()

    tgz = build_tarball(version)
    print(f"UPM tarball:   {tgz} ({os.path.getsize(tgz):,} bytes)")

    unitypackage, count = build_unitypackage(version)
    print(f"unitypackage:  {unitypackage} ({os.path.getsize(unitypackage):,} bytes, {count} assets)")


if __name__ == "__main__":
    main()
