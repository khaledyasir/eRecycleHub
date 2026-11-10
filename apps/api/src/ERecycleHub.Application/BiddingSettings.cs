using Microsoft.Extensions.Configuration;

namespace ERecycleHub.Application;

/// <summary>Switches for the bidding rules. Kept behind an interface so tests can flip them per test.</summary>
public interface IBiddingSettings
{
    /// <summary>
    /// When true a bid takes its amount from the bidder's wallet (and an outbid or cancelled bid gives it back). When false bids behave as before
    /// (insurance check only), but money already held by earlier bids is still returned whenever those bids are outbid or cancelled.
    /// </summary>
    bool DebitWallet { get; }
}

/// <summary><c>Bidding:DebitWallet</c> (environment variable <c>Bidding__DebitWallet</c>), read once at startup: changing it needs a restart. Default false.</summary>
public sealed class ConfiguredBiddingSettings(IConfiguration config) : IBiddingSettings
{
    public bool DebitWallet { get; } = config.GetValue("Bidding:DebitWallet", false);
}
