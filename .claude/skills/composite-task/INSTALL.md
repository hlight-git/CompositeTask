# Installing the Composite Task skill in your project

This skill lives inside the submodule so it travels with the package. Claude Code does **not** auto-discover skills nested in submodules — it only scans `<project-root>/.claude/skills/` and `~/.claude/skills/`. You need to expose this skill to one of those locations.

Pick one of the options below.

## Option A: Symlink (recommended)

One source of truth, updates automatically when the submodule updates.

### macOS / Linux

From your project root:

```bash
mkdir -p .claude/skills
ln -s "../../Assets/Submodules/Composite Task/.claude/skills/composite-task" \
      ".claude/skills/composite-task"
```

### Windows (PowerShell, admin or Developer Mode on)

```powershell
New-Item -ItemType Directory -Force .claude\skills
New-Item -ItemType SymbolicLink `
  -Path ".claude\skills\composite-task" `
  -Target "..\..\Assets\Submodules\Composite Task\.claude\skills\composite-task"
```

Add `/.claude/skills/composite-task` to `.gitignore` (or track the symlink itself, either works).

## Option B: Copy

Simple, but you'll need to re-copy after every submodule update.

```bash
cp -R "Assets/Submodules/Composite Task/.claude/skills/composite-task" \
      ".claude/skills/composite-task"
```

Track the copy in git if you want all team members to get the skill without a setup step.

## Option C: User-level install (machine-wide)

If you work on multiple projects that use this package:

```bash
mkdir -p ~/.claude/skills
cp -R "Assets/Submodules/Composite Task/.claude/skills/composite-task" \
      "~/.claude/skills/composite-task"
```

The skill will be available in every Claude Code session on your machine.

## Verifying installation

Start a new Claude Code session in the project. In a conversation, type:

> `/composite-task`

…or ask a question that triggers the skill, e.g. "I want to build a task tree that waits then plays a sound". If the skill is installed, Claude will reference the skill's docs before answering.

You can also list discovered skills with `/` in the input box — `composite-task` should appear.

## What's in the skill

- `SKILL.md` — entry point with decision flow and core rules
- `references/` — deep-dive guides: node creation, built-in node catalog, JSON blueprint format, lifecycle, MCP build flow, preset trees, conditionals, inline tasks, DI visitor
- `scripts/new-task-node.py` — generator for new TaskNode / TaskNode<T> / TaskCondition .cs files
- `assets/templates/*.cs.tmpl` — templates used by the generator

No extra dependencies. The generator uses standard Python 3.
