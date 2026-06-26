namespace IotPocApi.Services;

public static class SimulationHelper
{
    private static readonly Dictionary<string, (decimal Min, decimal Max)> Ranges = new()
    {
        ["Temperature"] = (20m,  110m),
        ["Pressure"]    = (1m,    15m),
        ["Vibration"]   = (0.1m,  3.5m),
        ["Humidity"]    = (20m,   95m),
        ["RPM"]         = (0m,  3000m),
    };

    private static readonly string[] Statuses = ["Normal", "Warning", "Critical"];

    public static decimal RandomValue(string sensorType, Random rng)
    {
        if (!Ranges.TryGetValue(sensorType, out var range))
            return Math.Round((decimal)rng.NextDouble() * 100m, 2);

        var span = (double)(range.Max - range.Min);
        return Math.Round((decimal)(rng.NextDouble() * span) + range.Min, 2);
    }

    public static string RandomStatus(Random rng) => Statuses[rng.Next(Statuses.Length)];
}
