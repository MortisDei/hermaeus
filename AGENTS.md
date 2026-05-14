# AGENTS.md

Aether is a local-first .NET/Avalonia app.

Rules:
- Be token-efficient. Read only relevant files.
- Prefer local/offline solutions.
- Be security-conscious.
- If you discover a bug, unsafe behaviour, data leak risk, or vulnerable pattern while working, fix it if it is in scope; otherwise add it to TODO/docs and mention it in the final response.
- Make the smallest complete change that solves the task.
- Prefer focused diffs, but do not avoid necessary cross-file changes or refactors.
- Follow existing patterns unless there is a clear reason to improve them.
- Do not rewrite unrelated code.
- Implement tasks fully. Do not leave shortcuts, stubs, TODO placeholders, or partial implementations unless explicitly requested.
- Do not invent missing APIs, files, behaviour, or architecture.
- Never use em dashes.

Before completing any task:
1. Ensure documentation is updated if behaviour, commands, setup, architecture, or user-facing features changed.
2. Run `dotnet build` and relevant tests.
3. Commit the completed work.
4. Push the commit only after build/tests pass and docs reflect the project.

If unable to run build/tests, update docs, commit, or push, say exactly what was not done and why.

Final response:
- What changed.
- Build/test result.
- Risks or follow-up work, if any.
