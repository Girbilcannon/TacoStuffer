# Security reporting

Please report suspected security vulnerabilities privately. Normal UI,
performance, rendering, and compatibility bugs belong in the public Issues form.

Use this repository's **Security → Advisories → Report a vulnerability** action
when available. It sends a private report to the maintainers. If private
reporting is unavailable, open an issue requesting a private reporting channel
without posting exploit instructions, private data, or a malicious sample.

Include the build number, Windows version, affected feature, reproduction steps,
potential impact, and a minimal safe sample where possible. Useful security
reports can involve malicious pack import, path traversal, unexpected writes,
unintended code execution, or a trust-boundary failure.

Reports are prioritized for the latest available beta. There is no guaranteed
response time, support contract, or bug bounty. Please allow the maintainer time
to investigate and coordinate any disclosure. Do not test against other users,
their accounts, or their data without permission.

Download verification is described in [VERIFY-DOWNLOAD.md](docs/VERIFY-DOWNLOAD.md).
For source contributions, see [CONTRIBUTING.md](CONTRIBUTING.md).
