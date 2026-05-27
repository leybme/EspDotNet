# ESPTool — Improvement Plan

Code-quality remediation plan based on a full scan of the library (branch: `Refactoring`).
Current grade: **B− (78/100)**. Testing work is intentionally out of scope for this plan.

Items are ordered by leverage: correctness first, then library hygiene, then polish.

---

## 1. Correctness fixes (highest priority)

### 1.1 Fix partial-read truncation in flash upload
- **File:** [FlashUploadTool.cs:34-36](ESPTool/Tools/FlashUploadTool.cs#L34-L36)
- **Problem:** `if (bytesRead != len) break;` silently aborts the upload when a stream
  returns fewer bytes than requested (legal stream behavior). Can truncate/corrupt a flash write.
- **Fix:** Loop reading into the block buffer until `len` bytes are read or the stream truly ends;
  only treat a real short final read as end-of-data. Consider a `ReadExactlyAsync` helper.
- **Effort:** S

### 1.2 Stop swallowing response-decode failures
- **File:** [BootLoaderCommandExecutor.cs:56-75](ESPTool/Loaders/ESP32BootLoader/BootLoaderCommandExecutor.cs#L56-L75)
- **Problem:** `catch { response.Success = false; }` hides all decode errors, including
  out-of-range indexing when `Size < 4`. The real failure reason is lost.
- **Fix:** Validate frame length before indexing (`Payload[Size - 4]`, `[Size - 3]`); on malformed
  frames set an explicit error status and/or log. Avoid catch-all that discards the exception.
- **Effort:** S

### 1.3 Make zlib compression not require a seekable input stream
- **File:** [ZlibCompressionHelper.cs:8-23](ESPTool/Utils/ZlibCompressionHelper.cs#L8-L23)
- **Problem:** Adler-32 is computed by rewinding the input stream (`Position = 0`) after
  compression; throws on non-seekable inputs.
- **Fix:** Compute Adler-32 incrementally during the copy (single pass), so no seek is needed.
- **Effort:** M

### 1.4 Document the DTR/RTS self-assignment workaround
- **File:** [Communicator.cs:74-75](ESPTool/Communication/Communicator.cs#L74-L75)
- **Problem:** `_serialPort.DtrEnable = _serialPort.DtrEnable;` is a real Windows RTS/DTR quirk
  workaround but reads as a bug with no explanation.
- **Fix:** Add a comment explaining the Windows driver quirk it works around (link to source if known).
- **Effort:** XS

---

## 2. Library hygiene

### 2.1 Replace `Console.WriteLine` with proper logging
- **File:** [ConfigProvider.cs:29,49](ESPTool/Config/ConfigProvider.cs#L29)
- **Problem:** A library should not write to stdout. `Microsoft.Extensions.Logging` is already
  referenced but unused.
- **Fix:** Either thread an `ILogger`/`ILoggerFactory` through the relevant classes, or remove the
  unused logging package. Pick one and apply consistently.
- **Effort:** M

### 2.2 Add `ConfigureAwait(false)` to all library awaits
- **Files:** all `await` sites (Communication, Loaders, Tools)
- **Problem:** No `ConfigureAwait(false)` anywhere. As a NuGet library consumed by GUI apps
  (e.g. ESPFlasher), this is a deadlock vector in sync-over-async contexts —
  including [FlashDownloadTool.cs:148](ESPTool/Tools/FlashDownloadTool.cs#L148) (`.GetAwaiter().GetResult()`).
- **Fix:** Append `.ConfigureAwait(false)` on every internal await. Reconsider the sync `Read`
  override that blocks on async.
- **Effort:** M

### 2.3 Use specific exception types consistently
- **Files:** [ESP32BootLoader.cs](ESPTool/Loaders/ESP32BootLoader/ESP32BootLoader.cs),
  [SoftLoader.cs](ESPTool/Loaders/SoftLoader/SoftLoader.cs), executors
- **Problem:** Mix of generic `throw new Exception(...)` and `InvalidOperationException`;
  callers can't catch meaningfully.
- **Fix:** Introduce a small exception type (e.g. `EspCommandException` carrying command byte +
  response status) and use it for protocol failures. Reserve `InvalidOperationException`/
  `ArgumentException` for misuse.
- **Effort:** M

### 2.4 Replace byte-polling read with a timeout-aware read
- **File:** [SlipFraming.cs:61-71](ESPTool/Communication/SlipFraming.cs#L61-L71)
- **Problem:** `while (BytesToRead == 0) await Task.Delay(10)` adds up to 10ms latency per byte and
  has no read timeout (only cancellation rescues a hung device).
- **Fix:** Read from `BaseStream.ReadAsync` into a buffer (batch bytes) and add a configurable
  read timeout. Removes busy-wait and improves throughput.
- **Effort:** M

### 2.5 Avoid O(n²) payload accumulation
- **File:** [RequestCommandBuilder.cs:25-30](ESPTool/Commands/RequestCommandBuilder.cs#L25-L30)
- **Problem:** `Payload.Concat(...).ToArray()` reallocates the whole array on every append.
- **Fix:** Back the builder with a `List<byte>` (or `ArrayBufferWriter<byte>`); materialize once in `Build()`.
- **Effort:** S

---

## 3. Documentation & repo polish

### 3.1 Fix the broken README example link
- **File:** [README.md:69](README.md#L69)
- **Problem:** Links to `Example.cs`, which does not exist in the repo.
- **Fix:** Either add a real `Example.cs` (and include it in compilation) or replace the link with
  an inline usage snippet in the README.
- **Effort:** S

### 3.2 Remove or reinstate dead code
- **File:** [GetAddressesTool.cs](ESPTool/Tools/GetAddressesTool.cs) — excluded from compilation in
  [EspDotNet.csproj:18,70](ESPTool/EspDotNet.csproj#L18) but left in the tree.
- **Fix:** Delete it, or finish and re-include it. Don't ship dead code.
- **Effort:** XS

### 3.3 Resolve naming drift — standardize on `EspDotNet`
- **Problem:** Package `ESPTool`, assembly `EspDotNet`, main class `ESPToolbox` — three names for
  one concept confuses consumers.
- **Decision:** `EspDotNet` is the chosen name. The assembly and root namespace already use it,
  so this is the lowest-churn target.
- **Fix:**
  - Rename `PackageId` `ESPTool` → `EspDotNet` in [EspDotNet.csproj:4](ESPTool/EspDotNet.csproj#L4)
    (note: this changes the NuGet package id — publish as a new package and/or add a deprecation
    pointer from the old `ESPTool` id; coordinate with a version bump).
  - Rename the entry class `ESPToolbox` → `EspToolbox` (or `EspDotNetToolbox`) for consistency.
  - Update README title/badges, repo references, and `PackageTags`.
  - Namespace `EspDotNet` already aligned — no change needed.
- **Effort:** M (public API + package id change — version-bump and document the migration)

### 3.4 Document protocol magic numbers
- **Files:** [RequestCommandBuilder.cs:67-75](ESPTool/Commands/RequestCommandBuilder.cs#L67-L75)
  (checksum seed `0xEF`, header offset `16`), command bytes across loaders.
- **Fix:** Name the constants and add short comments referencing the esptool serial protocol doc.
- **Effort:** S

---

## Suggested sequencing

1. **Phase 1 — Correctness:** 1.1, 1.2, 1.3, 1.4
2. **Phase 2 — Hygiene:** 2.1, 2.3, 2.5, then 2.2 and 2.4
3. **Phase 3 — Polish:** 3.1, 3.2, 3.4, then 3.3 (versioned)

Items 1.1 and 3.1 are the quickest wins with the most visible impact.
