#!/usr/bin/env python3
"""Inspects the packages `dotnet pack -c Release` produced and fails on anything a public release must not have.

Usage: tools/check-packages.py <directory holding the .nupkg and .snupkg files>

Checks, per package: licence expression, copyright, description, tags, repository metadata, an embedded README that
is not empty, XML documentation next to the assembly, a symbol package, and dependencies limited to the family's own
packages (and none at all for the first package). Also checks that all three packages share one version.
"""
import re
import sys
import zipfile
from pathlib import Path

PREFIX = "RecursiveAI"
PACKAGES = {
    f"{PREFIX}.OneHtml": set(),
    f"{PREFIX}.OneHtml.Accessibility": {f"{PREFIX}.OneHtml"},
    f"{PREFIX}.OneHtml.Accessibility.Validation": {f"{PREFIX}.OneHtml.Accessibility"},
}
REPO = "https://github.com/recursive-ai-oss/onehtml-csharp"


def tag(nuspec: str, name: str) -> str | None:
    m = re.search(rf"<{name}(?:\s[^>]*)?>(.*?)</{name}>", nuspec, re.S)
    return m.group(1).strip() if m else None


def main() -> int:
    folder = Path(sys.argv[1])
    problems: list[str] = []
    versions = set()
    for package, allowed in PACKAGES.items():
        found = sorted(folder.glob(f"{package}.[0-9]*.nupkg"))
        # "RecursiveAI.OneHtml.1.0.0" must not match "RecursiveAI.OneHtml.Accessibility.1.0.0" and vice versa.
        found = [f for f in found if re.fullmatch(rf"{re.escape(package)}\.\d.*\.nupkg", f.name)]
        if len(found) != 1:
            problems.append(f"{package}: expected one .nupkg, found {len(found)}")
            continue
        nupkg = found[0]
        symbols = nupkg.with_suffix(".snupkg")
        if not symbols.exists():
            problems.append(f"{package}: no .snupkg next to {nupkg.name}")
        with zipfile.ZipFile(nupkg) as z:
            names = z.namelist()
            specs = [n for n in names if n.endswith(".nuspec")]
            if len(specs) != 1:
                problems.append(f"{package}: expected one nuspec")
                continue
            nuspec = z.read(specs[0]).decode("utf-8")
            readme_name = tag(nuspec, "readme")
            readme = z.read(readme_name).decode("utf-8") if readme_name in names else ""
            dll = f"lib/net10.0/{package}.dll"
            xml = f"lib/net10.0/{package}.xml"
            has_dll, has_xml = dll in names, xml in names
            xml_size = z.getinfo(xml).file_size if has_xml else 0
            notices = "THIRD-PARTY-NOTICES.md" in names

        version = tag(nuspec, "version")
        versions.add(version)
        checks = {
            "id": tag(nuspec, "id") == package,
            "license expression 'MIT OR Apache-2.0'": re.search(r'<license type="expression">MIT OR Apache-2\.0</license>', nuspec) is not None,
            "copyright names Recursive AI, LLC": "Recursive AI, LLC" in (tag(nuspec, "copyright") or ""),
            "authors": "Recursive AI, LLC" in (tag(nuspec, "authors") or ""),
            "description": len(tag(nuspec, "description") or "") > 40,
            "tags": len((tag(nuspec, "tags") or "").split()) >= 3,
            "project url": tag(nuspec, "projectUrl") == REPO,
            "repository url": re.search(rf'<repository [^>]*url="{re.escape(REPO)}"', nuspec) is not None,
            "repository type git": re.search(r'<repository [^>]*type="git"', nuspec) is not None,
            "repository commit": re.search(r'<repository [^>]*commit="[0-9a-f]{40}"', nuspec) is not None
            or "--allow-no-commit" in sys.argv,
            "embedded readme": readme_name == "README.md" and f"# {package}" in readme,
            "assembly": has_dll,
            "xml documentation": has_xml and xml_size > 1000,
            "third-party notices": notices,
            "version is a pre-release or release": re.fullmatch(r"\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?", version or "") is not None,
        }
        for name, ok in checks.items():
            if not ok:
                problems.append(f"{package}: {name}")

        deps = set(re.findall(r'<dependency id="([^"]+)"', nuspec))
        if deps != allowed:
            problems.append(f"{package}: dependencies {sorted(deps)}, expected {sorted(allowed)}")
        for dep_version in re.findall(r'<dependency id="[^"]+" version="([^"]+)"', nuspec):
            if not dep_version.startswith(f"[{version}") and not dep_version.startswith(version or "?"):
                problems.append(f"{package}: dependency version {dep_version} differs from {version}")
        print(f"{nupkg.name}: {'ok' if not [p for p in problems if p.startswith(package + ':')] else 'PROBLEMS'}")

    if len(versions) > 1:
        problems.append(f"packages have different versions: {sorted(v for v in versions if v)}")
    for p in problems:
        print("FAIL:", p)
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
