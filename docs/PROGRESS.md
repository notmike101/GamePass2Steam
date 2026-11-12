# Implementation ledger

2026-11-12: Empty upstream cloned into provided workspace; work branch feat/windows-app. No existing work to preserve. State of Decay 2 has a GDK config and registered full-trust app whose executable is GameLaunchHelper.exe. Real executable is declared separately in config. Steam has two local profiles and is running.

Decision: fully C# for direct Windows integration and one runtime. User delegated architecture and prohibited subagents; design/plan/review are inline, no extra approval gates. Use current checkout rather than creating an unnecessary worktree. Preserve research scratch only in ignored .local.

Pre-flight: Core Game identity is consumed by discovery, launcher and Steam. Session tracker is consumed by native launcher and deterministic tests. Typed VDF nodes are consumed only by Steam safety layer and tests. UI/CLI share services. No conflicting interfaces.
