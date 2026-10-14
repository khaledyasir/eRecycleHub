-- Generated from the EF model (Entities). Dev/fresh-install schema.
-- Production DB already exists; use Database/migrations/*.sql for changes.
create table [dbo].[AboutUs] (
    [Id] [int] not null identity,
    [TitleAr] [nvarchar](max) null,
    [TitleEn] [nvarchar](max) null,
    [DescEn] [nvarchar](max) null,
    [DescAr] [nvarchar](max) null,
    [FirstImage] [nvarchar](max) null,
    [SecoundImage] [nvarchar](max) null,
    [TotalBidder] [int] null,
    [GuidelinesVideo] [nvarchar](max) null,
    primary key ([Id])
);
create table [dbo].[AdditionalInformation] (
    [Id] [int] not null identity,
    [AuctionId] [int] not null,
    [TitleEn] [nvarchar](max) null,
    [TitleAr] [nvarchar](max) null,
    [ValueEn] [nvarchar](max) null,
    [ValueAr] [nvarchar](max) null,
    [IsActive] [bit] not null,
    primary key ([Id])
);
create table [dbo].[Announcement] (
    [Id] [int] not null identity,
    [CountryId] [int] not null,
    [Image] [nvarchar](max) null,
    [SortOrder] [int] not null,
    [IsActive] [bit] not null,
    primary key ([Id])
);
create table [dbo].[Auction] (
    [Id] [int] not null identity,
    [CountryId] [int] not null,
    [PublisherId] [int] null,
    [TitleEn] [nvarchar](max) null,
    [TitleAr] [nvarchar](max) null,
    [MainImage] [nvarchar](max) null,
    [ShortDescEn] [nvarchar](max) null,
    [ShortDescAr] [nvarchar](max) null,
    [StartPrice] [float] not null,
    [CurrentPrice] [float] not null,
    [CurrentPriceAfterTax] [float] null,
    [IncreaseRange] [float] not null,
    [StartDate] [datetime] not null,
    [EndDate] [datetime] null,
    [DescEn] [nvarchar](max) null,
    [DescAr] [nvarchar](max) null,
    [IsActive] [bit] not null,
    [RefNumber] [nvarchar](max) null,
    [Status] [nvarchar](max) null,
    [PreviewTime] [datetime] not null,
    [ConditionForPayment] [nvarchar](max) null,
    [Contract] [nvarchar](max) null,
    [Video] [nvarchar](max) null,
    [TermAndConditon] [nvarchar](max) null,
    [InsuranceAmount] [float] null,
    primary key ([Id])
);
create table [dbo].[AuctionBid] (
    [Id] [int] not null identity,
    [AuctionId] [int] not null,
    [UserId] [int] not null,
    [Amount] [float] not null,
    [AmountAfterTax] [float] null,
    [CreatedDate] [datetime] not null,
    primary key ([Id])
);
create table [dbo].[AuctionImage] (
    [Id] [int] not null identity,
    [AuctionId] [int] not null,
    [Image] [nvarchar](max) null,
    [Title] [nvarchar](max) null,
    [Size] [nvarchar](max) null,
    [IsActive] [bit] not null,
    primary key ([Id])
);
create table [dbo].[Benefit] (
    [Id] [int] not null identity,
    [TitleAr] [nvarchar](max) null,
    [TitleEn] [nvarchar](max) null,
    [DescEn] [nvarchar](max) null,
    [DescAr] [nvarchar](max) null,
    [Icon] [nvarchar](max) null,
    [SortOrder] [int] not null,
    [IsActive] [bit] not null,
    primary key ([Id])
);
create table [dbo].[Category] (
    [Id] [int] not null identity,
    [CountryId] [int] not null,
    [TitleEn] [nvarchar](max) null,
    [TitleAr] [nvarchar](max) null,
    [SortOrder] [int] not null,
    [IsActive] [bit] not null,
    [Icon] [nvarchar](max) null,
    [Image] [nvarchar](max) null,
    primary key ([Id])
);
create table [dbo].[ContactForm] (
    [Id] [int] not null identity,
    [Name] [nvarchar](max) null,
    [Email] [nvarchar](max) null,
    [Mobile] [nvarchar](max) null,
    [Message] [nvarchar](max) null,
    [CreatedDate] [datetime] not null,
    primary key ([Id])
);
create table [dbo].[ContactInfo] (
    [Id] [int] not null identity,
    [CountryId] [int] not null,
    [Mobile] [nvarchar](max) null,
    [Address] [nvarchar](max) null,
    [Email] [nvarchar](max) null,
    [Map] [nvarchar](max) null,
    [Facebook] [nvarchar](max) null,
    [Instagram] [nvarchar](max) null,
    [Twitter] [nvarchar](max) null,
    [Linkedin] [nvarchar](max) null,
    primary key ([Id])
);
create table [dbo].[Country] (
    [Id] [int] not null identity,
    [Name] [nvarchar](max) null,
    [NameAr] [nvarchar](max) null,
    [Code] [nvarchar](max) null,
    [Image] [nvarchar](max) null,
    [IsActive] [bit] not null,
    primary key ([Id])
);
create table [dbo].[Faq] (
    [Id] [int] not null identity,
    [CategoryId] [int] not null,
    [QuestionEn] [nvarchar](max) null,
    [QuestionAr] [nvarchar](max) null,
    [AnswerEn] [nvarchar](max) null,
    [AnswerAr] [nvarchar](max) null,
    [SortOrder] [int] not null,
    [IsActive] [bit] not null,
    primary key ([Id])
);
create table [dbo].[FaqCategory] (
    [Id] [int] not null identity,
    [TitleAr] [nvarchar](max) null,
    [TitleEn] [nvarchar](max) null,
    [SortOrder] [int] not null,
    [IsActive] [bit] not null,
    primary key ([Id])
);
create table [dbo].[HowToBid] (
    [Id] [int] not null identity,
    [TitleAr] [nvarchar](max) null,
    [TitleEn] [nvarchar](max) null,
    [DescEn] [nvarchar](max) null,
    [DescAr] [nvarchar](max) null,
    [SortOrder] [int] not null,
    [IsActive] [bit] not null,
    primary key ([Id])
);
create table [dbo].[Menu] (
    [Id] [int] not null identity,
    [ParentId] [int] null,
    [MenuText] [nvarchar](max) null,
    [MenuURL] [nvarchar](max) null,
    [MenuIcon] [nvarchar](max) null,
    [SortOrder] [int] not null,
    primary key ([Id])
);
create table [dbo].[MenuPermission] (
    [Id] [int] not null identity,
    [MenuId] [int] not null,
    [RoleId] [int] not null,
    [UserId] [int] not null,
    [SortOrder] [int] not null,
    [IsCreate] [bit] not null,
    [IsRead] [bit] not null,
    [IsUpdate] [bit] not null,
    [IsDelete] [bit] not null,
    primary key ([Id])
);
create table [dbo].[Notification] (
    [Id] [int] not null identity,
    [ToUserID] [int] not null,
    [FromUserID] [int] not null,
    [NotificationTitle] [nvarchar](max) null,
    [NotificationDesciption] [nvarchar](max) null,
    [NotificationURL] [nvarchar](max) null,
    [IsRead] [bit] not null,
    [CreatedDate] [datetime] not null,
    primary key ([Id])
);
create table [dbo].[NotifyMe] (
    [Id] [int] not null identity,
    [AuctionId] [int] not null,
    [UserId] [int] not null,
    primary key ([Id])
);
create table [dbo].[Publisher] (
    [Id] [int] not null identity,
    [CountryId] [int] not null,
    [UserId] [int] not null,
    [FirstName] [nvarchar](max) null,
    [LastName] [nvarchar](max) null,
    [Address] [nvarchar](max) null,
    [Image] [nvarchar](max) null,
    [Background] [nvarchar](max) null,
    [SecoundMobile] [nvarchar](max) null,
    primary key ([Id])
);
create table [dbo].[Review] (
    [Id] [int] not null identity,
    [AuctionId] [int] not null,
    [UserId] [int] not null,
    [Rate] [int] not null,
    [CreatedDate] [datetime] not null,
    [Notes] [nvarchar](max) null,
    [Email] [nvarchar](max) null,
    [Name] [nvarchar](max) null,
    primary key ([Id])
);
create table [dbo].[Role] (
    [Id] [int] not null identity,
    [RoleName] [nvarchar](50) not null,
    [IsActive] [bit] not null,
    primary key ([Id])
);
create table [dbo].[Setting] (
    [Id] [int] not null identity,
    [TitleAr] [nvarchar](max) null,
    [TitleEn] [nvarchar](max) null,
    [ValueEn] [nvarchar](max) null,
    [ValueAr] [nvarchar](max) null,
    primary key ([Id])
);
create table [dbo].[Slider] (
    [Id] [int] not null identity,
    [Title] [nvarchar](max) null,
    [Link] [nvarchar](max) null,
    [Image] [nvarchar](max) null,
    [Type] [nvarchar](max) null,
    [SortOrder] [int] not null,
    [IsActive] [bit] not null,
    primary key ([Id])
);
create table [dbo].[SubscriptionCategory] (
    [Id] [int] not null identity,
    [CategoryId] [int] not null,
    [SubscriptionId] [int] not null,
    [CreatedDate] [datetime] not null,
    primary key ([Id])
);
create table [dbo].[SubscriptionRequest] (
    [Id] [int] not null identity,
    [Name] [nvarchar](max) null,
    [Email] [nvarchar](max) null,
    [Company] [nvarchar](max) null,
    [Password] [nvarchar](max) null,
    [Mobile] [nvarchar](max) null,
    [Status] [nvarchar](max) null,
    [CreatedDate] [datetime] null,
    primary key ([Id])
);
create table [dbo].[Transaction] (
    [Id] [int] not null identity,
    [UserId] [int] not null,
    [Type] [nvarchar](max) null,
    [Amount] [float] not null,
    [Status] [nvarchar](max) null,
    [CreatedDate] [datetime] not null,
    [Files] [nvarchar](max) null,
    [AuctionId] [int] null,
    primary key ([Id])
);
create table [dbo].[User] (
    [Id] [int] not null identity,
    [CountryId] [int] not null,
    [UserName] [nvarchar](max) null,
    [FullName] [nvarchar](max) null,
    [Email] [nvarchar](max) null,
    [Phone] [nvarchar](max) null,
    [Password] [nvarchar](max) null,
    [Image] [nvarchar](max) null,
    [RoleId] [int] not null,
    [IsActive] [bit] not null,
    [IsDeleted] [bit] not null,
    [Wallet] [float] null,
    [ActivationCode] [nvarchar](max) null,
    [CompanyName] [nvarchar](max) null,
    [CrNumber] [nvarchar](max) null,
    [VatNumber] [nvarchar](max) null,
    [CR] [nvarchar](max) null,
    [VatCertifcate] [nvarchar](max) null,
    [BankName] [nvarchar](max) null,
    [CardNumber] [nvarchar](max) null,
    [AccountNumber] [nvarchar](max) null,
    [IdNumber] [nvarchar](max) null,
    primary key ([Id])
);
create table [dbo].[UserLog] (
    [Id] [int] not null identity,
    [UserId] [int] not null,
    [Datetime] [nvarchar](max) null,
    [IPaddress] [nvarchar](max) null,
    [Platform] [nvarchar](max) null,
    [Cookies] [nvarchar](max) null,
    [Browser] [nvarchar](max) null,
    primary key ([Id])
);
create table [dbo].[Warning] (
    [Id] [int] not null identity,
    [Message] [nvarchar](max) null,
    [Method] [nvarchar](max) null,
    [Page] [nvarchar](max) null,
    [Fix] [bit] not null,
    [CreatedDate] [datetime] not null,
    primary key ([Id])
);
create table [dbo].[Wishlist] (
    [Id] [int] not null identity,
    [AuctionId] [int] not null,
    [UserId] [int] not null,
    primary key ([Id])
);
alter table [dbo].[AdditionalInformation] add constraint [AdditionalInformation_Auction] foreign key ([AuctionId]) references [dbo].[Auction]([Id]) on delete no action;
alter table [dbo].[Announcement] add constraint [Announcement_Country] foreign key ([CountryId]) references [dbo].[Country]([Id]) on delete no action;
alter table [dbo].[Auction] add constraint [Auction_Country] foreign key ([CountryId]) references [dbo].[Country]([Id]) on delete no action;
alter table [dbo].[AuctionImage] add constraint [AuctionImage_Auction] foreign key ([AuctionId]) references [dbo].[Auction]([Id]) on delete no action;
alter table [dbo].[Category] add constraint [Category_Country] foreign key ([CountryId]) references [dbo].[Country]([Id]) on delete no action;
alter table [dbo].[ContactInfo] add constraint [ContactInfo_Country] foreign key ([CountryId]) references [dbo].[Country]([Id]) on delete no action;
alter table [dbo].[Faq] add constraint [Faq_FaqCategory] foreign key ([CategoryId]) references [dbo].[FaqCategory]([Id]) on delete no action;
alter table [dbo].[Menu] add constraint [Menu_Menu2] foreign key ([ParentId]) references [dbo].[Menu]([Id]);
alter table [dbo].[MenuPermission] add constraint [MenuPermission_Menu] foreign key ([MenuId]) references [dbo].[Menu]([Id]) on delete no action;
alter table [dbo].[MenuPermission] add constraint [MenuPermission_Role] foreign key ([RoleId]) references [dbo].[Role]([Id]) on delete no action;
alter table [dbo].[MenuPermission] add constraint [MenuPermission_User] foreign key ([UserId]) references [dbo].[User]([Id]) on delete no action;
alter table [dbo].[Publisher] add constraint [Publisher_Country] foreign key ([CountryId]) references [dbo].[Country]([Id]) on delete no action;
alter table [dbo].[Publisher] add constraint [Publisher_User] foreign key ([UserId]) references [dbo].[User]([Id]) on delete no action;
alter table [dbo].[SubscriptionCategory] add constraint [SubscriptionCategory_Category] foreign key ([CategoryId]) references [dbo].[Category]([Id]) on delete no action;
alter table [dbo].[SubscriptionCategory] add constraint [SubscriptionCategory_SubscriptionRequest] foreign key ([SubscriptionId]) references [dbo].[SubscriptionRequest]([Id]) on delete no action;
alter table [dbo].[Transaction] add constraint [Transaction_User] foreign key ([UserId]) references [dbo].[User]([Id]) on delete no action;
alter table [dbo].[User] add constraint [User_Country] foreign key ([CountryId]) references [dbo].[Country]([Id]) on delete no action;
alter table [dbo].[User] add constraint [User_Role] foreign key ([RoleId]) references [dbo].[Role]([Id]) on delete no action;
alter table [dbo].[UserLog] add constraint [UserLog_User] foreign key ([UserId]) references [dbo].[User]([Id]) on delete no action;

