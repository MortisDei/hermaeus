# Security Policy

## Supported Versions

Hermaeus is pre-1.0 alpha software. Only the latest released version is
supported with security fixes; there are no maintained older branches.

## Reporting a Vulnerability

Please do not open a public issue for a security vulnerability.

Use GitHub's [private vulnerability reporting](https://github.com/MortisDei/hermaeus/security/advisories/new)
for this repository. This notifies the maintainer directly without exposing
the report publicly.

If that is unavailable to you, open a GitHub issue with the `security` label
containing only "I have a security report" and no details; the maintainer
will follow up to arrange a private channel.

This is a solo-maintained project. Response times are best effort, typically
within a week.

## Scope

In scope: the Hermaeus application itself (`src/`), its build and packaging
scripts, and its CI workflows.

Out of scope: vulnerabilities in third-party runtimes Hermaeus manages but does
not author (`llama.cpp`, Ollama, ONNX Runtime, etc.) — report those upstream.
See `docs/security-review.md` for the project's threat model and documented
security posture.
