[Download the installer ZIP](Codex-Fixed-Windows-x64-26.1007.2314.0-repair1.zip) (open the file, then use Download raw file), or see [Releases](https://github.com/WillyBilly06/codex-windows-memory-repair/releases).

ZIP SHA-256: `12eb1e8b0cff40bd8a625a05b3666c0de344bda1a912b57b1afc688149c191c8`

# Codex Fixed — experimental Windows local-copy repair

Supports **Windows x64 OpenAI.Codex package 26.1007.2314.0**, app 26.1007.21434, build 13901, only. This is an unofficial community repair for one reproduced history-loading OOM path. It is not an OpenAI release or a fix for every renderer crash.

## Use

1. Extract this ZIP and open **Codex-Fixed-Installer.exe**.
2. Keep the detected installed app folder, or select the folder containing ChatGPT.exe in your installed Codex package.
3. Choose a new empty destination and click **Create fixed copy**. Allow roughly 2.4 GB of disk space. The installer verifies all 3,594 source files and refuses a different or modified build.
4. Click **Open Codex Fixed**. Subsequently open **Codex-Fixed.exe** inside the destination, and create a shortcut to that file if desired.

The installer creates fresh files outside WindowsApps and never patches the original installed application. It does not require administrator access, Node, Python, or a command window. Its automatic source detection briefly runs PowerShell hidden. The copied application gets its own UI profile, but still uses your existing Codex account, backend and conversation files. This is not an isolated account. Do not run both app copies against an active task unless you intend to share that backend.

The installer and modified app are unsigned. Modifying the copied executable's ASAR integrity resource invalidates its OpenAI Authenticode signature. Electron's ASAR archive validation stays enabled. No instructions here ask you to disable Windows security protections. Inspect the included source and hashes before choosing to run this community build.

## What changed

- **Agent tab restoration:** uses existing descendant verification and ancestry metadata rather than loading the complete parent conversation to locate a spawn record. This was the captured trigger for excessive live history retention.
- **Complete-history loading:** explicit estimated payload budgets of 128 MiB per operation and 256 MiB shared pending data per JavaScript context, with cleanup on every exit. Oversized operations fail explicitly; incomplete histories are not reported as complete.
- **Resident history:** enables existing eligible-history eviction and adds byte thresholds (96 MiB high, 64 MiB target), retaining existing viewport, reload-anchor and in-progress-item protections.
- **Older messages:** automatic page loading requires user scroll, preventing layout changes from repeatedly requesting older pages. Explicit retry and ordinary pagination remain available.
- **Normal launch:** a native GUI launcher opens the repaired copy without CMD, diagnostic logging flags, or a remote debugger.
- **Updates:** quiet checks of OpenAI's public Windows release feed on startup and every six hours. Help → Check for Updates uses the original Windows dialog options/style. This is a check-only adaptation, not the original MSIX updater engine: it does not replace the repair or install updates. A newer official build requires a newly verified repair.

The app requests its own Codex.Fixed Windows AppUserModelID where supported. This does **not** establish that Task Manager will group every backend process, shell, or model-launched command. Full packaged-app integration is not restored in this loose copy. An unsupported per-window grouping call that caused a startup error during development was removed before this package was built.

## Evidence and limits

The original signed app crashed with explicit V8 OOM after about 425 seconds on the captured large conversation. A native repaired-copy run on the same conversation lasted 9 minutes 36 seconds with 116 consecutive visible-thread samples, no renderer crash, and natural JS heap of 470–624 MiB. Older-page loading and return to latest messages also worked. No forced garbage collection was used during that observation.

That native soak used the same history repair before the later launcher/update changes. The final installer has separately reproduced byte-identical final app.asar and ChatGPT.exe files, and the final native copy starts successfully. Eleven extracted-function regression checks pass; these are separate from native validation. See EVIDENCE.json and TESTS.json. This finite observation does not prove indefinite stability or a shared cause with other issue reports.

The budgets estimate payload size; they are not hard process-RAM limits. Large full-history copy/export operations can now return a memory-budget error and have not been rewritten as streaming operations. GPU, other windows, current/visible content and individual incoming pages can still use additional memory.

No full OpenAI application binary, account profile, token, conversation, raw log, heap snapshot or crash dump is included in the ZIP. The installer contains patch instructions, archive-header metadata and our launcher. It reconstructs the copy from your own exact installed build. This package does not support ARM64, macOS or other Windows versions of Codex.

To stop using it, close the repaired app and use your original Codex shortcut. The installer creates no registry entries, services, tasks, or automatic startup entry. Its destination folder can be removed after the copy is closed; the original installation remains available.

Technical report: https://github.com/openai/codex/issues/53012#issuecomment-6108915768

## Inspect or rebuild the installer

Installer.cs and Launcher.cs are the native C# source. manifest.json contains version pins and UTF-8 byte edits; header.gz contains the rebuilt ASAR header, not the packed application contents. Build-Installer.ps1 uses the Windows .NET Framework compiler and embeds those local resources. The original app binaries are not required to compile the installer, but are required when running it to create a repaired copy.
