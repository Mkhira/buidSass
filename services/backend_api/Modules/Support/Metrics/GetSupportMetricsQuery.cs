namespace BackendApi.Modules.Support.Metrics;

public sealed record GetSupportMetricsQuery(string? MarketCode);

public sealed record SupportMetricsRow(
    string MarketCode,
    int OpenTicketCount,
    int InProgressTicketCount,
    int WaitingCustomerTicketCount,
    int ResolvedTicketCount,
    int FirstResponseBreachCount,
    int ResolutionBreachCount,
    double? AverageFirstResponseMinutes,
    double? AverageResolutionMinutes);

public sealed record SupportMetricsResult(IReadOnlyList<SupportMetricsRow> Rows);
