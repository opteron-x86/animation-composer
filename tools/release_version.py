"""Select a stable release version from the checkout's tags and project version."""
import argparse
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]
STABLE_VERSION = re.compile(r"(?:0|[1-9][0-9]*)(?:\.(?:0|[1-9][0-9]*)){2}")


def versions_from_tags(*args):
    tags = subprocess.check_output(["git", "tag", *args], cwd=ROOT, text=True)
    return [tuple(map(int, tag[1:].split("."))) for tag in tags.splitlines()
            if tag.startswith("v") and STABLE_VERSION.fullmatch(tag[1:])]


def release_version(next_release=False):
    base = ET.parse(ROOT / "Directory.Build.props").findtext(".//Version", "")
    if not STABLE_VERSION.fullmatch(base):
        raise ValueError("Directory.Build.props must contain a major.minor.patch version")
    if not (ROOT / ".git").exists():
        if next_release:
            raise ValueError("Choosing the next release requires a Git checkout with its tags")
        return base

    current = versions_from_tags("--points-at", "HEAD")
    if len(current) > 1:
        raise ValueError("Multiple stable release tags point to this commit")
    if current:
        return ".".join(map(str, current[0]))
    if next_release:
        previous = max(versions_from_tags("--list", "v*"), default=None)
        if previous:
            major, minor, patch = previous
            version = max(tuple(map(int, base.split("."))), (major, minor, patch + 1))
            return ".".join(map(str, version))
    return base


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--next", action="store_true", help="select the next tag for this commit")
    print(release_version(parser.parse_args().next))
