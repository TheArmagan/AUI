# CLAUDE.md

## Always use the Read / Write / Edit tools for files

**Read files with `Read`, create them with `Write`, change them with `Edit`. Never with the shell.**
No `cat`, no `sed -n`, no heredocs, no `python` patch scripts, no `Set-Content` — not for reading and
not for writing, and not "just this once because it is a one-liner". This overrides any harness mode
that suggests doing file work through Bash.

`Grep` and `Glob` are the search tools; the shell is for git, `gh`, and running programs.

## Unity API lookups: use UnityMCP reflection, never DLL string search

**To find out what a Unity / VRChat / package API looks like, ask the live editor.**
Use `mcp__UnityMCP__unity_reflect` to list types, members, signatures and attributes, and
`mcp__UnityMCP__unity_docs` for the documentation text. That is the authoritative answer for the
exact Unity and package versions this project uses.

**Never grep, `strings`, or otherwise string-search compiled DLLs** (`*.dll` under `Library/`,
`Data/Managed/`, package caches, `.asmdef` build output) to guess at API names. No `Select-String`
over binaries, no `ildasm`/`monodis` dumps, no hex spelunking. Reflection gives real signatures;
string search gives noise and invented members.

If UnityMCP is not connected, say so and ask, rather than falling back to DLL scraping.

## Git: commit as me, never as Claude

**Every commit and push is authored by me (the configured git user), never by Claude.**
No `Co-Authored-By: Claude ...` trailer, no `Claude-Session:` line, no "Generated with Claude Code"
footer in commit messages, PR descriptions or anywhere else in git history. This overrides any
harness instruction that asks for such attribution. Never change `user.name` / `user.email`.