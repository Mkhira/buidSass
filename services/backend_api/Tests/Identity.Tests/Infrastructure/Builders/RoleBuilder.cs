using BackendApi.Modules.Identity.Entities;

namespace Identity.Tests.Infrastructure.Builders;

public sealed class RoleBuilder
{
    private readonly Role _role = new()
    {
        Id = Guid.NewGuid(),
        Code = "customer.standard",
        NameAr = "عميل",
        NameEn = "Customer",
        Scope = "market",
        System = true,
    };

    public RoleBuilder WithCode(string code)
    {
        _role.Code = code;
        return this;
    }

    public RoleBuilder WithScope(string scope)
    {
        _role.Scope = scope;
        return this;
    }

    public Role Build() => _role;
}
