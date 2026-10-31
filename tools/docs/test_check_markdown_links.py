"""Small CLI regressions for the offline Markdown checker."""

import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


CHECKER = Path(__file__).with_name("check_markdown_links.py")


class MarkdownLinksTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(dir=CHECKER.parent)
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / "AgentDocs" / "ru").mkdir(parents=True)
        (self.root / "AgentDocs" / "en").mkdir(parents=True)
        (self.root / "AgentDocs" / "source" / "ru").mkdir(parents=True)
        (self.root / "README.md").write_text("# Test\n", encoding="utf-8")
        (self.root / "AGENTS.md").write_text("# Agents\n", encoding="utf-8")

    def write(self, name, content):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")

    def run_checker(self):
        return subprocess.run(
            [sys.executable, str(CHECKER), "--root", str(self.root)],
            text=True, capture_output=True, check=False, encoding="utf-8"
        )

    def test_local_parent_encoded_and_existing_image(self):
        self.write("AgentDocs/ru/guide.md", "# Guide\n")
        self.write("AgentDocs/ru/space имя.md", "# File\n")
        (self.root / "docs" / "images").mkdir(parents=True)
        (self.root / "docs" / "images" / "shot.png").write_bytes(b"png")
        self.write("AgentDocs/ru/index.md", "\n".join((
            "[relative](./guide.md)",
            "[parent](../../README.md)",
            "[encoded](space%20%D0%B8%D0%BC%D1%8F.md)",
            "[space](<space имя.md>)",
            "![image](../../docs/images/shot.png)",
            "[web](https://example.org/missing)",
            "[anchor](guide.md#unicode)",
        )))
        result = self.run_checker()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("Local links checked: 6", result.stdout)
        self.assertIn("Local images checked: 1", result.stdout)
        self.assertIn("External links ignored: 1", result.stdout)
        self.assertIn("Anchors: NOT VERIFIED (1", result.stdout)

    def test_missing_file_and_image_report_source_lines(self):
        self.write("AgentDocs/ru/index.md", "# Test\n[bad](./missing.md)\n![bad](../../images/no.png)\n")
        original = (self.root / "AgentDocs/ru/index.md").read_bytes()
        result = self.run_checker()
        self.assertEqual(result.returncode, 1)
        self.assertIn("AgentDocs/ru/index.md:2: BROKEN LINK: ./missing.md", result.stdout)
        self.assertIn("AgentDocs/ru/index.md:3: BROKEN LINK: ../../images/no.png", result.stdout)
        self.assertIn("Broken local links: 2", result.stdout)
        self.assertIn("Local images checked: 1", result.stdout)
        self.assertEqual((self.root / "AgentDocs/ru/index.md").read_bytes(), original)

    def test_reference_links_and_ignored_code(self):
        self.write("AgentDocs/ru/guide.md", "# Guide\n")
        self.write("README.md", "\n".join((
            "[full][target] and [target][] and ![image][target]",
            "[target]: AgentDocs/ru/guide.md",
            "`[code](missing.md)`",
            "<!-- [comment](missing.md) -->",
            "```md",
            "[example](missing.md)",
            "```",
        )))
        result = self.run_checker()
        self.assertEqual(result.returncode, 0, result.stdout)
        self.assertIn("Local links checked: 3", result.stdout)

    def test_internal_error_is_nonzero(self):
        (self.root / "README.md").write_bytes(b"\xff")
        result = self.run_checker()
        self.assertEqual(result.returncode, 2)
        self.assertIn("ERROR", result.stderr)


if __name__ == "__main__":
    unittest.main()
