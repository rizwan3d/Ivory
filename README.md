# Tusk

Tusk is a cross-platform CLI tool that manages **local PHP runtimes** and runs **Composer** and PHP scripts in a consistent, project-aware way.

It lets you:

- Install and manage multiple PHP versions under `~/.tusk`
- Pick a **default (global)** PHP version and **per-project** version
- Generate a `tusk.json` project config
- Run PHP directly via Tusk
- Proxy Composer commands through a known PHP version
- Define reusable “scripts” in `tusk.json` and run them easily
- Inspect your environment and check what Tusk sees (`tusk doctor`)

---

## Table of contents

- [Features](#features)
- [Requirements](#requirements)
- [Installation](#installation)
- [Configuration (`tusk.json`)](#configuration-tuskjson)
- [Commands](#commands)
  - [`tusk init`](#tusk-init)
  - [`tusk install`](#tusk-install)
  - [`tusk list`](#tusk-list)
  - [`tusk php`](#tusk-php)
  - [`tusk run`](#tusk-run)
  - [`tusk scripts`](#tusk-scripts)
  - [`tusk use`](#tusk-use)
  - [`tusk default`](#tusk-default)
  - [`tusk composer`](#tusk-composer)
  - [`tusk isolate`](#tusk-isolate)
  - [`tusk scaffold:ci`](#tusk-scaffoldci)
  - [`tusk scaffold:docker`](#tusk-scaffolddocker)
  - [`tusk completion`](#tusk-completion)
  - [`tusk doctor`](#tusk-doctor)
- [Data & directories](#data--directories)
- [License](#license)

---

## Features

- **Local PHP version management**
  - Installs PHP builds under `~/.tusk/versions/<platform>/<version>`
  - Uses a JSON manifest (`~/.tusk/php-versions.json`) to know where to download PHP from
  - `tusk install 8.3` etc.

- **Project-aware PHP selection**
  - Per-project PHP version via `.tusk.php-version`
  - Global default PHP version via `tusk default`
  - Falls back to system `php` when appropriate

- **Project configuration with `tusk.json`**
  - `php.version`, extra INI and CLI args
  - Named scripts that describe how to run your app/tools
  - Framework presets (Generic, Laravel, Symfony) via `tusk init`

- **Composer integration**
  - Finds or downloads a `composer.phar`
  - Runs Composer using a specific PHP version
  - Can run Composer scripts through your configured PHP toolchain

  - **Nice DX**
    - Uses `System.CommandLine` for a clean CLI
    - Uses `tusk doctor` to show what Tusk sees (PHP, Composer, config, etc.)
    - Scaffolds a basic `public/index.php` that tries to load `vendor/autoload.php`
    - Optional per-project PHP home (`.tusk/php`) to keep ini overrides local

---

## Requirements

- **.NET SDK** with support for `net10.0` (Tusk targets `net10.0` in `Tusk.csproj`).
- Supported runtime platforms:
  - Windows x64
  - Linux x64
  - macOS arm64 (Apple Silicon)
---

## Installation

### Quick install (prebuilt binaries)

Downloads come from the latest release at `https://github.com/rizwan3d/Tusk/releases/latest/download/<platform-archive>`.

```bash
# Linux (x64)
bash <(curl -fsSL https://raw.githubusercontent.com/rizwan3d/Tusk/refs/heads/master/install/linux.sh)

# macOS (arm64 or x64)
bash <(curl -fsSL https://raw.githubusercontent.com/rizwan3d/Tusk/refs/heads/master/install/mac.sh)
```

```powershell
# Windows (PowerShell 5+; AMD64 or ARM64)
powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "irm https://raw.githubusercontent.com/rizwan3d/Tusk/refs/heads/master/install/windows.ps1 | iex"
```

The installers place `tusk` under a user-scoped bin directory (`~/.tusk/bin` on Unix, `%LOCALAPPDATA%\Tusk\bin` on Windows) and add it to `PATH`.

### Uninstall

```bash
# Linux/macOS
bash <(curl -fsSL https://raw.githubusercontent.com/rizwan3d/Tusk/refs/heads/master/install/uninstall.sh)
```

```powershell
# Windows (PowerShell 5+)
powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "irm https://raw.githubusercontent.com/rizwan3d/Tusk/refs/heads/master/install/uninstall.ps1 | iex"
```

---

## Configuration (`tusk.json`)

Tusk looks for a `tusk.json` starting from the current directory and walking up parent directories.

### Example `tusk.json`

```json
{
  "php": {
    "version": "8.3",
    "ini": [
      "memory_limit=512M"
    ],
    "args": [
      "-d", "date.timezone=UTC"
    ]
  },
  "scripts": {
    "serve": {
      "description": "Run PHP built-in server",
      "phpFile": "-S",
      "phpArgs": [
        "localhost:8000",
        "public/index.php"
      ],
      "args": []
    },
    "console": {
      "description": "Run framework console",
      "phpFile": "bin/console",
      "phpArgs": [],
      "args": []
    }
  }
}
```

### Schema (simplified)

* **`php`**

  * `version` (string, optional) - preferred PHP version for this project (e.g. `"8.3"` or `"latest"`).
  * `ini` (array of string) - extra `-d` INI settings to pass to PHP.
  * `args` (array of string) - extra arguments always passed to PHP.

* **`scripts`** (object)

  * keys = script names (e.g. `"serve"`, `"test"`, `"console"`).
  * values = **TuskScript**:

    * `description` (string, optional) – human-readable description.
    * `phpFile` (string, **required**) – the PHP entry file or command (e.g. `"public/index.php"`, `"bin/console"`, `"-S"`).
    * `phpArgs` (array of string, optional) – arguments placed **before** the `phpFile` or used as raw PHP arguments.
    * `args` (array of string, optional) – additional arguments appended after `phpFile` / `phpArgs`.

Internally, these are represented by `TuskConfig` and `TuskConfig.TuskScript` in `Tusk.Domain.Config`.

---

## Commands

### `tusk init`

Create a starter `tusk.json` for the current project and scaffold `public/index.php` if it doesn’t exist.

```bash
tusk init [--framework <Generic|Laravel|Symfony>] [--php <version>] [--force]
```

* `--framework` – choose a preset (Generic, Laravel, Symfony).
* `--php` – default PHP version to write into `tusk.json`.
* `--force` – overwrite an existing `tusk.json`.

This uses `IPhpVersionResolver`, `TuskConfigSerializer` and `IPublicIndexScaffolder` under the hood.

---

### `tusk install`

Install a PHP runtime for the current platform.

```bash
tusk install <version>
```

Examples:

```bash
tusk install 8.3
tusk install 8.2
```

This calls `IPhpInstaller.InstallAsync`, which:

* Looks up the version in `~/.tusk/php-versions.json` (`PhpVersionsManifest`).
* Downloads the appropriate artifact.
* Verifies SHA256.
* Extracts it into `~/.tusk/versions/<platform>/<version>`.

---

### `tusk list` / `tusk ls`

List installed PHP versions:

```bash
tusk list
# or
tusk ls
```

Uses `IPhpInstaller.ListInstalledAsync()` and prints out versions in order.

---

### `tusk php`

Run `php` through Tusk with a specific PHP version (or project/global default).

```bash
tusk php [--php <version>] [--] [args...]
```

Examples:

```bash
tusk php -- -v
tusk php --php 8.3 -- -v
tusk php --php 8.3 -- -r "echo 'Hello from Tusk';"
```

This calls `IPhpRuntimeService.RunPhpAsync` with `null` script path and just forwards your arguments.

---

### `tusk run`

Run a named script from `tusk.json` or a direct PHP file.

```bash
tusk run <script-or-file> [--php <version>] [--] [extra-args...]
```

Behavior:

1. Tusk loads the nearest `tusk.json` via `IProjectConfigProvider`.
2. If `<script-or-file>` matches a script name in `scripts`, it:

   * Builds the PHP command from `php`, `phpFile`, `phpArgs`, and `args`.
3. Otherwise, it treats `<script-or-file>` as a path to a PHP file relative to the current directory.

Examples:

```bash
tusk run serve
tusk run console -- migrate
tusk run public/index.php --php 8.3
```

---

### `tusk scripts`

List available scripts from `tusk.json`:

```bash
tusk scripts
```

Shows:

* Script name
* Description
* The effective PHP command line that would be executed.

---

### `tusk use`

Set the **project-local** PHP version by writing `.tusk.php-version` in the current directory:

```bash
tusk use <version>
```

Example:

```bash
tusk use 8.3
```

Tusk will prefer this version when running commands inside this project directory.

---

### `tusk default`

Set the **default / global** PHP version (e.g. stored in `~/.tusk/config.json` or equivalent):

```bash
tusk default <version>
```

Example:

```bash
tusk default 8.2
```

Used when there is no `.tusk.php-version` and no `php.version` in `tusk.json`.

---

### `tusk composer`

Run Composer through Tusk.

```bash
tusk composer [--php <version>] [--] [args...]
```

Examples:

```bash
tusk composer install
tusk composer update
tusk composer run-script test
tusk composer --php 8.3 -- install
```

Internally, `IComposerService`:

* Locates `composer.json` and project root.
* Ensures a `composer.phar` is available (downloading if needed).
* Uses `IPhpRuntimeService` to run `php composer.phar <args>` with the chosen PHP version.

---

### `tusk isolate`

Create a per-project PHP home in `.tusk/php/` with its own `php.ini` and `conf.d/` directory for extension ini files.

```bash
tusk isolate
```

After running this once in a project, Tusk will automatically set `PHPRC` and `PHP_INI_SCAN_DIR` to use your local `php.ini` and `conf.d/` when executing PHP/Composer through Tusk, keeping settings and extensions from bleeding across projects.

---

### `tusk scaffold:ci`

Generate CI templates wired to Tusk for GitHub Actions or GitLab CI.

```bash
tusk scaffold:ci              # GitHub by default
tusk scaffold:ci --target gitlab
tusk scaffold:ci --target both --force
```

Creates `.github/workflows/tusk-ci.yml` and/or `.gitlab-ci.yml` running `dotnet build` plus a Tusk doctor + PHP version check.

---

### `tusk scaffold:docker`

Generate a Dockerfile and docker-compose.yml using the resolved PHP version.

```bash
tusk scaffold:docker
```

Produces a simple `Dockerfile` (php:<version>-cli) and `docker-compose.yml` exposing port 8000; extend to install needed PHP extensions.

---

### `tusk completion`

Generate a shell completion script (bash, zsh, fish, PowerShell) that includes commands, tusk.json script names, and PHP versions/aliases from your manifest.

```bash
tusk completion bash     # bash
tusk completion zsh      # zsh
tusk completion fish     # fish
tusk completion powershell
```

Tip: `source <(tusk completion bash)` or add to your shell init file.

---

### `tusk doctor`

Inspect environment and show what Tusk sees:

```bash
tusk doctor
```

Typical output includes:

* Current platform (`PlatformId`)
* Home directory and `~/.tusk` layout (versions, cache, manifest path)
* Installed PHP versions
* Effective project / global PHP version
* Locations of `composer.phar` and system `composer`
* Whether `tusk.json` was found and where
* Whether per-project isolation is enabled for this directory (and paths to `.tusk/php/php.ini` and `conf.d` if present)

This uses:

* `IEnvironmentProbe` to find system `php`/`composer`
* `IPhpInstaller` to inspect installed versions
* `IComposerService` to locate Composer
* `IProjectConfigProvider` to find `tusk.json`

---

## Data & directories

Tusk stores its own data under:

```text
~/.tusk/
    php-versions.json   # PhpVersionsManifest (download sources & checksums)
    versions/           # Installed PHP runtimes, per platform/version
    cache/php/          # Download cache for PHP archives
    config.json         # Global config (e.g. default PHP version)
```

Per-project data:

* `tusk.json` – project configuration (PHP + scripts).
* `.tusk.php-version` – per-project PHP version pin.
