namespace IotPocApi.Models;

public record SensorReading(
    int Id,
    string SensorId,
    string MachineName,
    string Zone,
    string SensorType,
    decimal Value,
    string Unit,
    string Status,
    DateTime LastUpdated
);
