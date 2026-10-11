---
name: review-spec
description: Orchestrate the post-implementation review of a spec in docs/specs/ — run the qa-engineer and security-engineer agents in parallel, gate on their results, then run the senior-code-reviewer agent, and produce one consolidated report. Use when a spec's implementation is complete (current-status.md says Done), or when asked to "review the spec", "run the review workflow" or "/review-spec [spec id]".
---

# Spec review workflow

You (the main agent) are the **orchestrator**. You run the review agents, gate on their results and report. You do
not review the code yourself, and nothing changes in the repository during the review stages.

Argument: an optional spec id or path (e.g. `04_02`). Without one, use the Active spec.

## Ground rules
- **No file changes during stages 1–4** — not by you, not by any agent. The only write in this workflow is the
  `current-status.md` update in stage 5 (and any fix you're explicitly authorized to make in the fix loop).
- Review agents never fix issues and never touch the same files. Each gets the read-only instructions below.
- **Never report a review as passed unless the agent ran and reported its checks.** An agent that errored, timed out,
  skipped a required check or couldn't inspect a relevant area has **not passed** — it's *Incomplete*.
- Preserve unrelated user changes: never stash, reset, checkout, clean or format.
- Subagents run in the background; wait for their completion notifications. Never predict or invent their results.

## Stage 1 — Preconditions

1. **Spec:** the argument, or the **Active** spec in `docs/implementation/current-status.md`. Confirm the file exists in
   `docs/specs/` and read it fully (requirements, acceptance criteria, resolved open questions), plus
   `docs/implementation/deferred-items.md`.
2. **Completion:** `current-status.md` must show `State = Done` for that spec with all progress steps ticked.
   If it's `Planned`, `In progress` or `Blocked`, or steps are unticked, **stop** and report what remains.
3. **Change set:** `git status --porcelain` and `git log --oneline -10`. Identify the spec's changes (uncommitted
   working tree, and/or the commit(s) named in `current-status.md` → *Branch / commit*). If you can't tell which
   changes belong to the spec, stop and ask.
4. **Context:** the agent files `.claude/agents/qa-engineer.md`, `security-engineer.md`, `senior-code-reviewer.md` and
   `.claude/skills/backend-code-conventions/SKILL.md` exist. Check `docker info` (integration tests need it); if
   Docker is down, ask the user whether to start it or continue with integration tests reported as *not run* (which
   makes QA *Incomplete*).
5. **Snapshot** the working tree for later verification: save the output of `git status --porcelain` and
   `git diff --stat` (in memory; do not write it to the repo).

If any precondition fails, report what must be completed first and stop.

## Stage 2 — QA and security in parallel

Launch both agents **in the same message** (two `Agent` calls) so they run concurrently. Give each the context below
— they start cold, so include the spec path, the change set (commit shas and/or "uncommitted"), the deferred items
that apply, and the Docker status.

**`qa-engineer`** prompt must include:
> Review-only mode (orchestrated by /review-spec): do NOT create, edit or delete any file. Where you would add a test,
> report it under "Proposed tests" with the full test code and target file instead; "Added" must be 0. Use only the
> console logger (no `trx`/result files). Verify every acceptance criterion of `<spec>`, map coverage, run
> `dotnet build` then `dotnet test` (or the unit-only filter if Docker is down, reporting integration tests as not
> run), and do root-cause analysis on every failure. For each finding give severity (Critical/High/Medium/Low),
> evidence, and the recommended fix. List any required check you could not perform.

**`security-engineer`** prompt must include:
> Orchestrated by /review-spec, running in parallel with QA: do not create, edit or delete any file, and do NOT run
> `dotnet build`, `dotnet test` or `dotnet restore` (QA is building the same projects). `dotnet list package
> --vulnerable --include-transitive` is allowed. Scope: the flow implemented by `<spec>` and the change set
> `<...>` — authentication, authorization (role policy + team resource checks), data access, input validation and
> sanitization, data exposure, secrets, and other relevant risks. Give severity, Confirmed/Plausible, evidence and
> the proposed fix for each finding, and list any area you could not inspect.

## Stage 3 — Gate

Wait until **both** agents have finished. Then:

1. **Verify no files changed:** rerun `git status --porcelain` and `git diff --stat` and compare with the snapshot
   (ignore `bin/`, `obj/` build output). If anything changed, stop, report exactly what changed and which agent
   likely did it, and do not revert it yourself — ask the user.
2. **Classify each review** as **Passed**, **Blocked** or **Incomplete**:
   - **Blocked** if it reports any *blocking* finding:
     - a **confirmed** Critical or High finding;
     - a required acceptance criterion **Not met**;
     - a **failing** test that is required for the spec (including pre-existing tests the change broke);
     - an unresolved security vulnerability that makes the change unsafe to approve (Critical/High, or a Plausible
       Critical/High you can't rule out without more verification).
   - **Incomplete** if the agent errored, didn't produce its report, skipped a required check (e.g. integration tests
     not run, an area not inspected) or reported counts it didn't observe. Decide whether the gap needs more
     verification: if it does, rerun that agent with a prompt targeting the gap (once), or ask the user.
   - **Passed** otherwise. Medium/Low findings, Potential risks and proposed tests are **non-blocking**.
3. If **both Passed** → stage 4. Otherwise → the fix loop below; do **not** invoke the senior code reviewer.

### Fix loop (only when Blocked)
1. Consolidate the blocking findings from both reviews (deduplicate; keep the agent's evidence and proposed fix).
2. Report them and **ask the user for authorization** to fix (all, some, or none). Never fix without it.
3. If authorized, the main agent (or the implementer the user names) applies only the authorized fixes, touching
   only the files they require, then runs `dotnet build` and the relevant tests. Update `current-status.md` per
   `CLAUDE.md` (it's an implementation change).
4. Rerun **stage 2 (both agents)** and **stage 3** — not just the agent that failed.
5. **Limit: 2 fix-and-review cycles.** If blocking issues remain after the second cycle, stop, report what is still
   failing and what was tried, and ask the user for direction.

## Stage 4 — Senior code review

Only after QA **and** security both Passed, launch **`senior-code-reviewer`** with: the spec path, the change set,
the deferred items, and a short list of the QA/security findings already reported (so it doesn't duplicate them).
Its prompt must state it is read-only and must review against the spec and the `backend-code-conventions` skill.

Wait for it to finish, then re-verify the working tree against the snapshot as in stage 3. Classify its result:
**Approved** (no confirmed Critical/High, all requirements Met), **Changes recommended** (confirmed Critical/High or a
requirement not Met — blocking), or **Incomplete**. Blocking code-review findings go through the same fix loop
(authorization first, rerun stages 2–4 after fixes, same 2-cycle limit).

## Stage 5 — Final report and status

Print the report (concise — the agents' full reports are not shown to the user, so carry over what matters):

```
## Spec review — <spec id> · <change set>

| Stage | Result | Key numbers |
|---|---|---|
| QA | Passed / Blocked / Incomplete | tests passed/failed/not run, proposed tests N |
| Security | Passed / Blocked / Incomplete | Critical/High/Medium/Low counts |
| Senior code review | Approved / Changes recommended / Incomplete / Not run (reason) | Critical/High/Medium/Low counts |

### Blocking findings
- [source · severity · Confirmed/Plausible] title — `file:line` — proposed fix (one line each, or "None")

### Non-blocking findings
- (grouped by source; one line each, or "None")

### Checks failed, skipped or not performed
- (e.g. integration tests not run — Docker down; dependency scan failed; area X not inspected — or "None")

### Verdict
**Approved** only if all three stages ran and none is Blocked / Changes recommended / Incomplete;
otherwise **Not approved** — with the reason.

### Recommended next action
<one or two sentences>
```

Then update `docs/implementation/current-status.md`: add a `### Review` subsection under the Active spec with the
table above (results and counts only) and append a Log line, e.g.
`- 2026-10-11 · 04_02 · Reviewed · Not approved — 1 High (security): …`. Don't change the spec's *State* unless a
fix loop reopened it. Leave every other file untouched.
