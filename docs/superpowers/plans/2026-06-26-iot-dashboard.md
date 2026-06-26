# IoT Real-Time Dashboard — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a full-stack POC proving SQL Server Service Broker → SqlDependency → SignalR → browser is a true event-driven push pipeline with zero polling.

**Architecture:** LocalDB stores 20 seeded sensor rows. A .NET 8 `IHostedService` registers a `SqlDependency` on `dbo.SensorReadings`; on change it re-queries all rows and pushes the full array to all SignalR clients in one call. A plain HTML page renders 20 sensor cards, connects via SignalR WebSocket, and updates cards in-place when `SensorUpdated` fires.

**Tech Stack:** SQL Server LocalDB · .NET 8 Web API · Microsoft.Data.SqlClient · SignalR (built-in) · xUnit · Plain HTML + JavaScript (SignalR CDN)

## Global Constraints

- Target framework: `net8.0`
- DB instance: `(localdb)\MSSQLLocalDB` · database: `IotPoc`
- Connection string key: `"Default"` under `ConnectionStrings` in `appsettings.json`
- API port: `http://localhost:5000` (HTTP only — avoids certificate issues from `file://` origin)
- All endpoints singular: `/api/sensor` (not `/api/sensors`)
- SqlDependency registration query: explicit column list + `dbo.SensorReadings` (two-part name), no `SELECT *`, no `ORDER BY`, no `JOIN`, no subquery, no `TOP`
- CORS: `SetIsOriginAllowed(_ => true)` + `AllowCredentials()` — required for SignalR from `file://` origin
- Frontend: single `frontend/index.html`, no build step, SignalR via `https://unpkg.com/@microsoft/signalr@8.0.0/dist/browser/signalr.min.js`
- Re-registration invariant: `RegisterDependency()` is always called unconditionally at the end of `OnDataChanged`, regardless of `e.Type`; the SignalR push is conditional on `e.Type == SqlNotificationType.Change`

---

## File Map

| File | Responsibility |
|------|---------------|
| `sql/setup.sql` | DB + table DDL, seed data, Service Broker enable |
| `IotPocApi/Models/SensorReading.cs` | C# record matching DB columns |
| `IotPocApi/Services/SimulationHelper.cs` | Pure static helpers: `RandomValue`, `RandomStatus` |
| `IotPocApi/Hubs/SensorHub.cs` | Empty SignalR hub (server→client push only) |
| `IotPocApi/Services/SqlChangeListenerService.cs` | `IHostedService`; owns SqlDependency lifecycle |
| `IotPocApi/Controllers/SensorController.cs` | `GET /api/sensor` + `POST /api/sensor/simulate` |
| `IotPocApi/Program.cs` | DI wiring, CORS, SignalR, hub route, Kestrel port |
| `IotPocApi/appsettings.json` | Connection string + Kestrel endpoint config |
| `IotPocApi.Tests/SimulationHelperTests.cs` | Unit tests for randomization ranges and status set |
| `frontend/index.html` | Single-file dashboard; SignalR client; connection indicator |

---

## Task 1: SQL Database Setup

**Files:**
- Create: `sql/setup.sql`

**Interfaces:**
- Produces: `IotPoc` database on `(localdb)\MSSQLLocalDB` with `dbo.SensorReadings` (20 rows), Service Broker enabled

- [ ] **Step 1: Create the SQL script file**

Create `d:\poc\sql\setup.sql` with this exact content:

```sql
-- Run against (localdb)\MSSQLLocalDB in SSMS or Azure Data Studio

CREATE DATABASE IotPoc;
GO

USE IotPoc;
GO

CREATE TABLE SensorReadings (
    Id            INT IDENTITY(1,1) PRIMARY KEY,
    SensorId      NVARCHAR(20)   NOT NULL,
    MachineName   NVARCHAR(100)  NOT NULL,
    Zone          NVARCHAR(50)   NOT NULL,
    SensorType    NVARCHAR(50)   NOT NULL,
    Value         DECIMAL(10,2)  NOT NULL,
    Unit          NVARCHAR(20)   NOT NULL,
    Status        NVARCHAR(20)   NOT NULL DEFAULT 'Normal',
    LastUpdated   DATETIME2      NOT NULL DEFAULT GETDATE()
);
GO

INSERT INTO SensorReadings (SensorId, MachineName, Zone, SensorType, Value, Unit, Status) VALUES
('SNS-001', 'Boiler Unit A',        'Zone 1 - Heat',       'Temperature', 72.4,  '°C',  'Normal'),
('SNS-002', 'Boiler Unit A',        'Zone 1 - Heat',       'Pressure',    3.8,   'bar', 'Normal'),
('SNS-003', 'Boiler Unit B',        'Zone 1 - Heat',       'Temperature', 94.1,  '°C',  'Warning'),
('SNS-004', 'Boiler Unit B',        'Zone 1 - Heat',       'Pressure',    5.2,   'bar', 'Critical'),
('SNS-005', 'Conveyor Belt 1',      'Zone 2 - Assembly',   'Vibration',   0.32,  'g',   'Normal'),
('SNS-006', 'Conveyor Belt 1',      'Zone 2 - Assembly',   'RPM',         1450,  'RPM', 'Normal'),
('SNS-007', 'Conveyor Belt 2',      'Zone 2 - Assembly',   'Vibration',   1.87,  'g',   'Warning'),
('SNS-008', 'Conveyor Belt 2',      'Zone 2 - Assembly',   'RPM',         980,   'RPM', 'Warning'),
('SNS-009', 'Compressor Alpha',     'Zone 3 - Pneumatics', 'Pressure',    8.9,   'bar', 'Normal'),
('SNS-010', 'Compressor Alpha',     'Zone 3 - Pneumatics', 'Temperature', 58.2,  '°C',  'Normal'),
('SNS-011', 'Compressor Beta',      'Zone 3 - Pneumatics', 'Pressure',    11.4,  'bar', 'Critical'),
('SNS-012', 'Compressor Beta',      'Zone 3 - Pneumatics', 'Vibration',   2.91,  'g',   'Critical'),
('SNS-013', 'Cooling Tower 1',      'Zone 4 - Cooling',    'Temperature', 28.7,  '°C',  'Normal'),
('SNS-014', 'Cooling Tower 1',      'Zone 4 - Cooling',    'Humidity',    64.0,  '%RH', 'Normal'),
('SNS-015', 'Cooling Tower 2',      'Zone 4 - Cooling',    'Temperature', 41.3,  '°C',  'Warning'),
('SNS-016', 'Cooling Tower 2',      'Zone 4 - Cooling',    'Humidity',    88.5,  '%RH', 'Warning'),
('SNS-017', 'Packaging Machine 1',  'Zone 5 - Packaging',  'RPM',         2200,  'RPM', 'Normal'),
('SNS-018', 'Packaging Machine 1',  'Zone 5 - Packaging',  'Vibration',   0.15,  'g',   'Normal'),
('SNS-019', 'Packaging Machine 2',  'Zone 5 - Packaging',  'RPM',         0,     'RPM', 'Offline'),
('SNS-020', 'Packaging Machine 2',  'Zone 5 - Packaging',  'Temperature', 22.1,  '°C',  'Offline');
GO

ALTER DATABASE IotPoc SET ENABLE_BROKER WITH ROLLBACK IMMEDIATE;
GO
```

- [ ] **Step 2: Run the script**

Open SSMS or Azure Data Studio. Connect to `(localdb)\MSSQLLocalDB`. Open `setup.sql` and execute (F5). All statements should succeed with no errors.

- [ ] **Step 3: Verify 20 rows loaded**

```sql
USE IotPoc;
SELECT COUNT(*) FROM dbo.SensorReadings;  -- expect: 20
```

- [ ] **Step 4: Verify Service Broker is enabled**

```sql
SELECT name, is_broker_enabled FROM sys.databases WHERE name = 'IotPoc';
-- expect: is_broker_enabled = 1
```

---

## Task 2: .NET Project Scaffold

**Files:**
- Create: `IotPocApi/` (via dotnet CLI)
- Create: `IotPocApi.Tests/` (via dotnet CLI)
- Modify: `IotPocApi/appsettings.json`
- Delete: `IotPocApi/WeatherForecast.cs`, `IotPocApi/Controllers/WeatherForecastController.cs`

**Interfaces:**
- Produces: buildable solution with connection string wired, test project referencing main project

- [ ] **Step 1: Scaffold the Web API project**

Run from `d:\poc`:

```powershell
dotnet new webapi -n IotPocApi -o IotPocApi --use-controllers
```

If `--use-controllers` is not recognised (older SDK), use: `dotnet new webapi -n IotPocApi -o IotPocApi`

- [ ] **Step 2: Add Microsoft.Data.SqlClient**

```powershell
dotnet add IotPocApi/IotPocApi.csproj package Microsoft.Data.SqlClient
```

- [ ] **Step 3: Scaffold the test project and link it**

```powershell
dotnet new xunit -n IotPocApi.Tests -o IotPocApi.Tests
dotnet add IotPocApi.Tests/IotPocApi.Tests.csproj reference IotPocApi/IotPocApi.csproj
```

- [ ] **Step 4: Delete boilerplate files**

```powershell
Remove-Item IotPocApi/WeatherForecast.cs -ErrorAction SilentlyContinue
Remove-Item IotPocApi/Controllers/WeatherForecastController.cs -ErrorAction SilentlyContinue
```

- [ ] **Step 5: Replace appsettings.json**

Overwrite `IotPocApi/appsettings.json` with:

```json
{
  "ConnectionStrings": {
    "Default": "Server=(localdb)\\MSSQLLocalDB;Database=IotPoc;Trusted_Connection=True;TrustServerCertificate=True;"
  },
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://localhost:5000"
      }
    }
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

> Note: `\\` in JSON is a single backslash in the actual string — the connection string becomes `Server=(localdb)\MSSQLLocalDB;...`

- [ ] **Step 6: Verify build**

```powershell
dotnet build IotPocApi/IotPocApi.csproj
```

Expected: `Build succeeded. 0 Error(s)`

---

## Task 3: SensorReading Model + SimulationHelper (TDD)

**Files:**
- Create: `IotPocApi/Models/SensorReading.cs`
- Create: `IotPocApi/Services/SimulationHelper.cs`
- Create: `IotPocApi.Tests/SimulationHelperTests.cs`

**Interfaces:**
- Produces:
  - `SensorReading(int Id, string SensorId, string MachineName, string Zone, string SensorType, decimal Value, string Unit, string Status, DateTime LastUpdated)` — positional record
  - `SimulationHelper.RandomValue(string sensorType, Random rng) → decimal` — value in type-appropriate range, rounded to 2dp
  - `SimulationHelper.RandomStatus(Random rng) → string` — one of `"Normal"`, `"Warning"`, `"Critical"`

- [ ] **Step 1: Write the failing tests**

Create `IotPocApi.Tests/SimulationHelperTests.cs`:

```csharp
using IotPocApi.Services;

namespace IotPocApi.Tests;

public class SimulationHelperTests
{
    [Theory]
    [InlineData("Temperature", 20.0,  110.0)]
    [InlineData("Pressure",     1.0,   15.0)]
    [InlineData("Vibration",    0.1,    3.5)]
    [InlineData("Humidity",    20.0,   95.0)]
    [InlineData("RPM",          0.0, 3000.0)]
    public void RandomValue_ReturnsValueInRange(string sensorType, double min, double max)
    {
        var rng = new Random(42);
        for (int i = 0; i < 200; i++)
        {
            var value = SimulationHelper.RandomValue(sensorType, rng);
            Assert.True(value >= (decimal)min, $"{sensorType}: {value} < {min}");
            Assert.True(value <= (decimal)max, $"{sensorType}: {value} > {max}");
        }
    }

    [Fact]
    public void RandomValue_UnknownType_ReturnsBetween0And100()
    {
        var rng = new Random(42);
        for (int i = 0; i < 200; i++)
        {
            var value = SimulationHelper.RandomValue("Unknown", rng);
            Assert.True(value >= 0m && value <= 100m, $"value {value} out of fallback range");
        }
    }

    [Fact]
    public void RandomStatus_ReturnsKnownStatus()
    {
        var rng = new Random(42);
        var valid = new[] { "Normal", "Warning", "Critical" };
        for (int i = 0; i < 200; i++)
        {
            var status = SimulationHelper.RandomStatus(rng);
            Assert.Contains(status, valid);
        }
    }

    [Fact]
    public void RandomValue_IsTwoDecimalPlaces()
    {
        var rng = new Random(99);
        for (int i = 0; i < 100; i++)
        {
            var value = SimulationHelper.RandomValue("Temperature", rng);
            Assert.Equal(value, Math.Round(value, 2));
        }
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure**

```powershell
dotnet test IotPocApi.Tests/IotPocApi.Tests.csproj
```

Expected: compile error — `SimulationHelper` does not exist yet.

- [ ] **Step 3: Create the SensorReading model**

Create `IotPocApi/Models/SensorReading.cs`:

```csharp
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
```

- [ ] **Step 4: Implement SimulationHelper**

Create `IotPocApi/Services/SimulationHelper.cs`:

```csharp
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
```

- [ ] **Step 5: Run tests — expect all pass**

```powershell
dotnet test IotPocApi.Tests/IotPocApi.Tests.csproj --verbosity normal
```

Expected output:
```
Passed! - Failed: 0, Passed: 8, Skipped: 0
```

(5 `[InlineData]` cases on `RandomValue_ReturnsValueInRange` + 3 `[Fact]` methods = 8 test methods total)

- [ ] **Step 6: Commit**

```powershell
git -C d:\poc init
git -C d:\poc add IotPocApi/Models/SensorReading.cs IotPocApi/Services/SimulationHelper.cs IotPocApi.Tests/SimulationHelperTests.cs
git -C d:\poc commit -m "feat: add SensorReading model and SimulationHelper with tests"
```

---

## Task 4: SensorHub

**Files:**
- Create: `IotPocApi/Hubs/SensorHub.cs`

**Interfaces:**
- Produces: `SensorHub` class in `IotPocApi.Hubs` namespace — used by `IHubContext<SensorHub>` in Task 5 and `MapHub<SensorHub>` in Task 6

- [ ] **Step 1: Create the hub**

Create `IotPocApi/Hubs/SensorHub.cs`:

```csharp
using Microsoft.AspNetCore.SignalR;

namespace IotPocApi.Hubs;

public class SensorHub : Hub { }
```

The hub has no server-side methods. All messages flow server → client via `IHubContext<SensorHub>`.

- [ ] **Step 2: Verify build**

```powershell
dotnet build IotPocApi/IotPocApi.csproj
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```powershell
git -C d:\poc add IotPocApi/Hubs/SensorHub.cs
git -C d:\poc commit -m "feat: add SensorHub"
```

---

## Task 5: SqlChangeListenerService

**Files:**
- Create: `IotPocApi/Services/SqlChangeListenerService.cs`

**Interfaces:**
- Consumes:
  - `IConfiguration` → connection string at `ConnectionStrings:Default`
  - `IHubContext<SensorHub>` from `IotPocApi.Hubs`
  - `SensorReading` from `IotPocApi.Models`
- Produces: `SqlChangeListenerService` in `IotPocApi.Services` — registered as `IHostedService` in Task 6

- [ ] **Step 1: Create the service**

Create `IotPocApi/Services/SqlChangeListenerService.cs`:

```csharp
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
            _ = _hubContext.Clients.All.SendAsync("SensorUpdated", readings);
        }
        else
        {
            _logger.LogWarning(
                "SqlDependency non-change notification: Type={Type} Info={Info} Source={Source}",
                e.Type, e.Info, e.Source);
        }

        // Re-register unconditionally — SqlDependency is one-shot; every notification
        // (change or housekeeping) requires a fresh registration or the listener dies.
        RegisterDependency();
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
```

- [ ] **Step 2: Verify build**

```powershell
dotnet build IotPocApi/IotPocApi.csproj
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```powershell
git -C d:\poc add IotPocApi/Services/SqlChangeListenerService.cs
git -C d:\poc commit -m "feat: add SqlChangeListenerService with SqlDependency lifecycle"
```

---

## Task 6: SensorController + Program.cs

**Files:**
- Create: `IotPocApi/Controllers/SensorController.cs`
- Overwrite: `IotPocApi/Program.cs`

**Interfaces:**
- Consumes:
  - `SimulationHelper.RandomValue` and `SimulationHelper.RandomStatus` from Task 3
  - `SensorReading` from Task 3
  - `SensorHub` from Task 4
  - `SqlChangeListenerService` from Task 5
- Produces:
  - `GET http://localhost:5000/api/sensor` → JSON array of 20 `SensorReading` objects (camelCase)
  - `POST http://localhost:5000/api/sensor/simulate` → HTTP 200 (triggers SqlDependency via DB write)
  - `WS http://localhost:5000/hubs/sensor` → SignalR hub

- [ ] **Step 1: Create SensorController**

Create `IotPocApi/Controllers/SensorController.cs`:

```csharp
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
```

- [ ] **Step 2: Write Program.cs**

Overwrite `IotPocApi/Program.cs`:

```csharp
using IotPocApi.Hubs;
using IotPocApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSignalR();

// SetIsOriginAllowed(_ => true) + AllowCredentials() is required for SignalR
// from a file:// origin (browser sends Origin: null, which AllowAnyOrigin() rejects).
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials()));

builder.Services.AddHostedService<SqlChangeListenerService>();

var app = builder.Build();

app.UseCors();
app.MapControllers();
app.MapHub<SensorHub>("/hubs/sensor");

app.Run();
```

- [ ] **Step 3: Build**

```powershell
dotnet build IotPocApi/IotPocApi.csproj
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Run the API**

```powershell
dotnet run --project IotPocApi/IotPocApi.csproj
```

Expected console output includes:
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5000
```

Leave it running. Open a second terminal for the next steps.

- [ ] **Step 5: Verify GET /api/sensor returns 20 rows**

```powershell
(Invoke-WebRequest -Uri "http://localhost:5000/api/sensor").Content | ConvertFrom-Json | Measure-Object | Select-Object -ExpandProperty Count
```

Expected: `20`

- [ ] **Step 6: Verify POST /api/sensor/simulate returns 200**

```powershell
Invoke-WebRequest -Uri "http://localhost:5000/api/sensor/simulate" -Method POST
```

Expected: `StatusCode: 200`

- [ ] **Step 7: Stop the API (Ctrl+C) and commit**

```powershell
git -C d:\poc add IotPocApi/Controllers/SensorController.cs IotPocApi/Program.cs
git -C d:\poc commit -m "feat: add SensorController and wire up Program.cs"
```

---

## Task 7: Frontend

**Files:**
- Create: `frontend/index.html`

**Interfaces:**
- Consumes:
  - `GET http://localhost:5000/api/sensor` → array of `{ id, sensorId, machineName, zone, sensorType, value, unit, status, lastUpdated }` (camelCase — .NET default JSON serialization)
  - `POST http://localhost:5000/api/sensor/simulate` → 200 OK
  - SignalR event `"SensorUpdated"` → same array shape as GET response
- Produces: visual dashboard; demo-able in any browser opened from `file://`

- [ ] **Step 1: Create the frontend directory and index.html**

Create `d:\poc\frontend\index.html`:

```html
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>IoT Sensor Dashboard</title>
<style>
  *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
  body { font-family: system-ui, sans-serif; background: #0f172a; color: #e2e8f0; min-height: 100vh; }

  header {
    display: flex; align-items: center; gap: 16px; padding: 16px 24px;
    background: #1e293b; border-bottom: 1px solid #334155;
  }
  header h1 { font-size: 1.25rem; font-weight: 600; flex: 1; }

  .pill {
    padding: 4px 12px; border-radius: 9999px; font-size: 0.75rem; font-weight: 500;
    white-space: nowrap;
  }
  .pill.connected    { background: #166534; color: #bbf7d0; }
  .pill.reconnecting { background: #854d0e; color: #fef08a; }
  .pill.disconnected { background: #7f1d1d; color: #fecaca; }

  #simulate-btn {
    padding: 8px 18px; border-radius: 6px; border: none; cursor: pointer;
    background: #3b82f6; color: white; font-size: 0.875rem; font-weight: 500;
  }
  #simulate-btn:hover { background: #2563eb; }

  #summary-bar {
    display: flex; gap: 24px; padding: 12px 24px;
    background: #1e293b; border-bottom: 1px solid #334155; font-size: 0.875rem;
  }
  .summary-item { display: flex; align-items: center; gap: 6px; }
  .dot { width: 10px; height: 10px; border-radius: 50%; flex-shrink: 0; }
  .dot-normal   { background: #22c55e; }
  .dot-warning  { background: #f59e0b; }
  .dot-critical { background: #ef4444; }
  .dot-offline  { background: #6b7280; }

  #sensor-grid { padding: 24px; }

  .zone-section { margin-bottom: 32px; }
  .zone-section h2 {
    font-size: 0.75rem; font-weight: 600; text-transform: uppercase;
    letter-spacing: 0.08em; color: #64748b; margin-bottom: 12px;
  }
  .cards-row { display: flex; flex-wrap: wrap; gap: 12px; }

  .sensor-card {
    background: #1e293b; border: 1px solid #334155; border-radius: 8px;
    padding: 14px 16px; min-width: 185px; flex: 0 0 auto;
    transition: border-color 0.15s ease;
  }
  .sensor-card.flash { border-color: #3b82f6; }

  .sensor-id { font-size: 0.68rem; color: #475569; font-family: monospace; }
  .machine   { font-size: 0.875rem; font-weight: 600; margin: 3px 0 4px; }
  .type      { font-size: 0.72rem; color: #94a3b8; margin-bottom: 10px; }
  .val       { font-size: 1.3rem; font-weight: 700; color: #f1f5f9; margin-bottom: 8px; }

  .badge {
    display: inline-block; padding: 2px 8px; border-radius: 4px;
    font-size: 0.68rem; font-weight: 700; text-transform: uppercase; letter-spacing: 0.06em;
  }
  .badge-normal   { background: #14532d; color: #86efac; }
  .badge-warning  { background: #78350f; color: #fcd34d; }
  .badge-critical { background: #7f1d1d; color: #fca5a5; }
  .badge-offline  { background: #1f2937; color: #9ca3af; }
</style>
</head>
<body>

<header>
  <h1>IoT Sensor Dashboard</h1>
  <span id="conn-pill" class="pill disconnected">Connecting…</span>
  <button id="simulate-btn">Simulate Changes</button>
</header>

<div id="summary-bar">
  <div class="summary-item"><div class="dot dot-normal"></div>Normal: <strong id="cnt-normal">–</strong></div>
  <div class="summary-item"><div class="dot dot-warning"></div>Warning: <strong id="cnt-warning">–</strong></div>
  <div class="summary-item"><div class="dot dot-critical"></div>Critical: <strong id="cnt-critical">–</strong></div>
  <div class="summary-item"><div class="dot dot-offline"></div>Offline: <strong id="cnt-offline">–</strong></div>
</div>

<div id="sensor-grid"></div>

<script src="https://unpkg.com/@microsoft/signalr@8.0.0/dist/browser/signalr.min.js"></script>
<script>
const API = 'http://localhost:5000';

// --- Connection indicator ---
const pill = document.getElementById('conn-pill');
function setConn(state) {
  const map = {
    connected:    { cls: 'connected',    text: '🟢 Connected' },
    reconnecting: { cls: 'reconnecting', text: '🟡 Reconnecting…' },
    disconnected: { cls: 'disconnected', text: '🔴 Disconnected — refresh to retry' },
  };
  const m = map[state] ?? map.disconnected;
  pill.className = `pill ${m.cls}`;
  pill.textContent = m.text;
}

// --- Summary bar ---
function updateSummary(readings) {
  const counts = { Normal: 0, Warning: 0, Critical: 0, Offline: 0 };
  readings.forEach(r => { counts[r.status] = (counts[r.status] ?? 0) + 1; });
  document.getElementById('cnt-normal').textContent   = counts.Normal;
  document.getElementById('cnt-warning').textContent  = counts.Warning;
  document.getElementById('cnt-critical').textContent = counts.Critical;
  document.getElementById('cnt-offline').textContent  = counts.Offline;
}

// --- Card helpers ---
function badgeCls(status) { return 'badge badge-' + status.toLowerCase(); }

function buildCard(r) {
  const div = document.createElement('div');
  div.className = 'sensor-card';
  div.id = 'card-' + r.sensorId;
  div.innerHTML = `
    <div class="sensor-id">${r.sensorId}</div>
    <div class="machine">${r.machineName}</div>
    <div class="type">${r.sensorType}</div>
    <div class="val" data-val>${r.value} ${r.unit}</div>
    <div class="${badgeCls(r.status)}" data-badge>${r.status}</div>
  `;
  return div;
}

function updateCard(r) {
  const card = document.getElementById('card-' + r.sensorId);
  if (!card) return;
  card.querySelector('[data-val]').textContent = `${r.value} ${r.unit}`;
  const badge = card.querySelector('[data-badge]');
  badge.textContent = r.status;
  badge.className = badgeCls(r.status);
  card.classList.add('flash');
  setTimeout(() => card.classList.remove('flash'), 600);
}

// --- Grid (initial render, grouped by zone) ---
function renderGrid(readings) {
  const grid = document.getElementById('sensor-grid');
  grid.innerHTML = '';
  const zones = {};
  readings.forEach(r => { (zones[r.zone] ??= []).push(r); });
  for (const [zone, sensors] of Object.entries(zones)) {
    const section = document.createElement('div');
    section.className = 'zone-section';
    section.innerHTML = `<h2>${zone}</h2>`;
    const row = document.createElement('div');
    row.className = 'cards-row';
    sensors.forEach(r => row.appendChild(buildCard(r)));
    section.appendChild(row);
    grid.appendChild(section);
  }
  updateSummary(readings);
}

// --- Initial data load ---
fetch(`${API}/api/sensor`)
  .then(r => r.json())
  .then(data => renderGrid(data))
  .catch(err => console.error('Initial load failed:', err));

// --- SignalR ---
// withAutomaticReconnect() is required for onreconnecting/onreconnected to fire.
// Without it, a dropped connection goes straight to onclose with no retry.
const connection = new signalR.HubConnectionBuilder()
  .withUrl(`${API}/hubs/sensor`)
  .withAutomaticReconnect()
  .build();

// Receives full array of all 20 rows in one message; update each card in-place.
connection.on('SensorUpdated', readings => {
  readings.forEach(r => updateCard(r));
  updateSummary(readings);
});

connection.onreconnecting(() => setConn('reconnecting'));
connection.onreconnected(() => setConn('connected'));
connection.onclose(() => setConn('disconnected'));

connection.start()
  .then(() => setConn('connected'))
  .catch(err => {
    console.error('SignalR start failed:', err);
    setConn('disconnected');
  });

// --- Simulate button ---
// HTTP response confirms the SQL write. UI update arrives separately via SignalR.
// These are completely decoupled — different channels, different timing.
document.getElementById('simulate-btn').addEventListener('click', () => {
  fetch(`${API}/api/sensor/simulate`, { method: 'POST' })
    .catch(err => console.error('Simulate error:', err));
});
</script>
</body>
</html>
```

- [ ] **Step 2: Start the API**

```powershell
dotnet run --project IotPocApi/IotPocApi.csproj
```

- [ ] **Step 3: Open the dashboard**

Open `d:\poc\frontend\index.html` directly in Chrome or Edge (double-click from Explorer, or drag into browser). You do not need a local web server.

- [ ] **Step 4: Verify initial render**

Expected:
- 20 sensor cards visible, grouped under 5 zone headings
- Summary bar shows correct counts (Normal: 10, Warning: 6, Critical: 3, Offline: 1 from seed data — adjust expectation based on seed)
- Connection indicator shows 🟢 Connected

- [ ] **Step 5: Verify Simulate button triggers real-time update**

Click "Simulate Changes". Within ~1 second, exactly 3 cards should briefly flash a blue border and update their Value and Status badge — without any page reload. The HTTP response returned nothing; the update arrived via SignalR.

- [ ] **Step 6: Verify manual SQL UPDATE triggers push**

In SSMS, run:

```sql
USE IotPoc;
UPDATE dbo.SensorReadings SET Value = 999.99, Status = 'Critical' WHERE SensorId = 'SNS-001';
```

Expected: the `SNS-001` card on the dashboard updates to show `999.99 °C` and a red **CRITICAL** badge within ~1 second, without touching the browser.

- [ ] **Step 7: Commit**

```powershell
git -C d:\poc add frontend/index.html
git -C d:\poc commit -m "feat: add frontend dashboard with SignalR real-time card updates"
```

---

## Done

The full pipeline is working: SQL write → Service Broker → SqlDependency callback → SignalR push → in-place card update. No polling anywhere in the chain.

**Manual smoke test checklist:**
- [ ] 20 cards render on load, grouped by zone
- [ ] Connection pill is 🟢 green
- [ ] Simulate button updates 3 cards within ~1s, no page reload
- [ ] Manual SQL UPDATE to any row reflects on screen within ~1s
- [ ] Kill and restart the API; pill goes 🔴 then back to 🟢 on reconnect
