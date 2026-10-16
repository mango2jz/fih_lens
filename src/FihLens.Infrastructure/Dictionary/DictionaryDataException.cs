namespace FihLens.Infrastructure.Dictionary;

public sealed class DictionaryDataException(string category, int? row = null)
    : Exception(row is null ? $"Local dictionary could not load ({category})." : $"Local dictionary could not load ({category}, row {row}).")
{
    public string Category { get; } = category;
    public int? Row { get; } = row;
}
