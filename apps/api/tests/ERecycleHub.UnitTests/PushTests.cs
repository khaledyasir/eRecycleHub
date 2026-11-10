using System.Text;
using ERecycleHub.Application;
using ERecycleHub.Application.Mobile;
using ERecycleHub.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace ERecycleHub.UnitTests;

public class PushTests
{
    [Fact]
    public void Tokens_are_split_into_batches_of_at_most_1000_without_duplicates()
    {
        var tokens = Enumerable.Range(0, 2500).Select(i => $"t{i}").Concat(["t1", "t2"]).ToList();
        var batches = PushBatching.Split(tokens).ToList();
        Assert.Equal([1000, 1000, 500], batches.Select(b => b.Length).ToArray());
        Assert.Equal(2500, batches.Sum(b => b.Length));
    }

    [Fact]
    public void Credentials_accept_raw_json_or_base64_and_reject_everything_else()
    {
        const string json = "{\"type\":\"service_account\"}";
        Assert.Equal(json, FirebaseCredentials.Normalize("  " + json + " "));
        Assert.Equal(json, FirebaseCredentials.Normalize(Convert.ToBase64String(Encoding.UTF8.GetBytes(json))));
        Assert.Null(FirebaseCredentials.Normalize(null));
        Assert.Null(FirebaseCredentials.Normalize("   "));
        Assert.Null(FirebaseCredentials.Normalize("not json and not base64!"));
        Assert.Null(FirebaseCredentials.Normalize(Convert.ToBase64String(Encoding.UTF8.GetBytes("plain text"))));
    }

    [Fact]
    public void Credentials_in_an_unusable_state_never_create_a_gateway_or_leak_text()
    {
        var config = new StubConfig(new() { ["Firebase:Credentials"] = "{\"type\":\"service_account\",\"private_key\":\"SECRET-KEY-TEXT\"}" });
        var log = new CapturingLogger();
        Assert.Null(FirebasePushGateway.TryCreate(config, log));
        Assert.DoesNotContain(log.Lines, l => l.Contains("SECRET-KEY-TEXT"));
        Assert.Null(FirebasePushGateway.TryCreate(new StubConfig([]), log));
    }

    [Theory]
    [InlineData("INVALID_ARGUMENT", true)]
    [InlineData("invalid-argument", true)]
    [InlineData("NOT_FOUND", true)]
    [InlineData("registration-token-not-registered", true)]
    [InlineData("INTERNAL", false)]
    [InlineData("UNAVAILABLE", false)]
    [InlineData(null, false)]
    public void Only_dead_token_reasons_cause_deletion(string? reason, bool dead) =>
        Assert.Equal(dead, FirebasePushGateway.IsDeadTokenReason(reason));

    [Fact]
    public async Task Queue_runs_nothing_itself_and_drops_and_counts_work_beyond_its_capacity()
    {
        var queue = new BackgroundWorkQueue(NullLogger<BackgroundWorkQueue>.Instance, capacity: 2);
        BackgroundWork noop = (_, _) => Task.CompletedTask;
        Assert.True(queue.TryEnqueue("a", noop));
        Assert.True(queue.TryEnqueue("b", noop));
        Assert.False(queue.TryEnqueue("c", noop));
        Assert.Equal(1, queue.Dropped);
        Assert.False(await queue.WhenIdleAsync(TimeSpan.FromMilliseconds(50)));   // nothing is draining it yet
        Assert.True(queue.Reader.TryRead(out var first));
        Assert.Equal("a", first!.Name);
    }

    [Fact]
    public async Task A_stopped_queue_still_hands_out_what_it_holds_but_takes_no_new_work()
    {
        var queue = new BackgroundWorkQueue(NullLogger<BackgroundWorkQueue>.Instance, capacity: 5);
        BackgroundWork noop = (_, _) => Task.CompletedTask;
        queue.TryEnqueue("kept", noop);
        queue.Complete();
        Assert.False(queue.TryEnqueue("late", noop));
        Assert.Equal(1, queue.Dropped);
        var names = new List<string>();
        await foreach (var item in queue.Reader.ReadAllAsync()) names.Add(item.Name);
        Assert.Equal(["kept"], names);
    }

    private static readonly DateTime At = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_push_carries_type_auction_id_title_body_and_the_row_id_all_as_strings()
    {
        var m = PushPayloads.Build(NotificationKinds.Outbid, 42, 981, "en", "Lot", "لوت", 130m, null, At);
        Assert.Equal("outbid", m.Type);
        Assert.Equal(42, m.AuctionId);
        Assert.Equal("outbid", m.Data["type"]);
        Assert.Equal("42", m.Data["auction_id"]);
        Assert.Equal("981", m.Data["notification_id"]);
        Assert.Equal("130.00", m.Data["amount"]);
        Assert.Equal("2026-11-10T12:00:00Z", m.Data["event_at"]);
        Assert.Equal(m.Title, m.Data["title"]);
        Assert.Equal(m.Body, m.Data["body"]);
        Assert.False(m.Data.ContainsKey("charged"));
        Assert.Equal("You were outbid", m.Title);
        Assert.Contains("Lot", m.Body);
    }

    [Fact]
    public void The_text_follows_the_devices_language_and_defaults_to_arabic()
    {
        var en = PushPayloads.Build(NotificationKinds.NewBid, 1, 2, "en", "Lot", "لوت", 10m, null, At);
        var ar = PushPayloads.Build(NotificationKinds.NewBid, 1, 2, "ar", "Lot", "لوت", 10m, null, At);
        var none = PushPayloads.Build(NotificationKinds.NewBid, 1, 2, null, "Lot", "لوت", 10m, null, At);
        Assert.Equal("New bid", en.Title);
        Assert.Equal("مزايدة جديدة", ar.Title);
        Assert.Equal(ar.Title, none.Title);
        Assert.Equal("New bid", none.Data["title_en"]);   // both languages are always in the data
        Assert.Equal("مزايدة جديدة", none.Data["title_ar"]);
    }

    [Theory]
    [InlineData(NotificationKinds.NewBid, "new_bid")]
    [InlineData(NotificationKinds.Outbid, "outbid")]
    [InlineData(NotificationKinds.Joined, "joined")]
    [InlineData(NotificationKinds.AuctionWon, "auction_won")]
    [InlineData(NotificationKinds.Ended, "auction_ended")]
    [InlineData(NotificationKinds.PaymentFailed, "payment_failed")]
    [InlineData(NotificationKinds.TopUpApproved, "topup_approved")]
    [InlineData(NotificationKinds.TopUpRejected, "topup_rejected")]
    [InlineData(NotificationKinds.WithdrawalApproved, "withdrawal_approved")]
    [InlineData(NotificationKinds.WithdrawalRejected, "withdrawal_rejected")]
    [InlineData(NotificationKinds.CreditReversed, "credit_reversed")]
    public void Every_kind_has_a_push_type(string kind, string type) => Assert.Equal(type, PushPayloads.TypeOf(kind));

    [Theory]
    [InlineData(NotificationKinds.TopUpApproved, "topup_approved")]
    [InlineData(NotificationKinds.TopUpRejected, "topup_rejected")]
    [InlineData(NotificationKinds.WithdrawalApproved, "withdrawal_approved")]
    [InlineData(NotificationKinds.WithdrawalRejected, "withdrawal_rejected")]
    [InlineData(NotificationKinds.CreditReversed, "credit_reversed")]
    public void A_wallet_push_has_the_phase_4_fields_but_no_auction_id(string kind, string type)
    {
        var m = PushPayloads.BuildWallet(kind, 55, 981, "en", 25m, At);
        Assert.Equal(type, m.Type);
        Assert.Null(m.AuctionId);
        Assert.Equal(type, m.Data["type"]);
        Assert.Equal("55", m.Data["transaction_id"]);
        Assert.Equal("981", m.Data["notification_id"]);
        Assert.Equal("25.00", m.Data["amount"]);
        Assert.False(m.Data.ContainsKey("auction_id"));
        Assert.All(new[] { "title", "body", "title_en", "title_ar", "body_en", "body_ar", "event_at" }, k => Assert.False(string.IsNullOrWhiteSpace(m.Data[k])));
        Assert.Equal(m.Data["title_en"], m.Title);
        Assert.Equal("wallet_55", m.CollapseKey);
    }

    [Fact]
    public void A_wallet_push_uses_the_devices_language_and_defaults_to_arabic()
    {
        var en = PushPayloads.BuildWallet(NotificationKinds.TopUpApproved, 1, 1, "en", 10m, At);
        var ar = PushPayloads.BuildWallet(NotificationKinds.TopUpApproved, 1, 1, "ar", 10m, At);
        var none = PushPayloads.BuildWallet(NotificationKinds.TopUpApproved, 1, 1, null, 10m, At);
        Assert.Equal(en.Data["title_en"], en.Title);
        Assert.Equal(ar.Data["title_ar"], ar.Title);
        Assert.Equal(ar.Title, none.Title);
    }

    [Fact]
    public void A_won_push_and_the_ended_push_collapse_into_one_notification_and_the_win_reports_the_charge()
    {
        var won = PushPayloads.Build(NotificationKinds.AuctionWon, 7, 1, "en", "Lot", "لوت", 130m, 130m, At);
        var ended = PushPayloads.BuildEndedForTopic(7, "Lot", "لوت", At);
        Assert.Equal("auction_7_end", won.CollapseKey);
        Assert.Equal(won.CollapseKey, ended.CollapseKey);
        Assert.Equal("130.00", won.Data["charged"]);
        Assert.Equal("auction_ended", ended.Type);
        Assert.Contains("Auction ended", ended.Title);    // one text for every device: both languages
        Assert.Contains("انتهى المزاد", ended.Title);
        Assert.NotEqual(PushPayloads.CollapseKey("new_bid", 7), PushPayloads.CollapseKey("outbid", 7));
    }

    [Theory]
    [InlineData(NotificationKinds.Joined, "auction")]
    [InlineData(NotificationKinds.Ended, "auction")]
    [InlineData(NotificationKinds.AuctionWon, "bid")]
    [InlineData(NotificationKinds.PaymentFailed, "wallet")]
    public void The_list_chip_of_the_new_kinds(string kind, string chip) => Assert.Equal(chip, NotificationTexts.TypeOf(kind));

    [Fact]
    public void The_new_kinds_have_texts_in_both_languages()
    {
        foreach (var kind in new[] { NotificationKinds.Joined, NotificationKinds.Ended, NotificationKinds.PaymentFailed })
        {
            var t = NotificationTexts.For(kind, "Lot", "لوت", 130m, null, null);
            Assert.False(string.IsNullOrWhiteSpace(t.TitleEn));
            Assert.False(string.IsNullOrWhiteSpace(t.TitleAr));
            Assert.False(string.IsNullOrWhiteSpace(t.BodyEn));
            Assert.False(string.IsNullOrWhiteSpace(t.BodyAr));
        }
    }

    private sealed class StubConfig(Dictionary<string, string?> values) : Microsoft.Extensions.Configuration.IConfiguration
    {
        public string? this[string key] { get => values.GetValueOrDefault(key); set => values[key] = value; }
        public IEnumerable<Microsoft.Extensions.Configuration.IConfigurationSection> GetChildren() => [];
        public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => throw new NotSupportedException();
        public Microsoft.Extensions.Configuration.IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    }

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger
    {
        public List<string> Lines { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception) + (exception?.ToString() ?? ""));
    }
}
