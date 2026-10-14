# Feature inventory

Every legacy feature, its expected behaviour, and what the rebuild does with it.
Legend: **Keep** = same behaviour; **Fix** = same purpose, corrected behaviour; **Change** = deliberate behavioural change (documented); **Drop** = dead/placeholder legacy UI not rebuilt.

## Public website

| Feature | Legacy behaviour | Rebuild |
|---|---|---|
| Country selector | Header dropdown, "All countries" + active countries; stored per visitor (cookie); filters auctions, categories, publishers, announcements, contact info | Keep (cookie `erh_country`; "All" now works — legacy JS ignored value 0) |
| Language switch | `en-US` / `ar-JO`, cookie; RTL for Arabic | Keep, as URL prefix `/en` / `/ar` (shareable, SEO alternates) |
| Homepage | Hero slider (images only), tabs Live / Upcoming / Expired / All auction cards, announcements sidebar | Fix: hero slides with optional link, live/upcoming/ended sections, categories, top publishers, "how bidding works" steps, announcements |
| Auction card | image, phase badge, favourite heart, countdown, title (by language), current bid, lot #, bid count, publisher, "notify me" | Keep (+ currency formatting) |
| Auction list | keyword, status (all/live/upcoming/expired), period (today/yesterday/7/30 days), price range, sort (price asc/desc), "highest bidding" widget, server pagination | Keep, all filtering/paging in SQL; category filter now real |
| Category filter | UI existed, never worked (no relation, counts never filled) | Fix: nullable `Auction.CategoryId`; legacy auctions stay uncategorised |
| Auction details | gallery, start/end, start/increment/current price, enrolled bidder count, countdown, bid box (−/+ by increment, min = current + increment, insurance shown, wallet ≥ insurance check, terms prompt, confirm), description, additional info table, reviews (list + form), PDF iframes for payment conditions / contract / terms, related auctions | Keep all real parts; Fix: localised title/description, documents as links + inline viewer, bid history shows amounts/times (identities only to owner/admin); Drop: fake SKU pane, dead "bidding" pane |
| Bid | AJAX, server rules (see LEGACY-AUDIT §5), localised error | Keep rules exactly; Change: administrators cannot bid; amounts with >2 decimals are rejected instead of rounded |
| Favourites | toggle heart; list page with remove | Keep |
| Notify me | toggle; list page; in-app notification on each new bid | Keep |
| Reviews | signed-in users; name, email, rating 1–5, notes | Fix: rating required 1–5, one review per user per auction, name from account, email not collected again |
| Stores (publishers) | `/Store/List` and `/Suppliers/List` (duplicate pages), cards with phones, auction count | Keep as one `/stores` page with search; Drop duplicate `Suppliers/Details` template page |
| Store details | banner, logo, contact, publisher's auctions with filters | Keep (filters actually wired) |
| Clients | grid of every active customer's avatar | Change: only customers an admin marks "featured client" (migration marks those that had a picture, preserving the page's content) |
| Benefits ("Services") | benefit cards (icon, title, rich text) | Keep |
| About | about sections, images, "total bidders" counter | Keep |
| How to buy / how to sell | steps from HowToBid; guidelines video | Keep (both pages use the same steps, as legacy) |
| FAQ | categories + questions | Fix (accordion ids, Arabic answers) |
| Support center | lorem cards + FAQ | Change: real FAQ search + contact options only |
| Contact | contact info for the country, message form | Fix: validated + rate-limited; map URL from CMS |
| Advertise | contact info + promo image | Keep |
| Terms / privacy | Setting rows 5 / 3 | Keep (settings keyed `terms`, `privacy-policy`) |
| Newsletter form (footer) | no action | Drop (dead UI) |
| Login / register / forgot / reset | see below | Keep |
| Publisher application ("Subscribe") | name, email, mobile, password, confirm, company, categories | Keep (+ country) |

## Customer account

| Feature | Legacy | Rebuild |
|---|---|---|
| Registration | full name, email, phone, password; company name, CR no., VAT no., CR file, VAT file; bank name, card no., account no., ID file; avatar. Auto sign-in | Keep (company/bank/KYC optional in the new form, documented) |
| Dashboard | counters: auctions attended, won, leading; bidding summary table (status always "Winning" — bug) | Fix: real per-bid status (leading / outbid / won / lost) |
| My auctions | Active (leading) and Won tabs (links were dead) | Fix |
| Calendar | FullCalendar of all auctions | Keep (lightweight month grid, no CDN) |
| Wallet | balance, top-up request (pending), transactions table | Keep + ledger history + optional proof upload + idempotency |
| Settings | name, phone, company/bank fields, documents, picture | Fix: whitelisted fields only; email change not self-service (admin) |
| Change password | old + new + confirm, min 8 | Keep (+ ends other sessions) |
| Notifications | list, mark read, delete, header badge | Keep (+ mark all read; localised texts) |

## Publisher portal (legacy `Stores` area)

| Feature | Legacy | Rebuild |
|---|---|---|
| Dashboard | KPI cards (several miswired), no charts render | Fix: real KPIs (auctions by phase, bids, bidders, reviews, rating), recent bids, recent reviews |
| Auctions | list (active/archived), filters, create, edit, activate/deactivate, delete (no bids), bids list modal, attachments | Keep + ownership enforced server-side on every call; Arabic fields, insurance, specs and documents restored to the forms |
| Reviews | list + delete (own auctions only) | Keep |
| Users (bidders) | customers who bid on my auctions + details (leaked wallet/bank/transactions of any user) | Fix: only bidders on my auctions; details = contact + their bids on my auctions |
| Store settings | publisher + company fields, images, documents | Keep (whitelisted) |
| My profile | name, phone | Merged into store settings |
| Change password | | Keep |

## Admin dashboard

| Feature | Legacy | Rebuild |
|---|---|---|
| Login | separate page; admins only | Keep (`/admin/login`, role-restricted) |
| Dashboard | KPI cards, recent publishers/users; charts never rendered | Fix: real KPIs, pending work queues, 30-day bids chart from real data |
| Report | auctions by end date / country, winner name & phone, totals ("after tax" meaningless) | Keep minus "after tax"; CSV export |
| Auctions | list/archive, filters (publisher incl. "admin's own", dates, status, title), create/edit (admin-owned or any publisher's), activate, delete (no bids), bids modal with user + phone, attachments | Keep |
| Auction winners | grouped by winner (total, phone); "send e-mail to winners" (re-sent to everyone each time) | Fix: per-auction winners list + notify; each auction's winner e-mailed once (`WinnerNotifiedAtUtc`) |
| Users (customers) | list/archive, create (incl. opening wallet), edit (wallet read-only), activate, details (bids, transactions, documents) | Keep; opening balance becomes an approved admin credit in the ledger |
| Publishers | list/archive, create (user + publisher + documents), edit, activate (flipped auctions — bug), delete, details | Fix activation (auctions hidden via publisher status, not mutated); delete only without auctions |
| Applications | list, approve (creates publisher), reject, delete, categories | Keep (+ e-mails) |
| Transactions | list + filters, add credit, approve, reject, proof upload/remove | Keep; "delete approved" becomes **Reverse** (ledger entry); delete only pending/rejected |
| Categories, Countries, Announcements, Sliders, About, Benefits, How-to steps, FAQ categories + FAQs, Contact info, Contact messages, Settings | CRUD | Keep (bilingual fields restored) |
| Languages | edits `.resx` files on the server | Change: database translation overrides on top of the shipped message catalogues |
| Menu permissions | tables only, no UI, consulted by 2 controllers | Drop (policy-based authorisation; documented) |
| Audit | `Warning`/`UserLog` tables | Change: `AuditLog` for sensitive operations + structured logs |
