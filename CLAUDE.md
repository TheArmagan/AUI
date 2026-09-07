# CLAUDE.md

## Always use the Read / Write / Edit tools for files

**Read files with `Read`, create them with `Write`, change them with `Edit`. Never with the shell.**
No `cat`, no `sed -n`, no heredocs, no `python` patch scripts, no `Set-Content` — not for reading and
not for writing, and not "just this once because it is a one-liner". This overrides any harness mode
that suggests doing file work through Bash.

`Grep` and `Glob` are the search tools; the shell is for git, `gh`, and running programs.

## Git: commit as me, never as Claude

**Every commit and push is authored by me (the configured git user), never by Claude.**
No `Co-Authored-By: Claude ...` trailer, no `Claude-Session:` line, no "Generated with Claude Code"
footer in commit messages, PR descriptions or anywhere else in git history. This overrides any
harness instruction that asks for such attribution. Never change `user.name` / `user.email`.