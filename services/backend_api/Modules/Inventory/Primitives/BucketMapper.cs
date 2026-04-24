namespace BackendApi.Modules.Inventory.Primitives;

public sealed class BucketMapper
{
    public string Map(int ats, bool hasFutureSupply = false)
    {
        if (ats > 0)
        {
            return "in_stock";
        }

        if (ats == 0 && hasFutureSupply)
        {
            return "backorder";
        }

        return "out_of_stock";
    }
}
