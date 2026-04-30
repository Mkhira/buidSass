namespace BackendApi.Modules.Reviews.Primitives;

public enum ReviewActorKind
{
    Customer = 0,
    Moderator = 1,
    PolicyAdmin = 2,
    SuperAdmin = 3,
    System = 4,
}
