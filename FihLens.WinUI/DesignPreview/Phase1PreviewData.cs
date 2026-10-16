using FihLens.Core.Models;
using FihLens.Core.Enums;
using FihLens.WinUI.ViewModels;
using CommunityToolkit.Mvvm.Input;
using FihLens.Application.Clipboard;

namespace FihLens.WinUI.DesignPreview;

public static class Phase1PreviewData
{
    public static TranslationPopupViewModel Japanese(Language target = Language.English) =>
        TranslationPopupViewModel.FromPreviewTranslation(new TranslationResult
        {
            SourceLanguage = Language.Japanese,
            TargetLanguage = target,
            OriginalText = "彼は走るのが速い。",
            NaturalTranslation = target == Language.Vietnamese ? "Anh ấy chạy nhanh." : "He runs fast.",
            Explanation = target == Language.Vietnamese ? "速い chỉ tốc độ trong ngữ cảnh này." : "速い describes speed in this context.",
            Vocabulary = new[]
            {
                new VocabularyItem
                {
                    Word = "速い", Reading = "はやい", PartOfSpeech = "adjective",
                    Meaning = target == Language.Vietnamese ? "nhanh" : "fast",
                    Synonyms = new[] { "素早い" }, ExampleSentence = "彼は走るのが速い。"
                }
            }
        }, "design fixture", "Sample Japanese word card for visual review; synonyms can differ in nuance.");

    public static TranslationPopupViewModel Compact()
    {
        var viewModel = TranslationPopupViewModel.FromPreviewTranslation(new TranslationResult
        {
            SourceLanguage = Language.English,
            TargetLanguage = Language.Vietnamese,
            OriginalText = "She told a joke to break the ice.",
            NaturalTranslation = "Cô ấy kể một câu chuyện cười để phá tan sự ngượng ngùng.",
            Explanation = "To make people feel relaxed when a conversation or meeting feels awkward.",
            Context = "Here, the joke helps everyone feel more comfortable. It does not mean breaking actual ice.",
            Vocabulary = new[]
            {
                new VocabularyItem
                {
                    Word = "break the ice",
                    PartOfSpeech = "Idiom",
                    Meaning = "phá tan sự ngượng ngùng"
                }
            }
        }, "design fixture", "Sample translation and vocabulary for visual review only.");
        viewModel.BindPreviewSaveActions(
            new RelayCommand(() => viewModel.SetPreviewState(viewModel.IsExpanded ? PopupPreviewState.ExpandedSaving : PopupPreviewState.Saving), () => !viewModel.IsPreviewSaving),
            new RelayCommand(() => viewModel.SetPreviewState(viewModel.IsExpanded ? PopupPreviewState.Expanded : PopupPreviewState.Compact), () => viewModel.IsPreviewSaved),
            new RelayCommand(() => viewModel.SetPreviewState(PopupPreviewState.OcrLoading)));
        viewModel.BindActions(new RelayCommand(() => viewModel.SetPreviewState(PopupPreviewState.Translating)),
            new RelayCommand(() => viewModel.SetPreviewState(PopupPreviewState.Canceled)), "Preview only: fake AI action; no text leaves this device.", new PreviewClipboardWriter());
        return viewModel;
    }

    private sealed class PreviewClipboardWriter : IClipboardWriter
    {
        public Task WriteTextAsync(string text, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
