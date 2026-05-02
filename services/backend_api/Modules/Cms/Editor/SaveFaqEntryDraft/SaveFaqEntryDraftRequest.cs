namespace BackendApi.Modules.Cms.Editor.SaveFaqEntryDraft;

public sealed record SaveFaqEntryDraftRequest(
    string Category,
    string? QuestionAr,
    string? QuestionEn,
    string? AnswerAr,
    string? AnswerEn,
    int? DisplayOrder,
    string MarketCode,
    DateTimeOffset? ScheduledPublishAtUtc,
    uint? Xmin);
