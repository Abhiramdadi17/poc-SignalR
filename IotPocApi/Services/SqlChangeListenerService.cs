using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using IotPocApi.Hubs;
using IotPocApi.Models;

namespace IotPocApi.Services;

public class SqlChangeListenerService : IHostedService
{
    // Must be explicit columns + two-part table name.
    // SELECT * or missing dbo. prefix causes e.Info = Invalid and silently kills the listener.
    private const string SelectQuery =
        "SELECT Id, SensorId, MachineName, Zone, SensorType, Value, Unit, Status, LastUpdated " +
        "FROM dbo.SensorReadings";

    private readonly string _connectionString;
    private readonly IHubContext<SensorHub> _hubContext;
    private readonly ILogger<SqlChangeListenerService> _logger;

    public SqlChangeListenerService(
        IConfiguration configuration,
        IHubContext<SensorHub> hubContext,
        ILogger<SqlChangeListenerService> logger)
    {
        _connectionString = configuration.GetConnectionString("Default")!;
        _hubContext = hubContext;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        SqlDependency.Start(_connectionString);
        RegisterDependency();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        SqlDependency.Stop(_connectionString);
        return Task.CompletedTask;
    }

    private void RegisterDependency()
    {
        using var connection = new SqlConnection(_connectionString);
        using var command = new SqlCommand(SelectQuery, connection);
        var dependency = new SqlDependency(command);
        dependency.OnChange += OnDataChanged;
        connection.Open();
        using var reader = command.ExecuteReader();
        // Reader and connection dispose here. The subscription lives in SQL Server
        // via Service Broker — the connection does not need to stay open.
    }

    private void OnDataChanged(object sender, SqlNotificationEventArgs e)
    {
        // Push only on real data changes; skip Subscribe/Error housekeeping fires.
        if (e.Type == SqlNotificationType.Change)
        {
            var readings = FetchAll();
            var pushTask = _hubContext.Clients.All.SendAsync("SensorUpdated", readings);
            _ = pushTask.ContinueWith(
                t => _logger.LogError(t.Exception, "SignalR push to clients failed"),
                TaskContinuationOptions.OnlyOnFaulted);
        }
        else
        {
            _logger.LogWarning(
                "SqlDependency non-change notification: Type={Type} Info={Info} Source={Source}",
                e.Type, e.Info, e.Source);
        }

        // Re-register unconditionally — SqlDependency is one-shot; every notification
        // (change or housekeeping) requires a fresh registration or the listener dies.
        try
        {
            RegisterDependency();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to re-register SqlDependency; change listener is inactive");
        }
    }

    private List<SensorReading> FetchAll()
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
        return readings;
    }
}
