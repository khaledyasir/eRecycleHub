-- OPTIONAL. Undoes ONLY the data change made by the migration 20261110114731_BidHoldsAndSettlement
-- (the one-time conversion of bids held under the old debit-at-bid model). It is a no-op if the conversion
-- never changed anything, which is the case for any database where Bidding__DebitWallet was never on.
-- Run it BEFORE wallet-holds-DOWN.sql (it needs WalletLedger rows with Reason = 10 and nothing else).
-- Untested against SQL Server: read it, run it inside the transaction below and check the SELECT output first.
-- It refuses to run if any user's balance is lower than the conversion credit (the money was spent since).

SET XACT_ABORT ON;
BEGIN TRANSACTION;

SELECT l.UserId, l.Amount AS ConversionCredit, u.WalletBalance
INTO #Undo
FROM WalletLedger l JOIN Users u ON u.Id = l.UserId
WHERE l.Reason = 10;

SELECT * FROM #Undo;   -- review: these balances are about to be reduced and these ledger rows removed

IF EXISTS (SELECT 1 FROM #Undo WHERE WalletBalance < ConversionCredit)
BEGIN
    DROP TABLE #Undo;
    ROLLBACK TRANSACTION;
    THROW 50001, 'A user spent the converted money; undo it by hand.', 1;
END;

UPDATE u SET WalletBalance = u.WalletBalance - x.TotalCredit
FROM Users u JOIN (SELECT UserId, SUM(ConversionCredit) AS TotalCredit FROM #Undo GROUP BY UserId) x ON x.UserId = u.Id;

DELETE FROM WalletLedger WHERE Reason = 10;

DROP TABLE #Undo;

-- Check, then replace ROLLBACK with COMMIT:
--   SELECT u.Id, u.WalletBalance, SUM(l.Amount) AS LedgerSum FROM Users u LEFT JOIN WalletLedger l ON l.UserId = u.Id GROUP BY u.Id, u.WalletBalance HAVING u.WalletBalance <> ISNULL(SUM(l.Amount), 0);
ROLLBACK TRANSACTION;
