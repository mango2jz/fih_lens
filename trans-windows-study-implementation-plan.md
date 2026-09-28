# Trans Windows — Study + Implementation Plan

## 1. Project Context

This document defines the study plan, technical architecture, implementation roadmap, and engineering guidelines for the **Windows version of Trans**, a desktop translation and language-learning tool.

The project has separate native implementations for each operating system:

- **macOS:** Swift / SwiftUI / native macOS APIs
- **Windows:** C# / WinUI 3 / Win32 / WinRT

The Windows version should prioritize:

1. Native Windows integration
2. Low idle CPU usage
3. Low memory usage
4. Fast popup and text-capture response
5. Clean architecture that allows translation providers and AI models to be replaced
6. Good support for global hotkeys, selected-text capture, OCR, system tray, floating windows, and eventually local AI inference
7. Learning value for the developer while still producing a usable product

The developer has worked with **C# before**, so the Windows implementation should build on that experience instead of introducing a completely new language.

Available study/implementation time:

- **~20 hours per week**
- Recommended split:
  - **30% study**
  - **70% implementation**

Primary development environment:

- **IDE:** Visual Studio 2022
- **AI coding assistant:** Antigravity CLI
- **OS:** Windows
- **Language:** C#

---

# 2. Recommended Windows Stack

## Core Stack

| Area | Technology |
|---|---|
| Language | C# |
| Runtime | .NET 10 LTS |
| UI | WinUI 3 |
| Windows framework | Windows App SDK |
| Architecture | Clean-ish architecture + MVVM |
| MVVM library | CommunityToolkit.Mvvm |
| Windows integration | Win32 + WinRT via C# interop/PInvoke |
| Global hotkeys | Win32 `RegisterHotKey` |
| Text extraction | UI Automation → Clipboard fallback → OCR fallback |
| Screen capture | `Windows.Graphics.Capture` |
| Local persistence | SQLite |
| HTTP | `HttpClient` |
| JSON | `System.Text.Json` |
| Translation/AI | Provider abstraction |
| Local AI later | Windows ML / ONNX |
| Packaging | MSIX |
| Testing | xUnit |
| IDE | Visual Studio 2022 |
| AI assistant | Antigravity CLI |

---

# 3. Why This Stack

## 3.1 Why C# + WinUI 3

The Windows version should be a truly native Windows desktop application.

The application will depend heavily on operating-system features such as:

- global keyboard shortcuts
- window handles
- foreground window detection
- selected-text extraction
- clipboard access
- accessibility/UI Automation
- screen capture
- floating windows
- multi-monitor positioning
- DPI scaling
- system tray integration
- OCR
- GPU/NPU inference

Because of this, native Windows APIs are an advantage rather than something to abstract away through a cross-platform framework.

Recommended direction:

```text
macOS
└── Swift
    └── SwiftUI
        └── native macOS APIs

Windows
└── C#
    └── WinUI 3
        ├── WinRT
        └── Win32
```

The two versions may share product concepts and architecture ideas, but they do not need to share UI code.

---

# 4. Why Not Other Stacks?

## 4.1 WPF

WPF is mature and perfectly viable.

It may initially be easier for a C# developer.

However, because this is a new Windows-native application, use **WinUI 3** unless a blocking technical limitation appears.

WPF remains a valid fallback if WinUI creates unacceptable friction for a particular requirement.

---

## 4.2 .NET MAUI

Do not use MAUI for the first Windows implementation.

The project intentionally has separate native implementations for macOS and Windows.

Using a cross-platform abstraction would reduce direct control over Windows APIs without providing meaningful value.

---

## 4.3 Avalonia

Avalonia is excellent for cross-platform desktop applications, but cross-platform UI reuse is not currently a project requirement.

Native Windows integration is more important.

---

## 4.4 Electron

Avoid Electron.

The application is expected to remain running for long periods, often all day.

A native C# application should provide:

- lower idle CPU usage
- lower memory usage
- faster startup
- better integration with Windows
- easier use of native APIs

---

## 4.5 C++

The theoretical maximum-performance stack would be:

```text
C++20/23
+
Win32
+
C++/WinRT
+
DirectX
```

Do **not** use this as the primary stack.

Instead:

```text
C# + WinUI 3
    ↓
Win32 / WinRT where necessary
    ↓
Optional C++ native module only if profiling proves it necessary
```

Translation latency, OCR latency, and network/LLM latency will usually dominate over the small runtime differences between C# and C++.

Optimize based on profiling rather than assumptions.

---

# 5. Development Tools

## 5.1 JetBrains Rider

Use **JetBrains Rider** as the main IDE.

Rider should be used for:

- solution management
- C# development
- refactoring
- debugging
- unit testing
- Git integration
- profiling where supported
- NuGet management
- code inspection
- architecture navigation

### Rider-specific workflow

Recommended solution layout:

```text
Trans.sln
```

Open the solution directly in Rider.

Useful Rider capabilities for this project:

- solution-wide analysis
- Find Usages
- Rename refactoring
- Move Type refactoring
- dependency visualization
- test runner
- debugger
- memory/CPU profiling integration where available
- Git diff and history

Use Rider's inspections aggressively, especially for:

- nullable reference types
- async misuse
- unnecessary allocations
- disposal issues
- incorrect API usage
- dead code

---

## 5.2 Antigravity CLI

Use **Antigravity CLI** as the AI helper for the project.

It should assist with implementation but not define the architecture independently.

Recommended uses:

- implementing a narrowly defined task
- generating repetitive boilerplate
- writing tests
- explaining unfamiliar Win32/WinRT APIs
- reviewing code
- suggesting refactors
- generating documentation
- creating small proof-of-concept implementations
- finding edge cases
- helping investigate bugs

Avoid asking the AI to:

- rewrite the entire project architecture at once
- make large undocumented changes
- introduce new frameworks without justification
- bypass interfaces to "make it work quickly"
- modify unrelated modules
- optimize code before profiling

### Recommended AI workflow

For each feature:

```text
1. Define requirement
2. Define interface / boundaries
3. Ask Antigravity CLI to implement a small scope
4. Review diff manually
5. Build
6. Test
7. Profile if performance-sensitive
8. Commit
```

### Example task style

Good prompt:

```text
Implement WindowsGlobalHotkeyService in Trans.Windows.

Requirements:
- implement IGlobalHotkeyService
- use RegisterHotKey
- handle WM_HOTKEY
- expose a C# event when the configured shortcut is pressed
- do not add additional NuGet packages
- do not modify ViewModels
- add unit-testable abstractions where possible
- document any Win32 interop code
```

Poor prompt:

```text
Implement global hotkeys for the entire app.
```

Prefer small, explicit tasks.

---

# 6. Target Architecture

Recommended high-level architecture:

```text
┌─────────────────────────────────────────┐
│              Trans.WinUI                │
│                                         │
│  Popup                                  │
│  Main Window                            │
│  Settings                               │
│  History                                │
│  System Tray                            │
└───────────────────┬─────────────────────┘
                    │
                    ▼
┌─────────────────────────────────────────┐
│            Trans.Application            │
│                                         │
│  TranslationCoordinator                 │
│  CaptureCoordinator                     │
│  HotkeyCoordinator                      │
│  HistoryService                         │
└──────┬─────────────────────────┬────────┘
       │                         │
       ▼                         ▼
┌───────────────┐       ┌─────────────────┐
│  Trans.Core   │       │ Trans.Windows   │
│               │       │                 │
│ Models        │       │ UI Automation   │
│ Interfaces    │       │ Clipboard       │
│ DTOs          │       │ Win32 Hotkeys   │
│ Languages     │       │ Screen Capture  │
│ Levels        │       │ OCR             │
└───────────────┘       └─────────────────┘
       │
       ▼
┌─────────────────────────────────────────┐
│          Trans.Infrastructure           │
│                                         │
│ Cloud AI providers                      │
│ Local Windows ML provider               │
│ SQLite                                  │
│ Settings                                │
│ Logging                                 │
└─────────────────────────────────────────┘
```

---

# 7. Core Architectural Rule

The UI must not know which translation provider is being used.

Use an abstraction such as:

```csharp
public interface ITranslationProvider
{
    Task<TranslationResult> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default);
}
```

Possible providers:

```text
ITranslationProvider
├── OpenAiTranslationProvider
├── DeepLTranslationProvider
├── GoogleTranslationProvider
├── CustomBackendTranslationProvider
└── WindowsLocalTranslationProvider
```

The same rule applies to OCR:

```csharp
public interface IOcrEngine
{
    Task<OcrResult> RecognizeAsync(
        OcrInput input,
        CancellationToken cancellationToken = default);
}
```

Possible implementations:

```text
IOcrEngine
├── WindowsOcrEngine
├── WindowsAiOcrEngine
└── FutureCustomOcrEngine
```

---

# 8. Translation Domain Model

Do not model translation as only:

```csharp
string translatedText;
```

The application is a language-learning tool as well as a translator.

Use structured requests and responses.

Example:

```csharp
public sealed record TranslationRequest
{
    public required string Text { get; init; }

    public Language SourceLanguage { get; init; }

    public Language TargetLanguage { get; init; }

    public LearningLevel? UserLevel { get; init; }

    public TranslationMode Mode { get; init; }
}
```

Example result:

```csharp
public sealed record TranslationResult
{
    public required string OriginalText { get; init; }

    public string? NaturalTranslation { get; init; }

    public string? Explanation { get; init; }

    public IReadOnlyList<VocabularyItem> Vocabulary { get; init; }

    public IReadOnlyList<GrammarPoint> Grammar { get; init; }

    public IReadOnlyList<TranslationAlternative> Alternatives { get; init; }

    public string? Context { get; init; }

    public string? Tone { get; init; }
}
```

---

# 9. Learning Levels

English:

```text
CEFR
├── A1
├── A2
├── B1
├── B2
├── C1
└── C2
```

Japanese:

```text
JLPT
├── N5
├── N4
├── N3
├── N2
└── N1
```

These levels should be represented as domain concepts rather than random prompt strings scattered throughout the codebase.

---

# 10. Text Capture Architecture

Text capture is likely to be more technically difficult than translation.

Target flow:

```text
User highlights text
       ↓
Ctrl + Shift + Space
       ↓
GlobalHotkeyService
       ↓
TextCaptureService
       ↓
┌─────────────────────────────┐
│ 1. Windows UI Automation    │
│           ↓ fail            │
│ 2. Clipboard capture        │
│           ↓ fail            │
│ 3. Screenshot + OCR         │
└─────────────────────────────┘
       ↓
Translation pipeline
       ↓
Floating popup
```

---

# 11. Global Hotkey Strategy

Prefer:

```text
RegisterHotKey
```

instead of a low-level keyboard hook.

Use low-level hooks such as `WH_KEYBOARD_LL` only if the application later requires arbitrary keyboard monitoring that `RegisterHotKey` cannot provide.

Recommended abstraction:

```csharp
public interface IGlobalHotkeyService
{
    Task RegisterAsync(
        HotkeyDefinition hotkey,
        CancellationToken cancellationToken = default);

    Task UnregisterAsync(
        HotkeyDefinition hotkey,
        CancellationToken cancellationToken = default);
}
```

Keep Win32 calls out of ViewModels.

---

# 12. Selected Text Capture

## Level 1 — UI Automation

Preferred capture path.

Advantages:

- fast
- low CPU usage
- no OCR
- no image processing
- does not modify clipboard

Attempt to retrieve selected text via Windows UI Automation.

This will not work in every application.

Possible problem applications:

- games
- custom-rendered applications
- canvas-based applications
- some Electron applications
- custom text controls

---

## Level 2 — Clipboard Fallback

Fallback flow:

```text
Save relevant clipboard state
        ↓
Send Ctrl+C
        ↓
Read selected text
        ↓
Process
```

Be careful when restoring clipboard contents.

Windows clipboard data can contain multiple formats.

Do not assume the clipboard contains only text.

---

## Level 3 — OCR

Final fallback:

```text
Screen
 ↓
Select region / detect region
 ↓
Capture
 ↓
OCR
 ↓
Text
 ↓
Translation
```

Use:

```text
Windows.Graphics.Capture
```

for native screen/window capture.

This path enables translation from:

- images
- manga
- games
- subtitles
- videos
- screenshots
- scanned PDFs
- custom-rendered interfaces

---

# 13. Translation Provider Architecture

Do not begin the project with a local LLM.

Start with:

```text
App
 ↓
ITranslationProvider
 ↓
Cloud provider
```

Get the product workflow working first.

Later:

```text
ITranslationProvider
 ├── CloudTranslationProvider
 ├── WindowsLocalProvider
 └── CustomModelProvider
```

The application must not depend directly on a particular AI provider.

---

# 14. Local AI Architecture

Later versions may use:

```text
TranslationRouter
        │
   ┌────┴──────────┐
   │               │
Cloud           Local
Provider        Provider
                   │
              Windows ML
             ┌─────┼─────┐
             ↓     ↓     ↓
            CPU   GPU   NPU
```

Windows ML / ONNX should be treated as a later optimization and offline-mode capability.

Do not block the MVP on local AI.

---

# 15. Native AOT

Do not enable Native AOT initially just because it sounds faster.

Native AOT can improve:

- startup
- memory usage
- deployment characteristics

But it can complicate:

- reflection
- trimming
- dynamic loading
- COM
- some interop scenarios
- third-party library compatibility

Start with normal .NET deployment.

Profile first.

Investigate Native AOT later only if startup or memory measurements justify it.

---

# 16. Repository Structure

Recommended mature structure:

```text
trans-windows/
│
├── src/
│   │
│   ├── Trans.Core/
│   │   ├── Models/
│   │   ├── Enums/
│   │   ├── Interfaces/
│   │   └── ValueObjects/
│   │
│   ├── Trans.Application/
│   │   ├── Translation/
│   │   ├── Capture/
│   │   ├── History/
│   │   └── Settings/
│   │
│   ├── Trans.Infrastructure/
│   │   ├── Translation/
│   │   ├── Persistence/
│   │   ├── Logging/
│   │   └── Configuration/
│   │
│   ├── Trans.Windows/
│   │   ├── Hotkeys/
│   │   ├── Clipboard/
│   │   ├── Automation/
│   │   ├── Capture/
│   │   ├── OCR/
│   │   ├── Windowing/
│   │   └── Interop/
│   │
│   └── Trans.WinUI/
│       ├── Views/
│       ├── ViewModels/
│       ├── Controls/
│       ├── Styles/
│       └── App.xaml
│
├── tests/
│   ├── Trans.Core.Tests/
│   ├── Trans.Application.Tests/
│   └── Trans.Infrastructure.Tests/
│
├── docs/
│   ├── ARCHITECTURE.md
│   ├── CAPTURE_PIPELINE.md
│   ├── TRANSLATION_SCHEMA.md
│   └── ROADMAP.md
│
├── experiments/
│   ├── HotkeyPlayground/
│   ├── ClipboardPlayground/
│   ├── UiAutomationPlayground/
│   ├── CapturePlayground/
│   ├── OcrPlayground/
│   └── PopupPlayground/
│
└── Trans.sln
```

Do not create all of these projects on day one if they slow development.

Initial version may begin with:

```text
Trans.Core
Trans.WinUI
Trans.Tests
```

and split into additional projects as complexity increases.

Avoid architecture for architecture's sake.

---

# 17. Study + Implementation Roadmap

Estimated time:

- **14–16 weeks for a strong V1**
- usable MVP significantly earlier
- approximately 20 hours/week

Core rule:

```text
30% study
70% implementation
```

Do not study Windows development for several weeks before building.

Each study topic should immediately support a project feature.

---

# 18. Phase 0 — Modern C# Refresher

## Week 1

### Study

Do not redo beginner C#.

Review:

```text
records
nullable reference types
pattern matching
LINQ
generics
interfaces
async / await
Task
CancellationToken
IAsyncEnumerable
dependency injection
IDisposable
IAsyncDisposable
Span<T> basics
```

Highest priority:

```text
async / await
CancellationToken
interfaces
dependency injection
records
```

### Implementation

Create:

```text
Trans.Core
Trans.WinUI
Trans.Tests
```

Optionally create the larger project structure immediately if it remains manageable.

Implement a fake provider:

```csharp
public sealed class FakeTranslationProvider : ITranslationProvider
{
    public Task<TranslationResult> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        // Return fake data.
    }
}
```

Goal:

```text
"Hello world"
       ↓
Fake provider
       ↓
TranslationResult
```

No real AI yet.

---

# 19. Phase 1 — WinUI 3

## Weeks 2–3

### Study

Learn:

```text
XAML
Grid
StackPanel
styles
resources
data binding
commands
INotifyPropertyChanged
MVVM
Navigation
Window
DispatcherQueue
```

Use:

```text
CommunityToolkit.Mvvm
```

Do not create a custom MVVM framework.

### Implementation

Build a basic main window:

```text
┌──────────────────────────────────────┐
│ TRANS                                │
├──────────────────────────────────────┤
│ Input                                │
│                                      │
│ [ selected / pasted text ]           │
│                                      │
├──────────────────────────────────────┤
│ EN → VI                              │
├──────────────────────────────────────┤
│ Translation                          │
│ Explanation                          │
│ Vocabulary                           │
└──────────────────────────────────────┘
```

Milestone:

> By the end of Week 3, manually pasted text should already be translatable.

---

# 20. Phase 2 — Translation Engine

## Week 4

Create:

```text
TranslationService
PromptBuilder
TranslationProvider
TranslationParser
```

Keep these concepts separate:

```text
Prompt construction
≠
Provider transport
≠
Response parsing
≠
UI rendering
```

Target pipeline:

```text
TranslationService
     ↓
PromptFactory
     ↓
ITranslationProvider
     ↓
Structured JSON
     ↓
TranslationParser
     ↓
TranslationResult
```

The UI must never parse raw LLM JSON.

---

# 21. Phase 3 — English → English

## Week 5

This is the first-priority language feature.

Implement:

```text
English
   ↓
English explanation
```

Support CEFR levels:

```text
A1
A2
B1
B2
C1
C2
```

Example input:

```text
It doesn't add up.
```

Possible A2 explanation:

```text
It means something does not seem correct or logical.
```

Possible C1 explanation:

```text
An idiomatic expression indicating that the available facts
are inconsistent or fail to produce a convincing explanation.
```

CEFR behavior belongs in the domain/application layer, not as scattered UI logic.

---

# 22. Phase 4 — English → Vietnamese

## Week 6

Add:

```text
English → Vietnamese
```

Target response:

```text
Original

Natural Vietnamese translation

English explanation

Context

Vocabulary

Grammar

Alternative meanings
```

Milestone:

> At this point the application should already be genuinely useful even before global capture is implemented.

---

# 23. Phase 5 — Global Hotkey

## Week 7

### Study

Learn:

```text
HWND
Windows messages
P/Invoke
RegisterHotKey
WM_HOTKEY
```

### Implementation

Create:

```text
IGlobalHotkeyService
```

Windows implementation:

```text
WindowsGlobalHotkeyService
```

Do not place Win32 calls inside:

- ViewModels
- views
- translation services

Isolate native Windows integration under `Trans.Windows`.

---

# 24. Phase 6 — Selected Text Capture

## Weeks 8–9

This will likely be one of the hardest MVP stages.

Create:

```text
ITextCaptureService
```

Windows implementation:

```text
WindowsTextCaptureService

├── UiAutomationCapture
├── ClipboardCapture
└── OcrCapture
```

Initially implement only:

```text
UI Automation
 ↓
Clipboard
```

Add OCR later.

### Compatibility Test Matrix

Test:

```text
Notepad
Chrome
Edge
Firefox
VS Code
JetBrains Rider
Word
Discord
Slack
PDF viewer
Windows Explorer
```

Maintain a table such as:

| Application | UI Automation | Clipboard | OCR |
|---|---:|---:|---:|
| Notepad | ✅ | ✅ | ✅ |
| Edge | ✅ | ✅ | ✅ |
| Rider | ? | ✅ | ✅ |
| VS Code | ? | ✅ | ✅ |
| Game | ❌ | ❌ | ✅ |

This matrix becomes part of product QA.

---

# 25. Phase 7 — Floating Popup

## Week 10

This is the point where the product starts feeling like an integrated translation utility.

Desired workflow:

```text
Highlight text
      ↓
Press hotkey
      ↓

               ┌──────────────────────┐
               │ doesn't add up       │
               │                      │
               │ "không hợp lý"       │
               │                      │
               │ Meaning              │
               │ Something seems ...  │
               └──────────────────────┘
```

### Study

Learn:

```text
AppWindow
HWND positioning
screen coordinates
cursor position
multi-monitor behavior
DPI scaling
focus
activation
always-on-top behavior
```

### Popup Requirements

The popup should:

```text
open near cursor
avoid stealing focus unnecessarily
close with Escape
close on click-away
resize to content
work on multiple monitors
handle DPI scaling
respond quickly
```

Spend significant time on this UX.

The popup experience is a major differentiator.

---

# 26. Phase 8 — System Tray

## Week 11

Add tray functionality.

Suggested menu:

```text
Tray icon

Left click:
    Open Trans

Right click:
    Translate
    History
    Settings
    Pause hotkey
    Exit
```

Add:

```text
Start with Windows
```

The application should be designed as an always-available utility.

---

# 27. Phase 9 — History

## Week 12

Introduce SQLite.

Suggested table:

```text
TranslationHistory
------------------
Id
OriginalText
SourceLanguage
TargetLanguage
Explanation
Translation
CreatedAt
Provider
PromptVersion
```

Optional:

```text
Favorites
```

Do not automatically introduce Entity Framework.

For this application's persistence needs, consider:

```text
SQLite + Dapper
```

or a simple repository layer.

Use EF Core only if its benefits become worthwhile.

---

# 28. Phase 10 — OCR

## Week 13

Implement:

```text
Capture area
    ↓
Windows.Graphics.Capture
    ↓
Image
    ↓
OCR
    ↓
Text
    ↓
Translation
```

This unlocks:

- manga
- anime subtitles
- games
- images
- YouTube
- screenshots
- scanned documents
- PDFs with non-selectable text

Keep OCR behind:

```text
IOcrEngine
```

so providers can be replaced later.

---

# 29. Phase 11 — Japanese

## Week 14

Add:

```text
Japanese → English
Japanese → Vietnamese
```

Add JLPT support.

Example:

```text
Original:
猫の手も借りたい

Reading:
ねこのてもかりたい

Romaji:
neko no te mo karitai

Literal:
I even want to borrow a cat's paw.

Natural:
I'm so busy I could use any help I can get.

JLPT:
Approx N2/N1 expression

Vocabulary:
猫      cat
手      hand
借りる  borrow

Grammar / idiom:
猫の手も借りたい
```

This demonstrates why structured translation results are essential.

---

# 30. Phase 12 — Performance

## Week 15

Do not optimize significantly before this stage unless a problem is obviously severe.

Measure:

```text
startup time
idle RAM
idle CPU
hotkey latency
capture latency
popup latency
translation latency
OCR latency
database latency
```

Initial targets:

```text
Idle CPU         ≈ 0%
Idle RAM         <100–150 MB target
Hotkey response  <50 ms
Popup appear     <100 ms
Capture          <100 ms when possible
```

Translation time depends mainly on the provider/network/model.

Important principle:

> Idle efficiency matters more than benchmark speed.

The application may remain open for an entire workday.

It should avoid:

- polling loops
- unnecessary timers
- repeated disk access
- repeated network checks
- unnecessary background threads
- constant CPU wakeups

Prefer event-driven architecture.

---

# 31. Phase 13 — Hardware Acceleration

## Week 16+

Only after the core product is stable should local model inference be investigated.

Study:

```text
Windows ML
ONNX
execution providers
CPU inference
GPU inference
NPU inference
model quantization
memory-mapped models
```

Potential architecture:

```text
TranslationRouter
        │
   ┌────┴──────────┐
   │               │
Cloud           Local
Provider        Provider
                   │
              Windows ML
             ┌─────┼─────┐
             ↓     ↓     ↓
            CPU   GPU   NPU
```

Goals for local models may include:

- offline translation
- privacy mode
- lower cost
- instant definitions
- local vocabulary analysis
- local classification
- CEFR/JLPT estimation

Do not make local inference a requirement for V1.

---

# 32. First Major Product Milestone

Do not define the first goal as:

> Build the complete translator.

Define the first major experience as:

```text
User highlights:

    "That doesn't add up."

User presses:

    Ctrl + Shift + Space

Within roughly 100 ms:

    a native popup appears

Then:

    loading state

Then:

┌───────────────────────────────────────┐
│ That doesn't add up.                  │
│                                       │
│ 🇻🇳 Điều đó không hợp lý.             │
│                                       │
│ Meaning                               │
│ Something seems inconsistent or      │
│ logically incorrect.                  │
│                                       │
│ Expression                            │
│ "add up" → make sense                 │
│                                       │
│ Level: B1                             │
└───────────────────────────────────────┘
```

If this interaction feels excellent, the core product is already valuable.

---

# 33. What to Study Deeply

Study these deeply:

```text
modern C#
async / await
CancellationToken
WinUI 3
XAML
MVVM
Win32 HWND/messages
P/Invoke
Windows UI Automation
Windows clipboard
Windows.Graphics.Capture
multi-monitor coordinates
DPI scaling
SQLite
HTTP
JSON
profiling
memory management
disposal
event-driven design
```

---

# 34. What to Learn Only As Needed

Learn enough to use them safely:

```text
COM
Direct3D concepts
WinRT
MSIX
Windows ML
ONNX
Windows startup integration
native handles
interop marshaling
```

---

# 35. What to Ignore Initially

Do not spend time initially on:

```text
C++
custom DirectX rendering
GPU shaders
Native AOT
microservices
Docker
Kubernetes
message queues
Redis
ASP.NET backend
CQRS
event sourcing
MAUI
Avalonia
Electron
distributed systems
```

These do not solve the immediate desktop product problem.

---

# 36. Weekly 20-Hour Routine

Recommended:

```text
4h  Microsoft/API documentation
2h  small isolated experiments

10h implementation
2h  automated/manual tests
1h  profiling/debugging
1h  architecture notes
──────────────────────────
20h
```

---

# 37. API Playground Strategy

Before integrating difficult Windows APIs into the main application, create small disposable experiments.

Examples:

```text
HotkeyPlayground
ClipboardPlayground
UiAutomationPlayground
CapturePlayground
OcrPlayground
PopupPlayground
```

Workflow:

```text
New Windows API
      ↓
Small playground
      ↓
Understand behavior
      ↓
Design interface
      ↓
Production implementation
```

Do not learn difficult Win32/WinRT APIs inside the full application whenever an isolated experiment can reduce complexity.

---

# 38. Four-Month Roadmap

## Month 1

```text
C# refresh
WinUI 3
MVVM
manual translation
EN → EN
EN → VI
```

## Month 2

```text
global hotkeys
UI Automation
clipboard
floating popup
system tray
```

## Month 3

```text
history
settings
OCR
screen capture
production UX
```

## Month 4

```text
Japanese
JLPT
performance
packaging
installer
error handling
release preparation
```

## V2+

```text
Windows ML
local models
GPU/NPU inference
offline translation
OCR improvements
dictionary
flashcards
sentence mining
TTS
Anki export
advanced language analytics
```

---

# 39. Expected Progress

Approximate targets:

```text
Week 3
manual translator works

Week 6
EN → EN + EN → VI useful

Week 9
selected text + global hotkey works

Week 10
popup workflow works

Week 12
usable daily-driver application

Week 14
Japanese + OCR

Week 16
credible V1
```

These are target checkpoints, not hard deadlines.

Quality of the core workflow matters more than matching the exact schedule.

---

# 40. Engineering Guidelines

## 40.1 Prefer Interfaces at External Boundaries

Create abstractions for:

```text
translation
OCR
text capture
global hotkeys
clipboard
history storage
settings storage
logging
screen capture
local inference
```

Do not abstract trivial internal logic unnecessarily.

---

## 40.2 Keep OS-Specific Logic Isolated

Everything directly using:

```text
Win32
WinRT
HWND
COM
P/Invoke
Windows UI Automation
Windows.Graphics.Capture
```

should live primarily in:

```text
Trans.Windows
```

Views and ViewModels should not contain direct native API calls.

---

## 40.3 Async Everywhere It Matters

Translation, OCR, storage, and network operations should be asynchronous.

Use:

```text
CancellationToken
```

through the request chain.

Example:

```text
Popup closes
    ↓
Cancel in-flight translation
    ↓
Provider request stops when supported
    ↓
No obsolete result updates the popup
```

This prevents stale responses.

---

## 40.4 Avoid Polling

Prefer:

```text
events
callbacks
Windows messages
async signals
observable state
```

instead of:

```text
while(true)
timer every 100 ms
repeated foreground-window checks
continuous clipboard polling
```

This is important for battery life.

---

## 40.5 Measure Before Optimizing

Before changing architecture for performance, capture measurements.

Create performance baselines.

Example:

```text
Metric                     Value
--------------------------------------
Idle CPU                   0.1%
Idle memory                82 MB
Hotkey dispatch            4 ms
UI Automation capture      18 ms
Popup creation             32 ms
API translation            780 ms
```

Optimize bottlenecks, not assumptions.

---

# 41. Testing Strategy

## Unit Tests

Prioritize:

```text
TranslationService
PromptBuilder
TranslationParser
CEFR logic
JLPT logic
provider selection
history logic
configuration
fallback decisions
```

---

## Integration Tests

Test:

```text
SQLite
provider HTTP clients
translation JSON parsing
settings persistence
```

---

## Manual Windows Compatibility Tests

Manually test:

```text
global hotkeys
selected text capture
clipboard restoration
popup position
multi-monitor
DPI scaling
focus behavior
system tray
startup behavior
OCR
different Windows applications
```

OS integration cannot be validated only through unit tests.

---

# 42. Logging

Use structured logging.

Important events:

```text
application startup
application shutdown
hotkey registration failure
capture method selected
capture failure
OCR failure
provider request
provider response duration
provider error
popup creation duration
database failure
```

Do not log sensitive translated text by default.

Allow diagnostic logging to be enabled explicitly.

---

# 43. Privacy

The application may capture text from any active application.

Treat captured text as sensitive.

Rules:

1. Do not persist text unless history is enabled.
2. Do not log captured text by default.
3. Clearly indicate when cloud providers receive text.
4. Future local-only mode should bypass cloud providers.
5. Provide a pause/disable shortcut.
6. Consider exclusions for password/security-sensitive controls where detectable.
7. Avoid background screen capture unless explicitly triggered.

---

# 44. Configuration

Persist settings such as:

```text
source language
target language
default CEFR level
default JLPT level
global hotkey
popup behavior
history enabled
startup enabled
preferred translation provider
OCR provider
cloud/local preference
privacy settings
```

Keep application configuration independent from ViewModels.

---

# 45. Suggested Initial Backlog

## P0 — Foundation

- [ ] Create Git repository
- [ ] Create Rider solution
- [ ] Add `.gitignore`
- [ ] Enable nullable reference types
- [ ] Enable analyzers
- [ ] Create `Trans.Core`
- [ ] Create `Trans.WinUI`
- [ ] Create `Trans.Tests`
- [ ] Add CommunityToolkit.Mvvm
- [ ] Define `TranslationRequest`
- [ ] Define `TranslationResult`
- [ ] Define `ITranslationProvider`
- [ ] Implement fake translation provider
- [ ] Render fake translation in UI

## P1 — Translation

- [ ] Implement real provider
- [ ] Implement structured JSON response
- [ ] Implement parser
- [ ] Implement EN → EN
- [ ] Implement CEFR level selection
- [ ] Implement EN → VI
- [ ] Add cancellation
- [ ] Add error handling

## P2 — Windows Integration

- [ ] Global hotkey experiment
- [ ] Implement `IGlobalHotkeyService`
- [ ] UI Automation experiment
- [ ] Clipboard experiment
- [ ] Implement `ITextCaptureService`
- [ ] Add UIA → clipboard fallback
- [ ] Create compatibility matrix

## P3 — Popup

- [ ] Popup experiment
- [ ] Cursor positioning
- [ ] Multi-monitor support
- [ ] DPI support
- [ ] Escape-to-close
- [ ] Click-away
- [ ] Loading state
- [ ] Cancellation on close

## P4 — Utility Features

- [ ] Tray icon
- [ ] Settings
- [ ] Startup with Windows
- [ ] SQLite
- [ ] Translation history
- [ ] Favorites

## P5 — OCR

- [ ] Screen capture experiment
- [ ] OCR experiment
- [ ] Define `IOcrEngine`
- [ ] Implement OCR provider
- [ ] Add OCR fallback
- [ ] Add region capture mode

## P6 — Japanese

- [ ] Japanese language support
- [ ] JA → EN
- [ ] JA → VI
- [ ] reading/furigana output
- [ ] romaji
- [ ] vocabulary extraction
- [ ] JLPT estimation
- [ ] Japanese grammar explanations

## P7 — Release

- [ ] Performance baseline
- [ ] Idle CPU profiling
- [ ] Memory profiling
- [ ] Startup profiling
- [ ] MSIX packaging
- [ ] Error reporting strategy
- [ ] Privacy settings
- [ ] Release checklist

---

# 46. Git Strategy

Keep Git simple.

Recommended branches:

```text
main
feature/*
fix/*
```

Examples:

```text
feature/global-hotkey
feature/uia-capture
feature/translation-popup
feature/english-vietnamese
feature/ocr
fix/clipboard-restore
```

Prefer small commits.

Example:

```text
feat: add global hotkey abstraction
feat: implement RegisterHotKey adapter
test: add translation parser tests
fix: cancel translation when popup closes
```

Antigravity CLI changes should be reviewed before committing.

Never commit AI-generated code solely because it builds.

---

# 47. Definition of Done for a Feature

A feature is complete when:

- [ ] requirement works
- [ ] architecture boundaries remain clean
- [ ] errors are handled
- [ ] cancellation is supported where relevant
- [ ] tests exist where practical
- [ ] manual Windows behavior is verified
- [ ] no obvious CPU/memory regression exists
- [ ] Rider inspections show no important unresolved warnings
- [ ] AI-generated changes have been manually reviewed
- [ ] documentation is updated if architecture changed

---

# 48. Recommended Antigravity CLI Usage by Phase

## Early Project

Use it for:

```text
C# refresh
record/interface generation
unit tests
basic WinUI MVVM code
reviewing architecture
```

## Windows Integration

Use it for:

```text
explaining P/Invoke
reviewing native signatures
creating isolated experiments
handling HWND/message loops
edge-case analysis
```

Always verify native signatures against Microsoft documentation.

## Translation

Use it for:

```text
response schemas
JSON models
parser tests
provider abstractions
prompt evaluation tooling
```

## Optimization

Use it for:

```text
profiling interpretation
allocation review
async review
hot-path analysis
benchmark generation
```

Do not accept optimization suggestions without measurements.

---

# 49. Documentation Files to Maintain

Recommended:

```text
docs/
├── ARCHITECTURE.md
├── CAPTURE_PIPELINE.md
├── TRANSLATION_SCHEMA.md
├── PROMPT_STRATEGY.md
├── WINDOWS_INTEROP.md
├── COMPATIBILITY_MATRIX.md
├── PERFORMANCE.md
├── PRIVACY.md
└── ROADMAP.md
```

Antigravity CLI can help update these documents after architectural changes.

---

# 50. Final Technical Direction

The preferred Windows strategy is:

```text
C#
+
.NET
+
WinUI 3
+
Windows App SDK
+
Win32 / WinRT
+
CommunityToolkit.Mvvm
+
SQLite
+
Cloud translation first
+
Windows ML later
```

Development environment:

```text
JetBrains Rider
+
Antigravity CLI
```

Overall design principle:

> Build a native Windows utility whose UI and operating-system integration are fast and lightweight, while all translation, OCR, persistence, and AI capabilities remain replaceable behind explicit interfaces.

Implementation priority:

```text
1. Excellent core translation model
2. EN → EN explanations
3. EN → VI translation
4. Fast selected-text capture
5. Global hotkey
6. Native floating popup
7. System tray
8. History/settings
9. OCR
10. Japanese
11. Performance tuning
12. Local AI / GPU / NPU inference
```

The most important V1 experience is:

```text
Select text
    ↓
Press hotkey
    ↓
Capture instantly
    ↓
Popup appears instantly
    ↓
Translation/explanation arrives
    ↓
User continues working without switching applications
```

Everything else should support this workflow.
