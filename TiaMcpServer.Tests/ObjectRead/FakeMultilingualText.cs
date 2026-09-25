using System.Globalization;

// Shape-compatible stand-ins for the Openness multilingual text types. RichValueReader matches
// the exact CLR name Siemens.Engineering.MultilingualText and reads it through public properties,
// so these doubles live in that namespace; the test project never references Siemens assemblies.
namespace Siemens.Engineering;

internal sealed class MultilingualText
{
    public MultilingualText(IEnumerable<MultilingualTextItem> items) => Items = items;

    public IEnumerable<MultilingualTextItem> Items { get; }
}

internal sealed class MultilingualTextItem
{
    public MultilingualTextItem(Language? language, string? text)
    {
        Language = language;
        Text = text;
    }

    public Language? Language { get; }

    public string? Text { get; }
}

internal sealed class Language
{
    public Language(CultureInfo culture) => Culture = culture;

    public CultureInfo Culture { get; }
}
