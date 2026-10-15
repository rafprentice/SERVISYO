"""Sync Claude Code prompts and your own notes into the Servisyo second brain.

What it does
  sync   (default)  Reads this project's Claude Code transcripts, pairs every prompt you typed with
                    Claude's final answer, and for each pair not synced before:
                      1. appends it to the Obsidian vault:  Servisyo/Prompts/<date>.md
                      2. records it in graphify memory:     graphify save-result ... (graphify-out/memory/)
                    then runs `graphify reflect` so graphify-out/reflections/LESSONS.md is up to date.
  note "text"       Writes a quick note of your own to Servisyo/Notes/<date>.md and to graphify memory.

Graph nodes cited in a prompt/answer (class names, file names in backticks, etc.) are matched against
graphify-out/graph.json and attached with --nodes, so the memory links back to the code.

Usage
  python sync_prompts_to_graphify.py                  # sync new prompts
  python sync_prompts_to_graphify.py --dry-run        # show what would be synced
  python sync_prompts_to_graphify.py note "Approval is final; no re-review flow" --title "Approval rules"
  python sync_prompts_to_graphify.py --resync         # forget sync state and sync everything again
"""
from __future__ import annotations

import argparse
import json
import re
import shutil
import subprocess
import sys
import tempfile
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parent
VAULT = ROOT / "Servisyo"
GRAPH = ROOT / "graphify-out" / "graph.json"
STATE = ROOT / "graphify-out" / ".prompt_sync_state.json"
TRANSCRIPTS = Path.home() / ".claude" / "projects" / re.sub(r"[:\\/]", "-", str(ROOT))

MAX_NODES = 12          # cap on --nodes per memory entry
MIN_LABEL_LEN = 4       # ignore tiny graph labels like "Id" when matching


# ---------------------------------------------------------------- transcripts

def _text_of(content) -> str:
    """Plain text of a message's content (string or list of blocks), ignoring tool calls and thinking."""
    if isinstance(content, str):
        return content
    if isinstance(content, list):
        return "\n".join(b.get("text", "") for b in content if isinstance(b, dict) and b.get("type") == "text")
    return ""


def _is_real_prompt(rec: dict) -> bool:
    """A prompt the user typed (not a tool result, slash-command echo or subagent message)."""
    if rec.get("type") != "user" or rec.get("isSidechain") or rec.get("isMeta"):
        return False
    content = (rec.get("message") or {}).get("content")
    if not isinstance(content, str):
        return False                                     # tool_result blocks come as lists
    text = content.strip()
    return bool(text) and not text.startswith(("<command-", "<local-command", "<system-reminder"))


def read_pairs(transcript: Path) -> list[dict]:
    """[{id, session, time, branch, prompt, answer}] in order; answer = Claude's last text before the next prompt."""
    pairs: list[dict] = []
    current: dict | None = None
    with transcript.open(encoding="utf-8") as fh:
        for line in fh:
            try:
                rec = json.loads(line)
            except json.JSONDecodeError:
                continue
            if _is_real_prompt(rec):
                current = {
                    "id": rec.get("uuid"),
                    "session": rec.get("sessionId", transcript.stem),
                    "time": rec.get("timestamp", ""),
                    "branch": rec.get("gitBranch", ""),
                    "prompt": rec["message"]["content"].strip(),
                    "answer": "",
                }
                pairs.append(current)
            elif current is not None and rec.get("type") == "assistant" and not rec.get("isSidechain"):
                text = _text_of((rec.get("message") or {}).get("content")).strip()
                if text:
                    current["answer"] = text                # keep the latest = the final answer
    return [p for p in pairs if p["id"] and p["answer"]]


# ---------------------------------------------------------------- graph nodes

def load_labels() -> dict[str, str]:
    """lower-cased lookup key -> graph node label, for matching names mentioned in text."""
    if not GRAPH.exists():
        return {}
    data = json.loads(GRAPH.read_text(encoding="utf-8"))
    labels: dict[str, str] = {}
    for node in data.get("nodes", []):
        label = str(node.get("label") or node.get("id") or "")
        key = label.strip("().").lower()
        if len(key) >= MIN_LABEL_LEN and re.fullmatch(r"[\w.\-]+", key):
            labels.setdefault(key, label)
    return labels


CODE_SPAN = re.compile(r"`([^`\n]+)`")
CODE_WORD = re.compile(
    r"\b(?:[A-Z][a-z0-9]+(?:[A-Z][a-z0-9]*)+"          # CamelCase: ProviderApprovalService
    r"|[\w\-]+\.(?:cs|cshtml|js|css|py|sql|md|json))\b"  # file names: AccountController.cs
    r"|\.[A-Z]\w+\(\)"                                    # members: .DecideAsync()
)


def cited_nodes(text: str, labels: dict[str, str]) -> list[str]:
    """Graph labels for code mentioned in the text (backticked spans, CamelCase names, file names, .Members()).
    Plain English words are skipped so "Page" or "Start" don't link to unrelated nodes."""
    tokens: list[str] = []
    for span in CODE_SPAN.findall(text):
        tokens += re.findall(r"[A-Za-z_][\w.\-]*\w(?:\(\))?", span)
    tokens += CODE_WORD.findall(text)

    found: list[str] = []
    for token in tokens:
        for candidate in (token, token.rsplit("/", 1)[-1], token.rsplit(".", 1)[-1]):
            label = labels.get(candidate.strip("().").lower())
            if label and label not in found:
                found.append(label)
                break
        if len(found) >= MAX_NODES:
            break
    return found


# ---------------------------------------------------------------- outputs

def _local_time(iso: str) -> datetime:
    try:
        return datetime.fromisoformat(iso.replace("Z", "+00:00")).astimezone()
    except ValueError:
        return datetime.now().astimezone()


def _title(text: str, limit: int = 80) -> str:
    first = text.strip().splitlines()[0] if text.strip() else "Untitled"
    return first if len(first) <= limit else first[: limit - 3].rstrip() + "..."


def append_vault(folder: str, when: datetime, heading: str, body: str, nodes: list[str], dry: bool) -> Path:
    path = VAULT / folder / f"{when:%Y-%m-%d}.md"
    links = " ".join(f"`{n}`" for n in nodes) or "—"
    entry = f"\n## {when:%H:%M} — {heading}\n\n{body.strip()}\n\n**Code:** {links}\n"
    if not dry:
        path.parent.mkdir(parents=True, exist_ok=True)
        if not path.exists():
            path.write_text(f"# {folder} — {when:%Y-%m-%d}\n", encoding="utf-8")
        with path.open("a", encoding="utf-8") as fh:
            fh.write(entry)
    return path


def save_to_graphify(exe: str, question: str, answer: str, nodes: list[str], dry: bool) -> None:
    if dry:
        return
    # The answer goes through a file: long markdown breaks Windows command lines.
    with tempfile.NamedTemporaryFile("w", suffix=".md", delete=False, encoding="utf-8") as tmp:
        tmp.write(answer)
    try:
        cmd = [exe, "save-result", "--question", question, "--answer-file", tmp.name, "--outcome", "useful"]
        if nodes:
            cmd += ["--nodes", *nodes]
        subprocess.run(cmd, cwd=ROOT, check=True, capture_output=True, text=True)
    finally:
        Path(tmp.name).unlink(missing_ok=True)


def load_state() -> set[str]:
    try:
        return set(json.loads(STATE.read_text(encoding="utf-8")).get("synced", []))
    except (OSError, json.JSONDecodeError):
        return set()


def save_state(synced: set[str]) -> None:
    STATE.parent.mkdir(parents=True, exist_ok=True)
    STATE.write_text(json.dumps({"synced": sorted(synced)}, indent=1), encoding="utf-8")


# ---------------------------------------------------------------- commands

def cmd_sync(exe: str, dry: bool, resync: bool) -> int:
    if not TRANSCRIPTS.exists():
        print(f"No Claude Code transcripts found at {TRANSCRIPTS}")
        return 1

    synced = set() if resync else load_state()
    labels = load_labels()
    new = 0

    for transcript in sorted(TRANSCRIPTS.glob("*.jsonl"), key=lambda p: p.stat().st_mtime):
        for pair in read_pairs(transcript):
            if pair["id"] in synced:
                continue
            when = _local_time(pair["time"])
            nodes = cited_nodes(pair["prompt"] + "\n" + pair["answer"], labels)
            # Push the answer's own headings down two levels so they nest under this prompt's "## " heading.
            answer = re.sub(r"(?m)^(#{1,4}) ", r"\1## ", pair["answer"])
            body = f"> **Prompt** ({pair['branch'] or 'no branch'})\n>\n" + \
                   "\n".join(f"> {l}" for l in pair["prompt"].splitlines()) + \
                   f"\n\n{answer}"
            path = append_vault("Prompts", when, _title(pair["prompt"]), body, nodes, dry)
            save_to_graphify(exe, pair["prompt"], pair["answer"], nodes, dry)
            synced.add(pair["id"])
            new += 1
            print(f"{'[dry] ' if dry else ''}+ {_title(pair['prompt'], 60)}  ->  {path.relative_to(ROOT)}"
                  f"  ({len(nodes)} nodes)")

    if not dry:
        save_state(synced)
        if new:
            reflect(exe)
    print(f"{new} new prompt(s) {'would be ' if dry else ''}synced.")
    return 0


def cmd_note(exe: str, text: str, title: str | None, dry: bool) -> int:
    nodes = cited_nodes(text, load_labels())
    heading = title or _title(text)
    path = append_vault("Notes", datetime.now().astimezone(), heading, text, nodes, dry)
    save_to_graphify(exe, heading, text, nodes, dry)
    if not dry:
        reflect(exe)
    print(f"{'[dry] ' if dry else ''}Note saved -> {path.relative_to(ROOT)}  ({len(nodes)} nodes: {', '.join(nodes) or '-'})")
    return 0


def reflect(exe: str) -> None:
    """Fold all memory entries into graphify-out/reflections/LESSONS.md (deterministic, no API cost)."""
    result = subprocess.run([exe, "reflect", "--graph", str(GRAPH)], cwd=ROOT, capture_output=True, text=True)
    if result.returncode != 0:
        print("graphify reflect failed:", (result.stderr or result.stdout).strip(), file=sys.stderr)


def main() -> int:
    parser = argparse.ArgumentParser(description="Sync Claude prompts and notes into the Servisyo second brain.")
    parser.add_argument("--dry-run", action="store_true", help="show what would happen, write nothing")
    parser.add_argument("--resync", action="store_true", help="ignore sync state and sync every prompt again")
    sub = parser.add_subparsers(dest="command")
    note = sub.add_parser("note", help="save a note of your own")
    note.add_argument("text", help="the note (markdown is fine)")
    note.add_argument("--title", help="heading for the note (default: its first line)")
    note.add_argument("--dry-run", action="store_true", default=argparse.SUPPRESS, help=argparse.SUPPRESS)
    args = parser.parse_args()

    exe = shutil.which("graphify")
    if exe is None:
        print("graphify is not on PATH; install it or activate its environment.", file=sys.stderr)
        return 1

    if args.command == "note":
        return cmd_note(exe, args.text, args.title, args.dry_run)
    return cmd_sync(exe, args.dry_run, args.resync)


if __name__ == "__main__":
    sys.exit(main())
