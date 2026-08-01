# JulOS documentation map

This directory is the authoritative specification for JulOS. Code, issues and commits must match these documents.

## Required reading order

1. [`../README.md`](../README.md) — product summary and repository entry point
2. [`../AGENTS.md`](../AGENTS.md) — mandatory engineering and AI-agent rules
3. [`PRODUCT.md`](PRODUCT.md) — product vision, scope and release success criteria
4. [`CONCEPT.md`](CONCEPT.md) — complete product and system concept
5. [`ARCHITECTURE.md`](ARCHITECTURE.md) — component boundaries and dependency direction
6. [`TECHNICAL_SPECIFICATION.md`](TECHNICAL_SPECIFICATION.md) — concrete runtime and implementation design
7. [`UX_SPECIFICATION.md`](UX_SPECIFICATION.md) — desktop, windows, widgets and responsive behavior
8. [`PACKAGES.md`](PACKAGES.md) — package format, lifecycle and official packages
9. [`DATA_AND_API_CONTRACTS.md`](DATA_AND_API_CONTRACTS.md) — core data model and communication rules
10. [`SECURITY_AND_OPERATIONS.md`](SECURITY_AND_OPERATIONS.md) — threat boundaries, permissions, deployment and recovery
11. [`QUALITY_AND_TESTING.md`](QUALITY_AND_TESTING.md) — test strategy, performance budgets and definition of done
12. [`JULGATE_MIGRATION.md`](JULGATE_MIGRATION.md) — controlled migration into JulOS Remote
13. [`IMPLEMENTATION_PLAN.md`](IMPLEMENTATION_PLAN.md) — milestone sequence
14. [`WORK_BREAKDOWN.md`](WORK_BREAKDOWN.md) — junior-ready issue plan
15. [`BACKLOG.md`](BACKLOG.md) — current implementation status
16. [`DECISIONS.md`](DECISIONS.md) — accepted architecture decisions
17. [`GLOSSARY.md`](GLOSSARY.md) — canonical terminology

Supporting document: [`RELEASE_NOTES_TEMPLATE.md`](RELEASE_NOTES_TEMPLATE.md) — the shape of every release note.

## Source-of-truth rule

When documents appear to conflict, use this priority:

1. accepted decision in `DECISIONS.md`
2. architecture and technical specifications
3. package, data, security and UX specifications
4. implementation plan and work breakdown
5. backlog and issue descriptions

A conflict must be resolved by updating all affected documents in the same commit. Do not silently choose one interpretation in code.

## Documentation ownership

- Product behavior: `PRODUCT.md`, `CONCEPT.md`, `UX_SPECIFICATION.md`
- System boundaries: `ARCHITECTURE.md`, `TECHNICAL_SPECIFICATION.md`
- Extension model: `PACKAGES.md`
- Persistent state and transport: `DATA_AND_API_CONTRACTS.md`
- Security, deployment and recovery: `SECURITY_AND_OPERATIONS.md`
- Validation: `QUALITY_AND_TESTING.md`
- Delivery order: `IMPLEMENTATION_PLAN.md`, `WORK_BREAKDOWN.md`, `BACKLOG.md`

## Change rule

Every implementation commit must identify the affected documents and update them. A feature is incomplete when the repository documentation still describes an older state.
