using System.ComponentModel;
using System.Text.Json;

namespace Structly.AI.Tests;

public sealed class OutputTests
{
    [Theory]
    [InlineData("{}", "MissingKey")]
    [InlineData("{\"value\":null}", "Null")]
    [InlineData("{\"value\":1,\"value\":2}", "DuplicateKey")]
    [InlineData("{\"value\":1,\"extra\":2}", "AdditionalKey")]
    [InlineData("{\"Value\":1}", "AdditionalKey")]
    [InlineData("{\"value\":\"1\"}", "TokenType")]
    [InlineData("{\"value\":1.5}", "ScalarValue")]
    [InlineData("{\"value\":2147483648}", "ScalarValue")]
    [InlineData("null", "Null")]
    [InlineData("[]", "TokenType")]
    [InlineData("```json\n{\"value\":1}\n```", "Deserialization")]
    [InlineData("{\"value\":1,}", "Deserialization")]
    public void OutputViolationsAreSafeFailuresWithRetainedMetadata(string json, string code)
    {
        var metadata = new StructuredMetadata { Usage = new() { InputTokens = 20 }, ResponseId = "r1" };
        var result = SchemaTests.Create<Numeric<int>>().ReadOutput(json, metadata: metadata);
        Assert.False(result.IsSuccess);
        Assert.Equal(StructuredErrorKind.InvalidOutput, result.Error!.Kind);
        Assert.Contains(result.Error.Issues, x => x.Code == code);
        Assert.Same(metadata, result.Metadata);
        Assert.Equal(0, result.Value?.Value ?? 0);
        var exception = Assert.Throws<StructuredOperationException>(() => result.EnsureSuccess());
        Assert.Same(metadata, exception.Metadata);
    }

    [Theory]
    [InlineData(typeof(byte), "255", "256")]
    [InlineData(typeof(sbyte), "-128", "128")]
    [InlineData(typeof(short), "-32768", "32768")]
    [InlineData(typeof(ushort), "65535", "65536")]
    [InlineData(typeof(int), "2147483647", "2147483648")]
    [InlineData(typeof(uint), "4294967295", "4294967296")]
    [InlineData(typeof(long), "9223372036854775807", "9223372036854775808")]
    [InlineData(typeof(ulong), "18446744073709551615", "18446744073709551616")]
    [InlineData(typeof(float), "3.4e38", "3.5e38")]
    [InlineData(typeof(double), "1.7e308", "1.8e308")]
    [InlineData(typeof(decimal), "79228162514264337593543950335", "79228162514264337593543950336")]
    public void NumericClrRangesAreEnforced(Type type, string valid, string invalid)
    {
        var method = GetType().GetMethod(nameof(CheckNumber), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.MakeGenericMethod(type);
        method.Invoke(null, [valid, invalid]);
    }

    static void CheckNumber<T>(string valid, string invalid)
    {
        var task = SchemaTests.Create<Numeric<T>>();
        Assert.True(task.ReadOutput("{\"value\":" + valid + "}").IsSuccess);
        Assert.False(task.ReadOutput("{\"value\":" + invalid + "}").IsSuccess);
    }

    [Fact]
    public void TypedDatesGuidsBooleansAndNullablePresenceMatchSerializer()
    {
        var task = SchemaTests.Create<Scalars>();
        const string valid = """{"id":"9b3ee845-e582-41dd-bfe9-b6d9c0f13c8b","date":"2024-02-29","time":"2024-02-29T01:02:03Z","offset":"2024-02-29T01:02:03+01:00","enabled":true,"optional":null}""";
        Assert.True(task.ReadOutput(valid).IsSuccess);
        Assert.False(task.ReadOutput(valid.Replace("2024-02-29", "2023-02-29", StringComparison.Ordinal)).IsSuccess);
        Assert.False(task.ReadOutput(valid.Replace("true", "\"true\"", StringComparison.Ordinal)).IsSuccess);
        Assert.False(task.ReadOutput(valid.Replace(",\"optional\":null", "", StringComparison.Ordinal)).IsSuccess);
        Assert.False(task.ReadOutput(valid.Replace("9b3ee845-e582-41dd-bfe9-b6d9c0f13c8b", "invalid", StringComparison.Ordinal)).IsSuccess);
    }

    [Fact]
    public void ConstraintsAgreeBetweenSchemaAndValidation()
    {
        var task = SchemaTests.Create<Constrained>();
        var properties = task.CreateSchema().GetProperty("properties");
        Assert.Equal(2, properties.GetProperty("text").GetProperty("minLength").GetInt32());
        Assert.Equal(-1.5, properties.GetProperty("number").GetProperty("minimum").GetDouble());
        Assert.Equal(1, properties.GetProperty("items").GetProperty("minItems").GetInt32());
        Assert.True(task.ReadOutput("""{"text":"😀x","number":-1.5,"items":["x"]}""").IsSuccess);
        Assert.False(task.ReadOutput("""{"text":"😀","number":-1.5,"items":["x"]}""").IsSuccess);
        Assert.False(task.ReadOutput("""{"text":"abcde","number":-1.5,"items":["x"]}""").IsSuccess);
        Assert.False(task.ReadOutput("""{"text":"ab","number":1.50001,"items":["x"]}""").IsSuccess);
        Assert.False(task.ReadOutput("""{"text":"ab","number":0,"items":[]}""").IsSuccess);
        Assert.True(SchemaTests.Create<PatternDto>().ReadOutput("""{"value":"prefix AB12 suffix"}""").IsSuccess);
        Assert.False(SchemaTests.Create<PatternDto>().ReadOutput("""{"value":"ab12"}""").IsSuccess);
    }

    [Theory]
    [InlineData("0.1", true)]
    [InlineData("1e-1", true)]
    [InlineData("0.10000000000000000000000000001", true)]
    [InlineData("0.09999999999999999999999999999", false)]
    [InlineData("-1e-100", false)]
    [InlineData("1e+100", false)]
    public void NumericBoundsUseOriginalJsonInsteadOfRoundedClrValues(string number, bool success)
    {
        Assert.Equal(success, SchemaTests.Create<PreciseBounds>().ReadOutput("{\"value\":" + number + "}").IsSuccess);
    }

    [Fact]
    public void PatternWorkAndFailureDiagnosticCountAreBounded()
    {
        var result = SchemaTests.Create<ExpensivePattern>().ReadOutput(JsonSerializer.Serialize(new { value = new string('a', 100000) + "ca" }));
        Assert.False(result.IsSuccess);
        Assert.Contains(result.Error!.Issues, x => x.Code is "PatternTimeout" or "Pattern");
        var large = JsonSerializer.Serialize(Enumerable.Range(0, 200).ToDictionary(i => "extra" + i, i => i));
        Assert.Equal(100, SchemaTests.Create<Numeric<int>>().ReadOutput(large).Error!.Issues.Count);
        var duplicates = JsonSerializer.Serialize(Enumerable.Range(0, 99).ToDictionary(i => "extra" + i, i => i));
        duplicates = duplicates[..^1] + ",\"extra0\":0}";
        Assert.Equal(100, SchemaTests.Create<Numeric<int>>().ReadOutput(duplicates).Error!.Issues.Count);
    }

    [Theory]
    [InlineData("date-time", "2024-02-29T12:00:00Z", "2024-02-30T12:00:00Z")]
    [InlineData("date-time", "2024-02-29T12:00:00+23:00", "2024-02-29T25:00:00Z")]
    [InlineData("time", "12:34:56+01:00", "24:00:00Z")]
    [InlineData("date", "2024-02-29", "2023-02-29")]
    [InlineData("duration", "P1DT2H", "P")]
    [InlineData("duration", "P2W", "P1WT1H")]
    [InlineData("email", "a.b@example.com", "a..b@example.com")]
    [InlineData("email", "\"a b\"@example.com", "Name <a@example.com>")]
    [InlineData("hostname", "example.com", "-bad.example")]
    [InlineData("hostname", "example.com.", "example.com..")]
    [InlineData("ipv4", "192.0.2.1", "192.0.2.999")]
    [InlineData("ipv6", "2001:db8::1", "not-ip")]
    [InlineData("uuid", "9b3ee845-e582-41dd-bfe9-b6d9c0f13c8b", "9b3ee845e58241ddbfe9b6d9c0f13c8b")]
    public void SupportedFormatsHavePositiveAndNegativeValidation(string format, string valid, string invalid)
    {
        var task = SchemaTests.Create<FormatsDto>();
        var data = new Dictionary<string, string>
        {
            ["dateTime"] = "2024-02-29T12:00:00Z",
            ["time"] = "12:34:56+01:00",
            ["date"] = "2024-02-29",
            ["duration"] = "P1DT2H",
            ["email"] = "a.b@example.com",
            ["hostname"] = "example.com",
            ["ipv4"] = "192.0.2.1",
            ["ipv6"] = "2001:db8::1",
            ["uuid"] = "9b3ee845-e582-41dd-bfe9-b6d9c0f13c8b"
        };
        var name = format == "date-time" ? "dateTime" : format;
        data[name] = valid;
        Assert.True(task.ReadOutput(JsonSerializer.Serialize(data)).IsSuccess);
        data[name] = invalid;
        Assert.Contains(task.ReadOutput(JsonSerializer.Serialize(data)).Error!.Issues, x => x.Path == "$." + name && x.Code == "Format");
    }

    [Fact]
    public void EnumCaseNumbersAndUnknownValuesAreRejected()
    {
        var task = SchemaTests.Create<SchemaTests.Box<SchemaTests.State>>();
        Assert.True(task.ReadOutput("""{"value":"in-progress"}""").IsSuccess);
        foreach (var json in new[] { "{\"value\":0}", "{\"value\":\"done\"}", "{\"value\":\"0\"}", "{\"value\":\"Unknown\"}" })
            Assert.False(task.ReadOutput(json).IsSuccess);
    }

    [Fact]
    public void HostConstructionFailureDoesNotLeakExceptionTextOrLoseUsage()
    {
        var metadata = new StructuredMetadata { Usage = new() { OutputTokens = 20 } };
        var result = SchemaTests.Create<Throwing>().ReadOutput("""{"value":1}""", metadata: metadata);
        Assert.Equal(StructuredErrorKind.InvalidOutput, result.Error!.Kind);
        Assert.DoesNotContain("sensitive", result.Error.Message);
        Assert.DoesNotContain("sensitive", String.Join(" ", result.Error.Issues.Select(x => x.Message)));
        Assert.Same(metadata, result.Metadata);
    }

    [Fact]
    public void ResultsAndIssuesAreImmutableAndDefaultValueSuccessIsUnambiguous()
    {
        var metadata = new StructuredMetadata();
        var success = StructuredResult<int>.Success(0, metadata);
        Assert.True(success.IsSuccess);
        Assert.Equal(0, success.EnsureSuccess());
        Assert.Null(success.Error);
        var source = new List<StructuredIssue> { new("$", "Code", "Safe") };
        var error = new StructuredError { Kind = StructuredErrorKind.InvalidOutput, Message = "Safe", Issues = source };
        var warnings = new List<StructuredWarning> { new("Code", "Safe") };
        var result = StructuredResult<int>.Failure(error, metadata, warnings);
        source.Clear(); warnings.Clear();
        Assert.Single(error.Issues); Assert.Single(result.Warnings);
        Assert.Throws<NotSupportedException>(() => ((IList<StructuredIssue>)error.Issues).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<StructuredWarning>)result.Warnings).Clear());
        Assert.Throws<ArgumentNullException>(() => StructuredResult<string>.Success(null!, metadata));
    }

    [Fact]
    public void PublicAccountingContractsValidateCountsAndDetachCapturedJson()
    {
        foreach (var property in typeof(StructuredUsage).GetProperties())
        {
            var exception = Assert.Throws<System.Reflection.TargetInvocationException>(() => property.SetValue(new StructuredUsage(), (long?)-1));
            Assert.IsType<ArgumentOutOfRangeException>(exception.InnerException);
        }
        using var document = JsonDocument.Parse("""{"id":"r1"}""");
        var metadata = new StructuredMetadata { RawResponse = document.RootElement };
        document.Dispose();
        Assert.Equal("r1", metadata.RawResponse!.Value.GetProperty("id").GetString());
        Assert.Throws<ArgumentException>(() => new StructuredError { Kind = (StructuredErrorKind)99, Message = "Safe" });
        Assert.Throws<ArgumentException>(() => new StructuredError { Kind = StructuredErrorKind.InvalidOutput, Message = " " });
        Assert.Throws<ArgumentOutOfRangeException>(() => new StructuredError { Kind = StructuredErrorKind.RateLimited, Message = "Safe", RetryAfter = TimeSpan.FromSeconds(-1) });
    }

    public sealed record Numeric<T>(T Value);
    public sealed record Scalars
    {
        public Guid Id { get; init; }
        public DateOnly Date { get; init; }
        public DateTime Time { get; init; }
        public DateTimeOffset Offset { get; init; }
        public bool Enabled { get; init; }
        public int? Optional { get; init; }
    }
    public sealed class Constrained
    {
        [StringConstraint(MinLength = 2, MaxLength = 4)] public string Text { get; set; } = "";
        [NumberConstraint(Minimum = -1.5, Maximum = 1.5)] public decimal Number { get; set; }
        [CollectionConstraint(MinItems = 1, MaxItems = 2)] public List<string> Items { get; set; } = [];
    }
    public sealed class PatternDto { [StringConstraint(Pattern = "[A-Z]{2}[0-9]{2}")] public string Value { get; set; } = ""; }
    public sealed class PreciseBounds { [NumberConstraint(Minimum = 0.1, Maximum = 0.2)] public double Value { get; set; } }
    public sealed class ExpensivePattern { [StringConstraint(Pattern = "^a*[ab]*a*[ab]*a*[ab]*a*[ab]*[ab]$")] public string Value { get; set; } = ""; }
    public sealed class FormatsDto
    {
        [StringConstraint(Format = "date-time")] public string DateTime { get; set; } = "";
        [StringConstraint(Format = "time")] public string Time { get; set; } = "";
        [StringConstraint(Format = "date")] public string Date { get; set; } = "";
        [StringConstraint(Format = "duration")] public string Duration { get; set; } = "";
        [StringConstraint(Format = "email")] public string Email { get; set; } = "";
        [StringConstraint(Format = "hostname")] public string Hostname { get; set; } = "";
        [StringConstraint(Format = "ipv4")] public string Ipv4 { get; set; } = "";
        [StringConstraint(Format = "ipv6")] public string Ipv6 { get; set; } = "";
        [StringConstraint(Format = "uuid")] public string Uuid { get; set; } = "";
    }
    public sealed class Throwing
    {
        public int Value { get => 0; set => throw new InvalidOperationException("sensitive host content"); }
    }
}
