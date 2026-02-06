namespace KubiczPlace.Finances.Shared;

public class Worker
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal DefaultCommissionPercentage { get; set; }
}
