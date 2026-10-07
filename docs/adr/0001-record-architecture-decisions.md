# ADR-0001: Record architecture decisions

- Status: Accepted
- Date: 2026-10-07

## Context
Job Scout is a learning and portfolio project that will be public on GitHub. Decisions are made step by step,
often after measuring something (e.g. how many job ads redirect to JavaScript pages). Without a record, the
reasons are lost and decisions look arbitrary – both to future me and to anyone reading the code.

## Decision
Every decision that affects structure, technology, data or quality attributes gets a short ADR in `docs/adr/`.
ADRs are written when the decision is made, not afterwards, and are updated only by adding a new ADR.

## Alternatives considered
- **Comments in code** – too scattered, can't describe rejected alternatives.
- **A wiki / design document** – drifts away from the code; ADRs live and version with it.

## Consequences
- The "why" is reviewable in pull requests together with the "what".
- Small overhead per decision (10–15 minutes).
