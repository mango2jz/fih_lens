using FihLens.WinUI.ViewModels;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace FihLens.WinUI.Views;

public sealed partial class TranslationPopupContent : UserControl
{
    private IReadOnlyList<WordChoice>? _shownChoices;

    public TranslationPopupContent()
    {
        InitializeComponent();
        UpdateSnapshotVisibility();
    }

    public TranslationPopupViewModel? ViewModel
    {
        get => (TranslationPopupViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(TranslationPopupViewModel), typeof(TranslationPopupContent),
        new PropertyMetadata(null, OnViewModelChanged));

    internal void FocusClose() => CloseButton.Focus(FocusState.Programmatic);

    internal void FocusSelectedWord() =>
        WordChips().FirstOrDefault(chip => chip.IsChecked == true)?.Focus(FocusState.Keyboard);

    private IEnumerable<RadioButton> WordChips() =>
        WordPanel.Children.OfType<StackPanel>().SelectMany(unit => unit.Children.OfType<RadioButton>());

    // Rebuild only for a new candidate list; a selection change just moves the check.
    private void UpdateWordPicker()
    {
        var choices = ViewModel?.WordChoices ?? [];
        if (!ReferenceEquals(choices, _shownChoices))
        {
            _shownChoices = choices;
            WordPanel.Children.Clear();
            var japanese = ViewModel?.SourceLanguage == FihLens.Core.Enums.Language.Japanese
                ? (FontFamily)Microsoft.UI.Xaml.Application.Current.Resources["Phase1JpRegularFontFamily"] : null;
            foreach (var choice in choices)
            {
                var unit = new StackPanel { Orientation = Orientation.Horizontal };
                // .fl-punct hugs its word: -4px toward it, 4px away.
                if (choice.Word.Leading.Length > 0) unit.Children.Add(Punct(choice.Word.Leading, new Thickness(4, 0, -4, 0)));
                if (choice.HasEntry)
                {
                    var chip = new RadioButton { Content = choice.Word.Text, GroupName = "CapturedWords", Tag = choice.Index, Style = WordStyle("Phase1WordChipStyle") };
                    AutomationProperties.SetName(chip, choice.AutomationName);
                    chip.Checked += WordChip_Checked;
                    if (japanese is not null) chip.FontFamily = japanese;
                    unit.Children.Add(chip);
                }
                else
                {
                    var miss = new TextBlock { Text = choice.Word.Text, Style = WordStyle("Phase1WordMissTextStyle") };
                    AutomationProperties.SetName(miss, choice.AutomationName);
                    if (japanese is not null) miss.FontFamily = japanese;
                    unit.Children.Add(miss);
                }
                if (choice.Word.Trailing.Length > 0) unit.Children.Add(Punct(choice.Word.Trailing, new Thickness(-4, 0, 4, 0)));
                WordPanel.Children.Add(unit);
                if (choice.Word.LineBreakAfter) WordPanel.Children.Add(new WordLineBreak());
            }
            // Park focus on the selection so Enter opens it instead of pressing Close.
            if (choices.Count > 0) DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) is null or Button { Name: nameof(CloseButton) }) FocusSelectedWord();
            });
        }
        int selected = ViewModel?.SelectedWordIndex ?? -1;
        foreach (var chip in WordChips()) chip.IsChecked = (int)chip.Tag == selected;
    }

    private static Style WordStyle(string key) => (Style)Microsoft.UI.Xaml.Application.Current.Resources[key];

    private static TextBlock Punct(string text, Thickness margin) =>
        new() { Text = text, Margin = margin, Style = WordStyle("Phase1WordPunctTextStyle") };

    private void WordChip_Checked(object sender, RoutedEventArgs args)
    {
        if (sender is RadioButton { Tag: int index }) ViewModel?.SelectWord(index);
    }

    internal bool CollapseSourceDisclosure()
    {
        if (!SourceDisclosure.IsExpanded) return false;
        SourceDisclosure.IsExpanded = false;
        SourceDisclosure.Focus(FocusState.Programmatic);
        return true;
    }

    private static void OnViewModelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var control = (TranslationPopupContent)sender;
        if (args.OldValue is TranslationPopupViewModel previous) previous.PropertyChanged -= control.ViewModel_Changed;
        if (args.NewValue is TranslationPopupViewModel current) current.PropertyChanged += control.ViewModel_Changed;
        control.UpdateSnapshotVisibility();
    }

    private void ViewModel_Changed(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(TranslationPopupViewModel.IsExpanded)) UpdateExpansionLayout();
        if (args.PropertyName is nameof(TranslationPopupViewModel.WordChoices) or nameof(TranslationPopupViewModel.SelectedWordIndex)) UpdateWordPicker();
        UpdateBar();
    }

    private void Content_Loaded(object sender, RoutedEventArgs args) => UpdateExpansionLayout();
    private void Content_SizeChanged(object sender, SizeChangedEventArgs args) => UpdateExpansionLayout();

    private void UpdateExpansionLayout()
    {
        bool expanded = ViewModel?.IsExpanded == true;
        // Preserve the compact action edge at reference height; shrink both scroll panes on small displays.
        int compactHeight = expanded ? (ViewModel?.PreferredHeight ?? 569) - (569 - 325) : 325;
        double compactSpace = Math.Max(1, AnswerRow.ActualHeight + ExpandedRow.ActualHeight - (ActualHeight - compactHeight));
        AnswerRow.Height = new GridLength(expanded ? compactSpace : 1, GridUnitType.Star);
        ExpandedRow.Height = expanded ? new GridLength(569 - 325, GridUnitType.Star) : new GridLength(0);
    }

    private void UpdateSnapshotVisibility()
    {
        // Generated x:Bind skips null paths; hide and disable their entire previous snapshot.
        bool hasSnapshot = ViewModel is not null;
        SnapshotRoot.Visibility = hasSnapshot ? Visibility.Visible : Visibility.Collapsed;
        IsEnabled = hasSnapshot;
        CloseButton.Command = ViewModel?.CloseCommand;
        ExplainButton.Command = ViewModel?.ExplainCommand;
        CopyButton.Command = ViewModel?.CopyCommand;
        SaveButton.Command = ViewModel?.SaveCommand;
        UpdateWordPicker();
        UpdateBar();
        UpdateExpansionLayout();
    }

    // The bar shows one text button for the current state; feedback pairs an icon with a word.
    private void UpdateBar()
    {
        static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
        var model = ViewModel;
        bool error = model?.IsErrorStatus == true;
        string feedback = string.Join(" · ", new[] { model?.CopyFeedback ?? "", model?.SaveFeedback ?? "" }.Where(text => text.Length > 0));
        bool hasFeedback = model?.HasFeedback == true;
        FeedbackStatus.Visibility = Show(hasFeedback);
        FeedbackOkIcon.Visibility = Show(hasFeedback && !error);
        FeedbackErrorIcon.Visibility = Show(hasFeedback && error);
        FeedbackOkText.Text = error ? "" : feedback;
        FeedbackErrorText.Text = error ? feedback : "";
        FeedbackOkText.Visibility = Show(hasFeedback && !error);
        FeedbackErrorText.Visibility = Show(hasFeedback && error);

        CopyButton.Visibility = Show(model?.HasNoWordEntries != true);
        StageAlert.Visibility = Show(model?.ShowStageAlert == true);
        BackendError.Visibility = Show(!string.IsNullOrEmpty(model?.BackendErrorMessage));
        FootText.Text = model is null ? "" : model.IsProcessingPreview || model.IsBusy ? "Keep working. This updates by itself."
            : model.ShowSelectAgain || model.HasNoWordEntries ? "Nothing was sent online." : model.FooterNotice;
        bool busy = model?.IsBusy == true || model?.IsProcessingPreview == true;
        FootText.Visibility = Show(model is not null && (model.HasNoWordEntries || !model.ShowResultActions && (model.ShowFooter || busy)));
        BusyShimmer.Visibility = Show(model is { IsBusy: true, IsProcessingPreview: false });
        CancelButton.Visibility = Show(busy);
        SelectAgainButton.Visibility = Show(model?.ShowSelectAgain == true);
        bool askAi = !busy && (model?.IsAiErrorPreview == true || model is { ShowBackendStatuses: true, ShowStageActions: true, ShowAnswer: true, IsPreview: false });
        AskAiButton.Visibility = Show(askAi);
        AskAiLabel.Text = model?.IsAiErrorPreview == true || !string.IsNullOrEmpty(model?.BackendErrorMessage) ? "Try again" : "Ask AI";
    }
}
