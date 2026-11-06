#!/usr/bin/env python3
"""Package already-published builds and their corresponding source; no game assets."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tarfile
import zipfile
from release_version import release_version, STABLE_VERSION

root = Path(__file__).resolve().parent.parent
parser = argparse.ArgumentParser()
parser.add_argument("destination", type=Path)
parser.add_argument("--version", help="version passed to dotnet publish")
args = parser.parse_args()
version = args.version or release_version()
if not STABLE_VERSION.fullmatch(version):
    parser.error("expected a major.minor.patch version")
out = args.destination.resolve()
out.mkdir(parents=True, exist_ok=True)

# Include the notices shipped in NuGet packages, plus upstream notices in docs.
assets = json.loads((root / "src/MovesetForge.Desktop/obj/project.assets.json").read_text())
cache_roots = [Path(p) for p in assets["packageFolders"]]
packages = {info["path"] for info in assets["libraries"].values() if info["type"] == "package"}
for rid in ("linux-x64", "win-x64"):
    deps = json.loads((root / "dist" / rid / "AnimationComposer.deps.json").read_bytes())["libraries"]
    if f"AnimationComposer/{version}" not in deps:
        raise ValueError(f"{rid} was not built at version {version}")
    packages.update(name.removeprefix("runtimepack.").lower() for name in deps if name.startswith("runtimepack."))
notice_root = root / "docs/third-party/nuget"
notice_root.mkdir(parents=True, exist_ok=True)
index = []
for package in sorted(packages):
    cached = next((c / package for c in cache_roots if (c / package).is_dir()), None)
    if cached is None:
        raise RuntimeError(f"Missing package cache: {package}")
    target = notice_root / package.replace("/", "-")
    target.mkdir(parents=True, exist_ok=True)
    for spec in cached.glob("*.nuspec"):
        shutil.copy2(spec, target)
    for file in cached.rglob("*"):
        if file.is_file() and any(s in file.name.lower() for s in ("license", "notice", "copying")):
            dest = target / file.relative_to(cached)
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(file, dest)
    index.append(package)
(notice_root / "packages.json").write_text(json.dumps(index, indent=2) + "\n")

archives = []
for rid in ("linux-x64", "win-x64"):
    build = root / "dist" / rid
    if not (build / ("AnimationComposer.exe" if rid.startswith("win") else "AnimationComposer")).exists():
        raise RuntimeError(f"Publish {rid} before packaging.")
    for file in ("README.md", "LICENSE", "THIRD_PARTY_NOTICES.md"):
        shutil.copy2(root / file, build / file)
    shutil.copytree(root / "docs", build / "docs", dirs_exist_ok=True)
    for path, label in (("vendor/HKX2Library/LICENSE", "HKX2-LICENSE"), ("vendor/SageAnimation/LICENSE", "Sage-LICENSE")):
        shutil.copy2(root / path, build / "docs/third-party" / label)
    name = f"AnimationComposer-{version}-{rid}"
    if rid.startswith("linux"):
        archive = out / (name + ".tar.gz")
        with tarfile.open(archive, "w:gz") as tar:
            tar.add(build, arcname=name)
    else:
        archive = out / (name + ".zip")
        with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as z:
            for file in sorted(build.rglob("*")):
                if file.is_file():
                    z.write(file, str(Path(name) / file.relative_to(build)))
    archives.append(archive)

source = out / f"AnimationComposer-{version}-source.zip"
if (root / ".git").exists():
    tracked = subprocess.check_output(["git", "ls-files", "-z"], cwd=root)
    sources = {root / os.fsdecode(name) for name in tracked.split(b"\0") if name}
    sources.update(notice_root.rglob("*"))
else:
    sources = root.rglob("*")
with zipfile.ZipFile(source, "w", zipfile.ZIP_DEFLATED) as z:
    for file in sorted(sources):
        relative = file.relative_to(root)
        if not file.is_file() or any(p in {"bin", "obj", "dist", ".git", ".cache", "__pycache__"} for p in relative.parts):
            continue
        if file in archives or file == source or (out != root and out in file.parents and root in out.parents):
            continue
        if file.suffix.lower() in {".hkx", ".nif", ".dds", ".bsa", ".esp", ".esm", ".esl"}:
            raise RuntimeError(f"Unexpected game asset in source: {relative}")
        member = str(Path("animation-composer") / relative)
        if relative == Path("Directory.Build.props"):
            props, count = re.subn(rb"<Version>[^<]+</Version>", f"<Version>{version}</Version>".encode(), file.read_bytes())
            if count != 1:
                raise ValueError("Expected one Version in Directory.Build.props")
            z.writestr(member, props)
        else:
            z.write(file, member)
archives.append(source)
(out / "SHA256SUMS.txt").write_text("".join(f"{hashlib.sha256(p.read_bytes()).hexdigest()}  {p.name}\n" for p in archives))
for file in archives:
    print(f"{file.name}: {file.stat().st_size / 1024 / 1024:.1f} MiB")
