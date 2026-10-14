-- Run with:  sqlcmd -S <server> -E -C -d <db> -f 65001 -i Database/seed.dev.sql   (65001 = UTF-8, needed for the Arabic text below)
-- DEV/TEST ONLY. Never run against production: it creates well-known accounts.
-- Passwords are stored in plaintext here on purpose; the app upgrades them to PBKDF2 on first login.
SET NOCOUNT ON;
IF NOT EXISTS (SELECT 1 FROM Role)
BEGIN
  SET IDENTITY_INSERT Role ON;
  INSERT Role (Id, RoleName, IsActive) VALUES (1, 'Admin', 1), (2, 'Publisher', 1), (3, 'Customer', 1);
  SET IDENTITY_INSERT Role OFF;
END
IF NOT EXISTS (SELECT 1 FROM Country)
  INSERT Country (Name, NameAr, Code, IsActive) VALUES ('United States', N'الولايات المتحدة', 'US', 1);
DECLARE @c int = (SELECT TOP 1 Id FROM Country ORDER BY Id);

IF NOT EXISTS (SELECT 1 FROM [User] WHERE Email = 'admin@test.local')
  INSERT [User] (CountryId, UserName, FullName, Email, Phone, Password, RoleId, IsActive, IsDeleted, Wallet)
  VALUES (@c, '', 'Test Admin', 'admin@test.local', '1', 'AdminPass#2026', 1, 1, 0, 0),
         (@c, '', 'Test Customer', 'customer@test.local', '2', 'CustomerPass#2026', 3, 1, 0, 500),
         (@c, '', 'Second Customer', 'customer2@test.local', '3', 'CustomerPass#2026', 3, 1, 0, 500),
         (@c, '', 'Test Publisher', 'publisher@test.local', '4', 'PublisherPass#2026', 2, 1, 0, 0),
         (@c, '', 'Other Publisher', 'publisher2@test.local', '5', 'PublisherPass#2026', 2, 1, 0, 0);

IF NOT EXISTS (SELECT 1 FROM Publisher)
  INSERT Publisher (CountryId, UserId, FirstName, LastName)
  SELECT @c, Id, FullName, '' FROM [User] WHERE RoleId = 2;

IF NOT EXISTS (SELECT 1 FROM Auction)
BEGIN
  DECLARE @p int = (SELECT TOP 1 p.Id FROM Publisher p JOIN [User] u ON u.Id = p.UserId WHERE u.Email = 'publisher@test.local');
  INSERT Auction (CountryId, PublisherId, TitleEn, TitleAr, ShortDescEn, ShortDescAr, StartPrice, CurrentPrice, IncreaseRange,
                  StartDate, EndDate, DescEn, DescAr, IsActive, RefNumber, Status, PreviewTime, InsuranceAmount)
  VALUES (@c, @p, 'Open test auction', N'مزاد', 's', 's', 100, 100, 10, DATEADD(day,-1,GETDATE()), DATEADD(day,30,GETDATE()), 'd', 'd', 1, 'T-1', 'New', GETDATE(), 50),
         (@c, @p, 'Ended test auction', N'مزاد', 's', 's', 100, 100, 10, DATEADD(day,-10,GETDATE()), DATEADD(day,-1,GETDATE()), 'd', 'd', 1, 'T-2', 'New', GETDATE(), 0),
         (@c, @p, 'Future test auction', N'مزاد', 's', 's', 100, 100, 10, DATEADD(day,5,GETDATE()), DATEADD(day,30,GETDATE()), 'd', 'd', 1, 'T-3', 'New', GETDATE(), 0),
         (@c, @p, 'High deposit auction', N'مزاد', 's', 's', 100, 100, 10, DATEADD(day,-1,GETDATE()), DATEADD(day,30,GETDATE()), 'd', 'd', 1, 'T-4', 'New', GETDATE(), 99999);
END

IF NOT EXISTS (SELECT 1 FROM ContactInfo)
  INSERT ContactInfo (CountryId, Mobile, Address, Email) VALUES (@c, '000', 'Test address', 'info@test.local');
