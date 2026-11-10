BEGIN TRANSACTION;
IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110120222_AuctionEndProcessed'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Auctions]') AND [c].[name] = N'EndProcessedAtUtc');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [Auctions] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [Auctions] DROP COLUMN [EndProcessedAtUtc];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110120222_AuctionEndProcessed'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110120222_AuctionEndProcessed';
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110114731_BidHoldsAndSettlement'
)
BEGIN
    DROP INDEX [UX_WalletLedger_AuctionPayment] ON [WalletLedger];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110114731_BidHoldsAndSettlement'
)
BEGIN
    DROP INDEX [IX_Notifications_UserId_EventKey] ON [Notifications];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110114731_BidHoldsAndSettlement'
)
BEGIN
    DROP INDEX [IX_Bids_UserId_ReleasedAtUtc_SettledAtUtc] ON [Bids];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110114731_BidHoldsAndSettlement'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Notifications]') AND [c].[name] = N'EventKey');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Notifications] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [Notifications] DROP COLUMN [EventKey];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110114731_BidHoldsAndSettlement'
)
BEGIN
    DECLARE @var2 nvarchar(max);
    SELECT @var2 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Notifications]') AND [c].[name] = N'PushedAtUtc');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [Notifications] DROP CONSTRAINT ' + @var2 + ';');
    ALTER TABLE [Notifications] DROP COLUMN [PushedAtUtc];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110114731_BidHoldsAndSettlement'
)
BEGIN
    DECLARE @var3 nvarchar(max);
    SELECT @var3 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[DeviceTokens]') AND [c].[name] = N'Language');
    IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [DeviceTokens] DROP CONSTRAINT ' + @var3 + ';');
    ALTER TABLE [DeviceTokens] DROP COLUMN [Language];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110114731_BidHoldsAndSettlement'
)
BEGIN
    DECLARE @var4 nvarchar(max);
    SELECT @var4 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Bids]') AND [c].[name] = N'SettledAtUtc');
    IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [Bids] DROP CONSTRAINT ' + @var4 + ';');
    ALTER TABLE [Bids] DROP COLUMN [SettledAtUtc];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110114731_BidHoldsAndSettlement'
)
BEGIN
    DECLARE @var5 nvarchar(max);
    SELECT @var5 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Auctions]') AND [c].[name] = N'SettlementFailedAtUtc');
    IF @var5 IS NOT NULL EXEC(N'ALTER TABLE [Auctions] DROP CONSTRAINT ' + @var5 + ';');
    ALTER TABLE [Auctions] DROP COLUMN [SettlementFailedAtUtc];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110114731_BidHoldsAndSettlement'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110114731_BidHoldsAndSettlement';
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110112804_BidHolds'
)
BEGIN
    ALTER TABLE [Bids] DROP CONSTRAINT [CK_Bid_Held];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110112804_BidHolds'
)
BEGIN
    DECLARE @var6 nvarchar(max);
    SELECT @var6 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Bids]') AND [c].[name] = N'HeldAmount');
    IF @var6 IS NOT NULL EXEC(N'ALTER TABLE [Bids] DROP CONSTRAINT ' + @var6 + ';');
    ALTER TABLE [Bids] DROP COLUMN [HeldAmount];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110112804_BidHolds'
)
BEGIN
    DECLARE @var7 nvarchar(max);
    SELECT @var7 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Bids]') AND [c].[name] = N'ReleasedAtUtc');
    IF @var7 IS NOT NULL EXEC(N'ALTER TABLE [Bids] DROP CONSTRAINT ' + @var7 + ';');
    ALTER TABLE [Bids] DROP COLUMN [ReleasedAtUtc];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110112804_BidHolds'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110112804_BidHolds';
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110111229_AuctionTopicsAndParticipants'
)
BEGIN
    DROP TABLE [AuctionParticipants];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110111229_AuctionTopicsAndParticipants'
)
BEGIN
    DROP INDEX [IX_DeviceTokens_UserId_DeviceId] ON [DeviceTokens];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110111229_AuctionTopicsAndParticipants'
)
BEGIN
    DECLARE @var8 nvarchar(max);
    SELECT @var8 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Auctions]') AND [c].[name] = N'TopicName');
    IF @var8 IS NOT NULL EXEC(N'ALTER TABLE [Auctions] DROP CONSTRAINT ' + @var8 + ';');
    ALTER TABLE [Auctions] DROP COLUMN [TopicName];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110111229_AuctionTopicsAndParticipants'
)
BEGIN
    DECLARE @var9 nvarchar(max);
    SELECT @var9 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[DeviceTokens]') AND [c].[name] = N'DeviceId');
    IF @var9 IS NOT NULL EXEC(N'ALTER TABLE [DeviceTokens] DROP CONSTRAINT ' + @var9 + ';');
    ALTER TABLE [DeviceTokens] DROP COLUMN [DeviceId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110111229_AuctionTopicsAndParticipants'
)
BEGIN
    DECLARE @var10 nvarchar(max);
    SELECT @var10 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[DeviceTokens]') AND [c].[name] = N'Platform');
    IF @var10 IS NOT NULL EXEC(N'ALTER TABLE [DeviceTokens] DROP CONSTRAINT ' + @var10 + ';');
    ALTER TABLE [DeviceTokens] DROP COLUMN [Platform];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110111229_AuctionTopicsAndParticipants'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110111229_AuctionTopicsAndParticipants';
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110110430_WalletLedgerReferences'
)
BEGIN
    DROP INDEX [IX_WalletLedger_ReferenceType_ReferenceId] ON [WalletLedger];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110110430_WalletLedgerReferences'
)
BEGIN
    DECLARE @var11 nvarchar(max);
    SELECT @var11 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[WalletLedger]') AND [c].[name] = N'BalanceBefore');
    IF @var11 IS NOT NULL EXEC(N'ALTER TABLE [WalletLedger] DROP CONSTRAINT ' + @var11 + ';');
    ALTER TABLE [WalletLedger] DROP COLUMN [BalanceBefore];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110110430_WalletLedgerReferences'
)
BEGIN
    DECLARE @var12 nvarchar(max);
    SELECT @var12 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[WalletLedger]') AND [c].[name] = N'ReferenceId');
    IF @var12 IS NOT NULL EXEC(N'ALTER TABLE [WalletLedger] DROP CONSTRAINT ' + @var12 + ';');
    ALTER TABLE [WalletLedger] DROP COLUMN [ReferenceId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110110430_WalletLedgerReferences'
)
BEGIN
    DECLARE @var13 nvarchar(max);
    SELECT @var13 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[WalletLedger]') AND [c].[name] = N'ReferenceType');
    IF @var13 IS NOT NULL EXEC(N'ALTER TABLE [WalletLedger] DROP CONSTRAINT ' + @var13 + ';');
    ALTER TABLE [WalletLedger] DROP COLUMN [ReferenceType];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110110430_WalletLedgerReferences'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110110430_WalletLedgerReferences';
END;

COMMIT;
GO

