namespace WalleFences.Core;

public sealed record TemperatureSensor(string Name, float Value);
public static class CpuTemperatures
{
    // Prefer the package/control sensor; motherboard thermal zones are never passed here.
    public static TemperatureSensor? Primary(IEnumerable<TemperatureSensor> sensors)
    {
        var valid = sensors.Where(s => float.IsFinite(s.Value) && s.Value > 0 && s.Value <= 150).ToArray();
        return valid.OrderBy(s => s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ? 0 :
            s.Name.Contains("Tdie", StringComparison.OrdinalIgnoreCase) ? 1 : s.Name.Contains("Average", StringComparison.OrdinalIgnoreCase) ? 2 : 3)
            .ThenByDescending(s => s.Value).FirstOrDefault();
    }
}
