# Database mapping (legacy → rebuild)

Target: SQL Server, new database (default name `ERecycleHub`), schema owned by EF Core 10 migrations in
`apps/api/src/ERecycleHub.Infrastructure/Migrations`. The legacy database is **read** by the import tool
(`apps/api/tools/ERecycleHub.LegacyImport`) and never modified.

Conventions in the new schema: every money column `decimal(18,2)`; every date `datetime2` holding **UTC**; strings
`nvarchar(400)` unless stated (`max` for rich text); deletes `RESTRICT` except an auction's own images/specs and an
application's category picks (`CASCADE`); enums stored as `int`; identifiers preserved by `IDENTITY_INSERT` during import.

## Conversions applied by the importer

| Concern | Rule |
|---|---|
| Dates | Legacy values are US Central wall-clock (`TimeHelper`). Converted with `TimeZoneInfo.ConvertTimeToUtc(value, "Central Standard Time")` (DST-aware). Ambiguous/invalid local times (DST transitions) resolve to standard time and are counted in the report. |
| Money | `float` → `decimal(18,2)` with `Math.Round(x, 2, MidpointRounding.AwayFromZero)` — the same rounding the hardened legacy code applied at write. Every value whose rounding changed it by more than 0.0001 is listed in the report (`money-adjustments.csv`) — no silent change. |
| Wallet | `User.Wallet` (rounded) becomes `Users.WalletBalance` **and** one ledger entry `MigrationOpeningBalance` with that amount. Legacy transactions are imported as history; they do **not** re-post to the ledger (the legacy balance already includes them, and pre-hardening approvals were inverted). The report lists every user where `legacy wallet ≠ sum(approved credits)` for manual review. |
| Passwords | `v1.` PBKDF2 hashes copied as is (verified then upgraded at next login). Plaintext values (pre-hardening rows) are hashed **during import** with the modern hasher, so the user keeps the password but no plaintext reaches the new DB. Empty → `NULL` (must use "forgot password"). |
| Emails | Trimmed + lower-cased. Duplicates (legacy had no unique index) are reported; the first (lowest id) keeps the address, later ones get `dup+{id}+original@invalid.local` and are deactivated. |
| Files | Legacy columns hold full URLs `…/App_File/<Folder>/<name>` or bare names (AuctionImage). With `--files <legacy Web/App_File path>` each referenced file is copied into storage, re-validated (type, magic bytes) and a `Files` row is created (`LegacyUrl` keeps the original). Missing/invalid files are reported and the reference becomes `NULL`. |
| Rich text | Re-sanitised with the rebuild's HTML sanitiser. |
| Status strings | `Transaction.Status` `Pending/Approved/Rejected` (case-insensitive) → enum; anything else → `Pending` + reported. `SubscriptionRequest.Status` `pending/approved/rejected` → enum. `Auction.Status` dropped (phase is computed). |

## Table by table

| Legacy table | New table | Notes |
|---|---|---|
| `Role` | — (enum `UserRole`, check constraint) | ids 1/2/3 preserved in `Users.Role` |
| `User` | `Users` | `Wallet`→`WalletBalance`; `Password`→`PasswordHash`; `Image`/`CR`/`VatCertifcate`/`IdNumber` (URLs) → file ids `AvatarFileId`/`CrDocumentFileId`/`VatCertificateFileId`/`IdDocumentFileId`; new `SecurityStamp`, `FailedLoginCount`, `LockoutEndUtc`, `CreatedAtUtc` (unknown → import time), `IsFeaturedClient` (= active customer with picture, preserving `/Clients`). Dropped: `UserName`, `ActivationCode` (unused). Unique index on `Email`. |
| `Publisher` | `Publishers` | `SecoundMobile`→`SecondMobile`, `Image`→`LogoFileId`, `Background`→`BackgroundFileId`; unique `UserId`. `CountryId` kept. |
| `Country` | `Countries` | `Name`→`NameEn`, `NameAr` (fallback `Name`), `Image`→`FlagFileId`. |
| `Category` | `Categories` | `Icon`/`Image` → file ids. |
| `Auction` | `Auctions` | `IncreaseRange`→`BidIncrement`, `InsuranceAmount` NULL→0, `StartDate`/`EndDate`→`StartsAtUtc`/`EndsAtUtc` (UTC), `PreviewTime`→`CreatedAtUtc`, `MainImage`→`MainImageFileId`, `ConditionForPayment`/`Contract`/`TermAndConditon` (URLs shown in iframes) → `PaymentConditionsFileId`/`ContractFileId`/`TermsFileId`, `Video`→`VideoUrl`, `RefNumber` kept (duplicates/blank get `L{id}`), `PublisherId` 0/NULL → NULL (admin-owned). **New** `CategoryId` = NULL for all legacy rows (no relation existed — none invented). **New** `BidCount` (computed). Dropped: `Status` (stale), `CurrentPriceAfterTax` (never maintained — see LEGACY-AUDIT L12). If `CurrentPrice < StartPrice` or `CurrentPrice` ≠ max bid, the row is reported and `CurrentPrice = max(StartPrice, max bid)`. If `EndDate <= StartDate` the end date is set to NULL (treated as ended) and reported. |
| `AuctionBid` | `Bids` | `Amount` → decimal; `CreatedDate` → UTC. Dropped `AmountAfterTax` (never written). |
| `AuctionImage` | `AuctionImages` + `Files` | `Image` (bare file name under `App_File/AuctionImages`) → `FileId`; `Title` kept; `Size`/`IsActive` dropped (inactive rows skipped and reported). |
| `AdditionalInformation` | `AuctionSpecs` | inactive rows skipped. |
| `Transaction` | `WalletTransactions` | `Type` is always `Credit` (others reported and skipped); `Files`→`ProofFileId`; `AuctionId` kept; `Amount <= 0` rows reported and skipped (check constraint). |
| — | `WalletLedger` | new; see Wallet rule. |
| `Wishlist` | `Wishlist` | composite PK (duplicates collapsed). |
| `NotifyMe` | `AuctionWatches` | composite PK (duplicates collapsed). |
| `Notification` | `Notifications` | `ToUserID`→`UserId`, `Kind='legacy'`, title/description/url kept. |
| `Review` | `Reviews` | `Rate` clamped to 1..5 (reported), `Notes`→`Comment`; `Email` dropped (account e-mail is known). |
| `SubscriptionRequest` | `PublisherApplications` | `Password` (already hashed since hardening) kept until decided. |
| `SubscriptionCategory` | `PublisherApplicationCategories` | composite PK. |
| `ContactForm` | `ContactMessages` | |
| `ContactInfo` | `ContactInfos` | `Map`→`MapUrl`. |
| `AboutUs` | `AboutSections` | `SecoundImage`→`SecondImageFileId`, `TotalBidder`→`TotalBidders`, `GuidelinesVideo`→`GuidelinesVideoUrl`. |
| `Benefit` | `Benefits` | `Icon` (text URL used as `<img src>`) → `IconFileId` when it points into `App_File`, else reported. |
| `HowToBid` | `HowToSteps` | |
| `FaqCategory`, `Faq` | `FaqCategories`, `Faqs` | |
| `Slider` | `Sliders` | `Image` → file id. |
| `Announcement` | `Announcements` | `Image` → file id. |
| `Setting` | `Settings` | `Key`: id 3 → `privacy-policy`, id 5 → `terms`, others `legacy-{id}`. |
| `Menu`, `MenuPermission` | — | Not migrated: no UI managed them and only 2 controllers read them; authorisation is policy-based (admin role). |
| `UserLog`, `Warning` | — | Not migrated (diagnostic logs). Replaced by `AuditLogs` + structured logging. |
| — | `PasswordResetTokens`, `AuditLogs`, `TranslationOverrides`, `Files` | new |

## Indexes and constraints added

Unique: `Users.Email`, `Auctions.RefNumber`, `Publishers.UserId`, `Settings.Key`, `PasswordResetTokens.TokenHash`,
`WalletTransactions(UserId, IdempotencyKey)` filtered, `WalletLedger(WalletTransactionId, Reason)` filtered (a credit can be posted once).
Query indexes: `Auctions(IsActive, CountryId, StartsAtUtc, EndsAtUtc)`, `Auctions(PublisherId)`, `Auctions(CategoryId)`, `Bids(AuctionId, Amount)`,
`Bids(UserId, CreatedAtUtc)`, `WalletTransactions(Status, CreatedAtUtc)`, `WalletLedger(UserId, Id)`, `Notifications(UserId, IsRead, CreatedAtUtc)`,
`Categories(CountryId, IsActive, SortOrder)`, `Reviews(AuctionId, UserId)`, `AuctionWatches(AuctionId)`.
Checks: `CK_User_Role`, `CK_Auction_Prices` (start ≥ 0, current ≥ start, increment ≥ 0, insurance ≥ 0), `CK_Auction_Dates` (end > start),
`CK_Bid_Amount` (> 0), `CK_WalletTransaction_Amount` (> 0), `CK_Review_Rating` (1..5).

## Reconciliation (printed by the importer, also written to `import-report/`)

Row counts per table (legacy vs imported vs skipped); sum of wallet balances (legacy float sum vs decimal sum, and the difference);
count and sum of bids per auction; max bid vs `CurrentPrice` per auction; sum of approved credits per user vs wallet; files copied / missing /
rejected; duplicate e-mails; date conversion anomalies. The import runs in one transaction per phase and refuses to run unless the target
database is empty (`--allow-non-empty` is deliberately not offered).
