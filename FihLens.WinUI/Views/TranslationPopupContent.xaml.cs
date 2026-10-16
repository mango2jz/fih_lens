using FihLens.WinUI.ViewModels;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FihLens.WinUI.Views;

public sealed partial class TranslationPopupContent : UserControl
{
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
        UpdateStatusStyles();
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
        ExplainButton.Command = ViewModel?.ToggleExpansionCommand;
        CopyButton.Command = ViewModel?.CopyCommand;
        SaveButton.Command = ViewModel?.SaveCommand;
        UpdateStatusStyles();
        UpdateExpansionLayout();
    }

    private void UpdateStatusStyles()
    {
        StageStatus.Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources[ViewModel?.IsErrorStatus == true ? "Phase1ErrorStatusStyle" : "Phase1StatusStyle"];
        StageStatus.Padding = ViewModel?.IsProcessingPreview == true ? new Thickness(16) : ViewModel?.IsPreviewStage == true ? new Thickness(12) : new Thickness(8);
        FeedbackStatus.Style = ViewModel?.CopyFeedback.Length > 0 && ViewModel.IsSuccessFeedback ? (Style)Resources["CopiedStatusStyle"] : (Style)Microsoft.UI.Xaml.Application.Current.Resources[ViewModel?.IsErrorStatus == true ? "Phase1ErrorStatusStyle" : ViewModel?.IsSuccessFeedback == true ? "Phase1SuccessStatusStyle" : "Phase1StatusStyle"];
        SaveButton.Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources[ViewModel?.IsPreviewSaved == true ? "Phase1ActionButtonStyle" : "Phase1PrimaryButtonStyle"];
        CopyFeedbackText.Visibility = string.IsNullOrEmpty(ViewModel?.CopyFeedback) ? Visibility.Collapsed : Visibility.Visible;
        SaveFeedbackText.Visibility = string.IsNullOrEmpty(ViewModel?.SaveFeedback) ? Visibility.Collapsed : Visibility.Visible;
        UpdateActionWidths(ActionButtons.ActualWidth);
    }

    private void ActionButtons_SizeChanged(object sender, SizeChangedEventArgs e)
        => UpdateActionWidths(e.NewSize.Width);

    private void UpdateActionWidths(double availableWidth)
    {
        int saveWidth = ViewModel?.IsPreviewSaved == true ? 112 : 136;
        bool fitsReferenceRow = availableWidth >= saveWidth + 112 + 84 + 16;
        ActionButtons.Orientation = fitsReferenceRow ? Orientation.Horizontal : Orientation.Vertical;
        SaveButton.Width = fitsReferenceRow ? saveWidth : double.NaN;
        ExplainButton.Width = fitsReferenceRow ? 112 : double.NaN;
        CopyButton.Width = fitsReferenceRow ? 84 : double.NaN;
    }
}
