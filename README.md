# IoT Real-Time Sensor Dashboard

A full-stack POC demonstrating event-driven server push with no polling. A raw SQL `UPDATE` in SSMS triggers an instant card refresh in the browser — the HTTP write and the WebSocket push travel through completely separate channels.

```
SQL UPDATE  →  Service Broker  →  SqlDependency callback  →  SignalR push  →  browser card
```

## Stack

| Layer | Technology |
|-------|-----------|
| Database | SQL Server LocalDB `(localdb)\MSSQLLocalDB` |
| Change detection | SqlDependency + Service Broker |
| Backend | .NET 8 Web API — `http://localhost:5000` |
| Real-time transport | ASP.NET Core SignalR (built-in) |
| Data access | Microsoft.Data.SqlClient (ADO.NET, no ORM) |
| Frontend | Plain HTML + JavaScript — no framework, no build step |
| Tests | xUnit — 8 unit tests for `SimulationHelper` |

## Prerequisites

- .NET 8 SDK — `dotnet --version` must show `8.x.x`
- SQL Server LocalDB — ships with Visual Studio 2019+; verify with `sqllocaldb info`
- SSMS or Azure Data Studio — to run the setup script and fire manual updates
- A modern browser (Edge or Chrome)

## Quick Start

**1. Create the database**

Open SSMS, connect to `(localdb)\MSSQLLocalDB`, open `sql/setup.sql`, and press F5. This creates the `IotPoc` database, the `dbo.SensorReadings` table, seeds 20 rows, and enables Service Broker.

```sql
-- verify
USE IotPoc;
SELECT COUNT(*) FROM dbo.SensorReadings;   -- 20
SELECT name, is_broker_enabled FROM sys.databases WHERE name = 'IotPoc';  -- 1
```

**2. Start the API**

```bash
dotnet run --project IotPocApi/IotPocApi.csproj
```

Wait for: `Now listening on: http://localhost:5000`

**3. Open the dashboard**

Double-click `frontend/index.html`. No web server needed — it opens directly from `file://`. The connection indicator in the header turns green within a second.

## The Demo — Manual SQL Update

With the API running and the dashboard open, execute any `UPDATE` against `dbo.SensorReadings` in SSMS:

```sql
USE IotPoc;

UPDATE dbo.SensorReadings
SET   Value       = 999,
      Status      = 'Critical',
      LastUpdated = GETDATE()
WHERE SensorId = 'SNS-001';
```

The matching sensor card in the browser updates in under a second — no page reload, no button click.

Other useful queries:

```sql
-- Set a sensor offline
UPDATE dbo.SensorReadings SET Value = 0, Status = 'Offline', LastUpdated = GETDATE()
WHERE SensorId = 'SNS-006';

-- Set an entire zone critical (one notification fires for all cards)
UPDATE dbo.SensorReadings SET Status = 'Critical', LastUpdated = GETDATE()
WHERE Zone = 'Zone 3 - Pneumatics';

-- Reset everything
UPDATE dbo.SensorReadings SET Status = 'Normal', LastUpdated = GETDATE()
WHERE Status != 'Normal';
```

## API Endpoints

| Method | Path | Purpose |
|--------|------|---------|
| `GET` | `/api/sensor` | Return all 20 sensor rows (initial page load) |
| `POST` | `/api/sensor/simulate` | Update 3 random rows — triggers SqlDependency automatically |
| `WS` | `/hubs/sensor` | SignalR hub — browser connects here, server pushes `SensorUpdated` events |

## Running Tests

```bash
dotnet test IotPocApi.Tests/IotPocApi.Tests.csproj
```

8 tests covering `SimulationHelper`: value ranges per sensor type, unknown-type fallback, decimal precision, and status output.

## Project Structure

```
poc/
├── sql/
│   └── setup.sql                          ← run once in SSMS
├── IotPocApi/
│   ├── Controllers/SensorController.cs    ← GET /api/sensor, POST /api/sensor/simulate
│   ├── Hubs/SensorHub.cs                  ← SignalR hub (thin — server push only)
│   ├── Models/SensorReading.cs            ← C# record matching DB columns
│   ├── Services/
│   │   ├── SqlChangeListenerService.cs    ← IHostedService; SqlDependency lifecycle
│   │   └── SimulationHelper.cs            ← randomise values per sensor type
│   ├── appsettings.json                   ← connection string, port 5000
│   └── Program.cs                         ← DI, CORS, SignalR, hub route
├── IotPocApi.Tests/
│   └── SimulationHelperTests.cs           ← 8 unit tests
├── frontend/
│   └── index.html                         ← full dashboard, no build step
└── docs/
    ├── POC-Documentation.html             ← detailed HTML docs with screenshots
    └── POC-Documentation.docx             ← Word version
```

## Key Design Decisions

**SqlDependency query constraints** — the registration query must list columns explicitly (no `SELECT *`), use the two-part table name (`dbo.SensorReadings`), and contain no `ORDER BY`, `JOIN`, or `TOP`. Violating these causes `e.Info = Invalid`, which silently kills the listener.

**Re-registration is unconditional** — SqlDependency fires once and expires. `OnDataChanged` always calls `RegisterDependency()` at the end regardless of notification type, so housekeeping fires also re-arm the listener.

**Full array push** — on every change the service re-queries all 20 rows and sends the complete array in a single `SendAsync("SensorUpdated", allRows)`. The browser updates only the cards whose values changed, by `SensorId`.

**CORS for `file://` origin** — browsers send `Origin: null` when opening an HTML file from disk. `AllowAnyOrigin()` rejects null origins when combined with `AllowCredentials()` (required by SignalR). The API uses `SetIsOriginAllowed(_ => true)` instead.
