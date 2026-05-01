using BackendApi.Modules.Reviews.Admin.AddAdminNote;
using BackendApi.Modules.Reviews.Admin.DecideModeration;
using BackendApi.Modules.Reviews.Admin.DeleteForbidden;
using BackendApi.Modules.Reviews.Admin.GetReviewDetail;
using BackendApi.Modules.Reviews.Admin.ListAdminNotes;
using BackendApi.Modules.Reviews.Admin.ListModerationQueue;
using BackendApi.Modules.Reviews.Admin.ListReviewsByCustomer;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Reviews;

/// <summary>
/// Companion partial implementations for the US4 admin moderation surface
/// (Phase 6). Wires the queue + detail + decide + notes + by-customer slices,
/// plus the hard-delete-forbidden 405 shim per FR-005a.
/// </summary>
public static partial class ReviewsModule
{
    static partial void AddUs4Slices(IServiceCollection services)
    {
        services.AddScoped<ListModerationQueueHandler>();
        services.AddScoped<GetReviewDetailHandler>();
        services.AddScoped<DecideModerationHandler>();
        services.AddScoped<AddAdminNoteHandler>();
        services.AddScoped<ListAdminNotesHandler>();
        services.AddScoped<ListReviewsByCustomerHandler>();
    }

    static partial void MapUs4AdminEndpoints(IEndpointRouteBuilder admin)
    {
        admin.MapListModerationQueueEndpoint();
        admin.MapGetReviewDetailEndpoint();
        admin.MapDecideModerationEndpoint();
        admin.MapAddAdminNoteEndpoint();
        admin.MapListAdminNotesEndpoint();
        admin.MapListReviewsByCustomerEndpoint();
        admin.MapDeleteForbiddenEndpoint();
    }
}
