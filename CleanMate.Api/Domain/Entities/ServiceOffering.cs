namespace CleanMate.Api.Domain.Entities;

public class ServiceOffering
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal HourlyRate { get; set; }
    public int MinHours { get; set; } = 1;
    public int MaxHours { get; set; } = 8;
    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;
    public DateTime? UpdatedAt { get; set; }
}
