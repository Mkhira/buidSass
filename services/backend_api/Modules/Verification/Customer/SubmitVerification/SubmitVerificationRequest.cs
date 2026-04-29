namespace BackendApi.Modules.Verification.Customer.SubmitVerification;

/// <summary>
/// Customer's submission payload per spec 020 contracts §2.1. The submission row
/// is created in <see cref="VerificationDbContext"/> by
/// <see cref="SubmitVerificationHandler"/> after the validator confirms the per-
/// market schema's required-fields are satisfied and no other non-terminal
/// verification exists for the customer (or this is a renewal).
/// </summary>
/// <param name="Profession">e.g. "dentist", "dental_lab_tech", "dental_student", "clinic_buyer".</param>
/// <param name="RegulatorIdentifier">License / registration number (PII; never logged).</param>
/// <param name="DocumentIds">Attachment ids previously uploaded via the AttachDocument slice; must all have <c>scan_status='clean'</c>.</param>
/// <param name="SupersedesId">Set only on renewal — pointer to the prior approved verification (FR-020).</param>
public sealed record SubmitVerificationRequest(
    string Profession,
    string RegulatorIdentifier,
    IReadOnlyList<Guid> DocumentIds,
    Guid? SupersedesId);
