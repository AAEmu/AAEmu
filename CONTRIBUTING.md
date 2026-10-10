## Contributing

If you want to contribute to a project and make it better, your help is very welcome. Contributing is also a great way to learn more about social coding on Github, new technologies and and their ecosystems and how to make constructive, helpful bug reports, feature requests and the noblest of all contributions: a good, clean pull request.

### How to make a clean pull request

Look for a project's contribution instructions. If there are any, follow them.

- Create a personal fork of the project on Github.
- Clone the fork on your local machine. Your remote repo on Github is called `origin`.
- Add the original repository as a remote called `upstream`.
- If you created your fork a while ago be sure to pull upstream changes into your local repository.
- Create a new branch to work on! Branch from `develop` if it exists, else from `master`.
- Implement/fix your feature, comment your code.
- Follow the code style of the project, including indentation.
- If the project has tests run them!
- Write or adapt tests as needed.
- Add or change the documentation as needed.
- Squash your commits into a single commit with git's [interactive rebase](https://help.github.com/articles/interactive-rebase). Create a new branch if necessary.
- Push your branch to your fork on Github, the remote `origin`.
- From your fork open a pull request in the correct branch. Target the project's `develop` branch if there is one, else go for `master`!
- ...
- Once the pull request is approved and merged you can pull the changes from `upstream` to your local repo and delete
your extra branch(es).

### Development workflow

AAEmu is written in Rust (it was ported from C#; the old .NET projects are being removed). All code
lives in the Cargo workspace in `rust/`. You need stable Rust installed via [rustup](https://rustup.rs/).

Before opening a pull request, run the same checks as CI from the `rust/` directory:

```sh
cargo fmt --all                                          # format (CI runs it with --check)
cargo clippy --workspace --all-targets -- -D warnings   # lint, warnings are errors
cargo test --workspace                                   # tests
cargo xtask ci                                           # all three, exactly as CI runs them
```

- Follow `rustfmt` formatting and keep `clippy` clean; do not add `#[allow(...)]` without a reason.
- Add or adapt tests next to the code you change (`#[cfg(test)]` modules or the crate's `tests/`).
- Tests that need a live MySQL server or a `compact.sqlite3` are `#[ignore]`d and gated by the
  `AAEMU_TEST_MYSQL` / `AAEMU_COMPACT_DB` environment variables; CI does not run them, so run them
  locally when your change touches that code, for example
  `AAEMU_COMPACT_DB=rust/crates/aaemu-game/app/Data/compact.sqlite3 cargo test -p aaemu-game -- --ignored`.
- `cargo xtask dev` starts MySQL (Docker), the login server and the game server for manual testing,
  see the [Local Development Guide](Docs/wiki/Local-Development-Guide.md).
- [`rust/LIVE-TESTING.md`](rust/LIVE-TESTING.md) explains how to test against a running server with
  the headless `aaemu-testclient` bot (scripted scenarios, parity diffs, load tests).
- Porting or extending game logic: read
  [`rust/crates/aaemu-game/CONTRIBUTING-PORT.md`](rust/crates/aaemu-game/CONTRIBUTING-PORT.md)
  (conventions and how to port a feature) and [`rust/PORT-AUDIT.md`](rust/PORT-AUDIT.md)
  (what has been ported from the former C# code).

### Commit messages

All commit messages must follow the [Conventional Commits 1.0.0 specification](https://www.conventionalcommits.org/en/v1.0.0/):

```text
<type>[optional scope][!]: <description>

[optional body]

[optional footer(s)]
```

- Use `feat` for new features and `fix` for bug fixes. Other appropriate types, such as `build`, `chore`, `ci`, `docs`, `perf`, `refactor`, `style`, and `test`, are also allowed.
- Use an optional scope to identify the affected area, for example: `fix(auth): reject expired tokens`.
- Mark breaking changes with `!` before the colon or a `BREAKING CHANGE:` footer.
- Write the description in the present tense. It should describe what the commit does when applied, not what you did while creating it.
