using System.Net;
using System.Text.Json;
using ERecycleHub.Application;
using ERecycleHub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ERecycleHub.IntegrationTests;

/// <summary>One account type: a customer applies to publish auctions; an admin approves. Covers the security rules around that flow.</summary>
[Collection(ApiCollection.Name)]
public class SellerApplicationTests(ApiFactory f)
{
    private const string Url = "/api/account/seller-application";
    private static readonly object Form = new { name = "Acme Recycling", company = "Acme Ltd", mobile = "0790000123", address = "Amman", categoryIds = new[] { 1 }, crNumber = "CR-1001", bankName = "Test Bank", accountHolderName = "Acme Ltd", accountNumber = "JO94CBJO0010000000000131000302" };

    private static Task<HttpResponseMessage> ApplyAsync(ApiClient c, bool withDocs = true, object? data = null) =>
        withDocs
            ? c.MultipartAsync(HttpMethod.Post, Url, data ?? Form, ("crDocument", "cr.pdf", TestFiles.Pdf), ("idDocument", "id.pdf", TestFiles.Pdf))
            : c.MultipartAsync(HttpMethod.Post, Url, data ?? Form);

    private async Task<(int userId, ApiClient client)> NewApplicantAsync()
    {
        var (id, email) = await f.NewCustomerAsync(wallet: 0);
        return (id, await f.SignedInAsync(email, ApiFactory.Password));
    }

    private static async Task<int> IdAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>(ApiClient.Json)).GetProperty("id").GetInt32();

    private async Task<ApiClient> AdminAsync() => await f.SignedInAsync(DevSeed.AdminEmail, DevSeed.AdminPassword);

    [Fact]
    public async Task Anonymous_visitors_cannot_apply_or_see_status()
    {
        var anon = f.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await ApplyAsync(anon)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync(Url)).StatusCode);
        // The old anonymous endpoint is gone: it no longer accepts submissions, whoever calls it.
        var old = await anon.PostAsync("/api/applications", new { name = "x", email = "x@y.z", password = "ChosenPass#2026" });
        Assert.Contains(old.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        Assert.False(await f.DbAsync(db => db.PublisherApplications.AnyAsync(a => a.Name == "x")));
    }

    [Fact]
    public async Task A_customer_cannot_create_auctions_before_approval()
    {
        var (_, c) = await NewApplicantAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/seller/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.MultipartAsync(HttpMethod.Post, "/api/seller/auctions", new { countryId = 1, titleEn = "x", titleAr = "x", startPrice = 1, bidIncrement = 1 })).StatusCode);
        // A pending application changes nothing about what they may do.
        Assert.Equal(HttpStatusCode.Created, (await ApplyAsync(c)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/seller/dashboard")).StatusCode);
    }

    [Fact]
    public async Task Individuals_apply_with_an_identity_document_and_no_company_details()
    {
        var (_, c) = await NewApplicantAsync();
        var data = new { name = "Solo Seller", accountType = "Individual", bankName = "Test Bank", accountNumber = "JO94CBJO0010000000000131000302" };
        Assert.Equal("application.documents_required", await ApiClient.CodeAsync(await c.MultipartAsync(HttpMethod.Post, Url, data, ("crDocument", "cr.pdf", TestFiles.Pdf))));
        Assert.Equal(HttpStatusCode.Created, (await c.MultipartAsync(HttpMethod.Post, Url, data, ("idDocument", "id.pdf", TestFiles.Pdf))).StatusCode);
    }

    [Fact]
    public async Task A_mobile_that_is_not_a_valid_jordanian_number_is_rejected()
    {
        var (_, c) = await NewApplicantAsync();
        Assert.Equal("phone.invalid", await ApiClient.CodeAsync(await ApplyAsync(c, data: new { name = "Acme", mobile = "12345", company = "Acme Ltd", crNumber = "CR-1", bankName = "B", accountNumber = "1" })));
    }

    [Fact]
    public async Task Documents_are_required_and_only_one_application_can_be_pending()
    {
        var (_, c) = await NewApplicantAsync();
        Assert.Equal("application.documents_required", await ApiClient.CodeAsync(await ApplyAsync(c, withDocs: false)));
        Assert.Equal("application.name_required", await ApiClient.CodeAsync(await ApplyAsync(c, data: new { name = " ", categoryIds = new int[0] })));
        Assert.Equal("application.company_required", await ApiClient.CodeAsync(await ApplyAsync(c, data: new { name = "Acme", bankName = "B", accountNumber = "1" })));
        Assert.Equal("application.bank_required", await ApiClient.CodeAsync(await ApplyAsync(c, data: new { name = "Acme", company = "Acme Ltd", crNumber = "CR-1" })));
        Assert.Equal(HttpStatusCode.Created, (await ApplyAsync(c)).StatusCode);
        Assert.Equal("application.already_pending", await ApiClient.CodeAsync(await ApplyAsync(c)));
        var status = await c.GetJsonAsync<JsonElement>(Url);
        Assert.False(status.GetProperty("canApply").GetBoolean());
        Assert.Equal("Pending", status.GetProperty("application").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Concurrent_submissions_create_a_single_pending_application()
    {
        var (uid, c) = await NewApplicantAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => ApplyAsync(c)));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Equal(1, await f.DbAsync(db => db.PublisherApplications.CountAsync(a => a.UserId == uid && a.Status == ApplicationStatus.Pending)));
    }

    [Fact]
    public async Task Disallowed_or_disguised_documents_are_rejected()
    {
        var (_, c) = await NewApplicantAsync();
        var html = await c.MultipartAsync(HttpMethod.Post, Url, Form, ("crDocument", "cr.pdf", TestFiles.FakePngWithHtml), ("idDocument", "id.pdf", TestFiles.Pdf));
        Assert.Equal("file.content_mismatch", await ApiClient.CodeAsync(html));
        var exe = await c.MultipartAsync(HttpMethod.Post, Url, Form, ("crDocument", "cr.exe", TestFiles.Pdf), ("idDocument", "id.pdf", TestFiles.Pdf));
        Assert.Equal("file.type_not_allowed", await ApiClient.CodeAsync(exe));
    }

    [Fact]
    public async Task Admins_and_existing_sellers_cannot_apply()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await ApplyAsync(await AdminAsync())).StatusCode);
        var seller = await f.SignedInAsync(DevSeed.PublisherEmail, DevSeed.PublisherPassword);
        Assert.Equal("application.already_seller", await ApiClient.CodeAsync(await ApplyAsync(seller)));
        var status = await seller.GetJsonAsync<JsonElement>(Url);
        Assert.True(status.GetProperty("isSeller").GetBoolean());
        Assert.False(status.GetProperty("canApply").GetBoolean());
    }

    [Fact]
    public async Task Approval_makes_the_customer_a_seller_without_a_new_login_and_without_changing_the_role()
    {
        var (uid, c) = await NewApplicantAsync();
        var appId = await IdAsync(await ApplyAsync(c));
        var admin = await AdminAsync();
        var approved = await admin.PostAsync($"/api/admin/applications/{appId}/approve");
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        // Same cookie, no re-login: the session is re-issued from the database state.
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/seller/dashboard")).StatusCode);
        var me = await c.GetJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal("Customer", me.GetProperty("user").GetProperty("role").GetString());
        Assert.NotEqual(JsonValueKind.Null, me.GetProperty("user").GetProperty("publisherId").ValueKind);

        var user = await f.DbAsync(db => db.Users.Include(u => u.Publisher).SingleAsync(u => u.Id == uid));
        Assert.Equal(UserRole.Customer, user.Role);
        Assert.NotNull(user.Publisher);
        Assert.NotNull(user.CrDocumentFileId);
        Assert.Equal(1, await f.DbAsync(db => db.Notifications.CountAsync(n => n.UserId == uid && n.Kind == NotificationKinds.SellerApproved)));
        Assert.Equal(1, await f.DbAsync(db => db.AuditLogs.CountAsync(a => a.Action == "application.approve" && a.EntityId == appId.ToString())));
    }

    [Fact]
    public async Task Double_approval_creates_one_publisher_and_the_second_call_conflicts()
    {
        var (uid, c) = await NewApplicantAsync();
        var appId = await IdAsync(await ApplyAsync(c));
        var admins = new[] { await AdminAsync(), await AdminAsync() };
        foreach (var a in admins) await a.CsrfAsync();
        var results = await Task.WhenAll(admins.Select(a => a.PostAsync($"/api/admin/applications/{appId}/approve")));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(1, await f.DbAsync(db => db.Publishers.CountAsync(p => p.UserId == uid)));
    }

    [Fact]
    public async Task Rejection_needs_a_reason_shows_it_to_the_applicant_and_allows_reapplying()
    {
        var (uid, c) = await NewApplicantAsync();
        var appId = await IdAsync(await ApplyAsync(c));
        var admin = await AdminAsync();
        Assert.Equal("application.reason_required", await ApiClient.CodeAsync(await admin.PostAsync($"/api/admin/applications/{appId}/reject", new { reason = "  " })));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/applications/{appId}/reject", new { reason = "ID photo is unreadable" })).StatusCode);

        var status = await c.GetJsonAsync<JsonElement>(Url);
        Assert.Equal("Rejected", status.GetProperty("application").GetProperty("status").GetString());
        Assert.Equal("ID photo is unreadable", status.GetProperty("application").GetProperty("rejectionReason").GetString());
        Assert.True(status.GetProperty("canApply").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/seller/dashboard")).StatusCode);
        Assert.Equal(1, await f.DbAsync(db => db.Notifications.CountAsync(n => n.UserId == uid && n.Kind == NotificationKinds.SellerRejected)));

        Assert.Equal(HttpStatusCode.Created, (await ApplyAsync(c)).StatusCode); // corrected application
        Assert.Equal("application.not_pending", await ApiClient.CodeAsync(await admin.PostAsync($"/api/admin/applications/{appId}/reject", new { reason = "again" })));
    }

    [Fact]
    public async Task Customers_and_sellers_cannot_decide_or_list_applications()
    {
        var (_, applicant) = await NewApplicantAsync();
        var appId = await IdAsync(await ApplyAsync(applicant));
        var seller = await f.SignedInAsync(DevSeed.PublisherEmail, DevSeed.PublisherPassword);
        foreach (var c in new[] { applicant, seller })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/admin/applications")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync($"/api/admin/applications/{appId}/approve")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync($"/api/admin/applications/{appId}/reject", new { reason = "no" })).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Client().PostAsync($"/api/admin/applications/{appId}/approve")).StatusCode);
        Assert.Equal(ApplicationStatus.Pending, await f.DbAsync(db => db.PublisherApplications.Where(a => a.Id == appId).Select(a => a.Status).SingleAsync()));
    }

    [Fact]
    public async Task Application_documents_are_private_to_the_applicant_and_admins()
    {
        var (_, applicant) = await NewApplicantAsync();
        var appId = await IdAsync(await ApplyAsync(applicant));
        var fileId = await f.DbAsync(db => db.PublisherApplications.Where(a => a.Id == appId).Select(a => a.IdDocumentFileId!.Value).SingleAsync());
        Assert.Equal(HttpStatusCode.OK, (await applicant.GetAsync($"/api/files/{fileId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await AdminAsync()).GetAsync($"/api/files/{fileId}")).StatusCode);
        var (_, stranger) = await NewApplicantAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/files/{fileId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.Client().GetAsync($"/api/files/{fileId}")).StatusCode);
    }

    [Fact]
    public async Task An_applicant_deactivated_before_the_decision_cannot_be_approved()
    {
        var (uid, c) = await NewApplicantAsync();
        var appId = await IdAsync(await ApplyAsync(c));
        await f.DbAsync(db => db.Users.Where(u => u.Id == uid).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false)));
        var admin = await AdminAsync();
        Assert.Equal("application.user_inactive", await ApiClient.CodeAsync(await admin.PostAsync($"/api/admin/applications/{appId}/approve")));
        Assert.False(await f.DbAsync(db => db.Publishers.AnyAsync(p => p.UserId == uid)));
        // The failed approval rolled back, so the application is still pending and can be rejected.
        Assert.Equal(ApplicationStatus.Pending, await f.DbAsync(db => db.PublisherApplications.Where(a => a.Id == appId).Select(a => a.Status).SingleAsync()));
    }

    [Fact]
    public async Task A_seller_still_bids_on_other_stores_but_not_on_their_own_lots()
    {
        var (uid, c) = await NewApplicantAsync();
        await f.DbAsync(db => db.Users.Where(u => u.Id == uid).ExecuteUpdateAsync(s => s.SetProperty(u => u.WalletBalance, 1000m)));
        await f.DbAsync(async db => { db.WalletLedger.Add(new WalletLedgerEntry { UserId = uid, Amount = 1000, BalanceAfter = 1000, Reason = LedgerReason.MigrationOpeningBalance, CreatedAtUtc = DateTime.UtcNow }); await db.SaveChangesAsync(); });
        var appId = await IdAsync(await ApplyAsync(c));
        await (await AdminAsync()).PostAsync($"/api/admin/applications/{appId}/approve");
        var pid = await f.DbAsync(db => db.Publishers.Where(p => p.UserId == uid).Select(p => p.Id).SingleAsync());

        var others = await f.NewAuctionAsync(publisherId: await f.PublisherIdAsync(DevSeed.PublisherEmail));
        var own = await f.NewAuctionAsync(publisherId: pid);
        Assert.Equal(HttpStatusCode.OK, (await c.BidAsync(others, 120)).StatusCode);
        Assert.Equal("bid.own_auction", await ApiClient.CodeAsync(await c.BidAsync(own, 120)));
    }

    [Fact]
    public async Task Sellers_are_still_customers_and_admins_can_never_become_sellers()
    {
        // The role enum no longer has a publisher value at all.
        Assert.DoesNotContain("Publisher", Enum.GetNames<UserRole>());
        var admin = await AdminAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/seller/dashboard")).StatusCode);
        Assert.Equal(0, await f.DbAsync(db => db.Publishers.CountAsync(p => p.User!.Role == UserRole.Admin)));
    }
}
