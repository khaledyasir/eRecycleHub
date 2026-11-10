# Wallet holds, settlement and notifications: SQL scripts

Generated from the EF Core migrations added for the hold-and-pay bidding model (all five come after `20261109134858_MobileApi`).

| File | What it does |
|---|---|
| `wallet-holds-UP.sql` | Applies the five migrations (idempotent: steps already recorded in `__EFMigrationsHistory` are skipped; one transaction per migration). |
| `wallet-holds-DOWN.sql` | Reverts them in reverse order (idempotent, one transaction per migration, removes the history rows). |
| `wallet-holds-UNDO-CONVERSION.sql` | Optional. Undoes only the one-time data conversion for bids held under the old debit model (a no-op if the old model was never on). Run before the DOWN script. |

Regenerate after any further migration:
```bash
cd apps/api
dotnet ef migrations script 20261109134858_MobileApi <last migration> -p src/ERecycleHub.Infrastructure -s src/ERecycleHub.Api --idempotent -o ../../infra/sql/wallet-holds-UP.sql
dotnet ef migrations script <last migration> 20261109134858_MobileApi -p src/ERecycleHub.Infrastructure -s src/ERecycleHub.Api --idempotent -o ../../infra/sql/wallet-holds-DOWN.sql
```
or apply directly with `dotnet ERecycleHub.Api.dll migrate`.

## Migrations in order
1. `20261110110430_WalletLedgerReferences`: `WalletLedger.ReferenceType`, `ReferenceId`, `BalanceBefore`, index `IX_WalletLedger_ReferenceType_ReferenceId`.
2. `20261110111229_AuctionTopicsAndParticipants`: `Auctions.TopicName` (computed `auction_{Id}`), `DeviceTokens.Platform`, `DeviceId` and index, table `AuctionParticipants`.
3. `20261110112804_BidHolds`: `Bids.HeldAmount`, `Bids.ReleasedAtUtc`, check constraint `CK_Bid_Held`.
4. `20261110114731_BidHoldsAndSettlement`: `Notifications.EventKey` / `PushedAtUtc`, `DeviceTokens.Language`, `Bids.SettledAtUtc`, `Auctions.SettlementFailedAtUtc`, unique index `UX_WalletLedger_AuctionPayment`, unique index `IX_Notifications_UserId_EventKey`, index `IX_Bids_UserId_ReleasedAtUtc_SettledAtUtc`, **plus the one-time data conversion** (see the UP script).
5. `20261110120222_AuctionEndProcessed`: `Auctions.EndProcessedAtUtc`.

The Phase 5 and 6 changes (wallet notifications, rate limits, safeguards) need **no** schema change.

## Before you apply or roll back
* Take a full backup first (`BACKUP DATABASE ... WITH COPY_ONLY`). It is the only complete rollback, because of the data notes below.
* Stop the API (or set `AuctionEnd__Enabled=false` and `Bidding__DebitWallet=false`) while running either script.

## What rolling back does NOT undo
* **Money already charged.** Winners charged at auction close (`WalletLedger.Reason = 9`, AuctionPayment) stay charged: the ledger rows and balances remain after the schema is rolled back. Refund them by hand (credit the user, with a note) if the business needs it.
* **Ledger rows of the new reasons** (9 AuctionPayment, 10 HoldConversion) stay in `WalletLedger`; the older code reads them as plain numbers.
* **Dropped data** is gone: `AuctionParticipants` rows (who took part in which auction), `Notifications.EventKey`/`PushedAtUtc`, `DeviceTokens.Language`/`Platform`/`DeviceId`, holds and settlement markers on bids. Restore from the backup if you need them.
* Rolling the schema back while a **hold is active** leaves the bid without a hold: the old code does not know holds, so the leader is simply the highest bid and nothing is reserved.

## Checks after either script
```sql
SELECT TOP 5 MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC;
SELECT u.Id, u.WalletBalance, SUM(l.Amount) AS LedgerSum
FROM Users u LEFT JOIN WalletLedger l ON l.UserId = u.Id
GROUP BY u.Id, u.WalletBalance HAVING u.WalletBalance <> ISNULL(SUM(l.Amount), 0);   -- expect no rows
```
After applying UP, also run `dotnet ERecycleHub.Api.dll check-wallets` (read-only).
