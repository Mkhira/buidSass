namespace BackendApi.Modules.Support.Customer.ReplyAsCustomer;

public sealed record ReplyAsCustomerCommand(
    Guid TicketId,
    Guid CustomerId,
    string Body);

public sealed record ReplyAsCustomerResult(
    bool Success,
    Guid? MessageId,
    string? NewState,
    string? ReasonCode,
    string? Detail);
