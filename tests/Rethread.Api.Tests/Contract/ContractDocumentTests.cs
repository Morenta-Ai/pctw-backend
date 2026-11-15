using System.Text.Json;

namespace Rethread.Api.Tests.Contract;

public sealed class ContractDocumentTests
{
    [Theory]
    [InlineData("user")]
    [InlineData("collector")]
    [InlineData("admin")]
    public void Every_sample_in_the_contract_is_valid_json(string contract)
    {
        var samples = ContractSamples.All(contract).ToList();
        Assert.NotEmpty(samples);
    }

    [Fact]
    public void Finds_response_samples_in_each_style()
    {
        // Admin style: a "Response `200`:" line before the block.
        Assert.Equal("RT-24817",
            ContractSamples.Response("admin", "GET /admin/pickups/{id}").GetProperty("id").GetString());
        // App style: "// 200" inside the block after "// request".
        Assert.Equal("otp_8f2c",
            ContractSamples.Response("collector", "POST /auth/otp/request").GetProperty("challengeId").GetString());
        // The user contract: the request block, then the response block, both unlabelled.
        Assert.Equal("otp_8f2c",
            ContractSamples.Response("user", "POST /auth/otp/request").GetProperty("requestId").GetString());
        // A plain GET block.
        Assert.Equal("col_457", ContractSamples.Response("collector", "GET /me").GetProperty("id").GetString());
        // A heading with a query string.
        Assert.Equal(JsonValueKind.Array,
            ContractSamples.Response("collector", "GET /jobs").GetProperty("items").ValueKind);
    }

    [Fact]
    public void Shape_assert_reports_missing_and_extra_keys()
    {
        var sample = JsonDocument.Parse("""{ "a": 1, "b": { "c": "x" }, "list": [{ "d": true }] }""").RootElement;
        ShapeAssert.Matches(sample,
            JsonDocument.Parse("""{ "a": 2, "b": { "c": null }, "list": [] }""").RootElement);

        var error = Assert.ThrowsAny<Exception>(() => ShapeAssert.Matches(sample,
            JsonDocument.Parse("""{ "a": "2", "b": { "e": 1 }, "list": [{ "d": 1 }] }""").RootElement));
        Assert.Contains("$.a: expected Number", error.Message, StringComparison.Ordinal);
        Assert.Contains("$.b.c: missing", error.Message, StringComparison.Ordinal);
        Assert.Contains("$.b.e: not in the contract", error.Message, StringComparison.Ordinal);
        Assert.Contains("$.list[0].d: expected a boolean", error.Message, StringComparison.Ordinal);
    }
}
