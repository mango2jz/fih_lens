using FihLens.Core.Models;
using FihLens.WinUI.ViewModels;

namespace FihLens.Tests.WinUI;

public sealed class DictionaryCardViewModelTests
{
    [Fact]
    public void ResultRowSummaryJoinsSensesOnOneLine()
    {
        var entry = new DictionaryEntry("break the ice", ["phá tan sự ngượng ngùng", "làm quen, bắt chuyện"], "idiom", null,
            "source", "article-1", "origin");

        var card = new DictionaryCardViewModel(entry, new PronunciationViewModel(null));

        Assert.Equal("phá tan sự ngượng ngùng; làm quen, bắt chuyện", card.Summary);
        Assert.Equal("", card.Reading);
        Assert.Equal("idiom", card.PartOfSpeech);
        Assert.Equal([new DictionarySense(1, "phá tan sự ngượng ngùng", ""), new DictionarySense(2, "làm quen, bắt chuyện", "")], card.Senses);
        Assert.True(card.IsLatin);
    }
}
