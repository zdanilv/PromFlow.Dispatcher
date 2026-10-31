"""Check local Markdown link targets without network access or third-party packages."""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path
from urllib.parse import unquote, urlsplit


LINK = re.compile(r"!?\[[^\]\n]*\]\(")
REFERENCE = re.compile(r"!?\[([^\]\n]*)\]\[([^\]\n]*)\]")
DEFINITION = re.compile(r"^ {0,3}\[([^\]]+)\]:\s*(?:<([^>]+)>|(\S+))(?:\s+.*)?$")
TITLE = re.compile(r"\s+(?:\"[^\"]*\"|'[^']*'|\([^)]*\))$")
FENCE = re.compile(r"^ {0,3}(`{3,}|~{3,})")
SCHEME = re.compile(r"^[A-Za-z][A-Za-z0-9+.-]*:")
INLINE_CODE = re.compile(r"(`+)(.*?)\1")


def documentation_files(root: Path) -> list[Path]:
    required = ("README.md", "AGENTS.md", "AgentDocs/ru", "AgentDocs/en", "AgentDocs/source/ru")
    missing = [name for name in required if not (root / name).exists()]
    if missing:
        raise ValueError(f"required documentation paths missing: {', '.join(missing)}")
    files = [root / "README.md", root / "AGENTS.md"]
    for folder in ("AgentDocs/ru", "AgentDocs/en", "AgentDocs/source/ru", "docs/plans"):
        files.extend(sorted((root / folder).rglob("*.md")))
    return [path for path in files if path.is_file()]


def visible_lines(content: str) -> list[str]:
    result = []
    fence_char = ""
    fence_size = 0
    comment = False
    for line in content.splitlines():
        marker = FENCE.match(line)
        if marker:
            token = marker.group(1)
            if not fence_char:
                fence_char, fence_size = token[0], len(token)
                result.append("")
                continue
            if token[0] == fence_char and len(token) >= fence_size:
                fence_char = ""
                result.append("")
                continue
        if fence_char:
            result.append("")
            continue
        # Keep surrounding prose and its original line number.
        cleaned = ""
        pos = 0
        while pos < len(line):
            if comment:
                end = line.find("-->", pos)
                if end < 0:
                    break
                pos, comment = end + 3, False
            else:
                start = line.find("<!--", pos)
                if start < 0:
                    cleaned += line[pos:]
                    break
                cleaned += line[pos:start]
                pos, comment = start + 4, True
        result.append(INLINE_CODE.sub("", cleaned))
    return result


def inline_targets(line: str):
    for match in LINK.finditer(line):
        start = match.end()
        depth = 1
        end = start
        while end < len(line) and depth:
            if line[end] == "\\":
                end += 2
                continue
            if line[end] == "(":
                depth += 1
            elif line[end] == ")":
                depth -= 1
            end += 1
        if depth:
            continue  # Multiline links are outside this small checker's scope.
        raw = line[start : end - 1].strip()
        if raw.startswith("<") and ">" in raw:
            target = raw[1 : raw.index(">")]
        else:
            target = TITLE.sub("", raw)
        yield target, line[match.start()] == "!"


def reference_key(label: str) -> str:
    return " ".join(label.split()).casefold()


def check_files(root: Path, files: list[Path]) -> tuple[dict[str, int], list[str]]:
    counts = {"files": 0, "local": 0, "images": 0, "external": 0, "other": 0, "anchors": 0}
    errors: list[str] = []
    for source in files:
        lines = visible_lines(source.read_text(encoding="utf-8-sig"))
        counts["files"] += 1
        definitions = {}
        for line in lines:
            match = DEFINITION.match(line)
            if match:
                definitions[reference_key(match.group(1))] = match.group(2) or match.group(3)
        for number, line in enumerate(lines, 1):
            if DEFINITION.match(line):
                continue
            targets = list(inline_targets(line))
            for match in REFERENCE.finditer(line):
                label = match.group(2) or match.group(1)
                target = definitions.get(reference_key(label))
                if target is not None:
                    targets.append((target, line[match.start()] == "!"))
            for raw, is_image in targets:
                raw = re.sub(r"\\([\\()[\] ])", r"\1", raw)
                if raw.lower().startswith(("https://", "http://", "//")):
                    counts["external"] += 1
                    continue
                if SCHEME.match(raw):
                    counts["other"] += 1  # mailto:, data:, etc.
                    continue
                parsed = urlsplit(raw)
                if parsed.fragment:
                    counts["anchors"] += 1
                if not parsed.path:
                    continue  # A fragment in the current file has no file target.
                counts["local"] += 1
                if is_image:
                    counts["images"] += 1
                path_text = unquote(parsed.path)
                target = (root / path_text.lstrip("/")) if path_text.startswith("/") else (source.parent / path_text)
                if not target.exists():
                    display = source.relative_to(root).as_posix()
                    errors.append(f"{display}:{number}: BROKEN LINK: {raw}")
    return counts, errors


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2],
                        help="repository root; useful for temporary test fixtures")
    args = parser.parse_args(argv)
    root = args.root.resolve()
    try:
        files = documentation_files(root)
        if not files:
            raise ValueError(f"no Markdown documentation found in {root}")
        counts, errors = check_files(root, files)
    except (OSError, UnicodeError, ValueError) as exc:
        print(f"Markdown documentation check: ERROR: {exc}", file=sys.stderr)
        return 2
    print("Markdown documentation check")
    print(f"Files checked: {counts['files']}")
    print(f"Local links checked: {counts['local']}")
    print(f"Local images checked: {counts['images']}")
    print(f"External links ignored: {counts['external']}")
    print(f"Other URI schemes ignored: {counts['other']}")
    print(f"Broken local links: {len(errors)}")
    print(f"Anchors: NOT VERIFIED ({counts['anchors']} links with fragments)")
    print("Syntax: simple single-line inline and explicit reference links; nested labels, shortcut references and HTML links NOT VERIFIED")
    for error in errors:
        print(error)
    print(f"Result: {'FAIL' if errors else 'PASS'}")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
