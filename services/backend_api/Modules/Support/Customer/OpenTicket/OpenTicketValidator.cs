using BackendApi.Modules.Support.Primitives;

namespace BackendApi.Modules.Support.Customer.OpenTicket;

/// <summary>
/// FR-006 + FR-007 validation for open-ticket. Pure-function so it can run
/// before the handler hits the DB.
/// </summary>
public static class OpenTicketValidator
{
    public static (bool ok, string? reasonCode, string? detail) Validate(OpenTicketCommand cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd.Subject))
        {
            return (false, TicketReasonCode.SubjectRequired, "Subject is required.");
        }
        if (cmd.Subject.Length > 150)
        {
            return (false, TicketReasonCode.SubjectTooLong, "Subject exceeds 150 characters.");
        }
        if (string.IsNullOrWhiteSpace(cmd.Body))
        {
            return (false, TicketReasonCode.BodyRequired, "Body is required.");
        }
        if (cmd.Body.Length > 8000)
        {
            return (false, TicketReasonCode.BodyTooLong, "Body exceeds 8000 characters.");
        }
        if (string.IsNullOrWhiteSpace(cmd.Category))
        {
            return (false, TicketReasonCode.CategoryRequired, "Category is required.");
        }
        if (Array.IndexOf(TicketCategoryNames.All, cmd.Category) < 0)
        {
            return (false, TicketReasonCode.CategoryInvalid, $"Unknown category '{cmd.Category}'.");
        }
        if (Array.IndexOf(TicketPriorityNames.All, cmd.Priority) < 0)
        {
            return (false, TicketReasonCode.PriorityInvalid, $"Unknown priority '{cmd.Priority}'.");
        }
        // FR-006: customers may select only low|normal at creation.
        if (Array.IndexOf(TicketPriorityNames.CustomerSelectable, cmd.Priority) < 0)
        {
            return (false, TicketReasonCode.PriorityNotCustomerSelectable,
                "Priority must be one of: low, normal.");
        }
        if (cmd.Locale != "ar" && cmd.Locale != "en")
        {
            return (false, TicketReasonCode.LocaleInvalid, "Locale must be 'ar' or 'en'.");
        }

        // FR-007: linked-entity-kind must be consistent with category.
        var category = TicketCategoryNames.FromWire(cmd.Category);
        if (!TicketCategoryNames.IsConsistentWithLinkedKind(category, cmd.LinkedEntityKind))
        {
            return (false, TicketReasonCode.LinkedEntityKindInconsistent,
                $"Category '{cmd.Category}' is not consistent with linked entity kind '{cmd.LinkedEntityKind ?? "(none)"}'.");
        }

        // Linked-entity coupling: kind+id must both be present or both absent.
        if ((cmd.LinkedEntityKind is null) != (cmd.LinkedEntityId is null))
        {
            return (false, TicketReasonCode.LinkedEntityKindInconsistent,
                "linked_entity_kind and linked_entity_id must be both present or both absent.");
        }

        if (cmd.LinkedEntityKind is not null
            && Array.IndexOf(TicketLinkedEntityKindNames.All, cmd.LinkedEntityKind) < 0)
        {
            return (false, TicketReasonCode.LinkedEntityKindInconsistent,
                $"Unknown linked entity kind '{cmd.LinkedEntityKind}'.");
        }

        return (true, null, null);
    }
}
