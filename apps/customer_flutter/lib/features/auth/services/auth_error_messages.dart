import '../../../generated/l10n/app_localizations.dart';

/// Maps spec 004 reason codes to localized messages.
/// Unknown codes fall back to a generic message; never expose raw codes
/// to the user (FR-008).
String localizeAuthError(AppLocalizations l10n, String reasonCode) {
  switch (reasonCode) {
    case 'identity.invalid_credentials':
      return l10n.errorIdentityInvalidCredentials;
    case 'identity.mfa_required':
      return l10n.errorIdentityMfaRequired;
    case 'identity.mfa_invalid':
      return l10n.errorIdentityMfaInvalid;
    case 'identity.rate_limited':
      return l10n.errorIdentityRateLimited;
    case 'identity.account_locked':
      return l10n.errorIdentityAccountLocked;
    case 'identity.gap':
      return l10n.errorIdentityGap;
    default:
      return l10n.errorGeneric;
  }
}
