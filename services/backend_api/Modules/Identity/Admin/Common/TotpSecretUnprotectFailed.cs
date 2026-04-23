namespace BackendApi.Modules.Identity.Admin.Common;

public sealed class TotpSecretUnprotectFailed(string message, Exception? inner = null) : Exception(message, inner);
