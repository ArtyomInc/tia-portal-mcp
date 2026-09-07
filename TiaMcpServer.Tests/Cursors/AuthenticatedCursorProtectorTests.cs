using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using TiaMcpServer.Cursors;
using Xunit;

namespace TiaMcpServer.Tests.Cursors;

public class AuthenticatedCursorProtectorTests
{
    private const string ProcessInstanceId = "process-a";
    private const string Purpose = "project-tree";
    private static readonly byte[] TestKey = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
    private static readonly byte[] OtherKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();

    [Fact]
    public void ProtectUnprotect_WithInjectedKeyAndProcessId_IsDeterministicAndRoundTrips()
    {
        using var protector = new AuthenticatedCursorProtector(TestKey, ProcessInstanceId);
        var state = new TestState("value");

        var first = protector.Protect(Purpose, state);
        var second = protector.Protect(Purpose, state);
        var result = protector.Unprotect<TestState>(Purpose, first);

        Assert.Equal(first, second);
        Assert.Equal(
            "{\"formatVersion\":1,\"processInstanceId\":\"process-a\",\"purpose\":\"project-tree\",\"state\":{\"value\":\"value\"}}",
            DecodePayload(first));
        Assert.DoesNotContain("=", first);
        Assert.Equal(1, first.Count(character => character == '.'));
        Assert.Equal(AuthenticatedCursorStatus.Success, result.Status);
        Assert.Equal(state, result.State);
    }

    [Fact]
    public void Unprotect_RejectsAnyPayloadOrSignatureByteChange()
    {
        using var protector = new AuthenticatedCursorProtector(TestKey, ProcessInstanceId);
        var parts = protector.Protect(Purpose, new TestState("value")).Split('.');

        Assert.Throws<AuthenticatedCursorException>(() =>
            protector.Unprotect<TestState>(Purpose, $"{ChangeByte(parts[0])}.{parts[1]}"));
        Assert.Throws<AuthenticatedCursorException>(() =>
            protector.Unprotect<TestState>(Purpose, $"{parts[0]}.{ChangeByte(parts[1])}"));
    }

    [Fact]
    public void PurposeSeparation_RejectsAValidCursorFromAnotherDomain()
    {
        using var protector = new AuthenticatedCursorProtector(TestKey, ProcessInstanceId);
        var cursor = protector.Protect("hardware-page", new TestState("value"));

        Assert.Throws<AuthenticatedCursorException>(() =>
            protector.Unprotect<TestState>(Purpose, cursor));
    }

    [Theory]
    [InlineData("")]
    [InlineData("%%%.AAAA")]
    [InlineData("one-part")]
    [InlineData("one.two.three")]
    [InlineData(".signature")]
    [InlineData("payload.")]
    [InlineData("cGF5bG9hZA==.c2lnbmF0dXJl")]
    public void Unprotect_RejectsMalformedOrPaddedBase64UrlFraming(string cursor)
    {
        using var protector = new AuthenticatedCursorProtector(TestKey, ProcessInstanceId);

        Assert.Throws<AuthenticatedCursorException>(() =>
            protector.Unprotect<TestState>(Purpose, cursor));
    }

    [Fact]
    public void Unprotect_RejectsNonCanonicalBase64UrlPadBits()
    {
        using var protector = new AuthenticatedCursorProtector(TestKey, ProcessInstanceId);
        var parts = protector.Protect(Purpose, new TestState("value")).Split('.');

        Assert.Throws<AuthenticatedCursorException>(() =>
            protector.Unprotect<TestState>(Purpose, $"{parts[0]}.{ChangeUnusedPadBits(parts[1])}"));
    }

    [Theory]
    [MemberData(nameof(NonCanonicalEnvelopes))]
    public void Unprotect_RejectsCorrectlySignedNonCanonicalOrStructurallyInvalidEnvelope(string payload)
    {
        using var protector = new AuthenticatedCursorProtector(TestKey, ProcessInstanceId);

        Assert.Throws<AuthenticatedCursorException>(() =>
            protector.Unprotect<TestState>(Purpose, Sign(payload, TestKey)));
    }

    [Fact]
    public void Unprotect_RejectsCorrectlySignedInvalidUtf8()
    {
        using var protector = new AuthenticatedCursorProtector(TestKey, ProcessInstanceId);
        var invalidUtf8 = new byte[] { 0xC3, 0x28 };

        Assert.Throws<AuthenticatedCursorException>(() =>
            protector.Unprotect<TestState>(Purpose, Sign(invalidUtf8, TestKey)));
    }

    [Fact]
    public void Unprotect_RejectsInputLongerThan4096Characters()
    {
        using var protector = new AuthenticatedCursorProtector(TestKey, ProcessInstanceId);

        Assert.Throws<AuthenticatedCursorException>(() =>
            protector.Unprotect<TestState>(Purpose, new string('A', 4097)));
    }

    [Fact]
    public void ForeignProcess_IsClassifiedBeforeDomainStateIsAccepted()
    {
        using var issuer = new AuthenticatedCursorProtector(TestKey, ProcessInstanceId);
        using var reader = new AuthenticatedCursorProtector(OtherKey, "process-b");
        var cursor = issuer.Protect(Purpose, new IncompatibleState(42));

        var result = reader.Unprotect<TestState>(Purpose, cursor);

        Assert.Equal(AuthenticatedCursorStatus.ForeignProcess, result.Status);
        Assert.Null(result.State);
    }

    [Fact]
    public void CurrentProcessCursor_WithBadSignature_IsRejected()
    {
        using var issuer = new AuthenticatedCursorProtector(OtherKey, ProcessInstanceId);
        using var reader = new AuthenticatedCursorProtector(TestKey, ProcessInstanceId);
        var cursor = issuer.Protect(Purpose, new TestState("value"));

        Assert.Throws<AuthenticatedCursorException>(() =>
            reader.Unprotect<TestState>(Purpose, cursor));
    }

    [Fact]
    public void Dispose_ZeroesOwnedKeyAndRejectsLaterOperations()
    {
        var callerKey = (byte[])TestKey.Clone();
        var protector = new AuthenticatedCursorProtector(callerKey, ProcessInstanceId);
        var cursor = protector.Protect(Purpose, new TestState("value"));
        var ownedKey = Assert.IsType<byte[]>(typeof(AuthenticatedCursorProtector)
            .GetField("_key", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(protector));

        protector.Dispose();

        Assert.All(ownedKey, value => Assert.Equal(0, value));
        Assert.Equal(TestKey, callerKey);
        Assert.Throws<ObjectDisposedException>(() => protector.Protect(Purpose, new TestState("value")));
        Assert.Throws<ObjectDisposedException>(() => protector.Unprotect<TestState>(Purpose, cursor));
    }

    public static IEnumerable<object[]> NonCanonicalEnvelopes()
    {
        const string canonical =
            "{\"formatVersion\":1,\"processInstanceId\":\"process-a\",\"purpose\":\"project-tree\",\"state\":{\"value\":\"value\"}}";
        yield return new object[]
        {
            "{\"processInstanceId\":\"process-a\",\"formatVersion\":1,\"purpose\":\"project-tree\",\"state\":{\"value\":\"value\"}}",
        };
        yield return new object[] { "{ " + canonical[1..] };
        yield return new object[] { canonical.Replace("process-a", "\\u0070rocess-a", StringComparison.Ordinal) };
        yield return new object[] { canonical[..^1] + ",\"purpose\":\"project-tree\"}" };
        yield return new object[] { canonical[..^1] + ",\"extra\":true}" };
        yield return new object[] { canonical.Replace(",\"purpose\":\"project-tree\"", string.Empty, StringComparison.Ordinal) };
        yield return new object[] { canonical.Replace("\"formatVersion\":1", "\"formatVersion\":2", StringComparison.Ordinal) };
        yield return new object[] { canonical.Replace("\"processInstanceId\":\"process-a\"", "\"processInstanceId\":\"\"", StringComparison.Ordinal) };
        yield return new object[] { canonical + "{}" };
    }

    private static string DecodePayload(string cursor)
        => Encoding.UTF8.GetString(DecodeBase64Url(cursor.Split('.')[0]));

    private static string Sign(string payload, byte[] key)
        => Sign(Encoding.UTF8.GetBytes(payload), key);

    private static string Sign(byte[] payload, byte[] key)
    {
        using var hmac = new HMACSHA256(key);
        return $"{EncodeBase64Url(payload)}.{EncodeBase64Url(hmac.ComputeHash(payload))}";
    }

    private static string ChangeByte(string value)
        => (value[0] == 'A' ? "B" : "A") + value[1..];

    private static string ChangeUnusedPadBits(string value)
    {
        var replacement = value[^1] switch
        {
            'A' => 'B',
            'Q' => 'R',
            'g' => 'h',
            'w' => 'x',
            _ => throw new InvalidOperationException("A 32-byte value must end in canonical two-bit base64 data."),
        };
        return value[..^1] + replacement;
    }

    private static string EncodeBase64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] DecodeBase64Url(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
        return Convert.FromBase64String(normalized);
    }

    private sealed record TestState(string Value);

    private sealed record IncompatibleState(int Value);
}
