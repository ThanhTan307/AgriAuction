using AgriAuction.Api.Configuration;
using Xunit;

namespace AgriAuction.IntegrationTests;

public sealed class GvhdDemoGuardTests
{
    private const string LocalDemo = "Host=127.0.0.1;Port=55432;Database=agri_gvhd_demo;Username=agri_gvhd_demo";
    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void DedicatedTargetIsAccepted(string environment) => GvhdDemoBootstrap.ValidateTarget(environment, LocalDemo, "agri_demo");

    [Theory]
    [InlineData("Production", LocalDemo, "agri_demo")]
    [InlineData("Staging", LocalDemo, "agri_demo")]
    [InlineData("Development", "Host=127.0.0.1;Port=55432;Database=agri_phase1_test;Username=agri_gvhd_demo", "agri_demo")]
    [InlineData("Development", "Host=127.0.0.1;Port=5432;Database=agri_gvhd_demo;Username=agri_gvhd_demo", "agri_demo")]
    [InlineData("Development", "Host=remote.invalid;Port=55432;Database=agri_gvhd_demo;Username=agri_gvhd_demo", "agri_demo")]
    [InlineData("Development", "Host=127.0.0.1;Port=55432;Database=agri_gvhd_demo;Username=agri_test", "agri_demo")]
    [InlineData("Development", LocalDemo, "public")]
    public void OtherTargetsAreRejectedBeforeConnection(string environment, string connection, string schema) =>
        Assert.Throws<InvalidOperationException>(() => GvhdDemoBootstrap.ValidateTarget(environment, connection, schema));
}
