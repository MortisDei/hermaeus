# Security Policy

## Supported Versions

Hermaeus is pre-1.0 alpha software. Only the latest released version is
supported with security fixes; there are no maintained older branches.

## Reporting a Vulnerability

Please do not open a public issue for a security vulnerability.

If GitHub private vulnerability reporting is enabled for the repository, use
[private vulnerability reporting](https://github.com/MortisDei/hermaeus/security/advisories/new).
This keeps the report out of public issue history.

If that option is unavailable, open a GitHub issue with the `security` label
containing only "I have a security report" and no details. Do not include a
proof of concept, logs, paths, account information, or vulnerability details
in the issue. The maintainer will follow up to arrange a private channel.

This is a solo-maintained project. Response times are best effort, typically
within a week.

## Scope

In scope: the Hermaeus application itself (`src/`), its build and packaging
scripts, and its CI workflows.

Out of scope: vulnerabilities in third-party runtimes Hermaeus manages but does
not author (`llama.cpp`, Ollama, ONNX Runtime, etc.) — report those upstream.
See `docs/security-review.md` for the project's threat model and documented
security posture.
