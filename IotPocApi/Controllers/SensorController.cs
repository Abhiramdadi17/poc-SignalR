using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using IotPocApi.Models;
using IotPocApi.Services;

namespace IotPocApi.Controllers;

[ApiController]
[Route("api/sensor")]
public class SensorController : ControllerBase
{
    private const string SelectQuery =
        "SELECT Id, SensorId, MachineName, Zone, SensorType, Value, Unit, Status, LastUpdated " +
        "FROM dbo.SensorReadings";

    private readonly string _connectionString;

    public SensorController(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Default")!;
    }

    [HttpGet]
    public IActionResult GetAll()
    {
        var readings = new List<SensorReading>();
        using var connection = new SqlConnection(_connectionString);
        using var command = new SqlCommand(SelectQuery, connection);
        connection.Open();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            readings.Add(new SensorReading(
                Id:          reader.GetInt32(0),
                SensorId:    reader.GetString(1),
                MachineName: reader.GetString(2),
                Zone:        reader.GetString(3),
                SensorType:  reader.GetString(4),
                Value:       reader.GetDecimal(5),
                Unit:        reader.GetString(6),
                Status:      reader.GetString(7),
                LastUpdated: reader.GetDateTime(8)
            ));
        }
        return Ok(readings);
    }

    [HttpPost("simulate")]
    public IActionResult Simulate()
    {
        // Load all sensor IDs + types to pick from
        var all = new List<(string SensorId, string SensorType)>();
        using (var connection = new SqlConnection(_connectionString))
        {
            using var command = new SqlCommand(
                "SELECT SensorId, SensorType FROM dbo.SensorReadings", connection);
            connection.Open();
            using var reader = command.ExecuteReader();
            while (reader.Read())
                all.Add((reader.GetString(0), reader.GetString(1)));
        }

        var rng = new Random();
        var picks = all.OrderBy(_ => rng.Next()).Take(3).ToList();

        using var updateConn = new SqlConnection(_connectionString);
        updateConn.Open();
        foreach (var (sensorId, sensorType) in picks)
        {
            var newValue  = SimulationHelper.RandomValue(sensorType, rng);
            var newStatus = SimulationHelper.RandomStatus(rng);
            using var cmd = new SqlCommand(
                "UPDATE dbo.SensorReadings SET Value=@v, Status=@s, LastUpdated=GETDATE() WHERE SensorId=@id",
                updateConn);
            cmd.Parameters.AddWithValue("@v",  newValue);
            cmd.Parameters.AddWithValue("@s",  newStatus);
            cmd.Parameters.AddWithValue("@id", sensorId);
            cmd.ExecuteNonQuery();
        }

        // HTTP response just confirms the write. Actual UI update arrives via SignalR.
        return Ok();
    }
}
