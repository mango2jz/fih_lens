# Phase 1 — Learning WinUI 3 and Building the Translation Screen

## 1. Goal, prerequisites, and boundaries

You will implement a single-window translation screen while learning XAML, layout, binding, MVVM, commands, asynchronous UI behavior, and window lifetime.

The mentor provides explanations, small examples, design review, and debugging guidance. You write the application code and tests.

### Complete Phase 0 first

The repository inspected while preparing this plan contains an empty WinUI window and initial Core types. Application, Infrastructure, and the complete translation models are still assignments.

Before starting this implementation, complete and verify the [Core plan](phase-0-core-implementation-plan.md) and [Application plan](phase-0-application-implementation-plan.md):

- Validated `TranslationRequest` and structured `TranslationResult`.
- `ITranslationProvider` and `TranslationService`.
- The Infrastructure fake provider.
- Passing applicable Phase 0 tests.
- A working .NET 10 SDK and Windows development environment for WinUI.

You can study the concepts below before finishing Phase 0, but the integrated screen depends on those components.

### What this milestone delivers

```text
MainWindow
    ↓ bindings and commands
MainWindowViewModel
    ↓
TranslationService
    ↓
FakeTranslationProvider
    ↓
TranslationResult
    ↓
Visible translation and learning information
```

The screen contains:

- An editable, multiline input.
- A fixed English → Vietnamese direction.
- Translate and Cancel buttons.
- Progress, status, and error feedback.
- Original text, translation, explanation, and vocabulary.
- Simple grammar and alternative sections when supplied.

Prefill the input with `Hello world`. Clearly label the screen as a demo supporting that exact fixture.

Your chosen defaults are:

- **Fake provider only.** Real translation remains Phase 2.
- **One request at a time.** Editing and Translate are disabled while running.
- **Cancellation waits for completion.** A new request cannot begin until the current operation finishes.

This milestone proves the UI workflow. It does not provide real translation for arbitrary pasted text.

## 2. Concepts to understand before implementation

### MVVM and architectural boundaries

**MVVM** separates presentation responsibilities:

| Part | Responsibility in this phase |
|---|---|
| View | Layout, controls, bindings, accessibility, window events. |
| ViewModel | Editable values, commands, progress, errors, and displayed result. |
| Model/use case | Core translation types and Application translation behavior. |

The ViewModel calls `TranslationService`. It does not construct providers, parse JSON, access the clipboard, or call Windows APIs.

Clean Architecture defines project dependencies. MVVM organizes the presentation layer within those dependencies.

Do not create a new presentation project for one ViewModel. Place it in WinUI’s `ViewModels` folder and keep its code free of WinUI and Windows types.

**Reasoning checkpoint:** Does displaying an error make that error a Core responsibility? Explain the distinction between operation failure and presentation of failure.

### XAML, code-behind, and `partial`

XAML describes an object tree: windows, layout containers, and controls.

```xml
<StackPanel Spacing="8">
    <TextBlock Text="Study topic" />
    <TextBox PlaceholderText="Enter a topic" />
</StackPanel>
```

The element names identify control types. Attributes assign properties. Nested elements establish containment.

The XAML compiler generates part of the associated C# class. Your code-behind supplies another part, which is why the class is declared `partial`.

`InitializeComponent()` loads and connects the generated UI.

Code-behind is appropriate for view responsibilities, including initialization and window lifetime events. Avoid placing translation workflows in click handlers when a command can represent them.

### `Grid`, `StackPanel`, and scrolling

A `Grid` arranges content into rows and columns. It is appropriate for the main screen structure.

- `Auto` sizes a row to its content.
- `*` receives a share of the remaining space.
- A fixed size reserves a specific amount.

A `StackPanel` arranges a small group sequentially. Use it for headings or action buttons, rather than as the entire resizable screen.

Your main layout should use a Grid with:

1. An automatic heading/demo-information row.
2. An automatic input/action row.
3. An automatic feedback row.
4. A remaining-space result row containing a ScrollViewer.

Give the input a reasonable height and its own text scrolling. Let long results scroll in the result region. Avoid putting the whole interface in an unbounded vertical StackPanel.

**Review question:** What happens to scrolling if the parent measures a results control with unlimited height?

### Resources, styles, and themes

A **resource** is a reusable value or object. A **style** groups property settings for a control type.

```xml
<Style x:Key="SectionHeading" TargetType="TextBlock">
    <Setter Property="FontWeight" Value="SemiBold" />
</Style>
```

`x:Key` gives the resource a lookup name:

```xml
<TextBlock Text="Explanation"
           Style="{StaticResource SectionHeading}" />
```

Use a local resource when only this window needs it. Move it into application resources when multiple views actually share it.

`StaticResource` resolves a resource reference. `ThemeResource` supports reevaluation when the theme changes. Prefer the built-in control styles and theme brushes for ordinary colors. See [theme resources](https://learn.microsoft.com/en-us/windows/apps/develop/platform/xaml/themeresource-markup-extension).

Do not build a design-system framework. Add only the few styles used repeatedly in this screen.

### Binding modes and live input updates

Binding connects a control property to a source property.

Use a typed `MainWindow.ViewModel` property with `x:Bind`.

```xml
<TextBox Text="{x:Bind ViewModel.InputText,
                      Mode=TwoWay,
                      UpdateSourceTrigger=PropertyChanged}" />
```

Read this as:

- `ViewModel.InputText` identifies the source.
- `TwoWay` sends changes in both directions.
- `PropertyChanged` updates the source as the user types.

Without live source updates, command eligibility can remain stale until focus changes.

For changing output, specify `Mode=OneWay`. `x:Bind` defaults to `OneTime`, which is insufficient for a result that arrives later.

Ordinary `{Binding}` usually resolves through a control’s `DataContext`; `x:Bind` uses the containing view’s typed members. Use one approach consistently here. See [WinUI binding and MVVM](https://learn.microsoft.com/en-us/windows/apps/develop/data-binding/data-binding-and-mvvm).

### Property-change notifications

Bindings need to know when a source property changes.

`INotifyPropertyChanged` provides that notification. Assigning a field alone does not tell the UI to reread its value.

For example, changing `IsBusy` must notify the UI. If `IsEditingEnabled` is calculated from `IsBusy`, that calculated property also needs notification.

Learn the mechanism, but use `CommunityToolkit.Mvvm.ObservableObject` instead of writing a custom MVVM framework.

### Why add CommunityToolkit.Mvvm?

The roadmap explicitly chooses this package. It supplies observable-property and command implementations, reducing repetitive code while leaving your architecture and workflow visible.

The alternative is handwritten `INotifyPropertyChanged`, property setters, and command implementations. That is useful for a small learning exercise, but maintaining a custom async-command implementation is unnecessary here.

Use **CommunityToolkit.Mvvm 8.4.2**, the stable version verified while preparing this plan. It is maintained by the .NET Community Toolkit project. Add the same version to WinUI and the test project because both will compile the ViewModel source. See the [package page](https://www.nuget.org/packages/CommunityToolkit.Mvvm).

No DI, navigation, or UI component package is required.

### Source generators and partial properties

A **source generator** produces additional C# during compilation.

This unrelated example illustrates observable-property syntax:

```csharp
public partial class StudyViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;
}
```

- `[ObservableProperty]` is an attribute interpreted by the generator.
- The partial class allows generated members to join your class.
- The partial property allows the generator to supply its implementation.

For WinUI, use annotated **partial properties**, rather than copying older field-based examples. Toolkit diagnostic MVVMTK0045 recommends this form for WinRT/AOT compatibility. This does not require enabling Native AOT. See [the diagnostic](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/generators/errors/mvvmtk0045).

With the selected package and .NET 10 compiler, preview language mode is unnecessary. See [Toolkit’s C# 14 support](https://github.com/CommunityToolkit/dotnet/releases/tag/v8.4.1).

Inspect the generated code in Rider once. Be able to explain what the attribute replaces.

### Commands, eligibility, and async execution

A **command** represents an action the view can invoke. Its `CanExecute` behavior determines whether that action is currently available.

This syntax example uses an unrelated action:

```csharp
[RelayCommand(IncludeCancelCommand = true)]
private async Task LoadAsync(CancellationToken token)
{
    await Task.Delay(100, token);
}
```

The generator creates `LoadCommand` and `LoadCancelCommand`.

For your translation command:

- Use an asynchronous method accepting a cancellation token.
- Supply a `CanExecute` method.
- Generate the corresponding cancel command.
- Keep concurrent execution disabled.

Property changes do not automatically reevaluate every command. Use `NotifyCanExecuteChangedFor` on input and busy properties. Use `NotifyPropertyChangedFor` for calculated presentation properties.

Unhandled async-command exceptions can reach the UI context. Your command must handle failures and restore state. See [the command generator](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/generators/relaycommand).

### UI thread affinity and `DispatcherQueue`

WinUI controls belong to their UI thread.

A command initiated from the UI can await the translation service and then update its presentation properties on the captured UI context. Do not add `Task.Run` or `ConfigureAwait(false)` to this ViewModel workflow.

A callback originating on another thread may require `DispatcherQueue.TryEnqueue` to schedule UI work.

```csharp
DispatcherQueue.TryEnqueue(() => Title = "Study example");
```

This example belongs in a window-level learning exercise, not inside the portable ViewModel.

No dispatcher abstraction is needed for the chosen Phase 1 workflow. See [WinUI threading guidance](https://learn.microsoft.com/en-us/windows/apps/develop/performance/keep-ui-thread-responsive).

### `Window`, `Page`, and navigation

A `Window` is the desktop host. A `Page` is navigable content. A `Frame` hosts pages and maintains navigation history.

```csharp
ContentFrame.Navigate(typeof(StudyPage));
```

`typeof(StudyPage)` supplies the page’s runtime type. Back navigation requires checking whether the Frame can go back.

Study the mechanism and trace a two-page navigation example. Do not add settings/history pages or a navigation service to the translation screen merely to exercise the API. A single screen does not need navigation. See [basic navigation](https://learn.microsoft.com/en-us/windows/apps/develop/ui/navigation/navigate-between-two-pages).

## 3. Your implementation assignments

### Assignment A — Draw the screen and its state transitions

Before coding, sketch the layout and write this behavior table:

| State | Editing | Translate | Cancel | Output |
|---|---|---|---|---|
| Ready | Enabled | Enabled for nonblank input | Disabled | Empty or previous completed result. |
| Running | Disabled | Disabled | Available until cancellation is requested | Previous result cleared; progress visible. |
| Completed | Enabled | Enabled for nonblank input | Disabled | New structured result. |
| Canceled | Enabled after operation finishes | Enabled for nonblank input | Disabled | No result; cancellation status. |
| Failed | Enabled after operation finishes | Enabled for nonblank input | Disabled | No result; useful error feedback. |
| Closed | No further interaction | Unavailable | Cancellation requested if running | No later ViewModel-owned display updates. |

Cancel requests cancellation; it does not forcibly terminate the provider. Keep the screen busy until the provider operation ends.

**Review target:** Every action has a defined effect on inputs, output, and feedback.

### Assignment B — Implement `MainWindowViewModel`

Use `ObservableObject` and constructor-inject the existing concrete `TranslationService`.

Provide these presentation members:

| Member | Purpose |
|---|---|
| `InputText` | Editable text, initially `Hello world`. |
| `Result` | Nullable completed-result snapshot. |
| `IsBusy` | Controls progress and editing. |
| `IsEditingEnabled` | Calculated from busy/closed state. |
| `StatusText` | Ready, translating, completed, or canceled feedback. |
| `ErrorMessage` | Optional user-facing failure message. |
| `TranslateCommand` | Generated async action. |
| `TranslateCancelCommand` | Generated cancellation action. |

Keep the closed flag private. Do not put `Window`, controls, `DispatcherQueue`, or provider implementations in this class.

Use the existing result’s read-only collections. Replacing the complete result does not require an `ObservableCollection`. That collection becomes useful when individual items are added or removed while displayed.

**Review target:** Presentation state is separated from translation behavior, and calculated properties notify correctly.

### Assignment C — Implement the command workflow

Write the workflow yourself:

1. Refuse execution when closed, busy, or given blank input.
2. Snapshot the input before awaiting.
3. Clear the previous result and error.
4. Mark the operation busy and show translating status.
5. Construct the validated Core request without trimming its text.
6. Call `TranslationService` with the command’s token.
7. Publish a completed result only if the window is still open and cancellation has not been requested.
8. Present requested cancellation as cancellation, not failure.
9. Present an unsupported fixture with the exact demo-input guidance.
10. Present other failures with a useful generic error.
11. Restore nonbusy state in `finally` while the ViewModel remains open.

Use fixed English → Vietnamese, `Translate` mode, and no learning level. Language and proficiency selectors belong to later behavior.

If a provider ignores cancellation and eventually returns success, suppress that result and finish in the canceled state.

An unexpected cancellation exception without a requested command cancellation follows the failure path.

**Review target:** No stale result, swallowed failure, stuck busy state, or overlapping request.

### Assignment D — Wire the view and startup

In `App.OnLaunched`, construct:

```text
FakeTranslationProvider
    → TranslationService
    → MainWindowViewModel
    → MainWindow
```

Change the window constructor to accept the ViewModel.

Expose a typed, get-only `ViewModel` property and assign it **before** calling `InitializeComponent()`. Bind through that property.

Use:

- Two-way, live-update binding for input.
- One-way binding for changing result and feedback.
- Command binding for Translate and Cancel.
- Editing-state binding for the input.
- Busy-state binding for a ProgressRing.

Bind read-only text and collection items to the result snapshot. Handle absent optional values without displaying `"null"`. Empty learning collections should display cleanly.

Do not manually assign translation text to controls after the operation. That bypasses the binding mechanism you are learning.

**Review target:** Typing updates command eligibility immediately, and result replacement updates the visible screen.

### Assignment E — Make running and cancellation observable

The immediate Phase 0 fake can finish before progress is visible.

Extend that same fake with an optional simulated latency, defaulting to zero. For this demo, startup supplies one second. The delay must observe the cancellation token.

Label the latency as simulated. Keep the known fixture and result unchanged. Do not create another provider class or a configuration subsystem.

This enhancement exists to observe async UI behavior. It is not a real translation-performance measurement.

### Assignment F — Handle window closure

Handle `Window.Closed` in the view and call a ViewModel `Close()` method.

That method must:

- Mark the ViewModel closed before requesting cancellation.
- Make Translate unavailable.
- Request cancellation through the generated async command.

After closure, suppress changes to the ViewModel’s result, error, status, and busy properties, including the ordinary `finally` reset. A discarded closed ViewModel may remain marked busy internally.

The command itself may finish and raise its own completion notifications. The guarantee concerns your ViewModel-owned display state; cancellation does not forcibly destroy pending work.

Do not create a second cancellation source or add disposal interfaces when you own no disposable resource.

**Review target:** Closing during pending work cannot publish a late result or access a destroyed window.

### Assignment G — Make the screen usable

Use native controls and theme resources. Verify:

- Labels identify the input and actions.
- Keyboard focus follows a sensible order.
- Output remains readable and selectable.
- Errors are described with text, not color alone.
- Input, output, and collections remain usable after resizing.
- Long text scrolls.
- Light and dark themes remain readable.
- Progress and cancellation are understandable.

No custom control library or detailed visual redesign is required.

## 4. Tests and acceptance checks

### Test the portable ViewModel

Compile the same ViewModel source into the existing .NET 10 test project using a linked file:

```xml
<Compile Include="../../FihLens.WinUI/ViewModels/MainWindowViewModel.cs"
         Link="ViewModels/MainWindowViewModel.cs" />
```

Add Application and the same Toolkit package version to that project. Retain its Core reference. Do not reference the Windows executable.

This deliberately compiles one source file in two projects. It is a small temporary seam for one portable ViewModel. If several ViewModels later need this arrangement, reconsider a separate presentation project.

Use the real Application service with controlled test providers. Exercise the generated commands, not a separately reimplemented workflow.

Verify:

- Blank input disables Translate; typing nonblank input enables it.
- A pending operation marks busy and disables editing and Translate.
- The command passes the intended request and cancellation token.
- Success publishes the result and restores readiness.
- Provider failure produces an error and restores readiness.
- Requested cancellation reaches the provider and clears busy only after completion.
- A noncooperative provider’s late success after Cancel is suppressed.
- Closing during a pending operation prevents later ViewModel-owned updates.
- Optional fields and empty collections have sensible presentation defaults.

Use `TaskCompletionSource` to control ordering. Do not use sleeps or arbitrary test delays.

### Check the actual Windows UI

Unit tests cannot prove XAML compilation, binding paths, UI-thread behavior, or accessibility.

On Windows:

1. Build and launch the x64 app.
2. Submit the known fixture and inspect its structured output.
3. Type different text and verify the honest demo limitation.
4. Verify live command eligibility without moving focus first.
5. Verify editing and Translate are disabled while running.
6. Cancel, wait for completion, then submit again.
7. Close during a pending operation and inspect for unhandled exceptions.
8. Check resizing, long text, keyboard operation, themes, and accessible labels.

Run the applicable Core/Application tests again. Inspect compiler warnings, Toolkit diagnostics, XAML warnings, and runtime binding errors.

## 5. Learning sequence and architecture critique

### Suggested two-week sequence

Budget approximately 40 hours, with about 12 hours of study and 28 hours of implementation.

| Stage | Concepts | Your work |
|---|---|---|
| 1 | XAML, layout, resources, Window. | Sketch and build the static screen. |
| 2 | MVVM, notifications, source generation. | Implement presentation properties and bindings. |
| 3 | Commands, async state, cancellation. | Implement the translation workflow and controlled tests. |
| 4 | Composition and lifetime. | Wire startup, fake latency, and close handling. |
| 5 | DispatcherQueue, navigation, usability. | Complete learning exercises and Windows checks. |

After each stage, explain your implementation before requesting review. The mentor should explain defects and let you correct them.

### Architecture and critic review

The proposed design received separate architecture and critic reviews. The critique was accepted after making these points explicit:

- Phase 0 completion is a prerequisite; planned components are not treated as existing code.
- Startup supplies the ViewModel, and the window exposes it before XAML initialization.
- Cancellation and closure have explicit late-result behavior.
- Linked-source tests have a documented limitation.
- The Toolkit dependency has a concrete purpose and a handwritten alternative.
- Navigation and dispatcher abstractions are deferred until an actual workflow needs them.

These reviews assess the plan. Your implementation still requires code review, builds, tests, and Windows verification.

At completion, you should be able to explain:

- How MVVM fits inside the project architecture.
- Why changing a field alone may not update the UI.
- Why `x:Bind` modes and source-update timing matter.
- What the source generators create.
- How CanExecute differs from merely hiding a button.
- Why cancellation does not guarantee immediate completion.
- Why UI-thread ownership matters.
- Why a closed window must reject late results.
- Why snapshot collections do not need incremental notifications.

**Defaults:** You implement all assignments. One window, one ViewModel, fixed English → Vietnamese, fake data, one request at a time, manual composition, and no real translation integration. No production navigation, persistence, capture, popup, or additional architectural framework is included.
