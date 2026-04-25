using System.Text.RegularExpressions;

namespace BackendApi.Modules.Checkout.Customer.Common;

public sealed record AddressDto(
    string FullName,
    string PhoneE164,
    string Line1,
    string? Line2,
    string City,
    string? Region,
    string? PostalCode,
    string CountryCode)
{
    // E.164: leading '+', then 8–15 digits. Strict format match — anything else is rejected
    // here so malformed numbers don't reach the shipping/payment layer (CR review on PR #30).
    private static readonly Regex E164Regex = new(@"^\+[1-9]\d{7,14}$", RegexOptions.Compiled);
    // ISO 3166-1 alpha-2 country code: two uppercase letters.
    private static readonly Regex CountryCodeRegex = new(@"^[A-Z]{2}$", RegexOptions.Compiled);

    public bool IsMinimallyValid() =>
        !string.IsNullOrWhiteSpace(FullName)
        && !string.IsNullOrWhiteSpace(Line1)
        && !string.IsNullOrWhiteSpace(City)
        && !string.IsNullOrWhiteSpace(PhoneE164)
        && E164Regex.IsMatch(PhoneE164)
        && !string.IsNullOrWhiteSpace(CountryCode)
        && CountryCodeRegex.IsMatch(CountryCode);
}
