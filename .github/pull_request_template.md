# Change summary

Describe the user or developer outcome of this pull request.

## Work item

- Issue or `WORK_BREAKDOWN.md` ID:
- Dependencies confirmed merged:

## Scope

- What changed?
- What deliberately did not change?
- Why is this one coherent reviewable change?

## Architecture and package impact

- Which Domain, Core, contracts, packages, Agents or runtimes changed?
- Confirm that no direct package-to-package dependency or cross-package database access was added.
- Confirm that Server did not gain raw container-runtime access.
- Explain every new dependency and why existing platform code was insufficient.
- Link the accepted decision when an architecture rule changed.

## Security impact

- Permissions and scopes added or changed:
- Secret or credential handling:
- New trust boundary, network access or runtime resource:
- Destructive action and confirmation behavior:
- Audit coverage:

Write `None` with a reason when the change has no security impact.

## Data and compatibility

- Database migration:
- Contract or manifest version impact:
- Upgrade behavior:
- Rollback limits:
- External product compatibility:

## User experience

- Desktop, responsive or accessibility impact:
- English and German localization impact:
- Loading, empty, offline, stale, unauthorized and error states:

## Validation

List the exact commands and manual checks performed.

- Build:
- Unit tests:
- Architecture tests:
- Integration tests:
- End-to-end tests:
- Manual checks:

## Documentation

List every Markdown file reviewed or updated. Explain why no documentation change was required when applicable.

## Quality checklist

- [ ] Root cause addressed; no workaround, hidden fallback or duplicate implementation added
- [ ] Change is limited to one coherent scope
- [ ] Package and Core boundaries remain valid
- [ ] Backend permissions, scopes and secret handling reviewed
- [ ] Cancellation, timeout and retry behavior defined
- [ ] Data migration and compatibility impact documented
- [ ] Relevant automated tests added or updated
- [ ] Errors remain visible and actionable
- [ ] User-facing text is localizable and required English/German resources are updated
- [ ] Accessibility and responsive behavior reviewed where applicable
- [ ] Affected Markdown files match the implementation
- [ ] `docs/BACKLOG.md` status updated
- [ ] Full repository validation passes
