using System.Text.Json;
using ERecycleHub.Application.Mobile;

namespace ERecycleHub.UnitTests;

public class MobileDetailsJsonTests
{
    private static AuctionDetailsDto Dto(bool leading = false, decimal held = 0, bool debits = false) => new(
        1, null, "T", "ت", null, null, null, null, null, 100m, 110m, 10m, DateTime.UtcNow, DateTime.UtcNow.AddHours(1), "1h", "R1", "Active", null, null, null,
        null, null, 0m, 390m, [], 1, false, "JOD", [], [], leading, held, debits);

    private static JsonElement Json(AuctionDetailsDto d) => JsonSerializer.SerializeToElement(d, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    [Fact]
    public void The_viewer_fields_are_always_present_and_the_bid_only_fields_are_left_out_when_null()
    {
        var j = Json(Dto(leading: true, held: 110m, debits: true));

        Assert.True(j.GetProperty("isLeading").GetBoolean());
        Assert.Equal(110m, j.GetProperty("heldByViewer").GetDecimal());
        Assert.True(j.GetProperty("debitsWallet").GetBoolean());
        Assert.False(j.TryGetProperty("availableAfter", out _));
        Assert.False(j.TryGetProperty("releasedFromPrevious", out _));
    }

    [Fact]
    public void The_bid_response_adds_available_after_and_released_from_previous_and_keeps_the_legacy_names()
    {
        var j = Json(Dto() with { AvailableAfter = 380m, ReleasedFromPrevious = 110m });

        Assert.Equal(380m, j.GetProperty("availableAfter").GetDecimal());
        Assert.Equal(110m, j.GetProperty("releasedFromPrevious").GetDecimal());
        Assert.Equal(0, j.GetProperty("insuranceAmount").GetDecimal());
        Assert.True(j.TryGetProperty("termAndConditon", out _));
        Assert.True(j.TryGetProperty("isexpire", out _));
        Assert.Equal(390m, j.GetProperty("wallet").GetDecimal());
    }

    [Fact]
    public void Zero_held_is_a_number_not_null()
    {
        var j = Json(Dto());
        Assert.Equal(JsonValueKind.Number, j.GetProperty("heldByViewer").ValueKind);
        Assert.Equal(0m, j.GetProperty("heldByViewer").GetDecimal());
        Assert.False(j.GetProperty("isLeading").GetBoolean());
        Assert.False(j.GetProperty("debitsWallet").GetBoolean());
    }
}
