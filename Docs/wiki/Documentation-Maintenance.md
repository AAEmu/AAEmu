# Documentation Maintenance

- Audience: Contributors
- Last verified against: `develop` on February 28, 2026
- Prerequisites: None

## Goal

Keep the wiki aligned with merged behavior in `develop`.

## Update checklist after merged PRs

1. Identify pages impacted by launch, config, networking, packaging, or data
   behavior changes.
1. Update affected pages and cross-links.
1. Add or adjust migration notes when old instructions become invalid.
1. Run doc quality checks:
1. Commit following the contributor guidelines.

## Writing conventions

- Use page metadata at top (`Audience`, `Last verified against`).
- Prefer relative Markdown links for internal wiki references.
- Add a short `Related` section on major pages.
- Keep instructions path-based and explicit.
- Prefer `Config.Local.json` examples for machine-specific values.

## Cross-linking model

1. `Home` is the primary table of contents.
1. Setup pages link to config and troubleshooting pages.
1. Troubleshooting pages link back to setup and FAQ.
1. Reference pages link to setup where relevant.

## Related

- [Home](Home)
- [Developer Notes](Developer-Notes)
- [Aspire Development Guide](Aspire-Development-Guide)
