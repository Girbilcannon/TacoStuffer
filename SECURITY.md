# Security reporting

Applies to the current **1.0.7** release. Reports are prioritized against the latest official release; no separate maintenance commitment is made for older betas.

## Report privately

Use the repository's **Security → Advisories → Report a vulnerability** action when available. If private reporting is unavailable, request a private contact channel through an issue or [Discord](https://discord.gg/KU8jwACydZ) without posting sensitive data, exploit details or malicious files publicly.

Include the application version, Windows version, affected feature, steps to reproduce, potential impact and a minimal safe example. Relevant reports include pack/archive path traversal, unexpected writes/deletes, unsafe project paths, unintended code execution and sensitive-data disclosure.

Please allow time to investigate and coordinate disclosure. There is no guaranteed response time, support contract or bug bounty. Do not test against other people's systems, accounts or files without permission.

## Scope and operating boundaries

Taco Stuffer opens imported XML, archives, binary trails, images, documents and custom definitions. Treat external packs and documents as untrusted input and back up projects before importing or restructuring them.

Lua editing and packaging do **not** execute scripts in Taco Stuffer. A target reader can execute scripts under its own implementation and permissions. Markdown preview disables raw HTML and filters URLs, but remote image references can still perform network requests. The app's checks are not a guarantee that arbitrary input is harmless.

Folder operations affect actual project files, and compile can write to multiple configured destinations. Confirm these locations before sharing or testing. See [PRIVACY.md](PRIVACY.md) for clipboard, telemetry, mouse observation and local logs.

## Downloads and normal bug reports

Use [official releases](https://github.com/Girbilcannon/TacoStuffer/releases). A source build or a successful compiler run does not establish that a distributed binary is signed, independently audited or free of defects. Follow any verification instructions/hashes supplied with the specific release; [download verification guidance](docs/VERIFY-DOWNLOAD.md) describes the process.

Normal UI, performance, rendering and reader-compatibility reports belong in repository Issues or Discord. Include concise reproduction steps and redact private information from attachments.
