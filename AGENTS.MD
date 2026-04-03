# AGENTS.md

## Purpose

This repository uses `design.md` as the **single source of truth** for architecture, design decisions, and rules.

All AI agents (including Codex) must treat `design.md` as mandatory context for every task.

---

## Required Context Loading

- ALWAYS read `design.md` at the start of every session.
- ALWAYS treat `design.md` as higher priority than assumptions or defaults.
- NEVER proceed with implementation before consulting `design.md`.

If `design.md` is missing, unclear, or incomplete:
- STOP and ask for clarification before continuing.

---

## Authority Rules

When conflicts occur:

1. `design.md` (highest authority)
2. Existing codebase patterns
3. General best practices (lowest authority)

Agents MUST follow this priority strictly.

---

## Implementation Rules

- All code MUST align with the architecture and constraints defined in `design.md`.
- Do NOT introduce patterns, libraries, or structures not approved in `design.md`.
- Do NOT refactor away from `design.md` without explicit instruction.
- Prefer consistency with `design.md` over “better” generic solutions.

---

## Change Management

- If a task requires deviating from `design.md`, the agent MUST:
  1. Explicitly state the conflict
  2. Propose a change
  3. Wait for approval before proceeding

---

## Code Generation Guidelines

- Mirror naming, structure, and conventions defined in `design.md`
- Reuse existing components and patterns referenced in `design.md`
- Keep implementations minimal and aligned with the documented design

---

## Validation Checklist (MANDATORY)

Before completing any task, verify:

- [ ] `design.md` was consulted
- [ ] Implementation matches documented architecture
- [ ] No conflicting patterns were introduced
- [ ] Any ambiguities were resolved or surfaced

---

## Summary

`design.md` is not optional context — it is the rulebook.

Agents should behave as if `design.md` is always implicitly included in the prompt.