using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriAuction.Application.Reporting;
using AgriAuction.Domain.Auctions;
using AgriAuction.Domain.Catalog;
using AgriAuction.Domain.Disputes;
using AgriAuction.Domain.Identity;
using AgriAuction.Domain.Lots;
using AgriAuction.Domain.Realtime;
using AgriAuction.Domain.Reporting;
using AgriAuction.Domain.Transactions;
using AgriAuction.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace AgriAuction.IntegrationTests;

public sealed class ReportingApiTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static readonly DateOnly Known = new(2010, 6, 1);
    private async Task<HttpClient> Login(string role = Roles.Administrator, HttpClient? client = null)
    {
        client ??= fixture.Host.CreateClient(new() { AllowAutoRedirect = false });
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = fixture.Accounts[role].Email, password = fixture.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()); return client;
    }
    private static string Query(DateOnly day, DateOnly? through = null) => $"from={day:yyyy-MM-dd}&through={through ?? day:yyyy-MM-dd}&timeZone={ReportPeriod.Zone}";
    private static long Count(ReportSummary report, string dimension, string? state = null) => report.Metrics.Where(m => m.Dimension == dimension && (state is null || m.State == state)).Sum(m => m.Count);
    private async Task<ReportSummary> Summary(HttpClient client, DateOnly day, DateOnly? through = null)
    {
        var r = await client.GetAsync("/api/v1/admin/reports/dashboard?" + Query(day, through)); Assert.True(r.StatusCode == HttpStatusCode.OK, await r.Content.ReadAsStringAsync()); return (await r.Content.ReadFromJsonAsync<ReportSummary>())!;
    }
    private static SimulationBidRules Rules(string currency, string basis) => RoomApiTests.Rules() with { Currency = currency, PriceBasis = basis, StartingPrice = 0.0001m, MinimumIncrement = 0.0001m };

    private async Task Seed(DateOnly day, int extraValueGroups = 0, bool lateFirstProjection = false)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var p = ReportPeriod.Create(day, day, ReportPeriod.Zone); var from = p.FromUtc;
        var farmer = fixture.Accounts[Roles.Farmer].Id; var enterprise = fixture.Accounts[Roles.Enterprise].Id; var admin = fixture.Accounts[Roles.Administrator].Id;
        var category = Category.Create("REP-" + Guid.NewGuid().ToString("N")[..20], "Known report fixture", true); db.Categories.Add(category);
        var pendingEnterprise = new Account { Email = Guid.NewGuid() + "@test.invalid", DisplayName = "Synthetic participant", Role = Roles.Enterprise, CreatedAt = from.AddYears(-1) }; pendingEnterprise.NormalizedEmail = pendingEnterprise.Email.ToUpperInvariant(); db.Users.Add(pendingEnterprise);
        foreach (var (role, enabled, at) in new[] { (Roles.Administrator, true, from), (Roles.Farmer, true, from.AddHours(1)), (Roles.Farmer, true, from.AddHours(2)), (Roles.Enterprise, false, p.ToUtc.AddMilliseconds(-1)), (Roles.Enterprise, true, p.ToUtc), (Roles.Farmer, true, from.AddMilliseconds(-1)) })
        { var user = new Account { Email = Guid.NewGuid() + "@test.invalid", Role = role, IsEnabled = enabled, DisplayName = "Report fixture only", CreatedAt = at }; user.NormalizedEmail = user.Email.ToUpperInvariant(); db.Users.Add(user); }
        await db.SaveChangesAsync();
        async Task<Lot> LotAt(DateTimeOffset created, string outcome = "Approve")
        {
            var lot = Lot.Create(farmer, new() { CategoryId = category.Id, Title = "=SUM(1,2) report " + Guid.NewGuid().ToString("N")[..8], Description = "Known synthetic report fixture", Location = "Test", Quantity = 1, QuantityUnit = "lot", Weight = 1, WeightUnit = "kg", ProposedStartingPrice = 1, ProposedIncrement = 1, Currency = "VND", PriceBasis = "proposal", ProposedStartsAt = from.AddDays(2), ProposedEndsAt = from.AddDays(3) });
            db.Lots.Add(lot); await db.SaveChangesAsync();
            if (outcome != "Draft")
            {
                var revision = lot.Submit(1); db.LotRevisions.Add(revision);
                if (outcome != "PendingReview")
                { lot.Decide(revision.Id, outcome, "Synthetic fixture"); db.LotReviews.Add(new() { LotId = lot.Id, RevisionId = revision.Id, AdminId = admin, Outcome = outcome, Reason = "Synthetic report fixture", ReviewedAt = created.AddMinutes(1) }); }
                await db.SaveChangesAsync();
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE lot_versions SET submitted_at = {created.AddMinutes(1)} WHERE id = {revision.Id}");
            }
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE lots SET created_at = {created}, updated_at = {created.AddMinutes(2)}, submitted_at = CASE WHEN state='Draft' THEN NULL ELSE {created.AddMinutes(1)} END WHERE id = {lot.Id}");
            return lot;
        }
        async Task<(Auction Auction, PostAuctionTransaction? Trade)> Closed(DateTimeOffset closed, decimal? price, string currency = "VND", string basis = "kg", DateTimeOffset? projectedAt = null)
        {
            var lot = await LotAt(closed.AddHours(-1));
            var terms = new SimulationTerms { RegistrationOpensAt = closed.AddHours(-1), RegistrationClosesAt = closed.AddMinutes(-21), StartsAt = closed.AddMinutes(-20), EndsAt = closed.AddSeconds(-1), DepositAmount = 1, Currency = currency };
            var auction = Auction.Prepare(lot, admin, terms, closed.AddMinutes(-30)); auction.Publish(admin, closed.AddMinutes(-30)); db.Auctions.Add(auction);
            var registration = AuctionRegistration.Register(auction, enterprise, closed.AddMinutes(-25)); registration.ConfirmSimulation(1, closed.AddMinutes(-24)); db.AuctionRegistrations.Add(registration);
            db.SimulationDeposits.Add(new() { RegistrationId = registration.Id, State = "SimulatedHeld", Amount = 1, Currency = currency, CreatedAt = closed.AddMinutes(-25), ConfirmedAt = closed.AddMinutes(-24), ConfirmedBy = admin });
            var room = SimulationRoom.Configure(auction.Id, admin, Rules(currency, basis), terms.StartsAt, terms.EndsAt, closed.AddMinutes(-29)); db.SimulationRooms.Add(room); await db.SaveChangesAsync(); room.Start(admin, terms.StartsAt);
            SimulationBid? bid = null; if (price.HasValue) { bid = room.Accept(enterprise, price.Value, closed.AddSeconds(-2)); db.SimulationBids.Add(bid); }
            var result = room.CloseIfDue(closed)!; db.SimulationResults.Add(result); await db.SaveChangesAsync();
            PostAuctionTransaction? trade = null;
            if (bid is not null) { trade = PostAuctionTransaction.FromResult(room, result, bid, lot.Id, auction.RevisionId, farmer, lot.Content.Title, "{}", projectedAt ?? closed.AddSeconds(10)); db.PostAuctionTransactions.Add(trade); await db.SaveChangesAsync(); }
            return (auction, trade);
        }
        var a = await Closed(from, 100.1250m, projectedAt: lateFirstProjection ? p.ToUtc : null);
        var b = await Closed(from.AddHours(6), 200.2500m, basis: "cả lô");
        var c = await Closed(from.AddHours(7), 5.5000m, currency: "USD");
        var a2 = await Closed(from.AddHours(13), 0.0001m);
        await Closed(p.ToUtc.AddMilliseconds(-1), null); await Closed(p.ToUtc, 50m); await Closed(from.AddMinutes(-1), null);
        for (var i = 0; i < extraValueGroups; i++) await Closed(from.AddHours(18).AddSeconds(i), 1m, basis: "isolated group " + i);
        var draftLot = await LotAt(from.AddHours(9));
        var draft = Auction.Prepare(draftLot, admin, new() { RegistrationOpensAt = from, RegistrationClosesAt = from.AddDays(1), StartsAt = from.AddDays(2), EndsAt = from.AddDays(3), DepositAmount = 1, Currency = "VND" }, from.AddHours(10)); db.Auctions.Add(draft);
        await LotAt(from.AddHours(11), "PendingReview"); await LotAt(from.AddHours(12), "Reject"); await LotAt(from.AddHours(14), "Draft");
        var pending = AuctionRegistration.Register(b.Auction, pendingEnterprise.Id, from.AddHours(5.5)); db.AuctionRegistrations.Add(pending); db.SimulationDeposits.Add(new() { RegistrationId = pending.Id, Amount = 1, Currency = "VND", CreatedAt = pending.RegisteredAt });
        foreach (var (trade, state) in new[] { (a.Trade!, "Open"), (b.Trade!, "UnderReview"), (c.Trade!, "Mediate"), (a2.Trade!, "Accept") })
        {
            var opened = trade.CreatedAt > from.AddHours(15) ? trade.CreatedAt.AddHours(1) : from.AddHours(15);
            var complaint = Complaint.Create(trade.Id, farmer, enterprise, "TransactionRecord", trade.Title, "Synthetic reason", "Synthetic description", 1, opened); db.Complaints.Add(complaint);
            db.ComplaintEntries.Add(new() { ComplaintId = complaint.Id, ActorId = farmer, ActorRole = Roles.Farmer, Kind = "Created", Text = "Fixture", State = "Open", Version = 1, OccurredAt = complaint.CreatedAt, NextAttemptAt = complaint.CreatedAt });
            if (state != "Open") { complaint.StartReview(1, from.AddHours(16)); db.ComplaintEntries.Add(new() { ComplaintId = complaint.Id, ActorId = admin, ActorRole = Roles.Administrator, Kind = "ReviewStarted", Text = "Fixture", State = "UnderReview", Version = 2, OccurredAt = complaint.UpdatedAt, NextAttemptAt = complaint.UpdatedAt }); }
            if (state is "Mediate" or "Accept") { complaint.Decide(2, state, true, from.AddHours(17)); db.ComplaintEntries.Add(new() { ComplaintId = complaint.Id, ActorId = admin, ActorRole = Roles.Administrator, Kind = "DecisionRecorded", Text = "Fixture", State = complaint.State, Outcome = state, Version = 3, OccurredAt = complaint.UpdatedAt, NextAttemptAt = complaint.UpdatedAt }); }
        }
        await db.SaveChangesAsync();
    }

    [PostgresFact] public async Task KnownCountsAndQuoteGroupsMatchHandCalculatedValues()
    {
        await Seed(Known); using var admin = await Login(); var data = await Summary(admin, Known);
        Assert.Equal("Simulation", data.Mode); Assert.Null(data.ActualRevenue); Assert.Equal("UnavailablePolicyLocked", data.ActualRevenueState);
        Assert.Equal(4, Count(data, "Accounts")); Assert.Equal(3, Count(data, "Accounts", "Enabled")); Assert.Equal(1, Count(data, "Accounts", "Disabled"));
        Assert.Equal(6, Count(data, "Lots", "Approved")); Assert.Equal(1, Count(data, "Lots", "PendingReview")); Assert.Equal(1, Count(data, "Lots", "Rejected")); Assert.Equal(1, Count(data, "Lots", "Draft"));
        Assert.Equal(5, Count(data, "Auctions", "Scheduled")); Assert.Equal(1, Count(data, "Auctions", "Draft"));
        Assert.Equal(4, data.WinningAuctions); Assert.Equal(1, data.NoWinnerAuctions); Assert.Equal(80m, data.WinnerRatePercent); Assert.Equal(4, Count(data, "Bids")); Assert.Equal(4, Count(data, "Transactions"));
        Assert.Equal(4, Count(data, "Complaints")); Assert.Equal(5, Count(data, "Pending"));
        Assert.Equal(6, data.Values.Count);
        var kg = Assert.Single(data.Values, v => v.Source == "Winning" && v.Currency == "VND" && v.PriceBasis == "kg"); Assert.Equal("100.1251", kg.Total); Assert.Equal(2, kg.Records);
        Assert.Equal("200.2500", Assert.Single(data.Values, v => v.Source == "Winning" && v.PriceBasis == "cả lô").Total);
        Assert.Equal("5.5000", Assert.Single(data.Values, v => v.Source == "Winning" && v.Currency == "USD").Total);
    }
    [PostgresFact] public async Task DailyChartIncludesZeroDaysAndMatchesGroupedTotals()
    {
        var day = Known.AddDays(4); await Seed(day); using var admin = await Login(); var data = await Summary(admin, day, day.AddDays(2));
        foreach (var group in data.Values)
        { Assert.Equal(3, group.Days.Count); Assert.Equal(group.Records, group.Days.Sum(d => d.Records)); Assert.Equal(decimal.Parse(group.Total, System.Globalization.CultureInfo.InvariantCulture), group.Days.Sum(d => decimal.Parse(d.Total, System.Globalization.CultureInfo.InvariantCulture))); Assert.Equal("0.0000", group.Days[2].Total); }
        var kg = Assert.Single(data.Values, v => v.Source == "Winning" && v.Currency == "VND" && v.PriceBasis == "kg"); Assert.Equal("50.0000", kg.Days[1].Total); Assert.Equal("150.1251", kg.Total);
    }
    [PostgresFact] public async Task EmptyRangeHasZeroCountsNullRateAndUnavailableRevenue()
    {
        using var admin = await Login(); var data = await Summary(admin, new(2002, 1, 1)); Assert.All(data.Metrics, m => Assert.Equal(0, m.Count)); Assert.Empty(data.Values); Assert.Equal(0, data.WinningAuctions); Assert.Null(data.WinnerRatePercent); Assert.Null(data.ActualRevenue);
        var page = (await admin.GetFromJsonAsync<ReportPage>("/api/v1/admin/reports/details?" + Query(new(2002, 1, 1))))!; Assert.Empty(page.Items); Assert.Equal(0, page.TotalCount);
    }
    [PostgresFact] public async Task LocalMidnightBoundariesUseResultCloseAndTransactionCreationSeparately()
    {
        var day = Known.AddDays(8); await Seed(day, lateFirstProjection: true); using var admin = await Login(); var period = ReportPeriod.Create(day, day, ReportPeriod.Zone); var data = await Summary(admin, day);
        Assert.Equal(period.FromUtc, data.Range.FromUtc); Assert.Equal(period.ToUtc, data.Range.ToExclusiveUtc); Assert.Equal(4, data.WinningAuctions); Assert.Equal(1, data.NoWinnerAuctions);
        var page = (await admin.GetFromJsonAsync<ReportPage>("/api/v1/admin/reports/details?" + Query(day) + "&dataset=results&pageSize=100"))!; Assert.Equal(5, page.TotalCount); Assert.Contains(page.Items, r => r.OccurredAt == period.FromUtc); Assert.DoesNotContain(page.Items, r => r.OccurredAt >= period.ToUtc || r.OccurredAt < period.FromUtc);
        Assert.Equal(3, Count(data, "Transactions"));
        var next = await Summary(admin, day.AddDays(1)); Assert.Equal(1, next.WinningAuctions); Assert.Equal(0, next.NoWinnerAuctions); Assert.Equal(2, Count(next, "Transactions")); Assert.Equal(0, Count(next, "Bids"));
    }
    [PostgresFact] public async Task DetailStateFiltersPaginationAndCsvAreScopedAndSafe()
    {
        var day = Known.AddDays(12); await Seed(day); using var admin = await Login(); var url = "/api/v1/admin/reports/details?" + Query(day) + "&dataset=results&state=ClosedWithWinner&pageSize=2";
        var first = (await admin.GetFromJsonAsync<ReportPage>(url))!; var next = (await admin.GetFromJsonAsync<ReportPage>(url + "&page=2"))!; Assert.Equal(4, first.TotalCount); Assert.Equal(2, first.Items.Count); Assert.Equal(2, next.Items.Count); Assert.Empty(first.Items.Select(i => i.Id).Intersect(next.Items.Select(i => i.Id)));
        var csv = await admin.GetAsync(url.Replace("/details?", "/details.csv?")); Assert.Equal(HttpStatusCode.OK, csv.StatusCode); Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType); var bytes = await csv.Content.ReadAsByteArrayAsync(); Assert.Equal(new byte[] { 239, 187, 191 }, bytes[..3]); var text = System.Text.Encoding.UTF8.GetString(bytes); Assert.Contains("\"'=SUM(1,2)", text); Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("@test.invalid", text); Assert.Equal(4, text.Split("\r\n").Length);
        var pending = (await admin.GetFromJsonAsync<ReportPage>("/api/v1/admin/reports/details?" + Query(day) + "&dataset=pending&state=ComplaintReview"))!; Assert.Equal(2, pending.TotalCount); Assert.All(pending.Items, r => Assert.Equal("ComplaintReview", r.Kind));
    }
    [PostgresFact] public async Task RbacAndDisabledSessionsProtectDashboardCsvAndHtml()
    {
        using var anon = fixture.Host.CreateClient(new() { AllowAutoRedirect = false }); using var farmer = await Login(Roles.Farmer); using var enterprise = await Login(Roles.Enterprise); using var admin = await Login();
        foreach (var route in new[] { "dashboard", "details", "details.csv" })
        { var url = "/api/v1/admin/reports/" + route + "?" + Query(Known); Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync(url)).StatusCode); foreach (var other in new[] { farmer, enterprise }) Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync(url)).StatusCode); }
        Assert.Equal(HttpStatusCode.Redirect, (await anon.GetAsync("/pages/admin/analytics-dashboard.html")).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await enterprise.GetAsync("/pages/admin/analytics-dashboard.html")).StatusCode);
        await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var actor = fixture.Accounts[Roles.Administrator].Id;
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE users SET is_enabled=false WHERE id={actor}"); try { Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync("/api/v1/admin/reports/dashboard?" + Query(Known))).StatusCode); } finally { await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE users SET is_enabled=true WHERE id={actor}"); }
    }
    [PostgresFact] public async Task InvalidRangesStatesTimezoneAndSqlFragmentsAreRejected()
    {
        using var admin = await Login(); foreach (var query in new[] { "from=2000-01-01&through=2002-01-01&timeZone=" + ReportPeriod.Zone, "from=2010-01-02&through=2010-01-01&timeZone=" + ReportPeriod.Zone, "from=2010-01-01&through=2010-01-01&timeZone=UTC", "from=bad&through=bad", "" }) Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/v1/admin/reports/dashboard?" + query)).StatusCode);
        foreach (var suffix in new[] { "&dataset=users%3BDROP%20TABLE%20users", "&dataset=lots&state=Paid", "&dataset=results&page=0", "&dataset=results&pageSize=101" }) Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/v1/admin/reports/details?" + Query(Known) + suffix)).StatusCode);
    }
    private async Task<string> Fingerprint(AppDbContext db)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection(); if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();
        var tables = new List<string>(); await using (var cmd = new NpgsqlCommand("SELECT tablename FROM pg_tables WHERE schemaname=@schema AND tablename <> '__EFMigrationsHistory' ORDER BY tablename", connection)) { cmd.Parameters.AddWithValue("schema", fixture.Schema); await using var reader = await cmd.ExecuteReaderAsync(); while (await reader.ReadAsync()) tables.Add(reader.GetString(0)); }
        var values = new List<string>(); foreach (var table in tables) { await using var cmd = new NpgsqlCommand("SELECT count(*)::text || ':' || md5(COALESCE(string_agg(to_jsonb(t)::text,'' ORDER BY to_jsonb(t)::text),'')) FROM \"" + fixture.Schema + "\".\"" + table + "\" t", connection); values.Add(table + ":" + await cmd.ExecuteScalarAsync()); } return string.Join('|', values);
    }
    [PostgresFact] public async Task ViewingSummaryDetailsAndCsvDoesNotMutateAnySourceTable()
    {
        var day = Known.AddDays(16); await Seed(day); using var admin = await Login(); await using var scope = fixture.Host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var before = await Fingerprint(db);
        await Summary(admin, day); foreach (var dataset in ReportRules.States.Keys) { Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/admin/reports/details?" + Query(day) + "&dataset=" + dataset)).StatusCode); Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/admin/reports/details.csv?" + Query(day) + "&dataset=" + dataset)).StatusCode); }
        Assert.Equal(before, await Fingerprint(db));
    }
    [PostgresFact] public async Task DatabaseFailureCannotReturnSuccessfulEmptyOrFallbackKpis()
    {
        using var admin = await Login(); fixture.Fault.FailNextRead(); Assert.Equal(HttpStatusCode.ServiceUnavailable, (await admin.GetAsync("/api/v1/admin/reports/dashboard?" + Query(Known))).StatusCode);
    }
    [PostgresFact] public async Task ProductionNamedTestHostCannotOpenSimulationReportApis()
    {
        using var prod = fixture.Host.WithWebHostBuilder(b => b.UseEnvironment("Production")); using var client = await Login(client: prod.CreateClient());
        foreach (var route in new[] { "dashboard", "details", "details.csv" }) Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync("/api/v1/admin/reports/" + route + "?" + Query(Known))).StatusCode);
    }
    [PostgresFact] public async Task TooManyIncomparablePriceGroupsRejectsRatherThanSilentlyTruncates()
    {
        var day = Known.AddDays(20); await Seed(day, 50); using var admin = await Login();
        var response = await admin.GetAsync("/api/v1/admin/reports/dashboard?" + Query(day)); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("50", await response.Content.ReadAsStringAsync());
        // Bounded detail remains available without loading a bid history or inventing one total.
        var page = (await admin.GetFromJsonAsync<ReportPage>("/api/v1/admin/reports/details?" + Query(day) + "&dataset=results&pageSize=20"))!; Assert.Equal(20, page.Items.Count); Assert.Equal(55, page.TotalCount);
    }
}
