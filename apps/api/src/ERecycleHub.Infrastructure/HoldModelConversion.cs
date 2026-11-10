namespace ERecycleHub.Infrastructure;

/// <summary>
/// One-time conversion of bids held under the OLD debit-at-bid model (ledger reasons 5-8) to the hold-only model. A database where the wallet flag was
/// never turned on has no such ledger rows, so every statement below touches nothing there. Run by the BidHoldsAndSettlement migration, and by a test
/// against seeded old-model data (so both use exactly this text). Safe to run twice: users already converted (a HoldConversion row, reason 10) are skipped.
/// </summary>
public static class HoldModelConversion
{
    public const string Script = """
-- Users that went through the old model: they have ledger rows with the old bid reasons and no conversion yet.
SELECT DISTINCT l.UserId INTO #OldModelUsers
FROM WalletLedger l
WHERE l.Reason IN (5, 6, 7, 8)
  AND NOT EXISTS (SELECT 1 FROM WalletLedger c WHERE c.UserId = l.UserId AND c.Reason = 10);

-- Held bids on ENDED auctions were already charged under the old model: they count as settled (paid) from the end time.
UPDATE b SET SettledAtUtc = COALESCE(a.EndsAtUtc, SYSUTCDATETIME())
FROM Bids b JOIN Auctions a ON a.Id = b.AuctionId
WHERE b.HeldAmount > 0 AND b.ReleasedAtUtc IS NULL AND b.SettledAtUtc IS NULL
  AND (a.EndsAtUtc IS NULL OR a.EndsAtUtc <= SYSUTCDATETIME())
  AND b.UserId IN (SELECT UserId FROM #OldModelUsers);

-- Held bids on RUNNING auctions were debited at bid time: credit that money back, so it is a hold again and the balance is whole.
SELECT u.Id AS UserId, u.WalletBalance AS BalanceBefore, h.Amount
INTO #Conversion
FROM Users u
JOIN (SELECT b.UserId, SUM(b.HeldAmount) AS Amount
      FROM Bids b JOIN Auctions a ON a.Id = b.AuctionId
      WHERE b.HeldAmount > 0 AND b.ReleasedAtUtc IS NULL AND b.SettledAtUtc IS NULL AND a.EndsAtUtc > SYSUTCDATETIME()
      GROUP BY b.UserId) h ON h.UserId = u.Id
WHERE u.Id IN (SELECT UserId FROM #OldModelUsers);

UPDATE u SET WalletBalance = u.WalletBalance + c.Amount
FROM Users u JOIN #Conversion c ON c.UserId = u.Id;

INSERT INTO WalletLedger (UserId, Amount, BalanceBefore, BalanceAfter, Reason, CreatedAtUtc)
SELECT UserId, Amount, BalanceBefore, BalanceBefore + Amount, 10, SYSUTCDATETIME() FROM #Conversion;

DROP TABLE #Conversion;
DROP TABLE #OldModelUsers;
""";
}
