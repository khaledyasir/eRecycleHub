BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110110430_WalletLedgerReferences'
)
BEGIN
    ALTER TABLE [WalletLedger] ADD [BalanceBefore] decimal(18,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110110430_WalletLedgerReferences'
)
BEGIN
    ALTER TABLE [WalletLedger] ADD [ReferenceId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110110430_WalletLedgerReferences'
)
BEGIN
    ALTER TABLE [WalletLedger] ADD [ReferenceType] nvarchar(30) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110110430_WalletLedgerReferences'
)
BEGIN
    CREATE INDEX [IX_WalletLedger_ReferenceType_ReferenceId] ON [WalletLedger] ([ReferenceType], [ReferenceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110110430_WalletLedgerReferences'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261110110430_WalletLedgerReferences', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110111229_AuctionTopicsAndParticipants'
)
BEGIN
    ALTER TABLE [DeviceTokens] ADD [DeviceId] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110111229_AuctionTopicsAndParticipants'
)
BEGIN
    ALTER TABLE [DeviceTokens] ADD [Platform] nvarchar(10) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110111229_AuctionTopicsAndParticipants'
)
BEGIN
    EXEC(N'ALTER TABLE [Auctions] ADD [TopicName] AS ''auction_'' + CAST([Id] AS nvarchar(20)) PERSISTED');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110111229_AuctionTopicsAndParticipants'
)
BEGIN
    CREATE TABLE [AuctionParticipants] (
        [AuctionId] int NOT NULL,
        [UserId] int NOT NULL,
        [JoinedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_AuctionParticipants] PRIMARY KEY ([AuctionId], [UserId]),
        CONSTRAINT [FK_AuctionParticipants_Auctions_AuctionId] FOREIGN KEY ([AuctionId]) REFERENCES [Auctions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_AuctionParticipants_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110111229_AuctionTopicsAndParticipants'
)
BEGIN
    INSERT INTO AuctionParticipants (AuctionId, UserId, JoinedAtUtc)
    SELECT AuctionId, UserId, MIN(CreatedAtUtc) FROM Bids GROUP BY AuctionId, UserId
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110111229_AuctionTopicsAndParticipants'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_DeviceTokens_UserId_DeviceId] ON [DeviceTokens] ([UserId], [DeviceId]) WHERE [DeviceId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110111229_AuctionTopicsAndParticipants'
)
BEGIN
    CREATE INDEX [IX_AuctionParticipants_UserId] ON [AuctionParticipants] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110111229_AuctionTopicsAndParticipants'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261110111229_AuctionTopicsAndParticipants', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110112804_BidHolds'
)
BEGIN
    ALTER TABLE [Bids] ADD [HeldAmount] decimal(18,2) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110112804_BidHolds'
)
BEGIN
    ALTER TABLE [Bids] ADD [ReleasedAtUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110112804_BidHolds'
)
BEGIN
    EXEC(N'ALTER TABLE [Bids] ADD CONSTRAINT [CK_Bid_Held] CHECK ([HeldAmount] >= 0 AND [HeldAmount] <= [Amount])');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110112804_BidHolds'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261110112804_BidHolds', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110114731_BidHoldsAndSettlement'
)
BEGIN
    ALTER TABLE [Notifications] ADD [EventKey] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110114731_BidHoldsAndSettlement'
)
BEGIN
    ALTER TABLE [Notifications] ADD [PushedAtUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110114731_BidHoldsAndSettlement'
)
BEGIN
    ALTER TABLE [DeviceTokens] ADD [Language] nvarchar(5) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110114731_BidHoldsAndSettlement'
)
BEGIN
    ALTER TABLE [Bids] ADD [SettledAtUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110114731_BidHoldsAndSettlement'
)
BEGIN
    ALTER TABLE [Auctions] ADD [SettlementFailedAtUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110114731_BidHoldsAndSettlement'
)
BEGIN
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
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110114731_BidHoldsAndSettlement'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_WalletLedger_AuctionPayment] ON [WalletLedger] ([ReferenceType], [ReferenceId], [Reason]) WHERE [Reason] = 9');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110114731_BidHoldsAndSettlement'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Notifications_UserId_EventKey] ON [Notifications] ([UserId], [EventKey]) WHERE [EventKey] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110114731_BidHoldsAndSettlement'
)
BEGIN
    CREATE INDEX [IX_Bids_UserId_ReleasedAtUtc_SettledAtUtc] ON [Bids] ([UserId], [ReleasedAtUtc], [SettledAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110114731_BidHoldsAndSettlement'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261110114731_BidHoldsAndSettlement', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110120222_AuctionEndProcessed'
)
BEGIN
    ALTER TABLE [Auctions] ADD [EndProcessedAtUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110120222_AuctionEndProcessed'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261110120222_AuctionEndProcessed', N'10.0.12');
END;

COMMIT;
GO

