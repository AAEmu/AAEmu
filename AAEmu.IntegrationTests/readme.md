# Setting up Integration Tests

The integration tests read game content from a local MySQL server and a local compact database.
Both are machine-local and gitignored, so a fresh clone needs two files before the suite is
meaningful.

## 1. Configuration

`Config.json` is the base configuration. When it is absent the project falls back to the
checked-in `ExampleConfig.json`, whose connection fields are still wildcards (`%db_host%`,
`%db_port%`, `%db_user%`, `%db_password%`, `%login_host%`, `%login_port%`). The container scripts
substitute those with `sed`; locally you supply the values yourself.

Create the machine-local overlay next to it:

```bash
cp Config.Local.json.example Config.Local.json
# then edit Config.Local.json with your real host, port, user and password
```

`Config.Local.json` is gitignored, so credentials and machine-specific addresses stay out of the
repository. It is applied last, on top of `Config.json` and the `Configurations/` overlay, exactly
like the `Config.Local.json` used by the Login, World and Game entry points.

If a wildcard is still unbound when the tests start, the run stops with a message naming the
offending keys and this file — it does not fail later with a binder conversion error.

## 2. Game content

Copy `compact.sqlite3` from the `AAEmu.Game` output `Data` folder into the integration test output
`Data` folder (`bin/Debug/net10.0/Data`). It is a large local artifact and is gitignored on purpose.
Tests that read quest or game content need it; the MySQL-only persistence tests do not.

## 3. Opting into the MySQL tests

The persistence tests each skip unless you point them at a server. They create and drop their own
throwaway database and never accept `aaemu_game`:

```bash
export AAEMU_RECRUITMENT_TEST_MYSQL="Server=127.0.0.1;Port=3306;User ID=<user>;Password=<password>;"
export AAEMU_FAMILY_TEST_MYSQL="Server=127.0.0.1;Port=3306;User ID=<user>;Password=<password>;"
export AAEMU_MUSIC_TEST_MYSQL="Server=127.0.0.1;Port=3306;User ID=<user>;Password=<password>;"
export AAEMU_BUTLER_TEST_MYSQL="Server=127.0.0.1;Port=3306;User ID=<user>;Password=<password>;"
```

Leave them unset to skip those tests.
