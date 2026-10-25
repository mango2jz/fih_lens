# FIH Lens Windows Architecture

> Core architecture reference for human developers working on the Windows version of FIH Lens.

## 1. Purpose

This document explains how the Windows version of **FIH Lens** is structured using **Clean Architecture**.

The goal is not only to describe the folders and projects, but to make it easy for developers to answer questions such as:

- Where should a new class go?
- Which project is allowed to depend on which other project?
- Where should Windows-specific code live?
- Where should translation-provider code live?
- How should WinUI and MVVM interact with application logic?
- How do we keep the project maintainable if FIH Lens later supports macOS, local AI, or additional translation providers?

The current Windows stack is:

- **C#**
- **.NET 10**
- **WinUI 3**
- **Windows App SDK**
- **CommunityToolkit.Mvvm**
- Windows-specific APIs isolated behind abstractions
- Dependency Injection for wiring implementations to interfaces

---

# 2. Architecture at a Glance

```text
                         FIH LENS - WINDOWS

┌─────────────────────────────────────────────────────────────────────┐
│                        FihLens.WinUI                                │
│                     Presentation Layer                              │
│                                                                     │
│  Views        ViewModels       Popup       Settings UI     Tray UI │
│    │              │                                                │
│    └──────────────┴──── Commands / User Events ───────────────┐     │
└───────────────────────────────────────────────────────────────│─────┘
                                                                │
                                                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│                    FihLens.Application                              │
│                        Use Cases                                    │
│                                                                     │
│  CaptureCoordinator                                                 │
│  TranslateSelection                                                 │
│  SaveHistory                                                        │
│  LoadHistory                                                        │
│  ChangeSettings                                                     │
│                                                                     │
│  Interfaces / Ports                                                 │
│  ┌────────────────────┐  ┌─────────────────────────┐               │
│  │ ITextCaptureService│  │ ITranslationProvider    │               │
│  │ IOcrService        │  │ IHistoryRepository      │               │
│  │ IHotkeyService     │  │ ISettingsRepository     │               │
│  └────────────────────┘  └─────────────────────────┘               │
│                              │                                      │
└──────────────────────────────│──────────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────────┐
│                         FihLens.Core                                │
│                         Domain                                     │
│                                                                     │
│  CapturedText        TranslationRequest      TranslationResult      │
│  Language            LanguagePair            Domain Rules           │
│  Value Objects       Enums                   Domain Exceptions       │
│                                                                     │
│               NO Windows / UI / HTTP / SQLite knowledge             │
└─────────────────────────────────────────────────────────────────────┘


       IMPLEMENTATIONS OF APPLICATION INTERFACES / PORTS

                    ▲                         ▲
                    │                         │

┌───────────────────┴──────────────┐ ┌────────┴──────────────────────┐
│     FihLens.Infrastructure       │ │       FihLens.Windows          │
│                                  │ │                                │
│ Translation providers            │ │ Global Hotkeys                 │
│ HTTP / HttpClient                │ │ UI Automation                  │
│ JSON serialization               │ │ Clipboard                      │
│ SQLite                           │ │ Screen Capture                  │
│ HistoryRepository                │ │ OCR                            │
│ SettingsRepository               │ │ Cursor position                │
│ Configuration                    │ │ DPI / multi-monitor             │
│ Logging                          │ │ Startup / Windows integration   │
│ Future local AI providers        │ │ Win32 / WinRT / PInvoke        │
└──────────────────────────────────┘ └────────────────────────────────┘


                       ┌─────────────────────┐
                       │    FihLens.Tests    │
                       │                     │
                       │ Core Tests          │
                       │ Application Tests   │
                       │ Integration Tests   │
                       │ Windows Tests       │
                       └─────────────────────┘
```

The most important architectural rule is simple:

```text
                    Business logic
                         ▲
                         │
                 Dependencies point
                       INWARD
                         │
               OS / Database / UI / APIs
```

Outer layers may know about inner layers.

Inner layers should not know about outer layers.

---

# 3. Project Responsibilities

The solution is divided into five main areas.

| Project | Main responsibility | Should know about |
|---|---|---|
| `FihLens.Core` | Domain concepts and business rules | Nothing external |
| `FihLens.Application` | Use cases and orchestration | Core |
| `FihLens.Infrastructure` | Storage, HTTP, providers, external technology | Application + Core |
| `FihLens.Windows` | Windows operating-system integration | Application + Core |
| `FihLens.WinUI` | User interface and composition root | Application and required outer implementations |
| `FihLens.Tests` | Verification of behavior | Projects under test |

A useful summary:

```text
Core            = What FIH Lens is
Application     = What FIH Lens does
Infrastructure  = How external services and storage work
Windows         = How FIH Lens talks to Windows
WinUI           = What the user sees and interacts with
```

---

# 4. FihLens.Core

`FihLens.Core` is the center of the application.

It contains concepts that belong to the FIH Lens domain and should remain independent from Windows, WinUI, databases, HTTP, and AI providers.

A possible structure:

```text
FihLens.Core
│
├── Entities/
│   ├── TranslationResult.cs
│   ├── TranslationHistoryEntry.cs
│   └── ...
│
├── ValueObjects/
│   ├── Language.cs
│   ├── LanguagePair.cs
│   └── SelectedText.cs
│
├── Enums/
│   ├── TranslationMode.cs
│   └── CaptureMethod.cs
│
├── Exceptions/
│
└── Rules/
```

Example:

```csharp
public sealed record LanguagePair(
    Language Source,
    Language Target);
```

The Core project should not contain dependencies such as:

```csharp
using Microsoft.UI.Xaml;
using Microsoft.Data.Sqlite;
using Windows.ApplicationModel;
using System.Net.Http;
```

Core should be portable.

In principle, these same domain types could be reused by:

- Windows
- macOS
- Linux
- a CLI
- a web backend
- automated tests

without rewriting the domain model.

---

# 5. FihLens.Application

`FihLens.Application` contains the application's **use cases**.

Core defines concepts.

Application defines what FIH Lens can actually do with those concepts.

Examples:

- capture currently selected text
- translate selected text
- save a translation to history
- load translation history
- change user settings
- coordinate capture fallbacks
- choose a translation mode

A typical flow might be:

```text
User presses hotkey

       ↓

Capture selected text

       ↓

Translate text

       ↓

Optionally save history

       ↓

Return TranslationResult
```

This orchestration belongs in Application.

Example use-case classes:

```text
CaptureCoordinator
TranslateSelectionUseCase
SaveHistoryUseCase
GetHistoryUseCase
UpdateSettingsUseCase
```

---

# 6. Application Ports and Interfaces

Application needs external capabilities, but it should not know the implementation details.

For example, Application needs text capture.

It should not directly call Windows UI Automation.

Instead, define an abstraction:

```csharp
public interface ITextCaptureService
{
    Task<CapturedText?> CaptureAsync();
}
```

Application knows only:

> I need something capable of capturing text.

It does not care whether the implementation uses:

- UI Automation
- Clipboard
- OCR
- macOS Accessibility APIs
- another future platform API

These interfaces act as architectural boundaries, often called **ports**.

A possible structure:

```text
FihLens.Application
│
├── Capture/
│   ├── ITextCaptureService.cs
│   └── CaptureCoordinator.cs
│
├── Translation/
│   ├── ITranslationProvider.cs
│   └── TranslateSelectionUseCase.cs
│
├── History/
│   ├── IHistoryRepository.cs
│   ├── SaveHistoryUseCase.cs
│   └── GetHistoryUseCase.cs
│
├── Settings/
│   ├── ISettingsRepository.cs
│   └── UpdateSettingsUseCase.cs
│
├── OCR/
│   └── IOcrService.cs
│
└── Hotkeys/
    └── IHotkeyService.cs
```

Important examples:

```text
ITextCaptureService
```

does not contain Windows-specific code.

```text
ITranslationProvider
```

does not contain provider-specific code.

```text
IHistoryRepository
```

does not contain SQLite-specific code.

This is Dependency Inversion in practice.

---

# 7. FihLens.Infrastructure

`FihLens.Infrastructure` contains implementations that depend on external technologies.

Typical responsibilities include:

- translation APIs
- HTTP communication
- JSON serialization
- local database access
- settings persistence
- logging
- provider-specific clients
- future local-model integrations

Possible structure:

```text
FihLens.Infrastructure
│
├── Translation/
│   ├── CloudTranslationProvider.cs
│   ├── TranslationHttpClient.cs
│   └── TranslationResponseParser.cs
│
├── Persistence/
│   ├── SQLite/
│   │   ├── FihLensDbContext.cs
│   │   ├── HistoryRepository.cs
│   │   └── Migrations/
│   │
│   └── SettingsRepository.cs
│
├── Configuration/
│
├── Logging/
│
└── AI/
    └── FutureProviders/
```

The relationship should look like this:

```text
FihLens.Application

ITranslationProvider
        ▲
        │ implements
        │

FihLens.Infrastructure

CloudTranslationProvider
```

For example:

```csharp
public sealed class CloudTranslationProvider : ITranslationProvider
{
    // HttpClient
    // JSON serialization
    // API communication
}
```

Application does not need to know whether the implementation uses:

- OpenAI
- Gemini
- DeepL
- a custom backend
- a local language model

That makes it possible to add new providers later without rewriting the main application flow.

Example:

```text
ITranslationProvider
       ▲
       │
 ┌─────┴───────────────┐
 │                     │
CloudProvider      LocalAIProvider
```

## Provider configuration

`FihLens.WinUI/TranslationStartup.cs` selects the Ask AI provider from environment variables at startup. Only the selected key is read. Invalid or missing values disable Ask AI only; dictionary lookup keeps working.

| `FIHLENS_PROVIDER_MODE` | Key variable | Model | Client timeout |
| --- | --- | --- | --- |
| unset or `demo` | none | Ask AI disabled | — |
| `gemini` | `GEMINI_API_KEY` | `gemini-3.5-flash-lite` | 30 s |
| `deepseek` | `DEEPSEEK_API_KEY` | `deepseek-flash` | 30 s |
| `openrouter-nemotron` | `OPENROUTER_API_KEY` | `nvidia/nemotron-3.5-super-vl:free` | 60 s |

`OpenRouterTranslationProvider` is generic over the OpenRouter chat-completions API: it takes a model id and a `useJsonSchema` flag, so another free model is one line in `TranslationStartup`. Free models are reasoning models that queue, so they get a longer timeout and send `reasoning: { effort: "low", exclude: true }` to keep reasoning within the `max_tokens` budget. With `useJsonSchema` (Nemotron), the provider sends the shared analysis schema (`TranslationAnalysisSchema`, also used by Gemini) as `json_schema` with `provider.require_parameters`, because plain `json_object` gave unparseable replies in live tests; models without structured-output support use `json_object`. A Gemma mode (`google/gemma-4-31b-it:free`) was dropped after its shared free endpoint returned 429 on every live request.

Free OpenRouter models:

- **Privacy:** free endpoints may log prompts or use them for training. Selected or captured text is sent to them. The provider label shows "(free, may log text)" wherever the app names the provider. Your OpenRouter privacy settings must allow free-model endpoints, or requests fail.
- **Limits:** 20 requests per minute, 50 per day with less than 10 USD of purchased credits, 1000 per day with at least 10 USD (OpenRouter docs, 2026-10). Exceeding them shows the rate-limited error.
- **Failure meanings:** 404 "No endpoints found" means the free model was retired, its endpoint is down, no endpoint supports the required structured output, or your privacy settings block it (shown as request rejected). Update the model id in `TranslationStartup`, or change the setting. 402 means a negative account balance, even for free models (shown as an authentication error). 429 means rate limited.

---

# 8. FihLens.Windows

`FihLens.Windows` contains operating-system-specific behavior.

This layer exists because FIH Lens needs capabilities that are unique to Windows.

Examples:

- global hotkeys
- UI Automation
- clipboard integration
- screen capture
- OCR
- mouse cursor position
- monitor detection
- DPI awareness
- startup integration
- Win32 calls
- WinRT APIs
- P/Invoke

Possible structure:

```text
FihLens.Windows
│
├── Capture/
│   ├── UiAutomationTextCapture.cs
│   ├── ClipboardTextCapture.cs
│   └── WindowsTextCaptureService.cs
│
├── Hotkeys/
│   └── GlobalHotkeyService.cs
│
├── Clipboard/
│   └── WindowsClipboardService.cs
│
├── ScreenCapture/
│   └── WindowsScreenCaptureService.cs
│
├── OCR/
│   └── WindowsOcrService.cs
│
├── Display/
│   ├── CursorService.cs
│   ├── MonitorService.cs
│   └── DpiService.cs
│
├── Startup/
│   └── StartupService.cs
│
└── Native/
    ├── Win32/
    ├── WinRT/
    └── NativeMethods.cs
```

The architectural relationship is:

```text
Application
─────────────────────────
ITextCaptureService
          ▲
          │ implements
          │
Windows
─────────────────────────
WindowsTextCaptureService
```

Application requests text capture.

Windows decides how to perform it.

---

# 9. Text Capture Strategy

Text capture is one of the best examples of why platform-specific behavior should be isolated.

A possible Windows capture flow:

```text
             Capture request
                   │
                   ▼
       WindowsTextCaptureService
                   │
          ┌────────┴─────────┐
          ▼                  │
   UI Automation             │
          │                  │
       Success? ─ YES ───────┤
          │                  │
          NO                 │
          ▼                  │
      Clipboard              │
          │                  │
       Success? ─ YES ───────┤
          │                  │
          NO                 │
          ▼                  │
         OCR                 │
          │                  │
          └──────────────────┘
                   │
                   ▼
              CapturedText
```

Application should not need to know that this fallback chain exists.

It should simply request:

```text
CaptureAsync()
```

and receive:

```text
CapturedText
```

or a failed result.

This keeps platform complexity out of the use-case layer.

---

# 10. FihLens.WinUI

`FihLens.WinUI` is the presentation layer.

It contains what users see and interact with.

Possible structure:

```text
FihLens.WinUI
│
├── App.xaml
├── App.xaml.cs
│
├── Views/
│   ├── MainWindow.xaml
│   ├── TranslationPopup.xaml
│   ├── SettingsPage.xaml
│   └── HistoryPage.xaml
│
├── ViewModels/
│   ├── TranslationPopupViewModel.cs
│   ├── SettingsViewModel.cs
│   └── HistoryViewModel.cs
│
├── Controls/
│
├── Converters/
│
├── Navigation/
│
└── DependencyInjection/
    └── ServiceRegistration.cs
```

Because the project uses MVVM, the preferred flow is:

```text
View
 │
 │ binding
 ▼
ViewModel
 │
 │ calls
 ▼
Application Use Case
```

Avoid flows such as:

```text
View
 │
 ▼
SQLite
 │
 ▼
Translation API
 │
 ▼
Win32
```

Views and ViewModels should not become service locators or infrastructure coordinators.

---

# 11. MVVM vs Clean Architecture

MVVM and Clean Architecture solve different problems.

**Clean Architecture** structures the whole solution.

**MVVM** structures the presentation layer.

Think of it like this:

```text
             CLEAN ARCHITECTURE

┌──────────────────────────────────────────────┐
│                                             │
│            Presentation                     │
│                                             │
│             ┌─────────┐                     │
│             │  MVVM   │                     │
│             │         │                     │
│             │ View    │                     │
│             │   ↕     │                     │
│             │ViewModel│                     │
│             └─────────┘                     │
│                                             │
├──────────────────────────────────────────────┤
│              Application                    │
├──────────────────────────────────────────────┤
│                  Core                       │
├──────────────────────────────────────────────┤
│ Infrastructure / Windows adapters           │
└──────────────────────────────────────────────┘
```

In short:

```text
Clean Architecture = solution-level architecture
MVVM               = presentation-layer architecture
```

---

# 12. Composition Root and Dependency Injection

At some point, abstractions must be connected to real implementations.

That happens in the **composition root**.

For the Windows application, this will normally live close to startup, for example:

```text
App.xaml.cs
```

or:

```text
DependencyInjection/
    ServiceRegistration.cs
```

This is where the application decides:

```text
ITranslationProvider
    → CloudTranslationProvider

ITextCaptureService
    → WindowsTextCaptureService

IHistoryRepository
    → SQLiteHistoryRepository
```

Example:

```csharp
services.AddSingleton<ITextCaptureService, WindowsTextCaptureService>();

services.AddSingleton<ITranslationProvider, CloudTranslationProvider>();

services.AddSingleton<IHistoryRepository, SQLiteHistoryRepository>();
```

This is one of the few places where multiple outer-layer implementations are intentionally wired together.

The composition root is effectively saying:

> This executable is the Windows edition of FIH Lens, and these are the concrete technologies it uses.

---

# 13. Main Runtime Flow

The first MVP can be understood through this flow:

```text
                       USER
                        │
               presses global hotkey
                        │
                        ▼
             ┌────────────────────┐
             │ GlobalHotkeyService│
             │  FihLens.Windows   │
             └──────────┬─────────┘
                        │ event
                        ▼
             ┌────────────────────┐
             │ CaptureCoordinator │
             │ FihLens.Application│
             └──────────┬─────────┘
                        │
                        ▼
              ITextCaptureService
                        │
                        ▼
          ┌──────────────────────────┐
          │ WindowsTextCaptureService│
          │     FihLens.Windows      │
          └────────────┬─────────────┘
                       │
       ┌───────────────┼────────────────┐
       ▼               ▼                ▼
 UI Automation     Clipboard           OCR
       │
       ▼
 CapturedText
       │
       ▼
 CaptureCoordinator
       │
       ▼
 ITranslationProvider
       │
       ▼
 CloudTranslationProvider
       │
       ▼
 Translation API
       │
       ▼
 TranslationResult
       │
       ▼
 CaptureCoordinator
       │
       ├────────────→ IHistoryRepository
       │                       │
       │                       ▼
       │                 SQLite database
       │
       ▼
 TranslationPopupViewModel
       │
       ▼
 TranslationPopup.xaml
       │
       ▼
┌───────────────────────┐
│ Translation displayed │
│ near user's cursor    │
└───────────────────────┘
```

This flow is worth keeping in mind because it shows how most first-MVP components connect.

---

# 14. Popup Positioning Example

Suppose the translation popup should appear near the user's cursor or selected text.

Do not put native Windows calls directly inside the ViewModel.

Avoid:

```text
TranslationPopupViewModel
        ↓
GetCursorPos()
        ↓
Win32
```

Prefer:

```text
TranslationPopupViewModel
          │
          ▼
    IDisplayService
          ▲
          │ implements
          │
 WindowsDisplayService
          │
          ├── cursor position
          ├── monitor detection
          ├── DPI scaling
          └── work-area calculation
```

The Application or Presentation layer works with a clean abstraction such as:

```csharp
ScreenPoint GetCursorPosition();
```

The Windows layer handles:

- Win32 calls
- DPI conversion
- multi-monitor behavior
- virtual-screen coordinates
- work-area boundaries

This keeps native platform complexity isolated.

---

# 15. Dependency Rule

This rule should be protected during code review.

```text
                    ┌──────────────┐
                    │     Core     │
                    └──────▲───────┘
                           │
                    ┌──────┴───────┐
                    │ Application  │
                    └──────▲───────┘
                           │
              ┌────────────┴─────────────┐
              │                          │
     ┌────────┴────────┐        ┌────────┴────────┐
     │ Infrastructure │        │     Windows     │
     └────────▲────────┘        └────────▲────────┘
              │                          │
              └───────────┬──────────────┘
                          │
                    ┌─────┴──────┐
                    │   WinUI    │
                    │ Composition│
                    │    Root    │
                    └────────────┘
```

The simplified rule is:

```text
Core

↑

Application

↑

Infrastructure / Windows / WinUI
```

Dependencies point inward.

Examples:

### Allowed

```text
FihLens.Application
    → FihLens.Core
```

```text
FihLens.Windows
    → FihLens.Application
    → FihLens.Core
```

```text
FihLens.Infrastructure
    → FihLens.Application
    → FihLens.Core
```

### Not allowed

```text
FihLens.Core
    → FihLens.Windows
```

```text
FihLens.Application
    → FihLens.WinUI
```

```text
FihLens.Application
    → Microsoft.UI.Xaml
```

```text
FihLens.Core
    → SQLite
```

If an inner layer needs functionality from an outer layer, define an interface in the inner layer and implement it in the outer layer.

---

# 16. Where Should a New Class Go?

Use this table during implementation and code review.

| Component | Project |
|---|---|
| `TranslationResult` | `FihLens.Core` |
| `Language` | `FihLens.Core` |
| `LanguagePair` | `FihLens.Core` |
| Translation-domain validation | `FihLens.Core` |
| `TranslateSelectionUseCase` | `FihLens.Application` |
| `CaptureCoordinator` | `FihLens.Application` |
| `ITranslationProvider` | `FihLens.Application` |
| `ITextCaptureService` | `FihLens.Application` |
| `IHistoryRepository` | `FihLens.Application` |
| `ISettingsRepository` | `FihLens.Application` |
| Cloud translation implementation | `FihLens.Infrastructure` |
| HTTP client | `FihLens.Infrastructure` |
| JSON serialization | `FihLens.Infrastructure` |
| SQLite repository | `FihLens.Infrastructure` |
| Local AI provider | `FihLens.Infrastructure` |
| Global hotkey implementation | `FihLens.Windows` |
| Windows UI Automation | `FihLens.Windows` |
| Clipboard capture | `FihLens.Windows` |
| Windows OCR implementation | `FihLens.Windows` |
| Win32 P/Invoke | `FihLens.Windows` |
| DPI handling | `FihLens.Windows` |
| Monitor detection | `FihLens.Windows` |
| Translation popup XAML | `FihLens.WinUI` |
| Settings page | `FihLens.WinUI` |
| `TranslationPopupViewModel` | `FihLens.WinUI` |
| XAML converters | `FihLens.WinUI` |
| DI registration | `FihLens.WinUI` composition root |

---

# 17. A Simple Decision Process

When adding a class, ask these questions in order.

## Question 1: Is this a business/domain concept?

Examples:

- `Language`
- `TranslationResult`
- `LanguagePair`
- translation-related rules

If yes:

```text
FihLens.Core
```

## Question 2: Is this an application action or use case?

Examples:

- translate selected text
- save history
- coordinate capture
- update settings

If yes:

```text
FihLens.Application
```

## Question 3: Does it talk to an external technology?

Examples:

- HTTP
- SQLite
- JSON
- cloud AI provider
- local model runtime

If yes:

```text
FihLens.Infrastructure
```

## Question 4: Does it use Windows-specific APIs?

Examples:

- Win32
- WinRT
- Clipboard
- UI Automation
- OCR
- global hotkeys
- monitor/DPI APIs

If yes:

```text
FihLens.Windows
```

## Question 5: Is it visible to or directly controlled by the user?

Examples:

- XAML
- window
- popup
- settings page
- ViewModel
- UI converter

If yes:

```text
FihLens.WinUI
```

---

# 18. Common Architectural Mistakes to Avoid

## 18.1 Putting Win32 code in ViewModels

Bad:

```text
ViewModel
  → P/Invoke
```

Better:

```text
ViewModel
  → interface
  → Windows implementation
```

---

## 18.2 Calling translation APIs from the ViewModel

Bad:

```text
TranslationPopupViewModel
  → HttpClient
  → Provider API
```

Better:

```text
TranslationPopupViewModel
  → TranslateSelectionUseCase
  → ITranslationProvider
  → Provider implementation
```

---

## 18.3 Letting Application reference Windows

Bad:

```text
FihLens.Application
  → Windows.UI.*
```

Application should remain platform-independent.

Define an interface instead.

---

## 18.4 Putting SQLite entities in Core without a domain reason

Database persistence models and domain models are not automatically the same thing.

Do not make Core depend on database structure just because SQLite needs a certain schema.

---

## 18.5 Turning `CaptureCoordinator` into a God class

The coordinator should coordinate.

It should not contain:

- raw Win32 calls
- OCR implementation details
- SQL
- HTTP parsing
- XAML logic

Delegate those responsibilities to the appropriate abstractions.

---

## 18.6 Creating interfaces for everything

Clean Architecture does not mean every class needs an interface.

Use interfaces where they create a meaningful boundary, especially around:

- operating-system APIs
- databases
- external providers
- network services
- system clock or filesystem when testability matters

A simple domain value object does not need `ILanguagePair`.

---

# 19. Testing Strategy

The architecture should make testing easier.

## Core tests

Test pure domain behavior.

Examples:

```text
Language validation
LanguagePair validation
Translation-domain rules
Value-object equality
```

These should be fast and require no external services.

---

## Application tests

Test use cases with mocked or fake ports.

Example:

```text
TranslateSelectionUseCase
    ↓
FakeTextCaptureService
FakeTranslationProvider
FakeHistoryRepository
```

No real Windows API or network connection should be required for most Application tests.

---

## Infrastructure tests

Test real integration boundaries when useful.

Examples:

- SQLite repository behavior
- provider response parsing
- HTTP serialization
- configuration loading

---

## Windows tests

Test platform-specific behavior.

Examples:

- hotkey registration
- DPI calculations
- cursor positioning
- capture fallback behavior

Some of these may need integration or manual verification because they depend on the active desktop environment.

---

# 20. Human-Friendly Mental Model

When deciding where code belongs, ask:

> Is this class describing the product, orchestrating the product, or dealing with technology?

Then map it like this:

```text
WHAT FIH LENS IS
        │
        ▼
      Core


WHAT FIH LENS DOES
        │
        ▼
   Application


HOW EXTERNAL TECHNOLOGY DOES IT
        │
        ├── Infrastructure
        │
        └── Windows


WHAT THE USER SEES
        │
        ▼
      WinUI
```

An even shorter mental model:

```text
Core            = vocabulary and rules
Application     = brain
Infrastructure  = gateway to storage/services/AI
Windows         = hands interacting with Windows
WinUI           = face
```

---

# 21. Architecture Review Checklist

Before merging a feature, ask:

- Does Core remain free from UI, Windows, HTTP, and database dependencies?
- Does Application depend only on Core and abstractions?
- Is Windows-specific behavior inside `FihLens.Windows`?
- Is external-service and persistence code inside `FihLens.Infrastructure`?
- Does the ViewModel call use cases rather than infrastructure directly?
- Are dependencies pointing inward?
- Is an abstraction needed at the boundary?
- Is the new class in the correct project?
- Is the feature testable without launching the complete desktop app?
- Could the use-case logic still work if the UI or provider changed?

If most of these answers are clear, the feature is probably respecting the architecture.

---

# 22. Final Rule

When in doubt, protect the inner layers.

The inner parts of FIH Lens should describe stable product behavior.

The outer parts should absorb change.

Windows APIs may change.

Translation providers may change.

Storage may change.

The UI may change.

The product's main use cases should not need to change every time one of those technologies changes.

That is the main reason FIH Lens uses Clean Architecture.
