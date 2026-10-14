using System.Globalization;
using System.Text;
using ERecycleHub.Domain;
using ERecycleHub.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ERecycleHub.Application;

public sealed record DayPoint(DateOnly Day, int Bids, decimal Volume);
public sealed record AdminDashboard(int Auctions, int Live, int Upcoming, int Ended, int Inactive, int Customers, int Publishers, int PendingApplications,
    int PendingTopUps, decimal PendingTopUpAmount, int UnreadMessages, int BidsThisMonth, int BidsLastMonth, decimal VolumeThisMonth, decimal VolumeLastMonth,
    decimal WalletTotal, IReadOnlyList<DayPoint> Last30Days, IReadOnlyList<ManagedAuctionRow> RecentAuctions, IReadOnlyList<UserRow> RecentCustomers,
    IReadOnlyList<PublisherRow> RecentPublishers);
public sealed record ReportRow(int Id, string RefNumber, string TitleEn, string TitleAr, string? PublisherName, string CountryNameEn, DateTime StartsAtUtc, DateTime? EndsAtUtc,
    AuctionPhase Phase, decimal StartPrice, decimal CurrentPrice, int BidCount, int? WinnerId, string? WinnerName, string? WinnerPhone);
public sealed record ReportResult(PagedResult<ReportRow> Rows, int TotalAuctions, int EndedWithBids, int Active, decimal SumFinalPrices);
public sealed record ReportFilter(DateTime? FromUtc, DateTime? ToUtc, int? CountryId, int Page = 1, int PageSize = 50);
public sealed record WinnerRow(int AuctionId, string RefNumber, string TitleEn, string TitleAr, DateTime? EndsAtUtc, decimal Amount, int UserId, string UserName,
    string Email, string? Phone, DateTime? NotifiedAtUtc);
public sealed record WinnerGroup(int UserId, string UserName, string Email, string? Phone, int AuctionsWon, decimal TotalAmount, DateTime? FirstEndUtc, DateTime? LastEndUtc);
public sealed record NotifyResult(int Sent, int Failed, int AlreadyNotified);

public sealed class ReportsService(AppDbContext db, AuctionManagement auctionsAdmin, AdminPeople people, IEmailSender mailer, TimeProvider clock, ILogger<ReportsService> log)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<AdminDashboard> DashboardAsync(Actor admin, CancellationToken ct = default)
    {
        var now = Now;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var lastMonthStart = monthStart.AddMonths(-1);
        var active = db.Auctions.Where(a => a.IsActive);
        var since = now.Date.AddDays(-29);
        var daily = await db.Bids.Where(b => b.CreatedAtUtc >= since).GroupBy(b => b.CreatedAtUtc.Date)
            .Select(g => new { Day = g.Key, Count = g.Count(), Volume = g.Sum(b => b.Amount) }).ToListAsync(ct);
        var points = Enumerable.Range(0, 30).Select(i => since.AddDays(i)).Select(d =>
        {
            var hit = daily.FirstOrDefault(x => x.Day == d);
            return new DayPoint(DateOnly.FromDateTime(d), hit?.Count ?? 0, hit?.Volume ?? 0);
        }).ToList();
        var pending = db.WalletTransactions.Where(t => t.Status == WalletTransactionStatus.Pending);

        return new AdminDashboard(
            await db.Auctions.CountAsync(ct),
            await AuctionQueries.InPhase(active, "live", now).CountAsync(ct),
            await AuctionQueries.InPhase(active, "upcoming", now).CountAsync(ct),
            await AuctionQueries.InPhase(active, "ended", now).CountAsync(ct),
            await db.Auctions.CountAsync(a => !a.IsActive, ct),
            await db.Users.CountAsync(u => u.Role == UserRole.Customer && u.IsActive && !u.IsDeleted, ct),
            await db.Publishers.CountAsync(p => p.User!.IsActive && !p.User.IsDeleted, ct),
            await db.PublisherApplications.CountAsync(a => a.Status == ApplicationStatus.Pending, ct),
            await pending.CountAsync(ct),
            await pending.SumAsync(t => (decimal?)t.Amount, ct) ?? 0,
            await db.ContactMessages.CountAsync(m => !m.IsRead, ct),
            await db.Bids.CountAsync(b => b.CreatedAtUtc >= monthStart, ct),
            await db.Bids.CountAsync(b => b.CreatedAtUtc >= lastMonthStart && b.CreatedAtUtc < monthStart, ct),
            await db.Bids.Where(b => b.CreatedAtUtc >= monthStart).SumAsync(b => (decimal?)b.Amount, ct) ?? 0,
            await db.Bids.Where(b => b.CreatedAtUtc >= lastMonthStart && b.CreatedAtUtc < monthStart).SumAsync(b => (decimal?)b.Amount, ct) ?? 0,
            await db.Users.Where(u => !u.IsDeleted).SumAsync(u => (decimal?)u.WalletBalance, ct) ?? 0,
            points,
            (await auctionsAdmin.ListAsync(admin, new ManagedAuctionFilter("all", PageSize: 5), ct)).Items,
            (await people.UsersAsync(true, null, new PageQuery(1, 5), ct)).Items,
            (await people.PublishersAsync(true, null, new PageQuery(1, 5), ct)).Items);
    }

    private IQueryable<ReportRow> ReportQuery(ReportFilter f)
    {
        var now = Now;
        var q = db.Auctions.AsNoTracking();
        if (f.FromUtc is not null) q = q.Where(a => a.EndsAtUtc >= f.FromUtc);
        if (f.ToUtc is not null) q = q.Where(a => a.EndsAtUtc < f.ToUtc);
        if (f.CountryId is > 0) q = q.Where(a => a.CountryId == f.CountryId);
        return q.OrderByDescending(a => a.EndsAtUtc).ThenByDescending(a => a.Id).Select(a => new
        {
            a,
            Leader = db.Bids.Where(b => b.AuctionId == a.Id).OrderByDescending(b => b.Amount).ThenBy(b => b.Id)
                .Select(b => new { b.UserId, b.User!.FullName, b.User.Phone }).FirstOrDefault(),
        }).Select(x => new ReportRow(x.a.Id, x.a.RefNumber, x.a.TitleEn, x.a.TitleAr,
            x.a.Publisher == null ? null : x.a.Publisher.FirstName + (x.a.Publisher.LastName == "" ? "" : " " + x.a.Publisher.LastName),
            x.a.Country!.NameEn, x.a.StartsAtUtc, x.a.EndsAtUtc, AuctionPhase.Upcoming, x.a.StartPrice, x.a.CurrentPrice, x.a.BidCount,
            x.Leader == null ? null : x.Leader.UserId, x.Leader == null ? null : x.Leader.FullName, x.Leader == null ? null : x.Leader.Phone));
    }

    private ReportRow Phase(ReportRow r)
    {
        var p = AuctionRules.Phase(r.StartsAtUtc, r.EndsAtUtc, Now);
        // Only an ended auction has a winner; while live the leader is not shown as the winner.
        return p == AuctionPhase.Ended ? r with { Phase = p } : r with { Phase = p, WinnerId = null, WinnerName = null, WinnerPhone = null };
    }

    public async Task<ReportResult> ReportAsync(ReportFilter f, CancellationToken ct = default)
    {
        var now = Now;
        var q = ReportQuery(f);
        var page = new PageQuery(f.Page, f.PageSize);
        var total = await q.CountAsync(ct);
        var rows = (await q.Skip(page.Skip).Take(page.SafeSize).ToListAsync(ct)).Select(Phase).ToList();
        var baseQ = db.Auctions.AsNoTracking();
        if (f.FromUtc is not null) baseQ = baseQ.Where(a => a.EndsAtUtc >= f.FromUtc);
        if (f.ToUtc is not null) baseQ = baseQ.Where(a => a.EndsAtUtc < f.ToUtc);
        if (f.CountryId is > 0) baseQ = baseQ.Where(a => a.CountryId == f.CountryId);
        var endedWithBids = baseQ.Where(a => a.BidCount > 0 && (a.EndsAtUtc == null || a.EndsAtUtc <= now));
        return new ReportResult(new(rows, page.SafePage, page.SafeSize, total), total, await endedWithBids.CountAsync(ct),
            await baseQ.CountAsync(a => a.IsActive && a.EndsAtUtc > now, ct), await endedWithBids.SumAsync(a => (decimal?)a.CurrentPrice, ct) ?? 0);
    }

    public async Task<string> ReportCsvAsync(ReportFilter f, CancellationToken ct = default)
    {
        var rows = (await ReportQuery(f).Take(10_000).ToListAsync(ct)).Select(Phase);
        var sb = new StringBuilder("Id,Ref,TitleEn,TitleAr,Publisher,Country,StartsAtUtc,EndsAtUtc,Phase,StartPrice,FinalPrice,Bids,Winner,WinnerPhone\r\n");
        foreach (var r in rows)
            sb.AppendJoin(',', r.Id, Csv(r.RefNumber), Csv(r.TitleEn), Csv(r.TitleAr), Csv(r.PublisherName), Csv(r.CountryNameEn), r.StartsAtUtc.ToString("u"),
                r.EndsAtUtc?.ToString("u"), r.Phase, r.StartPrice.ToString(CultureInfo.InvariantCulture), r.CurrentPrice.ToString(CultureInfo.InvariantCulture),
                r.BidCount, Csv(r.WinnerName), Csv(r.WinnerPhone)).Append("\r\n");
        return sb.ToString();
    }

    /// <summary>Quotes a CSV cell and neutralises spreadsheet formulas (CSV injection).</summary>
    private static string Csv(string? v)
    {
        if (string.IsNullOrEmpty(v)) return "";
        if (v[0] is '=' or '+' or '-' or '@' or '\t' or '\r') v = "'" + v;
        return "\"" + v.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>Ended auctions with bids and their winning (highest, earliest on ties) bid. All filters run in SQL before the record projection.</summary>
    private IQueryable<WinnerRow> Winners(DateTime? fromUtc = null, DateTime? toUtc = null, string? term = null, bool? notified = null, int? auctionId = null)
    {
        var now = Now;
        var auctions = db.Auctions.AsNoTracking().Where(a => a.BidCount > 0 && (a.EndsAtUtc == null || a.EndsAtUtc <= now));
        if (auctionId is not null) auctions = auctions.Where(a => a.Id == auctionId);
        if (fromUtc is not null) auctions = auctions.Where(a => a.EndsAtUtc >= fromUtc);
        if (toUtc is not null) auctions = auctions.Where(a => a.EndsAtUtc < toUtc);
        if (notified is not null) auctions = auctions.Where(a => (a.WinnerNotifiedAtUtc != null) == notified);
        var rows = auctions.Select(a => new
        {
            a.Id, a.RefNumber, a.TitleEn, a.TitleAr, a.EndsAtUtc, a.WinnerNotifiedAtUtc,
            W = db.Bids.Where(b => b.AuctionId == a.Id).OrderByDescending(b => b.Amount).ThenBy(b => b.Id)
                .Select(b => new { b.UserId, b.Amount, b.User!.FullName, b.User.Email, b.User.Phone }).FirstOrDefault(),
        });
        if (term is not null) rows = rows.Where(x => x.W!.FullName.Contains(term) || x.TitleEn.Contains(term) || x.TitleAr.Contains(term) || x.RefNumber == term);
        return rows.OrderByDescending(x => x.EndsAtUtc).ThenByDescending(x => x.Id)
            .Select(x => new WinnerRow(x.Id, x.RefNumber, x.TitleEn, x.TitleAr, x.EndsAtUtc, x.W!.Amount, x.W.UserId, x.W.FullName, x.W.Email, x.W.Phone, x.WinnerNotifiedAtUtc));
    }

    public async Task<PagedResult<WinnerRow>> WinnersAsync(DateTime? fromUtc, DateTime? toUtc, string? q, bool? notified, PageQuery page, CancellationToken ct = default)
    {
        var rows = Winners(fromUtc, toUtc, Normalize.Text(q), notified);
        var total = await rows.CountAsync(ct);
        var items = await rows.Skip(page.Skip).Take(page.SafeSize).ToListAsync(ct);
        return new(items, page.SafePage, page.SafeSize, total);
    }

    /// <summary>Legacy "Auction Winners" page: one row per winning bidder with the total of their winning bids.</summary>
    public async Task<List<WinnerGroup>> WinnersByBidderAsync(DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default)
    {
        // ponytail: groups in memory over the filtered winners; move to SQL GROUP BY if ended auctions reach tens of thousands.
        var list = await Winners(fromUtc, toUtc).ToListAsync(ct);
        return list.GroupBy(r => r.UserId).Select(g => new WinnerGroup(g.Key, g.First().UserName, g.First().Email, g.First().Phone, g.Count(), g.Sum(r => r.Amount),
            g.Min(r => r.EndsAtUtc), g.Max(r => r.EndsAtUtc))).OrderByDescending(g => g.TotalAmount).ToList();
    }

    /// <summary>
    /// E-mails each ended auction's winner once. The auction is claimed with a conditional UPDATE before sending, so two admins
    /// clicking at once cannot double-send; a failed send releases the claim so it can be retried.
    /// </summary>
    public async Task<NotifyResult> NotifyWinnersAsync(Actor admin, int? auctionId, string auctionUrlBase, bool arabic, CancellationToken ct = default)
    {
        var targets = await Winners(notified: false, auctionId: auctionId).Take(500).ToListAsync(ct);
        var already = auctionId is null ? 0 : await Winners(notified: true, auctionId: auctionId).CountAsync(ct);
        if (auctionId is not null && targets.Count == 0 && already == 0) throw AppException.NotFound("winner");
        int sent = 0, failed = 0;
        foreach (var w in targets)
        {
            var now = Now;
            var claimed = await db.Auctions.Where(a => a.Id == w.AuctionId && a.WinnerNotifiedAtUtc == null).ExecuteUpdateAsync(s => s.SetProperty(a => a.WinnerNotifiedAtUtc, now), ct);
            if (claimed == 0) { already++; continue; }
            try
            {
                var (subject, html) = EmailTemplates.AuctionWon(arabic, w.UserName, arabic ? w.TitleAr : w.TitleEn, w.RefNumber, w.Amount, w.EndsAtUtc, auctionUrlBase + w.AuctionId);
                await mailer.SendAsync(w.Email, subject, html, ct);
                db.Notifications.Add(new Notification { UserId = w.UserId, FromUserId = admin.UserId, Kind = NotificationKinds.AuctionWon, AuctionId = w.AuctionId, Amount = w.Amount, Url = $"/auctions/{w.AuctionId}", CreatedAtUtc = now });
                db.AuditLogs.Add(new AuditLog { ActorUserId = admin.UserId, Action = "winner.notify", EntityType = nameof(Auction), EntityId = w.AuctionId.ToString(), CreatedAtUtc = now, IpAddress = admin.Ip });
                await db.SaveChangesAsync(ct);
                sent++;
            }
            catch (Exception ex)
            {
                failed++;
                log.LogError(ex, "Winner e-mail for auction {AuctionId} failed", w.AuctionId);
                await db.Auctions.Where(a => a.Id == w.AuctionId).ExecuteUpdateAsync(s => s.SetProperty(a => a.WinnerNotifiedAtUtc, (DateTime?)null), ct);
            }
        }
        return new NotifyResult(sent, failed, already);
    }
}
