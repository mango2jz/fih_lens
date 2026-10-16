using System.Text.Json;
using FihLens.Core.Models;
using FihLens.WinUI.DesignPreview;
using FihLens.WinUI.ViewModels;
using FihLens.WinUI.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Graphics;

namespace Phase1WindowProbe;

public partial class App
{
    private async Task RunContent()
    {
        var window = new Window { Title = "T02 actual compact content fixture — no production host" };
        owned.Add(window);
        window.Closed += (_, _) => closed.Add(window);
        var results = new List<object>();
        var bindingTransitions = new List<object>();
        var view = new TranslationPopupContent();
        var wrapper = new Grid { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 17, 34, 51)) };
        wrapper.Children.Add(view);
        var loaded = new TaskCompletionSource();
        RoutedEventHandler handler = (_, _) => loaded.TrySetResult();
        view.Loaded += handler;
        window.Content = wrapper;
        try { window.Activate(); await loaded.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
        finally { view.Loaded -= handler; }
        Require(((Border)view.FindName("SnapshotRoot")).Visibility == Visibility.Collapsed && !view.IsEnabled
            && ((Button)view.FindName("CloseButton")).Command is null, "Initial null snapshot is not collapsed, disabled and detached from close.");
        bindingTransitions.Add(new { transition = "initial-null", collapsedAndDisabled = true, closeCommandNull = true });
        var priorCloseEvents = 0;
        foreach (var longContent in new[] { false, true })
        {
            var vm = longContent
                ? TranslationPopupViewModel.FromTranslation(new TranslationResult
                {
                    OriginalText = string.Join(" ", Enumerable.Repeat("This is a known long English fixture, never captured user text.", 20)),
                    NaturalTranslation = string.Join(" ", Enumerable.Repeat("Đây là dữ liệu mẫu dài để kiểm tra cuộn nội dung.", 30))
                }, "long design fixture", string.Join("\n", Enumerable.Repeat("Fixture notice: retain complete attribution and source information.", 12)) + "\nEND OF SOURCE NOTICE FIXTURE", isPreview: true)
                : Phase1PreviewData.Compact();
            view.ViewModel = vm;
            vm.CloseRequested += (_, _) => priorCloseEvents++;
            var scale = view.XamlRoot.RasterizationScale;
            window.AppWindow.ResizeClient(new SizeInt32((int)Math.Round(400 * scale), (int)Math.Round(325 * scale)));
            for (var transition = 0; transition < 3; transition++)
            {
                var theme = transition == 1 ? ElementTheme.Dark : ElementTheme.Light;
                view.RequestedTheme = theme;
                await Task.Delay(150);
                view.UpdateLayout();
                var descendants = Descendants(view).ToArray();
                TextBlock Text(string name) => descendants.OfType<TextBlock>().Single(e => AutomationProperties.GetName(e) == name);
                var original = Text("Original English text");
                var outputText = Text("Current output");
                var preview = descendants.OfType<TextBlock>().Single(e => e.Text == "Design preview");
                var source = Text("Result source");
                Require(original.Text == vm.OriginalText && outputText.Text == vm.OutputText && source.Text == vm.SourceLabel, "Actual compact x:Bind snapshot differs from VM.");
                Require(preview.Visibility == Visibility.Visible && vm.IsPreview && AutomationProperties.GetName(preview) == vm.PreviewNotice && Equals(ToolTipService.GetToolTip(preview), vm.PreviewNotice), "Fixture lacks full accessible preview notice and tooltip.");
                Require(view.ActualTheme == theme, "Actual content retained theme did not change.");
                var surface = (Border)VisualTreeHelper.GetChild(view, 0);
                var color = ((SolidColorBrush)surface.Background).Color.ToString();
                Require(color == (theme == ElementTheme.Light ? "#FFFAFAFA" : "#FF2C2C2C"), "Actual content surface did not resolve retained theme.");
                var buttons = descendants.OfType<Button>().Where(b => AutomationProperties.GetName(b) is "Close translation popup" or "Save phrase unavailable" or "Explanation unavailable" or "Copy unavailable").ToArray();
                Require(buttons.Length == 4, "Expected actual header close plus three reference actions.");
                foreach (var button in buttons) Require(Within(view, button), "Header close or action clipped outside actual content.");
                var save = buttons.Single(b => AutomationProperties.GetName(b) == "Save phrase unavailable");
                var explain = buttons.Single(b => AutomationProperties.GetName(b) == "Explanation unavailable");
                var copy = buttons.Single(b => AutomationProperties.GetName(b) == "Copy unavailable");
                Require(!save.IsEnabled && !explain.IsEnabled && !copy.IsEnabled, "Unavailable fixture action enabled.");
                Require(Math.Abs(save.ActualWidth - 136) < 0.1 && Math.Abs(explain.ActualWidth - 112) < 0.1 && Math.Abs(copy.ActualWidth - 84) < 0.1, "Reference action widths differ.");
                var saveBounds = Bounds(view, save);
                var explainBounds = Bounds(view, explain);
                var copyBounds = Bounds(view, copy);
                Require(Math.Abs(explainBounds.X - saveBounds.Right - 8) < 0.1 && Math.Abs(copyBounds.X - explainBounds.Right - 8) < 0.1, "Reference action gaps differ.");
                var scroll = descendants.OfType<ScrollViewer>().First(s => s.Content is StackPanel p && Descendants(p).Contains(original));
                scroll.ChangeView(null, 0, null, true);
                await Task.Delay(100);
                Require(scroll.ScrollableWidth <= 1 && scroll.ExtentWidth <= scroll.ViewportWidth + 1, "Actual compact body has horizontal overflow.");
                var viewportBounds = Bounds(view, scroll);
                var outputBounds = Bounds(view, outputText);
                var shortAnswerFullyVisible = Contains(viewportBounds, outputBounds);
                bool vocabularyFullyVisible = false;
                if (!longContent)
                {
                    var vocabulary = Text("Provided vocabulary");
                    var vocabularyRow = (Border)VisualTreeHelper.GetParent(vocabulary);
                    vocabularyFullyVisible = Contains(viewportBounds, Bounds(view, vocabularyRow));
                    Require(shortAnswerFullyVisible && vocabularyFullyVisible, "Initial short translation or actual vocabulary row is clipped by body viewport.");
                }
                var icons = descendants.OfType<BitmapIcon>().ToArray();
                Require(icons.Length == 5, "Actual compact content must use five original artwork slots.");
                var assetResults = new List<object>();
                foreach (var icon in icons)
                {
                    Require(Math.Abs(icon.ActualWidth - 20) <= 0.1 && Math.Abs(icon.ActualHeight - 20) <= 0.1, "Original icon effective geometry changed.");
                    var image = new RenderTargetBitmap();
                    await image.RenderAsync(icon);
                    var pixels = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(await image.GetPixelsAsync());
                    var visible = 0;
                    var transparent = 0;
                    for (var n = 3; n < pixels.Length; n += 4) { if (pixels[n] > 0) visible++; else transparent++; }
                    Require(visible > 0 && transparent > 0, "Original icon native render empty or opaque.");
                    assetResults.Add(new { uri = icon.UriSource.ToString(), widthDip = icon.ActualWidth, heightDip = icon.ActualHeight, visiblePixels = visible, transparentPixels = transparent });
                }
                var name = $"{(longContent ? "long" : "compact")}-{theme.ToString().ToLowerInvariant()}-{transition}";
                await RenderMixedRoot(view, name + "-top.png");
                var expander = descendants.OfType<Expander>().Single(e => Equals(e.Header, "Source and notices"));
                expander.IsExpanded = true;
                await Task.Delay(100);
                view.UpdateLayout();
                var details = Descendants(view).OfType<TextBlock>().Single(e => AutomationProperties.GetName(e) == "Full source attribution and notices");
                Require(details.Text == vm.SourceDetails && details.IsTextSelectionEnabled, "Full source notices were lost or not selectable.");
                scroll.ChangeView(null, scroll.ScrollableHeight, null, true);
                await Task.Delay(150);
                var detailsBounds = Bounds(view, details);
                var scrollBounds = Bounds(view, scroll);
                Require(Math.Abs(scroll.VerticalOffset - scroll.ScrollableHeight) <= 1 && detailsBounds.Bottom <= scrollBounds.Bottom + 1 && detailsBounds.Bottom > scrollBounds.Top, "Body cannot reach final source notice at bottom.");
                Require(scroll.ScrollableWidth <= 1, "Expanded source notices cause horizontal clipping.");
                await RenderMixedRoot(view, name + "-source-bottom.png");
                results.Add(new
                {
                    fixture = longContent ? "long" : "compact", transition, requestedTheme = theme.ToString(), actualTheme = view.ActualTheme.ToString(),
                    scale, widthDip = view.ActualWidth, heightDip = view.ActualHeight, surface = color,
                    bindingOriginal = original.Text == vm.OriginalText, bindingOutput = outputText.Text == vm.OutputText,
                    bindingSource = source.Text == vm.SourceLabel, previewVisible = true, sourceDetailsIntact = true,
                    initialShortAnswerFullyVisible = shortAnswerFullyVisible, initialVocabularyRowFullyVisible = vocabularyFullyVisible,
                    referenceActionWidths = new[] { save.ActualWidth, explain.ActualWidth, copy.ActualWidth }, referenceActionGaps = new[] { explainBounds.X - saveBounds.Right, copyBounds.X - explainBounds.Right },
                    headerCloseAndActionsWithinView = true, horizontalOverflow = scroll.ScrollableWidth,
                    bodyViewportHeight = scroll.ViewportHeight, bodyScrollableHeight = scroll.ScrollableHeight, sourceBottomReached = true,
                    icons = assetResults, topImage = name + "-top.png", sourceBottomImage = name + "-source-bottom.png"
                });
                expander.IsExpanded = false;
            }
            var previousOriginal = Descendants(view).OfType<TextBlock>().Single(e => AutomationProperties.GetName(e) == "Original English text");
            var previousOutput = Descendants(view).OfType<TextBlock>().Single(e => AutomationProperties.GetName(e) == "Current output");
            view.ViewModel = null;
            await Task.Delay(100);
            var nullOriginal = previousOriginal.Text;
            var nullOutput = previousOutput.Text;
            var snapshot = (Border)view.FindName("SnapshotRoot");
            var close = (Button)view.FindName("CloseButton");
            Require(snapshot.Visibility == Visibility.Collapsed && !view.IsEnabled && close.Command is null, "Null snapshot remains visible, enabled or attached to prior close command.");
            var blank = new RenderTargetBitmap();
            await blank.RenderAsync(wrapper);
            var blankPixels = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(await blank.GetPixelsAsync());
            Require(blankPixels.Length > 0, "Own wrapper blank-null render unavailable.");
            for (var p = 0; p < blankPixels.Length; p += 4)
                Require(blankPixels[p] == 51 && blankPixels[p + 1] == 34 && blankPixels[p + 2] == 17 && blankPixels[p + 3] == 255, "Prior snapshot pixels survive in actual null wrapper render.");
            bindingTransitions.Add(new
            {
                transition = longContent ? "long-snapshot-to-null" : "compact-snapshot-to-null",
                viewModelActuallyNull = view.ViewModel is null,
                originalCleared = string.IsNullOrEmpty(nullOriginal), outputCleared = string.IsNullOrEmpty(nullOutput),
                retainedPriorOriginal = nullOriginal == vm.OriginalText, retainedPriorOutput = nullOutput == vm.OutputText,
                snapshotCollapsed = true, viewDisabled = true, closeCommandNull = true, nativeWrapperBlank = true
            });
            await RenderMixedRoot(wrapper, (longContent ? "long" : "compact") + "-after-null.png");
        }
        var rebound = TranslationPopupViewModel.FromTranslation(new TranslationResult
        {
            OriginalText = "Current rebound English fixture.", NaturalTranslation = "Bản dịch mẫu hiện tại."
        }, "rebound design fixture", isPreview: true);
        var currentCloseEvents = 0;
        rebound.CloseRequested += (_, _) => currentCloseEvents++;
        view.ViewModel = rebound;
        await Task.Delay(100);
        var reboundOriginal = Descendants(view).OfType<TextBlock>().Single(e => AutomationProperties.GetName(e) == "Original English text");
        var reboundOutput = Descendants(view).OfType<TextBlock>().Single(e => AutomationProperties.GetName(e) == "Current output");
        var reboundClose = Descendants(view).OfType<Button>().Single(e => AutomationProperties.GetName(e) == "Close translation popup");
        Require(reboundOriginal.Text == rebound.OriginalText && reboundOutput.Text == rebound.OutputText && view.IsEnabled
            && ((Border)VisualTreeHelper.GetChild(view, 0)).Visibility == Visibility.Visible && ReferenceEquals(reboundClose.Command, rebound.CloseCommand), "Null to current rebound snapshot or close command is stale.");
        reboundClose.Command.Execute(null);
        Require(currentCloseEvents == 1 && priorCloseEvents == 0, "Current snapshot close invoked prior snapshot event.");
        bindingTransitions.Add(new { transition = "null-to-current-rebound", currentTextOnly = true, currentCloseEvents, priorCloseEvents });
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
        {
            success = true, observedUtc = DateTimeOffset.UtcNow, operatingSystem = Environment.OSVersion.VersionString, dotnet = Environment.Version.ToString(), windowsAppSdkPackage = "2.5.1",
            highContrast = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast,
            createdWindows = owned.Count, fixtureOnly = true, productionHostImplemented = false,
            fixtures = results, bindingTransitions, unproved = new[] { "T03 expanded translation popup", "production desktop host/full E1", "real keyboard/Narrator", "real provider/capture/clipboard", "desktop shadow/focus fidelity" }
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static Rect Bounds(FrameworkElement root, FrameworkElement child) => child.TransformToVisual(root).TransformBounds(new Rect(0, 0, child.ActualWidth, child.ActualHeight));
    private static bool Contains(Rect viewport, Rect content) => content.Width > 0 && content.Height > 0 && content.Left >= viewport.Left - 1 && content.Top >= viewport.Top - 1 && content.Right <= viewport.Right + 1 && content.Bottom <= viewport.Bottom + 1;
    private static bool Within(FrameworkElement root, FrameworkElement child)
    {
        var b = Bounds(root, child);
        return b.Width > 0 && b.Height > 0 && b.X >= -1 && b.Y >= -1 && b.Right <= root.ActualWidth + 1 && b.Bottom <= root.ActualHeight + 1;
    }
}
