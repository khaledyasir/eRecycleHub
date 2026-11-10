# eRecycleHub mobile API: summary for the app developer

Base path `/api/v1`. The full machine-readable contract is `apps/api/openapi/erecyclehub.json` (regenerated on every API build; Swagger serves it at `/api/openapi/v1.json`). This page explains what the contract does not: the money model, the error names, and the notification payloads.

## 1. Basics

| | |
|---|---|
| Auth | `POST /auth/login` returns a bearer token. Send `Authorization: Bearer <token>` on every other call except the public catalog and the auth/OTP calls. |
| Language | Send header `lang: en` or `lang: ar`. It picks the language of `message` in errors. Without it the API answers in Arabic. |
| Success | `{ "success": true, "message": null, "data": ... }` |
| Failure | `{ "success": false, "message": "...", "errorCode": "BID_TOO_LOW", "errors": { "amount": ["..."] } }` (`errors` only for field problems). `message` is already translated and safe to show. |
| Money | JSON numbers, 2 decimals at most, currency JOD. Send amounts as numbers (`110.5`), never strings. |
| Rate limit | 429 `RATE_LIMITED`, with a `Retry-After` header when known. Limits: bids 30/min per user, wallet requests 10/min per user, sign-in and OTP 20/min per IP. |
| Time | UTC, ISO 8601. |

## 2. Money model (read this once)

Wallet bidding is switched on by the server (`Bidding__DebitWallet=true`). When it is on:

* **A bid holds its amount; it does not charge it.** Your wallet balance and ledger do not change when you bid, raise your bid, or are outbid. The hold just moves or is released.
* **Only the current leader holds.** When someone outbids you, your hold is released at once. When you raise your own bid, the hold moves to the new bid.
* **You can lead several auctions** as long as the sum of your holds fits your available balance.
* **Available = balance - pending withdrawals - held.** It is what you can bid or request to withdraw. `GET /wallet/balance` returns all four figures, and the `wallet` field of the auction details (returned by the bid call too) is the available balance.
* **At the end of the auction the winner is charged once** (the held amount becomes a ledger entry `AuctionPayment`). The money goes to the platform; there is no seller payout in the system.
* **If the winner's wallet cannot cover it** at that moment, nothing is charged, the winner gets a `payment_failed` notification asking them to top up, and the server retries every few seconds; topping up completes the sale. An administrator can cancel such a sale.
* **A withdrawal request does not move money.** It reserves nothing by itself but counts against your available balance, and the wallet is debited only when an administrator approves it.
* With the switch off, a bid only needs the auction's insurance amount in the wallet, nothing is held and the winner is not charged.

## 3. Endpoints

All responses use the envelope above. "Auth" means bearer token required.

### Auth (public)
| Method | Path | Notes |
|---|---|---|
| POST | `/auth/otp/send`, `/auth/otp/verify` | 5-digit code; verify returns what register or password reset needs. |
| POST | `/auth/register`, `/auth/login`, `/auth/password/reset` | Login returns `{ token, ... }`. |
| POST | `/auth/logout` | Auth. Ends this session and forgets this device's push token. |

### Auctions and bids
| Method | Path | Notes |
|---|---|---|
| GET | `/auctions`, `/auctions/statuses` | Public lists. |
| GET | `/auctions/{id}` | Auth. The full details, including `wallet` (your available balance when wallet bidding is on), `insuranceAmount`, `latestBids` and the viewer fields below. |
| POST | `/auctions/{id}/bids` | Auth. Body `{ "amount": 130 }`. Returns the same details object, plus `availableAfter` and `releasedFromPrevious`. Rate limited. |
| WS | `wss://host/api/ws/auctions/{id}` | Server-to-client only. Events: `bid.placed`, `auction.started`, `auction.extended`, `auction.ended`. (`/ws/auctions/{id}` also works.) |

### Wallet (Auth)
| Method | Path | Notes |
|---|---|---|
| GET | `/wallet` | `{ wallet, transaction: [{ id, type: Deposit\|Withdraw, amount, status: Pending\|Completed\|Rejected\|Reversed, createdDate }] }`. `wallet` is the available balance when wallet bidding is on. |
| GET | `/wallet/balance` | `{ balance, pendingWithdrawals, available, held }`. |
| GET | `/wallet/ledger?page&pageSize` | Every balance change, newest first: `{ id, reason, amount (signed), balanceBefore, balanceAfter, referenceType, referenceId, createdDate }`. `pageSize` max 100. |
| POST | `/wallet/increase` | Body `{ "amount": 50 }`. Creates a Pending deposit. An administrator credits it after the bank transfer arrives. |
| POST | `/wallet/withdraw` | Body `{ "amount": 50 }`. Creates a Pending withdrawal. 422 `INSUFFICIENT_WALLET` when it exceeds your available balance. |

Both POSTs accept an optional `Idempotency-Key` header (max 100 characters; use a fresh UUID per user action). Repeating the call with the same key returns the same request instead of creating another. The same key with a different amount, or for the other kind of request, is `409 WALLET_IDEMPOTENCY_KEY_REUSED`. Use it to retry safely after a timeout.

### Notifications and devices (Auth)
| Method | Path | Notes |
|---|---|---|
| GET | `/notifications?unread=true&page&pageSize` | Newest first (at most 500). Item: `{ id, type, titleEn, titleAr, bodyEn, bodyAr, createdAt, read, kind, auctionId, amount }`. |
| GET | `/notifications/unread-count` | `{ count }`. |
| POST | `/notifications/read` | Body `{ "ids": [1,2] }`. |
| POST | `/notifications/read-all` | |
| DELETE | `/notifications/{id}`, POST `/notifications/{id}/restore` | Soft delete / undo (within a day). |
| POST | `/devices/fcm-token` | Body `{ token, platform: android\|ios, deviceId }`. Send header `lang: en\|ar` here: pushes use that language (default Arabic). Re-send whenever Firebase gives a new token. |

Viewer fields on the auction details (`GET /auctions/{id}` and the bid response):

| Field | Type | Meaning |
|---|---|---|
| `isLeading` | bool | You hold the highest bid right now. |
| `heldByViewer` | number | What your leading bid in this auction holds; `0` when you are not leading or nothing is held (always `0` while wallet bidding is off). |
| `debitsWallet` | bool | Wallet bidding is on: a bid holds its amount and the winner is charged at close. When `false`, only the insurance amount has to be in the wallet. |
| `isWinner` | bool | The auction has ended, its sale was not voided, and you hold the winning bid. `false` while the auction is running. |
| `saleStatus` | string or null | What became of the sale. `null` for running and upcoming auctions and for ended auctions without bids. Otherwise one of the values below. |
| `availableAfter` | number | **Bid response only** (absent on `GET`). Your available balance right after the bid. |
| `releasedFromPrevious` | number | **Bid response only** (absent on `GET`). The hold released from the previous leader because of your bid; `0` when there was no previous leader or it was you. |

`saleStatus` for an ended auction with bids. **Who sees what:** the user who holds the winning bid (even if the sale was voided) sees the real value below. Every other viewer sees only `Paid` or `Pending`: `PaymentFailed`, `Voided` and `NotCharged` are shown to them as `Pending`, so one person's payment problem is not visible to others.

| Value | Meaning | What the winner sees |
|---|---|---|
| `Paid` | The winning bid was settled: the held amount was charged to the winner's wallet. | "Paid". |
| `PaymentFailed` | The wallet could not cover the winning amount at close. Nothing was charged and the hold is kept; topping up completes the sale by itself within seconds. | "Top up your wallet to complete the payment". |
| `Pending` | Ended, but the server has not processed it yet (normally seconds). | "Processing". |
| `Voided` | An administrator voided the sale or the auction was deactivated, and the winner's hold was released. `isWinner` is `false`. | "This sale was cancelled". |
| `NotCharged` | Closed and nothing was charged: the winning bid held no money (wallet bidding was off when it was placed, or the auction ended before it existed). Payment, if any, is arranged outside the app. | "Congratulations; we will contact you". |

`NotCharged` is not one of the four values in the original request; it covers sales that are finished but were never paid through the wallet, so they are not shown as `Pending` forever.

Use `isLeading` and `heldByViewer` to show "You are leading, 110 JOD reserved", and `debitsWallet` to decide whether to show hold wording at all. The bid response is the same object as `GET`, so one parser handles both; the two bid-only fields are simply missing on `GET`.

New and changed fields: `kind` (the exact event, section 5), `auctionId` (the auction to open when the notification is tapped; absent for wallet events), `amount` on notifications; `held` and `available` on `/wallet/balance`; `wallet` on the auction details is now the available balance.

### Account, favorites, content
`GET/PUT/DELETE /profile` (delete is refused with `ACCOUNT_DELETE_BLOCKED` while money is in the wallet, pending or held), `POST /profile/image`, `POST /profile/password`, `GET /profile/auctions` (live and won), `GET/POST/DELETE /favorites`, `GET /categories`, `/countries`, `/contents`, `/faqs`, `/contact-us`, `/home/slides`, `GET /files/{id}`. Unchanged; see the OpenAPI file.

## 4. Error codes

Your original names map to what the API returns:

| Your name | `errorCode` returned | HTTP | When |
|---|---|---|---|
| INSUFFICIENT_BALANCE | **`INSUFFICIENT_WALLET`** | 422 | The available balance cannot cover the bid hold, the insurance amount, or a withdrawal. The message states what is required and available. |
| AUCTION_ENDED | **`AUCTION_CLOSED`** | 409 | Auction ended, inactive or deactivated. |
| BID_TOO_LOW | `BID_TOO_LOW` | 422 | Below the current price plus the increment. |
| OUTBID | `OUTBID` | 409 | Someone bid first: reload and bid again. |
| BID_NOT_ALLOWED | `BID_NOT_ALLOWED` | 403 | Own auction, administrator, or blocked account. |

Other codes the app can see:

| `errorCode` | HTTP | When |
|---|---|---|
| `AUCTION_NOT_STARTED` | 409 | The auction has not started. |
| `VALIDATION_FAILED` | 422 (400 for malformed input) | See `errors` per field. Includes a bad `amount` (zero, negative, more than 2 decimals, above the maximum). |
| `INVALID_CREDENTIALS`, `INVALID_OTP`, `OTP_NOT_VERIFIED`, `INVALID_RESET_TOKEN`, `OTP_UNAVAILABLE` | 401 / 422 / 503 | Auth and OTP. |
| `UNAUTHORIZED` / `FORBIDDEN` | 401 / 403 | Missing or invalid token / not allowed. |
| `NOT_FOUND` | 404 | Unknown id, or someone else's resource. |
| `RATE_LIMITED` | 429 | Wait for `Retry-After`. |
| `PAYLOAD_TOO_LARGE` | 413 | Upload too big. |
| `WALLET_IDEMPOTENCY_KEY_REUSED` | 409 | See Idempotency-Key. |
| `WALLET_INVALID_IDEMPOTENCY_KEY` | 400 | Key longer than 100 characters. |
| `WALLET_TOO_MANY_PENDING` | 409 | You already have the maximum (5) deposit or withdrawal requests waiting for approval. A retry with the same `Idempotency-Key` as an existing request still returns that request. |
| `ACCOUNT_DELETE_BLOCKED` | 409 | `DELETE /profile` while the account has a wallet balance, a pending withdrawal or money held by a leading bid. The message states the amounts; ask the user to withdraw or use the money first. |
| `SERVER_ERROR` | 500 | Unexpected; nothing was changed. |

Administrator-only calls (not used by the app) can also return `WALLET_NOT_PENDING` (409, already decided), `WALLET_SELF_APPROVAL_NOT_ALLOWED` (403), `WALLET_SELF_CREDIT_NOT_ALLOWED` (403), `WALLET_OWNER_INACTIVE` (409: the account is inactive or deleted), `AUCTION_LEADER_UNFUNDED` (409) and `AUCTION_NO_UNSETTLED_SALE` (409).

## 5. Push notifications (Firebase)

Each push has a visible notification (title and body in the device's language) and a data map. **All data values are strings.** Route taps with `type`.

Fields on every push: `type`, `notification_id` (the in-app row; use it to mark it read), `title`, `body`, `title_en`, `title_ar`, `body_en`, `body_ar`, `event_at` (UTC). Auction pushes add `auction_id`; wallet pushes add `transaction_id` instead and have **no** `auction_id`. Amount-bearing pushes add `amount` (like `"40.00"`); `auction_won` adds `charged`.

| `type` | In-app `kind` | `type` in the list | Extra data | Sent to |
|---|---|---|---|---|
| `new_bid` | `auction.new_bid` | bid | `auction_id`, `amount` | participants and watchers, except the bidder and the outbid leader |
| `outbid` | `auction.outbid` | bid | `auction_id`, `amount` | the previous leader |
| `joined` | `auction.joined` | auction | `auction_id` | the bidder, on their first bid in that auction |
| `auction_won` | `auction.won` | bid | `auction_id`, `amount`, `charged` | the winner at close. `charged` is what was taken from the wallet (absent when nothing was charged) |
| `auction_ended` | `auction.ended` | auction | `auction_id` | everyone on the auction's topic; carries both languages in `title`/`body` |
| `payment_failed` | `auction.payment_failed` | wallet | `auction_id`, `amount` | the winner whose wallet could not cover the sale |
| `topup_approved` | `wallet.topup_approved` | wallet | `transaction_id`, `amount` | the requester |
| `topup_rejected` | `wallet.topup_rejected` | wallet | `transaction_id`, `amount` | the requester |
| `withdrawal_approved` | `wallet.withdrawal_approved` | wallet | `transaction_id`, `amount` | the requester |
| `withdrawal_rejected` | `wallet.withdrawal_rejected` | wallet | `transaction_id`, `amount` | the requester |
| `credit_reversed` | `wallet.credit_reversed` | wallet | `transaction_id`, `amount` | the user whose credit an administrator reversed |

Rules to rely on:
* **One notification per event.** The server stores the in-app row once per user and event and pushes each row once, so a retry never produces a second push. A repeated delivery of the same push has the same `notification_id`: ignore duplicates by that id.
* **The winner sees one notification** for the end of an auction: the winner's push and the generic "ended" push share a collapse id, and the newer replaces the older.
* **WebSocket and push do not duplicate each other:** the WebSocket is for the open auction screen (live price); pushes are for the background and are also stored in `/notifications`. When the app is in the foreground on that auction, show the push silently and rely on the socket for the price.
* **Language:** the device's language is the `lang` header sent with `POST /devices/fcm-token`. Re-register the token after the user switches language.
* Tokens the push service reports as dead are deleted by the server; register again on the next app start.

## 6. Checklist for the app

1. Send `lang` on every request and on the FCM registration.
2. Treat `wallet`/`available` as what the user can spend, and show `held` where it helps ("reserved for your leading bids").
3. Use an `Idempotency-Key` on `/wallet/increase` and `/wallet/withdraw`.
4. Handle `OUTBID`, `BID_TOO_LOW` (reload the price), `INSUFFICIENT_WALLET` (offer a top-up) and `AUCTION_CLOSED`.
5. Route taps by `type` and `auction_id` / `transaction_id`; mark the row read with `notification_id`.
