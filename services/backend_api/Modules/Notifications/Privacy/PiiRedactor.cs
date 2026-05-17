using System.Text.RegularExpressions;

namespace BackendApi.Modules.Notifications.Privacy;

/// <summary>
/// T051 — PII redaction for notification payloads (AC-27). Applied at the
/// payload-builder layer before a body is persisted to
/// <c>notifications.payload_redacted_json</c> and again at the
/// audit-emitter layer before any state is published to the audit-log.
///
/// Redaction rules:
/// - E.164 phone numbers (+&lt;cc&gt;&lt;digits&gt;) are masked to
///   <c>+&lt;cc&gt;****&lt;last-4&gt;</c>.
/// - Saudi national IDs (10 digits starting with 1 or 2) and Egyptian
///   national IDs (14 digits starting with 2 or 3) are stripped entirely.
/// - PAN-shaped strings (13-19 digits, optionally hyphen-separated) are
///   stripped entirely — SAQ-A surface is zero (Principle 13 + spec 027).
/// - CVV-shaped (3-4 standalone digits surrounded by clear context) is left
///   unmasked here because false-positives are too high; the egress
///   payload-filter at the provider layer catches them with stricter rules.
/// </summary>
public static partial class PiiRedactor
{
    [GeneratedRegex(@"\+(\d{1,3})(\d+)(\d{4})\b")]
    private static partial Regex PhoneE164();

    [GeneratedRegex(@"\b[12]\d{9}\b")]
    private static partial Regex SaNationalId();

    [GeneratedRegex(@"\b[23]\d{13}\b")]
    private static partial Regex EgNationalId();

    [GeneratedRegex(@"\b(?:\d[ -]?){13,19}\b")]
    private static partial Regex PanShaped();

    public static string Redact(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var s = PhoneE164().Replace(input, m =>
        {
            var cc = m.Groups[1].Value;
            var last4 = m.Groups[3].Value;
            return $"+{cc}****{last4}";
        });
        s = SaNationalId().Replace(s, "[redacted-id]");
        s = EgNationalId().Replace(s, "[redacted-id]");
        s = PanShaped().Replace(s, "[redacted-pan]");
        return s;
    }

    /// <summary>
    /// Masks a phone to <c>****&lt;last-4&gt;</c>. Used when only the last-4
    /// of a phone is wanted (audit copy on OTP / SMS notifications).
    /// </summary>
    public static string MaskPhoneToLast4(string phone)
    {
        if (string.IsNullOrEmpty(phone) || phone.Length < 4) return "****";
        return $"****{phone[^4..]}";
    }
}
