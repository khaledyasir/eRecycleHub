# Page / route matrix

All new pages live under a locale prefix (`/en/...`, `/ar/...`). Legacy URLs are permanently redirected (see `apps/web/src/proxy.ts`, covered by `proxy.test.ts` and the Playwright suite).
API paths are relative to the same origin (`/api/...`, proxied by Next.js to ASP.NET Core).
Roles: **Anon** anyone, **Cust** customer, **Pub** publisher, **Adm** admin, **Auth** any signed-in account.
States: L = loading, E = empty, Er = error, V = validation, S = success, D = destructive confirm.
Tests column = the plan made at the start; what actually exists and passes is listed in `REBUILD-REPORT.md` §9 (several pages are covered only by the
smoke, responsive and accessibility specs, not by a dedicated journey).
Tests: U = backend unit, I = backend integration (real SQL Server), C = component (Vitest), E2E = Playwright.
Status values are updated in `REBUILD-STATUS.md`; the final state is in `REBUILD-REPORT.md` §9.

## Public

| Legacy route | New route | Role | Purpose | API | Data | States | Tests |
|---|---|---|---|---|---|---|---|
| `/`, `/Default/Index` | `/` | Anon | Home | `GET /api/home` | sliders, announcements, categories, live/upcoming/ended auctions, top stores, how-to steps | E | E2E |
| `/Default/SaveCountryToCache` (POST) | header selector | Anon | Choose country | cookie `erh_country` (client) | countries | – | E2E |
| `/Default/ChangeLanguage` | locale switcher | Anon | Switch language | – | – | – | E2E |
| `/Auctions`, `/Auctions/Index`, `/Auctions/SearchAuctions` (AJAX) | `/auctions` | Anon | Search/filter/sort/paginate | `GET /api/auctions` | auctions, categories, facets | L E | I, E2E |
| `/Auctions/Details/{id}` | `/auctions/{id}` | Anon | Auction page | `GET /api/auctions/{id}`, `GET /api/auctions/{id}/bids`, `GET /api/auctions/{id}/reviews` | auction, images, specs, documents, publisher, viewer state | L Er | I, E2E |
| `/Auctions/BidNew` (POST) | bid panel | Auth (not Adm) | Place bid | `POST /api/auctions/{id}/bids` | – | V Er S | U, I (concurrency), C, E2E |
| `/Auctions/AddToFav`, `RemoveFromFav` | heart toggle | Auth | Favourite | `PUT/DELETE /api/auctions/{id}/favorite` | – | Er | I, E2E |
| `/Auctions/AddNotifyMe`, `RemoveNotifyMe` | notify toggle | Auth | Watch auction | `PUT/DELETE /api/auctions/{id}/watch` | – | Er | I |
| `/Auctions/submitReview` | review form | Auth | Review | `POST /api/auctions/{id}/reviews` | – | V S Er | I, E2E |
| `/Store/List`, `/Suppliers/List`, `/Suppliers/SearchAuctions` | `/stores` | Anon | Publishers | `GET /api/stores` | publishers | E | E2E |
| `/Store/Details/{id}`, `/Store/SearchAuctions` | `/stores/{id}` | Anon | Publisher page + auctions | `GET /api/stores/{id}`, `GET /api/auctions?storeId=` | publisher, auctions | E | E2E |
| `/Suppliers/Details` | — | – | 100 % template page | – | – | – | Dropped |
| `/Clients` | `/clients` | Anon | Featured clients | `GET /api/clients` | users flagged featured | E | E2E |
| `/Services` | `/benefits` | Anon | Membership benefits | `GET /api/content/benefits` | benefits | E | E2E |
| `/Topic/About` | `/about` | Anon | About | `GET /api/content/about` | about sections | E | E2E |
| `/Topic/HowToBuy`, `/Topic/HowToSell` | `/how-to-buy`, `/how-to-sell` | Anon | Guides | `GET /api/content/how-to` | steps, video | E | E2E |
| `/Topic/FAQs` | `/faq` | Anon | FAQ | `GET /api/content/faq` | categories, faqs | E | C, E2E |
| `/Topic/SupportCenter` | `/support` | Anon | Help | `GET /api/content/faq`, contact info | – | E | E2E |
| `/Topic/Contact` (GET/POST) | `/contact` | Anon | Contact form | `GET /api/content/contact-info`, `POST /api/contact` | contact info | V S Er | I, E2E |
| `/Topic/Advertise` | `/advertise` | Anon | Advertise | `GET /api/content/contact-info` | contact info | – | E2E |
| `/Topic/Condition` | `/terms` | Anon | Terms | `GET /api/content/settings/terms` | setting | E | E2E |
| `/Topic/PrivacyPolicy` | `/privacy` | Anon | Privacy | `GET /api/content/settings/privacy-policy` | setting | E | E2E |
| `/Topic/GetTermsAndConditionsByTitle` | — | – | Unused JSON dump | – | – | – | Dropped |
| `/Account/Login`, `/Registration/Login` | `/login` | Anon | Sign in | `POST /api/auth/login` | – | V Er | I, E2E |
| `/Account/Register`, `/Registration/Register` | `/register` | Anon | Customer sign-up | `POST /api/auth/register` (multipart) | countries | V Er S | I, E2E |
| `/Account/PasswordRecovery` | `/forgot-password` | Anon | Request reset | `POST /api/auth/forgot-password` | – | S | I, E2E |
| `/Account/ResetPassword?token=` | `/reset-password?token=` | Anon | Set new password | `GET /api/auth/reset-password/validate`, `POST /api/auth/reset-password` | – | V Er S | I |
| `/Account/Subscribe` | `/apply` | Anon | Publisher application | `POST /api/applications` | categories | V S Er | I, E2E |
| `/Account/SignOut` | header menu | Auth | Sign out | `POST /api/auth/logout` | – | – | E2E |
| `/NotFound` | `not-found` | Anon | 404 | – | – | – | E2E |

## Customer (`/account`)

| Legacy route | New route | Role | Purpose | API | States | Tests |
|---|---|---|---|---|---|---|
| `/Account/Profile`, `/Account/GetBiddingSummary` | `/account` | Auth | Dashboard + bidding summary | `GET /api/account/dashboard`, `GET /api/account/bids` | L E | I, E2E |
| `/Account/MyAuction` | `/account/auctions` | Auth | Leading / won | `GET /api/account/auctions?tab=` | E | I, E2E |
| `/Account/CalanderAuction` | `/account/calendar` | Auth | Auction calendar | `GET /api/auctions/calendar` | E | C, E2E |
| `/Account/Transactions`, `/Account/MyWallet` | `/account/wallet` | Auth | Balance, top-up, history | `GET /api/account/wallet`, `GET .../transactions`, `GET .../ledger`, `POST .../top-ups` | V S Er E | I, E2E |
| `/Account/Settings`, `/Account/UpdateImage` | `/account/settings` | Auth | Profile | `GET/PUT /api/account/profile` | V S | I, E2E |
| `/Account/ChangePassword` | `/account/password` | Auth | Password | `POST /api/auth/change-password` | V S Er | I, E2E |
| `/Topic/Favorite` | `/account/favorites` | Auth | Favourites | `GET /api/account/favorites` | E | E2E |
| `/Topic/NotifyMe` | `/account/watching` | Auth | Notify-me list | `GET /api/account/watching` | E | E2E |
| `/Topic/Notifications`, `MarkAsRead`, `DeleteNotification` | `/account/notifications` | Auth | Notifications | `GET /api/account/notifications`, `POST .../{id}/read`, `POST .../read-all`, `DELETE .../{id}` | E | I, E2E |

## Publisher (`/seller`, legacy `Stores` area)

| Legacy route | New route | Role | API | Tests |
|---|---|---|---|---|
| `/Stores/HomeStore/Index` | `/seller` | Pub | `GET /api/seller/dashboard` | I, E2E |
| `/Stores/Auction/Index`, `/Archive` | `/seller/auctions?view=active|archived` | Pub | `GET /api/seller/auctions` | I, E2E |
| `/Stores/Auction/Create`, `CreateAuctionAttachment` | `/seller/auctions/new` | Pub | `POST /api/seller/auctions` (multipart), `POST .../{id}/images` | I, E2E |
| `/Stores/Auction/Edit/{id}`, `AuctionAttachment`, `DeleteAuctionAttachment` | `/seller/auctions/{id}` | Pub (owner) | `GET/PUT /api/seller/auctions/{id}`, `DELETE .../images/{imageId}` | I (IDOR), E2E |
| `/Stores/Auction/Activation`, `/Delete` | list actions | Pub (owner) | `POST .../{id}/active` (`{active}`), `DELETE .../{id}` | I (IDOR) |
| bids modal | auction page tab | Pub (owner) | `GET /api/seller/auctions/{id}/bids` | I |
| `/Stores/Review/Index`, `/Delete` | `/seller/reviews` | Pub | `GET /api/seller/reviews`, `DELETE .../{id}` | I |
| `/Stores/Users/Index`, `/Details/{id}` | `/seller/bidders`, `/seller/bidders/{id}` | Pub | `GET /api/seller/bidders`, `GET .../{id}` | I (IDOR) |
| `/Stores/StoreSetting`, `/Stores/MyProfile` | `/seller/profile` | Pub | `GET/PUT /api/seller/profile` | I, E2E |
| `/Stores/ChangePasswordStore` | `/seller/password` | Pub | `POST /api/auth/change-password` | E2E |

## Admin (`/admin`, legacy `Dashboard` area)

| Legacy route | New route | API (all require Adm) | Tests |
|---|---|---|---|
| `/Dashboard/CMSLogin` | `/admin/login` | `POST /api/auth/login` (`requiredRole=Admin`) | I, E2E |
| `/Dashboard/Home` | `/admin` | `GET /api/admin/dashboard` | I, E2E |
| `/Dashboard/Home/Report` | `/admin/reports` | `GET /api/admin/reports/auctions` (+ `format=csv`) | I, E2E |
| `/Dashboard/Auction/AuctonWinners`, `SendEmailsToWinners` | `/admin/winners` | `GET /api/admin/winners`, `GET /api/admin/winners/by-bidder`, `POST /api/admin/winners/notify`, `POST /api/admin/auctions/{id}/notify-winner` | I |
| `/Dashboard/Auction/*` | `/admin/auctions`, `/new`, `/{id}` | `/api/admin/auctions` (same shape as seller + `publisherId`) | I, E2E |
| `/Dashboard/Users/*` | `/admin/users`, `/new`, `/{id}` | `/api/admin/users`, `POST .../{id}/active`, `POST .../{id}/featured` | I, E2E |
| `/Dashboard/Publishers/*` | `/admin/publishers`, `/new`, `/{id}` | `/api/admin/publishers` | I, E2E |
| `/Dashboard/SubscriptionRequest/*` | `/admin/applications` | `GET /api/admin/applications`, `POST .../{id}/approve`, `.../{id}/reject`, `DELETE .../{id}` | I, E2E |
| `/Dashboard/Transaction/*` | `/admin/transactions`, `/new` | `GET/POST /api/admin/transactions`, `POST .../{id}/approve|reject|reverse`, `PUT .../{id}/proof`, `DELETE .../{id}` | U, I (concurrency), E2E |
| `/Dashboard/Category/*` | `/admin/categories` | `/api/admin/categories` | I |
| `/Dashboard/Country/*` | `/admin/countries` | `/api/admin/countries` | I |
| `/Dashboard/Announcements/*` | `/admin/announcements` | `/api/admin/announcements` | I |
| `/Dashboard/Sliders/*` | `/admin/sliders` | `/api/admin/sliders` | I |
| `/Dashboard/AboutUs/*` | `/admin/about` | `/api/admin/about` | I |
| `/Dashboard/Benefits/*` | `/admin/benefits` | `/api/admin/benefits` | I |
| `/Dashboard/HowToBid/*` | `/admin/how-to` | `/api/admin/how-to` | I |
| `/Dashboard/FaqCategory/*`, `/Dashboard/Faqs/*` | `/admin/faq` | `/api/admin/faq-categories`, `/api/admin/faqs` | I |
| `/Dashboard/ContactInfo/*` | `/admin/contact-info` | `/api/admin/contact-info` | I |
| `/Dashboard/ContactForm/*` | `/admin/messages` | `/api/admin/messages` | I |
| `/Dashboard/Settings/*` | `/admin/settings` | `/api/admin/settings` | I |
| `/Dashboard/Languages/*` | `/admin/translations` | `/api/admin/translations` | I |
| (none) | `/admin/audit` | `GET /api/admin/audit` | I |
