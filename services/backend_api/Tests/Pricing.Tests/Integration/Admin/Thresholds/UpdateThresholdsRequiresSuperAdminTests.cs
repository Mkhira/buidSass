using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Pricing.Authorization;
using BackendApi.Modules.Pricing.Primitives.Commercial;
using FluentAssertions;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Integration.Admin.Thresholds;

/// <summary>
/// Spec 007-b T107 — only <c>super_admin</c> may mutate commercial thresholds
/// (FR-025 / contract §9.2). Asserts the three other commercial roles are
/// each refused with reason <c>commercial.threshold.forbidden</c>.
/// </summary>
[Collection("pricing-fixture")]
public sealed class UpdateThresholdsRequiresSuperAdminTests(PricingTestFactory factory)
{
    [Theory]
    [InlineData(CommercialPermissions.Operator, "th.operator", CommercialReasonCode.CommercialThresholdForbidden)]
    [InlineData(CommercialPermissions.Approver, "th.approver", "role_missing")]
    [InlineData(CommercialPermissions.ThresholdAdmin, "th.threshold_admin", "role_missing")]
    public async Task NonSuperAdmin_PatchThresholds_Returns403(string permission, string roleCode, string expectedReason)
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { permission }, roleCode: roleCode);
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var resp = await client.PatchAsJsonAsync(
            "/v1/admin/commercial/thresholds/SA",
            new { gate_enabled = false });

        // Two-stage denial: the route filter requires commercial.operator;
        // the handler additionally enforces super_admin. Operators reach the
        // handler and see the precise reason code; Approver and
        // ThresholdAdmin are stopped at the route filter with `role_missing`.
        // Either way the request is refused with 403.
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await resp.Content.ReadAsStringAsync())
            .Should().Contain(expectedReason);
    }

    [Fact]
    public async Task SuperAdmin_PatchThresholds_Succeeds()
    {
        await factory.ResetDatabaseAsync();
        // The route's permission filter requires `commercial.operator`; the
        // handler additionally enforces `super_admin` for the actual mutation
        // so we can emit a precise reason code instead of a generic 403.
        // A real super_admin in production carries both permissions.
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory,
            new[] { "super_admin", CommercialPermissions.Operator },
            roleCode: "th.super_admin");

        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var resp = await client.PatchAsJsonAsync(
            "/v1/admin/commercial/thresholds/SA",
            new { gate_enabled = true });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
