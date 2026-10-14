# Legacy audit — eRecycleHub (ASP.NET MVC 5)

> **The legacy sources were removed from the working tree on 2026-10-14** (at the owner's request). Nothing was lost from version control:
> they are all in git at the tag `legacy-before-removal` (commit `ef1b903`, branch `hardening`) and on `master`. File paths cited in these documents
> (`Services/...`, `Web/...`, `Database/...`, `REVIEW-REPORT.md`) refer to that tag; read one with `git show legacy-before-removal:Services/PasswordHasher.cs`,
> or restore everything into a separate folder with `git worktree add ../legacy-checkout legacy-before-removal`.

Scope: the repository at the root of this workspace, branch `hardening` (commit `ef1b903`). The legacy project was treated as read-only.
Everything below was verified by reading source, not taken from `REVIEW-REPORT.md` alone; where the report and code disagree it is noted.

## 1. Shape of the system

| Item | Legacy |
|---|---|
| Runtime | .NET Framework 4.8, ASP.NET MVC 5, IIS / IIS Express |
| Data | EF6 code-first model (`Entities/Models`, 30 tables) on SQL Server; dev DB `ErecycleHubDev` |
| Projects | `Entities` (EF models + `*Custom` DTOs), `Services` (37 service classes, ~3.9k lines), `Web` (MVC; ~6k lines of controllers, ~28k lines of Razor) |
| Areas | public site (no area), `Dashboard` (admin, 20 controllers), `Stores` (publisher portal, 9 controllers) |
| Front end | jQuery 3.7.1, Bootstrap, a purchased auction theme (Swiper, noUiSlider, SweetAlert), admin theme with ~70 demo scripts, TinyMCE 6.8.5, Dropzone, FullCalendar (CDN) |
| Languages | `en-US` (default per Web.config) and `ar-JO`, via `App_GlobalResources/web.resx` (410 keys) / `web.ar.resx` (458 keys) |
| Auth | OWIN cookie with claims `Name`=email, `Role`=role id, `Sid`=user id; roles 1 Admin, 2 Publisher, 3 Customer |
| Clock | Auction dates are stored as **US Central wall-clock time** (`TimeHelper.Now()` converts UTC → "Central Standard Time") |
| Money | `float` columns (`Auction.StartPrice/CurrentPrice/IncreaseRange/InsuranceAmount/CurrentPriceAfterTax`, `AuctionBid.Amount/AmountAfterTax`, `Transaction.Amount`, `User.Wallet`); rounded to cents on write since hardening |
| Currency | Jordanian dinar: resource `Jod` ("Jod" / "دينار"); several views print raw numbers or `$` (theme default) |

## 2. Architectural problems (why a rebuild rather than a port)

1. **Service classes are anemic CRUD wrappers** that each `new` a DbContext and copy properties field-by-field between `Entity` and `EntityCustom`. Business rules live in controllers (status computation is copy-pasted in 9 controller actions).
2. **In-memory filtering/paging everywhere**: `ListAll()` loads every auction/user/transaction, then LINQ-to-Objects filters and pages. `PublisherService.ListAll()` runs 5 correlated sub-queries per row.
3. **Status is stored and stale**: `Auction.Status` is a string written at create time ("New"/"Upcoming"/"Expired") and then recomputed in controllers from dates; the stored value is meaningless.
4. **Category relation does not exist**: `AuctionCustom.CategoryId` is never persisted (controllers set it to `0`/`1`); `CategoryCustom.AuctionCount` is never populated, so every `AuctionCount > 0` filter returns nothing and the public category UI is permanently empty.
5. **Two-table identity**: a publisher is a `User` (role 2) plus a `Publisher` row; profile data is split (company/KYC fields on `User`, names/images on `Publisher`). Publisher "active" is `User.IsActive`, and toggling it *flips* every auction's `IsActive` (so previously inactive auctions become active).
6. **Per-request controller fields** (`int UserId = Convert.ToInt32(Env.GetUserInfo("userid"))`) and `ViewBag` everywhere; no view models validation.
7. **Menu/MenuPermission** fine-grained permission tables are only consulted by `Dashboard/BaseController`, which only `HomeController` and `LanguagesController` inherit. There is no UI to manage them. Effective model: admin = everything.
8. **Theme debt**: three unrelated templates' leftovers (car parts "BMS Souq", "Lamas", demo e-mail links, lorem SupportCenter, fake SKU/BMW specs pane, hard-coded period counts), duplicate element ids breaking countdowns and FAQ accordions.
9. **Brand inconsistency**: "Super Hotdeals" (header alt, footer, `<title>`), "TenderJO" (footer credit, advertise text, a resource string), "eRecycleHUB/Erecyclehub" (resource keys, fallback publisher name, admin titles, `info@erecyclehub.com`). "KrazyHotDeals" does not occur in views. The workspace user's email domain is `tenderjo.com` (the agency that designed the site, per the footer credit). **Decision:** the product is *eRecycleHub*; the TenderJO credit link is kept in the footer as the design credit.

## 3. What the hardening branch fixed (verified)

All items in `REVIEW-REPORT.md` §3.1/§3.2 were checked against code: PBKDF2 `v1` hashing with lazy upgrade (`Services/PasswordHasher.cs`), global CSRF filter and area access filter (`Web/App_Start/SecurityFilters.cs`), locked bid transaction (`AuctionBidService.PlaceBid`), `WalletService` as sole wallet writer with conditional `UPDATE … WHERE Status='Pending'`, upload allow-list + magic bytes (`Web/Utility/UploadHelper.cs`), `SafeHtml` sanitiser, HMAC reset tokens bound to the password stamp, in-memory login throttle, per-visitor country cookie.

## 4. Remaining legacy defects found during this audit (not in the report)

| # | Where | Defect |
|---|---|---|
| L1 | `Stores/UsersController.Details(int Id)` | Any publisher can open **any user's** details (phone, wallet, bank/card/VAT/CR numbers, transactions) by id — IDOR/PII leak. |
| L2 | `Stores/MyProfileController.Index` POST | Binds a whole `UserCustom` and calls `UserServices.Edit` → mass assignment (posted `Id` edits another user; `IsActive`, `Email` writable). |
| L3 | `Stores/StoreSettingController` POST | Writes `user.Password = userCustom.Password` from a hidden field (no-op only because `Edit` ignores hashed values); `Email` changeable without re-authentication. |
| L4 | `Dashboard/PublishersController.Edit` & `Users/Edit` views | Pre-fill the `Password` input with the stored hash (leaks hashes to HTML). |
| L5 | `UserServices.GetAllForPublisher` | `IsMineUser` compares `bid.UserId == publisherId` — the "my bidders" list is wrong. |
| L6 | `PublisherService.Activation` | Flips every auction's `IsActive` instead of hiding them. |
| L7 | `Dashboard/AuctionController.SendEmailsToWinners` | E-mails *every* winner of *every* ended auction on each click (no "already notified" state); returns `ex.ToString()` to the browser. |
| L8 | `Dashboard/AboutUsController.Edit` | Saves `Image` again for `Image2` (second image upload is broken). |
| L9 | Dashboard FAQ forms | Arabic answer field is named `AnswerEn`, so `AnswerAr` is never saved. |
| L10 | `Account/Transactions.cshtml` | Button labelled "Cash With Drawer" (withdrawal) and JS checks "amount ≤ wallet", but the server creates a **top-up** credit request. The server behaviour (top-up pending approval) is the one preserved. |
| L11 | `Clients/Index` | Publishes every active customer's profile picture. |
| L12 | Reports | `CurrentPriceAfterTax` is set to the start price at creation and never updated; `AmountAfterTax` is never written. "After tax" totals are meaningless. |
| L13 | Auction create/edit forms | Arabic title/description, insurance amount, additional info rows and the three PDF uploads are commented out, yet the public page shows/depends on them. |
| L14 | Contact form | No required fields, no validation, no rate limit. |
| L15 | `TopicController.GetTermsAndConditionsByTitle` | Anonymous JSON dump of settings by title. |
| L16 | Throttle | In memory, per process; bypassed by restart or web farm. |

## 5. Business rules extracted (source of truth for the rebuild)

* **Bid** (`AuctionBidService.PlaceBid`): signed-in; auction exists, `IsActive`, `now >= StartDate`, `EndDate` present and `now < EndDate`; account active & not deleted; bidder is not the auction's publisher user; `wallet >= InsuranceAmount` (check only — no hold/charge); `amount >= CurrentPrice + IncreaseRange` (also for the first bid); one transaction with `UPDLOCK, ROWLOCK` on the auction row; on success `CurrentPrice = amount`, bid row inserted, "notify me" subscribers (except bidder) get an in-app notification.
* **Winner**: highest bid; ties → earliest (`LeadingBidder`); none when no bids.
* **Phase**: Upcoming before start, live until end, expired after end (legacy labels New/Upcoming/Expired).
* **Price terms frozen once bids exist** (`StartPrice`, `CurrentPrice`, `IncreaseRange`); **auction with bids cannot be deleted** (deactivate instead).
* **Wallet**: customer top-up request → `Pending`, no balance change; admin approve → `Approved` + credit exactly once; reject → `Rejected`; delete of an approved credit reverses it; only the proof file may be edited afterwards; admin can add an already-approved credit; amount `0 < x ≤ 1,000,000`.
* **Publisher application**: anonymous form (name, email, mobile, company, password, categories) → `Pending`; approve creates a role-2 user + Publisher row with the applicant's chosen password (hashed at submission) or a random password e-mailed; double approval guarded; reject sets status.
* **Password reset**: identical response for known/unknown e-mails; 1-hour single-use link.
* **Country**: visitor-selected country filters auctions, categories, publishers, announcements and contact info; "all countries" when none selected. Creating a country also creates its `ContactInfo` row.
* **Settings**: legacy rows `Id=3` = privacy policy, `Id=5` = terms.

## 6. Tests found

`tests/unit.cs` (16 checks: hashing, reset tokens), `tests/smoke.sh` (88 HTTP+SQL checks: CSRF, access control, login upgrade, every bid rule, concurrent equal bids, wallet top-up/approve-once, profile mass assignment, XSS, recovery, page renders, publisher ownership, application approval), `tests/browser-check.js` (41 pages × 4 roles: console errors, failed requests), `tests/browser-flows.js` (25 interactions incl. bid buttons, countdown in 3 time zones), `tests/browser-arabic.js` (58 Arabic page views). They target legacy URLs and the legacy schema; their scenarios are re-expressed in the rebuild's test suites (see `PAGE-ROUTE-MATRIX.md` "Tests" column and `REBUILD-REPORT.md`).
