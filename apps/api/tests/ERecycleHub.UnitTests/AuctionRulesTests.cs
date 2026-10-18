using ERecycleHub.Domain;

namespace ERecycleHub.UnitTests;

public class AuctionRulesTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private static AuctionRules.BidContext Ctx(
        bool active = true, bool publisherActive = true, double startOffsetH = -1, double? endOffsetH = 24, decimal current = 100, decimal increment = 10,
        decimal insurance = 50, int? owner = 7, int bidder = 3, UserRole role = UserRole.Customer, bool bidderActive = true, decimal wallet = 500) =>
        new(active, publisherActive, Now.AddHours(startOffsetH), endOffsetH is null ? null : Now.AddHours(endOffsetH.Value), current, increment, insurance,
            owner, bidder, role, bidderActive, wallet);

    [Theory]
    [InlineData(1, 5, AuctionPhase.Upcoming)]
    [InlineData(-1, 5, AuctionPhase.Live)]
    [InlineData(-5, -1, AuctionPhase.Ended)]
    [InlineData(0, 5, AuctionPhase.Live)]   // starts exactly now: live
    [InlineData(-5, 0, AuctionPhase.Ended)] // ends exactly now: ended (end is exclusive)
    public void Phase_follows_start_and_end(double startH, double endH, AuctionPhase expected) =>
        Assert.Equal(expected, AuctionRules.Phase(Now.AddHours(startH), Now.AddHours(endH), Now));

    [Fact]
    public void Missing_end_date_counts_as_ended_once_started() =>
        Assert.Equal(AuctionPhase.Ended, AuctionRules.Phase(Now.AddHours(-1), null, Now));

    [Fact]
    public void Minimum_next_bid_is_current_plus_increment() => Assert.Equal(110m, AuctionRules.MinimumNextBid(100m, 10m));

    [Fact]
    public void Valid_bid_at_exact_minimum_is_accepted() => Assert.Null(AuctionRules.Validate(Ctx(), 110m, Now));

    [Fact]
    public void Bid_below_minimum_is_rejected() => Assert.Equal(BidErrors.BelowMinimum, AuctionRules.Validate(Ctx(), 109.99m, Now));

    [Fact]
    public void First_bid_equal_to_start_price_is_rejected_like_legacy() => Assert.Equal(BidErrors.BelowMinimum, AuctionRules.Validate(Ctx(current: 100), 100m, Now));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(110.001)]
    public void Non_positive_or_sub_cent_amounts_are_invalid(decimal amount) => Assert.Equal(BidErrors.InvalidAmount, AuctionRules.Validate(Ctx(), amount, Now));

    [Fact]
    public void Inactive_auction_rejected() => Assert.Equal(BidErrors.Inactive, AuctionRules.Validate(Ctx(active: false), 500m, Now));

    [Fact]
    public void Auction_of_inactive_publisher_rejected() => Assert.Equal(BidErrors.Inactive, AuctionRules.Validate(Ctx(publisherActive: false), 500m, Now));

    [Fact]
    public void Not_started_rejected() => Assert.Equal(BidErrors.NotStarted, AuctionRules.Validate(Ctx(startOffsetH: 1), 500m, Now));

    [Fact]
    public void Ended_rejected() => Assert.Equal(BidErrors.Ended, AuctionRules.Validate(Ctx(startOffsetH: -48, endOffsetH: -1), 500m, Now));

    [Fact]
    public void Bid_at_the_end_instant_rejected() => Assert.Equal(BidErrors.Ended, AuctionRules.Validate(Ctx(endOffsetH: 0), 500m, Now));

    [Fact]
    public void Blocked_bidder_rejected() => Assert.Equal(BidErrors.AccountBlocked, AuctionRules.Validate(Ctx(bidderActive: false), 500m, Now));

    [Fact]
    public void Admin_cannot_bid() => Assert.Equal(BidErrors.AdminCannotBid, AuctionRules.Validate(Ctx(role: UserRole.Admin), 500m, Now));

    [Fact]
    public void Seller_may_bid_on_someone_elses_auction() => Assert.Null(AuctionRules.Validate(Ctx(role: UserRole.Customer), 500m, Now));

    [Fact]
    public void Owner_cannot_bid_on_own_auction() => Assert.Equal(BidErrors.OwnAuction, AuctionRules.Validate(Ctx(owner: 3, bidder: 3, role: UserRole.Customer), 500m, Now));

    [Fact]
    public void Wallet_below_insurance_rejected() => Assert.Equal(BidErrors.InsufficientInsurance, AuctionRules.Validate(Ctx(insurance: 600, wallet: 599.99m), 500m, Now));

    [Fact]
    public void Wallet_equal_to_insurance_is_enough() => Assert.Null(AuctionRules.Validate(Ctx(insurance: 500, wallet: 500), 500m, Now));

    [Fact]
    public void Platform_owned_auction_has_no_owner_check() => Assert.Null(AuctionRules.Validate(Ctx(owner: null), 500m, Now));

    [Theory]
    [InlineData(0.01, true)]
    [InlineData(1000000, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(1.005, false)]
    public void Money_validation(decimal amount, bool ok) => Assert.Equal(ok, Money.IsValidPositive(amount, 1_000_000m));

    [Fact]
    public void Money_above_maximum_rejected() => Assert.False(Money.IsValidPositive(1_000_000.01m, 1_000_000m));
}
