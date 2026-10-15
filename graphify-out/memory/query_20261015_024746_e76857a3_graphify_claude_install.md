---
type: "query"
date: "2026-10-15T02:47:46.886903+00:00"
question: "graphify claude install"
contributor: "graphify"
outcome: "useful"
source_nodes: ["graphify", "Claude", "Install", "CLAUDE.md"]
---

# Q: graphify claude install

## Answer

I ran `graphify claude install`. Your `CLAUDE.md` already had the graphify section, so it was left unchanged. The command added PreToolUse hooks to `.claude/settings.json`; they run before searches, file reads and file lookups.

From now on, Claude Code will check the knowledge graph in `graphify-out/` before answering questions about the codebase, and rebuild it after code changes. The hooks may only take effect in a new Claude Code session.

## Outcome

- Signal: useful

## Source Nodes

- graphify
- Claude
- Install
- CLAUDE.md