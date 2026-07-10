namespace OmniKeyVault.Cli;

/// <summary>
/// P7-T3: Help text for CLI commands, extracted from CommandHandlers.cs.
/// </summary>
public static class HelpText
{
    public const string Root = @"OmniKey Vault v1.0 — CLI

Usage: okv [global options] <command> [subcommand] [command options]

Commands:
  vault      Vault lifecycle (create / unlock / lock / info)
  profile    Multi-profile management (list / create / switch / delete / info)
  entry      Entry CRUD (list / get / set / delete)
  template   Platform templates (list / show / apply)
  export     Export to external format (okv-dev)
  import     Import from external format (bitwarden-json / okv-dev)
  sync       Multi-device sync (status / force)
  version    Print version information
  help       Show help (use 'okv help <command>' for command help)

Global options:
  --vault <path>           Vault file path (default: %USERPROFILE%\OmniKeyVault\vault.okv)
  --profile <name>         Default profile (default: prod)
  --format <text|json|raw> Output format (default: text)
  --yes                    Skip confirmation prompts
  --quiet                  Suppress informational output
  --verbose                Verbose logging to stderr
  --password-file <path>   Read master password from file
  --password-env <var>     Read master password from environment variable
  --password-stdin         Read master password from stdin

Examples:
  okv vault create --vault ./my.okv
  okv profile create --name dev --color yellow --no-sync
  okv export --output ./seed.okv.dev --format okv-dev --source-profile dev
  okv import --input ./seed.okv.dev --format okv-dev --profile dev
  okv sync force --remote /path/to/remote/vault.okv
  okv entry set --name ""OpenAI prod"" --template openai
  okv entry list --profile prod
  okv entry get --id <uuid> --field api_key --format raw

Documentation: https://github.com/omnikeyvault/omnikeyvault
";

    public static string For(string command) => command switch
    {
        "vault" => @"okv vault — Vault lifecycle

Subcommands:
  create           Create a new vault
  unlock           Unlock an existing vault (loads keys into memory)
  lock             Lock the vault (zeroes all keys)
  info             Show vault metadata (no password required)
  change-password  Re-wrap all profile DEKs under a new master password

Options:
  --vault <path>           Vault file path
  --name <name>            (create) Vault display name
  --password-file <path>   Read master password from file
  --password-env <var>     Read master password from environment variable
  --password-stdin         Read master password from stdin
  --yes                    (create) Skip overwrite confirmation
  --format <text|json>     Output format

Exit codes:
  0 success  2 arg error  3 locked  4 crypto  6 file I/O  13 corrupt
",
        "profile" => @"okv profile — Multi-profile management

Subcommands:
  list       List all profiles (table or JSON)
  create     Create a new profile (e.g. dev, test, client-A)
  switch     Switch the active profile (session-only)
  delete     Delete a profile and all its entries
  info       Show one profile's details

Options:
  --name <name>            Profile name (create / switch / delete / info)
  --color <c>              (create) green / yellow / blue / red / purple (default by name)
  --no-sync                (create) Disable sync participation
  --idle-lock-min <int>    (create) Idle-lock minutes (default 15)
  --yes                    (delete) Skip confirmation
  --format <text|json>     (list / info) Output format

Notes:
  - dev / test profiles default to sync=off (PRD §5.5.1).
  - The last profile in a vault cannot be deleted.

Exit codes:
  0 success  2 arg error  5 profile not found  9 name conflict
",
        "entry" => @"okv entry — Entry CRUD + search + rotate + history

Subcommands:
  list       List entries in current profile
  get        Get a single entry or field
  set        Create entry from template OR update a single field (value from stdin)
  delete     Delete an entry (with confirmation)
  search     Full-text + field-level search (ROADMAP S6-T1)
  rotate     One-click platform rotation via IPlatformRotator (v0.4 S8-T1/2/3)
  history    List an entry's history snapshots, or --restore to a previous version

Options:
  --id <uuid>              Entry ID (for get/set/delete/rotate/history)
  --name <name>            Entry name (for create)
  --template <id>          Template ID (for create)
  --field <key>            Field key (for get/set)
  --tag <tag>              (list) Filter by tag
  --platform <id>          (list) Filter by platform
  --search <query>         (list) Free-text filter
  --query / -q <q>         (search) Query string (tags:dev AND field:api_key:sk-*)
  --restore <version>      (history) Restore entry to a previous version
  --reveal                 (get) Show sensitive values in plaintext
  --yes                    (delete/history) Skip confirmation

Examples:
  okv entry set --name ""OpenAI prod"" --template openai
  echo ""sk-proj-abc..."" | okv entry set --id <uuid> --field api_key
  okv entry get --id <uuid> --field api_key --format raw
  okv entry search --query ""tags:ai AND field:api_key:sk-*""
  okv entry rotate --id <uuid>
  okv entry history --id <uuid> --restore 1 --yes

Exit codes:
  0 success  3 locked  7 entry not found  8 field not found
",
        "template" => @"okv template — Platform templates

Subcommands:
  list       List available templates
  show       Show full template definition
  apply      Create a new entry from a template

Options:
  --mvp-only               (list) Only v0.1 MVP templates
  --category <cat>         (list) Filter by category
  --search <query>         (list) Filter by ID/name substring
  --id <template-id>       (show/apply) Template ID
  --name <name>            (apply) New entry name
  --format <text|json>     Output format

Categories: cloud, ai_llm, code_hosting, payment, database_auth, communication, monitoring, productivity
",
        "import" => @"okv import — Import from external format

Required:
  --input <path>           Input file path
  --format <format>        Format: 'bitwarden-json' | 'okv-dev'
  --profile <name>         Target profile (default: prod; for 'okv-dev' must be dev|test)

Supported formats (v0.2):
  bitwarden-json           Bitwarden vault JSON export (encrypted=false only)
  okv-dev                  .okv.dev seed file (forces dev/test target profile)
",
        "export" => @"okv export — Export to external format

Required:
  --output <path>          Output file path
  --format <format>        Format (v0.2: 'okv-dev')

Options (okv-dev):
  --source-profile <name>  Profile to export (default: dev)
  --strip-secrets         Replace sensitive field values with 'REDACTED-***'
  --allow-prod-profile    Allow exporting 'prod' (default refuses, per PRD §5.5.3)

WARNING: okv-dev files contain a PLAINTEXT Dev Master Key (OKV_FORMAT §11.4).
Distribute only in dev/test contexts; never for production credentials.

Supported formats (v0.2):
  okv-dev                  .okv.dev seed file
",
        "sync" => @"okv sync — Multi-device sync

Subcommands:
  status     Show local manifest (vector clock, profiles, last-modified)
  force      Pull from --remote and merge via vector clock
  pause      Disable auto-sync (GUI FileSystemWatcher + CLI 'sync force' still warn)
  resume     Re-enable auto-sync

Options (force):
  --remote <vault.okv>     Remote vault file to pull from

Notes:
  - Sync uses vector clocks per PRD §10.2 to detect concurrent edits.
  - On conflict (same version, different content), local-side wins (PRD §4.7).
  - dev / test profiles default to sync=off (use 'profile update' to enable).
  - pause / resume is process-local (each CLI invocation is independent).

Exit codes:
  0 success  2 arg error  3 locked  14 sync conflict
",
        "config" => @"okv config — User configuration

Subcommands:
  get       Read a single config value
  set       Write a single config value
  list      List all config keys + values

Options (get / set):
  --key <key>       Config key (run 'okv config list' for valid keys)
  --value <value>   (set only) New value

Notes:
  - Config is process-local; values are read by the running CLI session.
  - The GUI's SettingsWindow writes to the same keys for cross-process persistence.

Exit codes:
  0 success  2 arg error
",
        _ => Root
    };
}
