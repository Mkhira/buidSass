namespace BackendApi.Modules.Inventory.Primitives;

public sealed class AtsCalculator
{
    public int Compute(int onHand, int reserved, int safetyStock)
    {
        return onHand - reserved - safetyStock;
    }
}
