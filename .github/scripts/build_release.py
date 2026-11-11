"""Build the release files for one version of AUI.

Writes into dist/:
  rest.armagan.aui-<version>.zip           the VPM package: package.json at the zip root
  rest.armagan.aui-<version>.unitypackage  the same files under Packages/rest.armagan.aui/
  package.json                             package.json plus "url" and "zipSHA256", the entry
                                           the VPM listing is built from (see build_listing.py)

The file list is `git ls-files`, so nothing untracked or ignored ships. Left out: tools~ (the
docs site, the language server and the VS Code extension sources; Unity ignores the folder) and
the repo's own dotfiles and CLAUDE.md.

Usage: python .github/scripts/build_release.py <repo-owner/name> [expected-version]
"""

import gzip
import hashlib
import io
import json
import os
import subprocess
import sys
import tarfile
import zipfile

PACKAGE_ID = "rest.armagan.aui"
EXCLUDED_DIRS = ("tools~/", ".github/", ".claude/")
EXCLUDED_FILES = {"CLAUDE.md", "CLAUDE.md.meta", ".editorconfig", ".gitattributes", ".gitignore", ".mcp.json"}


def tracked_files():
    out = subprocess.run(["git", "ls-files", "-z"], check=True, capture_output=True).stdout
    files = [f for f in out.decode("utf-8").split("\0") if f]
    keep = []
    for f in files:
        if f.startswith(EXCLUDED_DIRS) or f in EXCLUDED_FILES:
            continue
        if not os.path.isfile(f):
            continue  # deleted in the working tree but still in the index
        keep.append(f)
    return sorted(keep)


def build_zip(files, path):
    # Fixed timestamps so the same tree always gives the same zip (and the same SHA-256).
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
        for f in files:
            info = zipfile.ZipInfo(f, date_time=(2020, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            with open(f, "rb") as src:
                z.writestr(info, src.read())


def meta_guid(meta_path):
    with open(meta_path, "r", encoding="utf-8") as m:
        for line in m:
            if line.startswith("guid:"):
                return line.split(":", 1)[1].strip()
    return None


def build_unitypackage(files, path):
    """A .unitypackage is a gzipped tar of <guid>/{asset, asset.meta, pathname} entries.

    Folders need entries too (their own .meta carries their guid). Files under Samples~ have no
    .meta (Unity never imports a ~ folder), so they get a guid derived from their path and a
    minimal .meta; that keeps the guid stable from one release to the next.
    """
    entries = {}  # guid -> (rel path, is_dir, meta bytes)
    file_set = set(files)

    def add(rel, is_dir):
        meta = rel + ".meta"
        if meta in file_set:
            guid = meta_guid(meta)
            with open(meta, "rb") as m:
                meta_bytes = m.read()
        else:
            guid = hashlib.md5((PACKAGE_ID + "/" + rel).encode("utf-8")).hexdigest()
            body = "fileFormatVersion: 2\nguid: " + guid + "\n"
            if is_dir:
                body += "folderAsset: yes\n"
            meta_bytes = body.encode("utf-8")
        if guid and guid not in entries:
            entries[guid] = (rel, is_dir, meta_bytes)

    dirs = set()
    for f in files:
        if f.endswith(".meta"):
            continue
        parts = f.split("/")
        for i in range(1, len(parts)):
            dirs.add("/".join(parts[:i]))
    for d in sorted(dirs):
        add(d, True)
    for f in files:
        if not f.endswith(".meta"):
            add(f, False)

    raw = io.BytesIO()
    with tarfile.open(fileobj=raw, mode="w", format=tarfile.GNU_FORMAT) as tar:
        def put(name, data):
            info = tarfile.TarInfo(name)
            info.size = len(data)
            info.mtime = 1577836800
            info.mode = 0o644
            tar.addfile(info, io.BytesIO(data))

        for guid in sorted(entries):
            rel, is_dir, meta_bytes = entries[guid]
            put(guid + "/pathname", ("Packages/" + PACKAGE_ID + "/" + rel).encode("utf-8"))
            put(guid + "/asset.meta", meta_bytes)
            if not is_dir:
                with open(rel, "rb") as src:
                    put(guid + "/asset", src.read())
    with open(path, "wb") as out:
        out.write(gzip.compress(raw.getvalue(), mtime=0))


def main():
    if len(sys.argv) < 2:
        sys.exit("usage: build_release.py <owner/repo> [expected-version]")
    repo = sys.argv[1]
    with open("package.json", "r", encoding="utf-8") as p:
        manifest = json.load(p)
    version = manifest["version"]
    if len(sys.argv) > 2 and sys.argv[2].lstrip("v") != version:
        sys.exit("tag " + sys.argv[2] + " does not match package.json version " + version)

    os.makedirs("dist", exist_ok=True)
    files = tracked_files()
    zip_name = PACKAGE_ID + "-" + version + ".zip"
    zip_path = os.path.join("dist", zip_name)
    build_zip(files, zip_path)
    build_unitypackage(files, os.path.join("dist", PACKAGE_ID + "-" + version + ".unitypackage"))

    with open(zip_path, "rb") as z:
        sha = hashlib.sha256(z.read()).hexdigest()
    entry = dict(manifest)
    entry["url"] = "https://github.com/" + repo + "/releases/download/v" + version + "/" + zip_name
    entry["zipSHA256"] = sha
    with open(os.path.join("dist", "package.json"), "w", encoding="utf-8", newline="\n") as out:
        json.dump(entry, out, indent=2)
        out.write("\n")
    print("version " + version + ": " + str(len(files)) + " files, zip sha256 " + sha)


if __name__ == "__main__":
    main()
