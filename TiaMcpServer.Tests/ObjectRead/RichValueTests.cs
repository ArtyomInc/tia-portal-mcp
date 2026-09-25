using System.Drawing;
using System.Globalization;
using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.DomainReads;
using TiaMcpServer.Network;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.ObjectModel;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.ObjectRead;

/// <summary>
/// The null supported-type fix (v2.8.1) and the rich value contract (R0.1): dates, durations,
/// colors, multilingual texts, and scalar arrays.
/// </summary>
public sealed class RichValueTests
{
    private static Siemens.Engineering.MultilingualText Texts(params (string Culture, string Text)[] items)
        => new(items.Select(item => new Siemens.Engineering.MultilingualTextItem(
            new Siemens.Engineering.Language(new CultureInfo(item.Culture)), item.Text)));

    [Fact]
    public void SupportedTypeList_DropsNullEntriesInsteadOfFailing()
    {
        var resolved = SupportedTypeList.Resolved(new Type?[] { typeof(int), null, typeof(string) });

        Assert.Equal(new[] { "System.Int32", "System.String" }, SupportedTypeList.Names(resolved));
        Assert.Empty(SupportedTypeList.Resolved(null));
        Assert.Empty(SupportedTypeList.Resolved(new Type?[] { null }));
    }

    [Fact]
    public void RichValueReader_ReadsColorAsHexAndAlpha()
    {
        Assert.True(RichValueReader.TryReadColor(Color.FromArgb(128, 255, 0, 16), out var hex, out var alpha));

        Assert.Equal("#FF0010", hex);
        Assert.Equal(128, alpha);
        Assert.False(RichValueReader.TryReadColor("#FF0010", out _, out _));
    }

    [Fact]
    public void RichValueReader_ReadsMultilingualTextByCulture()
    {
        Assert.True(RichValueReader.TryReadMultilingualText(Texts(("fr-FR", "Démarrer"), ("en-US", "Start")), out var texts));

        Assert.Equal(new[] { "en-US", "fr-FR" }, texts.Keys.ToArray());
        Assert.Equal("Démarrer", texts["fr-FR"]);
    }

    [Fact]
    public void RichValueReader_SkipsUnreadableItemsAndRejectsOtherTypes()
    {
        var text = new Siemens.Engineering.MultilingualText(new[]
        {
            new Siemens.Engineering.MultilingualTextItem(null, "orphan"),
            new Siemens.Engineering.MultilingualTextItem(new Siemens.Engineering.Language(new CultureInfo("de-DE")), null),
        });

        Assert.True(RichValueReader.TryReadMultilingualText(text, out var texts));
        Assert.Equal(string.Empty, Assert.Single(texts).Value);
        Assert.False(RichValueReader.TryReadMultilingualText(new object(), out _));
        Assert.False(RichValueReader.TryReadMultilingualText(new Siemens.Engineering.MultilingualText(null!), out _));
    }

    [Fact]
    public void NetworkNormalizer_PublishesDatesAndDurations()
    {
        var date = NetworkAttributeValueNormalizer.Normalize(new DateTime(2026, 9, 25, 7, 0, 0, DateTimeKind.Utc));
        var duration = NetworkAttributeValueNormalizer.Normalize(TimeSpan.FromMilliseconds(1500));

        Assert.Equal("dateTime", date.Value!.Kind);
        Assert.Equal("2026-09-25T07:00:00.0000000Z", date.Value.Value);
        Assert.Equal("duration", duration.Value!.Kind);
        Assert.Equal("00:00:01.5000000", duration.Value.Value);
    }

    [Fact]
    public void NetworkNormalizer_PublishesGuidAndVersionAsStrings()
    {
        var guid = Guid.Parse("2f1b8c7e-0000-4000-8000-000000000001");

        Assert.Equal(guid.ToString("D"), NetworkAttributeValueNormalizer.Normalize(guid).Value!.Value);
        Assert.Equal("1.2.3", NetworkAttributeValueNormalizer.Normalize(new Version(1, 2, 3)).Value!.Value);
    }

    [Fact]
    public void NetworkNormalizer_PublishesColorsAndTexts()
    {
        var color = NetworkAttributeValueNormalizer.Normalize(Color.FromArgb(255, 0, 128, 255));
        var text = NetworkAttributeValueNormalizer.Normalize(Texts(("en-US", "Start")));

        Assert.Equal("color", color.Value!.Kind);
        var colorValue = Assert.IsType<NetworkColorValueInfo>(color.Value.Value);
        Assert.Equal("#0080FF", colorValue.Hex);
        Assert.Equal(255, colorValue.Alpha);
        Assert.Equal("multilingualText", text.Value!.Kind);
        Assert.Equal("Start", Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(text.Value.Value)["en-US"]);
    }

    [Fact]
    public void NetworkNormalizer_PublishesScalarArraysAsTypedItems()
    {
        var result = NetworkAttributeValueNormalizer.Normalize(new[] { "a", "b" });

        Assert.Equal("array", result.Value!.Kind);
        var items = Assert.IsAssignableFrom<IReadOnlyList<NetworkAttributeValueInfo>>(result.Value.Value);
        Assert.Equal(new object?[] { "a", "b" }, items.Select(item => item.Value));
        Assert.All(items, item => Assert.Equal("string", item.Kind));
    }

    public static IEnumerable<object[]> UnrepresentableArrays()
    {
        yield return new object[] { new[] { new[] { 1 } } };
        yield return new object[] { new object[] { 1, new object() } };
        yield return new object[] { Enumerable.Range(0, NetworkAttributeValueNormalizer.MaxArrayLength + 1).ToArray() };
    }

    [Theory]
    [MemberData(nameof(UnrepresentableArrays))]
    public void NetworkNormalizer_RejectsNestedMixedOrOversizedArrays(object input)
    {
        var result = NetworkAttributeValueNormalizer.Normalize(input);

        Assert.False(result.IsRepresentable);
        Assert.Equal(input.GetType().FullName, result.ClrTypeName);
    }

    [Fact]
    public void ScalarNormalizer_PublishesColorsAndTexts()
    {
        Assert.True(ObjectScalarNormalizer.TryNormalize(Color.FromArgb(0, 1, 2, 3), out var color));
        Assert.True(ObjectScalarNormalizer.TryNormalize(Texts(("en-US", "Stop")), out var text));

        Assert.Equal("""{"alpha":0,"hex":"#010203"}""", JsonSerializer.Serialize(new SortedDictionary<string, object?>((IDictionary<string, object?>)color!)));
        Assert.Equal("""{"en-US":"Stop"}""", JsonSerializer.Serialize(text));
    }

    [Fact]
    public void ReadValues_ListsColorAndTextAttributesOfAWidget()
    {
        var button = new FakeObjectNode("Siemens.Engineering.HmiUnified.UI.Widgets.HmiButton", "Button_1")
            .WithAttribute("BackColor", Color.FromArgb(255, 200, 200, 200))
            .WithAttribute("Text", Texts(("en-US", "Go")))
            .WithAttribute("Left", 400);
        var unavailable = new List<string>();

        var values = ObjectScalarNormalizer.ReadValues(button, null, unavailable);

        Assert.Empty(unavailable);
        Assert.Equal(new[] { "BackColor", "Left", "Text" }, values.Keys.OrderBy(key => key, StringComparer.Ordinal));
    }

    [Fact]
    public void ReadValues_ListsNavigableMultilingualTextsButNotOtherNavigableObjects()
    {
        var button = new FakeObjectNode("HmiButton", "Button_1")
            .WithAttribute("Text", Texts(("en-US", "Go")))
            .WithAttribute("Owner", "not listed");
        button.NavigableAttributes.Add("Text");
        button.NavigableAttributes.Add("Owner");
        var unavailable = new List<string>();

        var values = ObjectScalarNormalizer.ReadValues(button, null, unavailable);

        Assert.Equal("Go", Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(Assert.Single(values).Value)["en-US"]);
        Assert.Equal("Text", values.Keys.Single());
    }

    [Theory]
    [InlineData("""{"kind":"dateTime","value":"2026-09-25T07:00:00.0000000Z"}""")]
    [InlineData("""{"kind":"duration","value":"1.02:03:04.5000000"}""")]
    [InlineData("""{"kind":"color","value":{"hex":"#0080FF","alpha":255}}""")]
    [InlineData("""{"kind":"multilingualText","value":{"en-US":"Start","fr-FR":"Démarrer"}}""")]
    [InlineData("""{"kind":"array","value":[{"kind":"integer","value":1},{"kind":"null","value":null}]}""")]
    public void HostContract_AcceptsRichValueKinds(string value)
    {
        var item = ProjectInspection(value);

        Assert.Equal(OperationBatchStatus.Succeeded, item.Status);
    }

    [Theory]
    [InlineData("""{"kind":"dateTime","value":"yesterday"}""")]
    [InlineData("""{"kind":"duration","value":1500}""")]
    [InlineData("""{"kind":"color","value":{"hex":"red","alpha":255}}""")]
    [InlineData("""{"kind":"color","value":{"hex":"#0080FF","alpha":256}}""")]
    [InlineData("""{"kind":"multilingualText","value":{"en-US":1}}""")]
    [InlineData("""{"kind":"array","value":[{"kind":"array","value":[]}]}""")]
    [InlineData("""{"kind":"array","value":[{"kind":"integer","value":"1"}]}""")]
    public void HostContract_RejectsMalformedRichValues(string value)
    {
        var item = ProjectInspection(value);

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.Equal("protocol_error", item.Failure!.Category);
    }

    [Theory]
    [InlineData("\"text\"")]
    [InlineData("12")]
    [InlineData("null")]
    [InlineData("""{"hex":"#F2F4FF","alpha":255}""")]
    [InlineData("""{"en-US":"Start","fr-FR":"Démarrer"}""")]
    [InlineData("""[1,"a",{"en-US":"x"}]""")]
    public void ListingValues_AcceptPublishedShapes(string json)
        => Assert.True(DomainPayloadProjector.IsListingValue(JsonDocument.Parse(json).RootElement.Clone()));

    [Theory]
    [InlineData("""{"hex":"#F2F4FF","alpha":255,"extra":1}""")]
    [InlineData("""{"nested":{"a":"b"}}""")]
    [InlineData("""{"count":1}""")]
    [InlineData("""[[1]]""")]
    public void ListingValues_RejectOtherObjectsAndNestedArrays(string json)
        => Assert.False(DomainPayloadProjector.IsListingValue(JsonDocument.Parse(json).RootElement.Clone()));

    private static StructuredOperationItem ProjectInspection(string value)
        => NetworkPayloadContract.Project(
            new NetworkOperationRequest { OperationId = "op-1", Operation = "inspect_network_object" },
            WorkerCallResult.Ok($$"""
                {
                  "target": {"kind":"node","deviceName":"PLC_1","nodeId":"node-1"},
                  "evidence": {"nodeName":"X1","nodeType":"Ethernet","deviceItemPath":[]},
                  "attributes": [
                    {"name":"Rich","source":"dynamic","access":"readOnly","supportedTypes":[],"availability":"available","value":{{value}}}
                  ],
                  "messages": []
                }
                """));
}
