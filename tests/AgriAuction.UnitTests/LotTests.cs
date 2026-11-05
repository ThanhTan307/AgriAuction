using AgriAuction.Domain.Catalog;
using AgriAuction.Domain.Identity;
using AgriAuction.Domain.Lots;
using Xunit;

namespace AgriAuction.UnitTests;

public sealed class LotTests
{
    private static LotContent Content() => new() { CategoryId = Guid.NewGuid(), Title = " Lô kiểm thử ", Description = "Mô tả khai báo", Location = "Địa điểm khai báo",
        Quantity = 10, QuantityUnit = "thùng", Weight = 200, WeightUnit = "kg", ProposedStartingPrice = 1000, ProposedIncrement = 10,
        Currency = " vnd ", PriceBasis = "cả lô (đề xuất)", ProposedStartsAt = DateTimeOffset.UtcNow.AddDays(1), ProposedEndsAt = DateTimeOffset.UtcNow.AddDays(2) };
    [Fact]
    public void ContentNormalizesWithoutInventingPricePolicy()
    {
        var content = Content().Validated();
        Assert.Equal("Lô kiểm thử", content.Title); Assert.Equal("VND", content.Currency);
        Assert.Equal("cả lô (đề xuất)", content.PriceBasis); Assert.Equal(1000, content.ProposedStartingPrice);
    }
    [Theory]
    [InlineData("title")][InlineData("quantity")][InlineData("weight")][InlineData("currency")][InlineData("priceBasis")]
    [InlineData("proposedIncrement")][InlineData("proposedStartingPrice")][InlineData("proposedEndsAt")][InlineData("categoryId")]
    public void InvalidFieldsAreRejected(string field)
    {
        var content = Content();
        content = field switch { "title" => content with { Title = " " }, "quantity" => content with { Quantity = 0.0001m },
            "weight" => content with { Weight = -1 }, "currency" => content with { Currency = "?" }, "priceBasis" => content with { PriceBasis = " " },
            "proposedIncrement" => content with { ProposedIncrement = 0 }, "proposedStartingPrice" => content with { ProposedStartingPrice = 0.00001m },
            "proposedEndsAt" => content with { ProposedEndsAt = content.ProposedStartsAt }, _ => content with { CategoryId = Guid.Empty } };
        Assert.Contains(field, Assert.Throws<RuleViolation>(() => content.Validated()).Errors.Keys);
    }
    [Fact]
    public void SubmitRequiresImageAndDoesNotPartiallyChangeState()
    {
        var lot = Lot.Create(Guid.NewGuid(), Content());
        Assert.Throws<RuleViolation>(() => lot.Submit(0));
        Assert.Equal(LotStates.Draft, lot.State); Assert.Equal(1, lot.Version); Assert.Null(lot.CurrentRevisionId);
    }
    [Fact]
    public void PendingAndApprovedContentCannotBeChanged()
    {
        var lot = Lot.Create(Guid.NewGuid(), Content()); var revision = lot.Submit(1);
        Assert.Throws<WorkflowConflict>(() => lot.Update(Content()));
        Assert.Throws<WorkflowConflict>(() => lot.ImagesChanged());
        lot.Decide(revision.Id, "Approve", "Đã đọc hồ sơ khai báo");
        Assert.Throws<WorkflowConflict>(() => lot.Update(Content()));
        Assert.Throws<WorkflowConflict>(() => lot.ImagesChanged());
        Assert.Throws<WorkflowConflict>(() => lot.Submit(1));
        Assert.Equal(3, lot.Version);
    }
    [Fact]
    public void RejectionCanBeEditedAndResubmittedWithoutChangingPriorSnapshot()
    {
        var lot = Lot.Create(Guid.NewGuid(), Content()); var first = lot.Submit(1);
        lot.Decide(first.Id, "Reject", "Cần sửa mô tả");
        lot.Update(Content() with { Title = "Lô đã sửa" }); var second = lot.Submit(1);
        Assert.Equal("Lô kiểm thử", first.Content.Title); Assert.Equal("Lô đã sửa", second.Content.Title);
        Assert.Equal(2, second.Number); Assert.NotEqual(first.Id, second.Id);
    }
    [Theory]
    [InlineData("Approve", " ")][InlineData("Reject", "")][InlineData("Unknown", "Lý do")]
    public void BadDecisionCannotChangeState(string outcome, string reason)
    {
        var lot = Lot.Create(Guid.NewGuid(), Content()); var revision = lot.Submit(1);
        Assert.Throws<RuleViolation>(() => lot.Decide(revision.Id, outcome, reason));
        Assert.Equal(LotStates.PendingReview, lot.State); Assert.Equal(2, lot.Version);
    }
    [Fact]
    public void StaleVersionOrWrongSubmissionCannotBeReviewed()
    {
        var lot = Lot.Create(Guid.NewGuid(), Content()); lot.Submit(1);
        Assert.Throws<WorkflowConflict>(() => lot.Expect(1));
        Assert.Throws<WorkflowConflict>(() => lot.Decide(Guid.NewGuid(), "Approve", "Nhận xét"));
    }
    [Theory]
    [InlineData("")][InlineData("  ")][InlineData("Tên\u0000ẩn")]
    public void ProfileRejectsInvalidName(string value) => Assert.Throws<RuleViolation>(() => ProfileRules.DisplayName(value));
    [Fact]
    public void ProfileNormalizesAndLimitsName()
    {
        Assert.Equal("Nông dân", ProfileRules.DisplayName(" Nông dân "));
        Assert.Throws<RuleViolation>(() => ProfileRules.DisplayName(new string('x', 121)));
    }
}
