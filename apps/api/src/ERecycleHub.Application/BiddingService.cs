using System.Data;
using ERecycleHub.Domain;
using ERecycleHub.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ERecycleHub.Application;

public sealed record BidPlaced(long BidId, int AuctionId, decimal CurrentPrice, decimal MinimumNextBid, int BidCount);

public sealed class BiddingService(AppDbContext db, TimeProvider clock)
{
    /// <summary>
    /// Places a bid atomically. The auction row is locked with UPDLOCK for the whole transaction, so concurrent bids on one
    /// auction are serialised and each is validated against the price the previous one committed. The clock is read after the
    /// lock is held, so a bid that waits past the end time is rejected.
    /// </summary>
    public async Task<BidPlaced> PlaceBidAsync(int userId, int auctionId, decimal amount, CancellationToken ct = default)
    {
        if (!Money.IsValidPositive(amount)) throw new AppException(BidErrors.InvalidAmount);

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        var locked = await db.Database
            .SqlQuery<int>($"SELECT Id AS Value FROM Auctions WITH (UPDLOCK, ROWLOCK) WHERE Id = {auctionId}")
            .ToListAsync(ct);
        if (locked.Count == 0) throw new AppException(BidErrors.NotFound, 404);

        var a = await db.Auctions.AsNoTracking().Where(x => x.Id == auctionId).Select(x => new
        {
            x.IsActive,
            x.StartsAtUtc,
            x.EndsAtUtc,
            x.CurrentPrice,
            x.BidIncrement,
            x.InsuranceAmount,
            x.TitleEn,
            OwnerUserId = x.Publisher == null ? (int?)null : x.Publisher.UserId,
            PublisherActive = x.Publisher == null || x.Publisher.User!.IsActive,
        }).SingleAsync(ct);

        var bidder = await db.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => new { u.Role, Active = u.IsActive && !u.IsDeleted, u.WalletBalance })
            .SingleOrDefaultAsync(ct) ?? throw new AppException(BidErrors.AccountBlocked, 403);

        var now = clock.GetUtcNow().UtcDateTime;
        var context = new AuctionRules.BidContext(a.IsActive, a.PublisherActive, a.StartsAtUtc, a.EndsAtUtc, a.CurrentPrice, a.BidIncrement,
            a.InsuranceAmount, a.OwnerUserId, userId, bidder.Role, bidder.Active, bidder.WalletBalance);
        var error = AuctionRules.Validate(context, amount, now);
        if (error is not null)
        {
            object? args = error switch
            {
                BidErrors.BelowMinimum => new { minimum = AuctionRules.MinimumNextBid(a.CurrentPrice, a.BidIncrement) },
                BidErrors.InsufficientInsurance => new { insurance = a.InsuranceAmount },
                _ => null,
            };
            throw new AppException(error, 400, args);
        }

        var bid = new Bid { AuctionId = auctionId, UserId = userId, Amount = amount, CreatedAtUtc = now };
        db.Bids.Add(bid);
        await db.Auctions.Where(x => x.Id == auctionId).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.CurrentPrice, amount)
            .SetProperty(x => x.BidCount, x => x.BidCount + 1)
            .SetProperty(x => x.UpdatedAtUtc, now), ct);

        // Legacy behaviour: everyone who asked to be notified about this auction (except the bidder) gets an in-app notification.
        var watchers = await db.AuctionWatches.Where(w => w.AuctionId == auctionId && w.UserId != userId).Select(w => w.UserId).ToListAsync(ct);
        db.Notifications.AddRange(watchers.Select(w => new Notification
        {
            UserId = w,
            FromUserId = userId,
            Kind = NotificationKinds.NewBid,
            AuctionId = auctionId,
            Amount = amount,
            Url = $"/auctions/{auctionId}",
            CreatedAtUtc = now,
        }));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var count = await db.Auctions.Where(x => x.Id == auctionId).Select(x => x.BidCount).SingleAsync(ct);
        return new BidPlaced(bid.Id, auctionId, amount, AuctionRules.MinimumNextBid(amount, a.BidIncrement), count);
    }
}
