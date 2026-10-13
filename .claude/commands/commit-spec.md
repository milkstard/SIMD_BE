---
description: Commit the changes of a spec with a 1-3 sentence message describing what the change is about
argument-hint: [spec id]  (e.g. 05_01; defaults to the Active spec in current-status.md)
allowed-tools: Read, Grep, Glob, Bash(git status:*), Bash(git diff:*), Bash(git log:*), Bash(git add:*), Bash(git commit:*), AskUserQuestion
---

# /commit-spec — commit one spec's changes

Argument received: `$ARGUMENTS` (optional spec id such as `05_01`).

You only stage and commit. **Do not push, amend, rebase, stash, reset, format or edit any file.**

## 1. Identify the spec
- Use the argument, or the **Active** spec in `docs/implementation/current-status.md`. Read that spec in `docs/specs/`
  (title and purpose only, to describe the change accurately).
- If `current-status.md` shows that spec as anything other than `Done` (or `Done` + reviewed), tell the user the state and
  ask (AskUserQuestion) whether to commit anyway. Never commit silently on `Planned`, `In progress` or `Blocked`.

## 2. Select the files
- Run `git status --porcelain` and `git diff --stat`.
- Stage only files that belong to the spec: the code, tests, migrations, config and docs it touches (including
  `docs/implementation/*` and the spec file itself). Ignore build output (`bin/`, `obj/`).
- If some changes clearly belong to something else, or you can't tell, ask the user which files to include
  (AskUserQuestion). Don't guess.
- Never stage secrets or local config (`.env`, `secrets.json`, user-secrets, credentials, connection strings with passwords).
  If you see one, leave it out and say so.
- Stage by explicit path (`git add <paths>`), not `git add -A`, unless every changed file belongs to the spec.
- Nothing to commit → say so and stop.

## 3. Write the message
- **1 to 3 sentences, no more.** Plain prose, no bullet lists, no headings, no file lists.
- Say what the change *does* and, if useful, why — in the style of the existing log (`git log --oneline -5`).
- Mention the spec id and BE item, e.g. `(BE-05 part 1)`, but keep it short.
- End the commit message with the attribution line given in this conversation's system-reminder (the `Co-Authored-By:`
  trailer), as a separate trailer paragraph. Add no other lines.

## 4. Commit
- Commit on the **current branch** (do not create or switch branches; this repo commits specs directly on `main`).
- Never use `--no-verify`, `--amend` or `--no-gpg-sign`. If a hook fails, report the failure and stop; fix nothing yourself.
- Use a heredoc or two `-m` arguments so the trailer stays on its own paragraph.

## 5. Report
One short message: the commit hash and subject, the number of files committed, and any files left uncommitted (with why).
Mention that nothing was pushed. Stop there.
