# Developer Notes

- Audience: Contributors
- Last verified against: `develop` on February 28, 2026
- Prerequisites: None

## Recent architecture note: manager DI and parallel loading

PRs `#1363` and `#1366` migrated manager construction toward dependency
injection and completed follow-up fixes for parallel loading.

What this means for contributors:

- Manager dependencies are increasingly explicit in constructors.
- Startup manager loading is now less manual and more dependency-driven.
- Parallel initialization surfaced and fixed some concurrency issues.

Operational impact for wiki setup docs is small:

- No user-facing launch command change beyond existing setup workflows.
- Main effects are internal maintainability, testability, and startup behavior.

## Related

- [Home](Home)
- [Aspire Development Guide](Aspire-Development-Guide)
- [Components](Components)
