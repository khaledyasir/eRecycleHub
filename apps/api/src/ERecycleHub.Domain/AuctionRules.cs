namespace ERecycleHub.Domain;

/// <summary>Stable error codes; the frontend maps them to localised messages.</summary>
public static class BidErrors
{
    public const string InvalidAmount = "bid.invalid_amount";
    public const string NotFound = "bid.not_found";
    public const string Inactive = "bid.inactive";
    public const string NotStarted = "bid.not_started";
    public const string Ended = "bid.ended";
    public const string AccountBlocked = "bid.account_blocked";
    public const string AdminCannotBid = "bid.admin_cannot_bid";
    public const string OwnAuction = "bid.own_auction";
    public const string InsufficientInsurance = "bid.insufficient_insurance";
    public const string BelowMinimum = "bid.below_minimum";
}

public static class Money
{
    public const decimal MaxAmount = 1_000_000_000m;

    /// <summary>True for positive amounts with at most two decimal places (no silent rounding of money).</summary>
    public static bool IsValidPositive(decimal amount, decimal max = MaxAmount) =>
        amount > 0 && amount <= max && decimal.Round(amount, 2) == amount;

    public static bool IsValidNonNegative(decimal amount, decimal max = MaxAmount) =>
        amount == 0 || IsValidPositive(amount, max);
}

public static class AuctionRules
{
    /// <summary>Upcoming before the start, live until the end (exclusive), ended afterwards or when no end date exists.</summary>
    public static AuctionPhase Phase(DateTime startsAtUtc, DateTime? endsAtUtc, DateTime nowUtc)
    {
        if (nowUtc < startsAtUtc) return AuctionPhase.Upcoming;
        if (endsAtUtc is null || nowUtc >= endsAtUtc.Value) return AuctionPhase.Ended;
        return AuctionPhase.Live;
    }

    /// <summary>The next bid must be at least the current price plus the increment (also for the first bid, as in the legacy system).</summary>
    public static decimal MinimumNextBid(decimal currentPrice, decimal increment) => currentPrice + increment;

    public sealed record BidContext(
        bool AuctionIsActive,
        bool PublisherIsActive,
        DateTime StartsAtUtc,
        DateTime? EndsAtUtc,
        decimal CurrentPrice,
        decimal BidIncrement,
        decimal InsuranceAmount,
        int? OwnerUserId,
        int BidderId,
        UserRole BidderRole,
        bool BidderIsActive,
        decimal BidderWallet);

    /// <summary>Returns null when the bid is acceptable, otherwise the error code. Order matches the legacy checks.</summary>
    public static string? Validate(BidContext c, decimal amount, DateTime nowUtc)
    {
        if (!Money.IsValidPositive(amount)) return BidErrors.InvalidAmount;
        if (!c.AuctionIsActive || !c.PublisherIsActive) return BidErrors.Inactive;
        var phase = Phase(c.StartsAtUtc, c.EndsAtUtc, nowUtc);
        if (phase == AuctionPhase.Upcoming) return BidErrors.NotStarted;
        if (phase == AuctionPhase.Ended) return BidErrors.Ended;
        if (!c.BidderIsActive) return BidErrors.AccountBlocked;
        // Legacy let any active account bid; administrators are excluded because they approve the wallet credits that gate bidding.
        if (c.BidderRole == UserRole.Admin) return BidErrors.AdminCannotBid;
        if (c.OwnerUserId == c.BidderId) return BidErrors.OwnAuction;
        if (c.BidderWallet < c.InsuranceAmount) return BidErrors.InsufficientInsurance;
        if (amount < MinimumNextBid(c.CurrentPrice, c.BidIncrement)) return BidErrors.BelowMinimum;
        return null;
    }
}
