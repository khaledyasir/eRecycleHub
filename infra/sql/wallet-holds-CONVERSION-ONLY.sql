-- Run this ONLY on a database where wallet-holds-UP.sql was already run with the OLD version of the script
-- (it printed "Msg 207 Invalid column name 'SettledAtUtc'" and skipped the data conversion, but the migration
-- 20261110114731_BidHoldsAndSettlement was recorded as applied, so re-running UP does not repeat it).
-- The columns exist now, so it can run as a normal batch. Safe to run twice: users that already have a conversion row are skipped.
--
-- Step 1: see whether there is anything to convert. Zero rows = the old debit-at-bid model was never used here; you do not need step 2.
SELECT COUNT(DISTINCT UserId) AS UsersOnTheOldModel FROM WalletLedger WHERE Reason IN (5, 6, 7, 8);
GO

-- Step 2: the conversion (same text the migration runs).
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
GO
