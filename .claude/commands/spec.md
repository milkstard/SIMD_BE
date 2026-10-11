---
description: Generate implementation spec(s) in docs/specs/ for a BE-NN item from docs/BACKEND-SPECS.md
argument-hint: <NN>  (e.g. 03, 04, 05)
allowed-tools: Read, Glob, Grep, Write, Edit, AskUserQuestion
---

# /spec — turn a BACKEND-SPECS item into detailed spec file(s)

Argument received: `$ARGUMENTS`

You only write spec files and update `docs/implementation/deferred-items.md`. **Do not implement any code.**

## 1. Validate the parameter
1. Grep `docs/BACKEND-SPECS.md` for headings matching `^### BE-\d+` to find the BE numbers that really exist
   (do not hardcode the range; Phase 3 "future" bullets are not specs).
2. Normalize `$ARGUMENTS` to a zero-padded 2-digit `NN` (`3` → `03`).
3. If the argument is empty, non-numeric, or not one of the existing BE numbers (e.g. beyond the highest one):
   **write nothing.** Tell the user the valid range, then ask them again for the parameter
   (AskUserQuestion, or a plain re-prompt with free-text answer). Repeat until valid, then continue.

## 2. Gather context
- Read the `### BE-NN · <title>` section of `docs/BACKEND-SPECS.md` (Context, Requirements, Acceptance Criteria, Dependencies).
- Read `docs/DATABASE.md` and `docs/API-CONTRACT.md` for the tables, DTOs, endpoints and errors involved.
- Read existing files in `docs/specs/` for style and already-decided names (e.g. BE-02 "Naming as built"),
  and the specs of this item's listed Dependencies if they exist.
- Read `docs/implementation/deferred-items.md`. Every item deferred to this BE-NN (or now required by it) goes into
  the scope and acceptance criteria of the right part. Never leave an item deferred if this BE item needs it.
  If it's unclear whether an item belongs here, ask the user.
- `CLAUDE.md` rules always apply (Clean Architecture direction, one use case = command + handler + validator,
  outbox, `TimeProvider`, ProblemDetails, xUnit naming, etc.).

## 3. Decide: single spec or breakdown
- `specabout` = kebab-case of the BE title (e.g. "Application catalog" → `application-catalog`).
- Split into parts when the item spans more than one independently shippable concern, e.g. more than one layer group
  (domain + persistence/migration + endpoints), more than ~5 requirements, or several subsystems (blob + scan + SAS + comments).
- Each part must be implementable and testable on its own; order parts by dependency (earlier parts never depend on later ones).
- If not broad, produce one spec.

## 4. File naming — `<NN>_<nn>_<specabout>`
- `NN` = the parameter; `nn` = 2-digit incremental part number starting at `01`.
- Single spec: `docs/specs/NN_01_<specabout>.md`.
- Breakdown: `docs/specs/NN_01_<part-slug>.md`, `NN_02_<part-slug>.md`, … where `<part-slug>` names what that part covers
  (e.g. `03_01_catalog-entities-and-migration.md`, `03_02_catalog-endpoints.md`).
- If any `docs/specs/NN_*` file already exists, stop and ask the user before overwriting.

## 5. Spec template (every file)
```
# Spec NN_nn: <part title>

> Status: **Draft** · Source: BE-NN in docs/BACKEND-SPECS.md · Part nn of N

## 1. Purpose & scope
## 2. Dependencies            (BE items and earlier parts)
## 3. Design                  (files/folders per layer: Domain, Application, Infrastructure, Api, Worker — only what this part touches)
## 4. Endpoints & DTOs        (must match API-CONTRACT.md; state if that doc or swagger.json must be updated)
## 5. Acceptance criteria     (checkboxes, carried from BE-NN and made specific to this part)
## 6. Test plan               (xUnit, Method_Scenario_ExpectedResult, [Trait("Category","Integration")] where needed)
## 7. Out of scope
## 8. Open questions          (flag any conflict between BACKEND-SPECS, DATABASE.md, API-CONTRACT.md, CLAUDE.md)
```
Never silently resolve a conflict between docs; list it under Open questions.

## 6. Update deferred items
- For each item a spec's **Out of scope** explicitly schedules for a later phase or BE item, add a row to
  `docs/implementation/deferred-items.md` (Item, Source spec, Deferred to, Notes). Skip duplicates, and skip anything
  that is plainly "not this project" rather than "later".
- Never move a mandatory requirement of this BE item into deferred items.
- Do not touch `completed-items.md`. Items move there only when an implementation delivers them.

## 7. Report
List each file created with a one-line summary, the deferred items added or pulled into scope, and the suggested
implementation order. Stop there.
