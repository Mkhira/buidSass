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
    public bool IsMinimallyValid() =>
        !string.IsNullOrWhiteSpace(FullName)
        && !string.IsNullOrWhiteSpace(PhoneE164)
        && !string.IsNullOrWhiteSpace(Line1)
        && !string.IsNullOrWhiteSpace(City)
        && !string.IsNullOrWhiteSpace(CountryCode);
}
