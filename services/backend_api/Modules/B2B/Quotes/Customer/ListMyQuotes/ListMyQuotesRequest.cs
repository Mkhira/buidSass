namespace BackendApi.Modules.B2B.Quotes.Customer.ListMyQuotes;

/// <summary>
/// Spec 021 contract §2.3 — query-string DTO for <c>GET /api/customer/quotes</c>.
/// Bound from the request URL, not from a JSON body.
///
/// Wire shape:
/// <code>
/// ?state=requested,revised        // optional CSV of QuoteState tokens
/// &amp;company_id=01HX...          // optional — narrow to one company
/// &amp;page=1                      // optional — 1-based; default 1
/// &amp;page_size=20                // optional — default 20, max 50
/// &amp;sort=newest|oldest          // optional — default 'newest'
/// </code>
///
/// Validation lives in <see cref="ListMyQuotesValidator"/>; visibility scoping
/// (caller's individual quotes + company quotes per the membership rules) lives
/// in <see cref="ListMyQuotesHandler"/>.
/// </summary>
public sealed record ListMyQuotesRequest(
    string? State,
    Guid? CompanyId,
    int? Page,
    int? PageSize,
    string? Sort);

/// <summary>
/// Sort token vocabulary mirrored from contract §2.3. Stable strings — clients switch
/// on these directly. Adding a new value is a breaking change to the wire contract.
/// </summary>
public static class ListMyQuotesSort
{
    public const string Newest = "newest";
    public const string Oldest = "oldest";

    public static bool IsValid(string token) => token is Newest or Oldest;
}
