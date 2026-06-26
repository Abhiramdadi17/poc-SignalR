# IoT Real-Time Dashboard POC — Design Spec
**Date:** 2026-06-26

## Overview

A full-stack POC demonstrating true server-push real-time updates using SQL Server's Service Broker → SqlDependency → SignalR → browser. No polling. The demo proves that a database write and the resulting UI update travel through completely separate channels.

---

## Stack

| Layer | Technology |
|-------|-----------|
| Database | SQL Server LocalDB (`(localdb)\MSSQLLocalDB`) |
| Backend | .NET 8 Web API |
| Real-time transport | SignalR (built-in to .NET 8) |
| Change detection | SqlDependency + Service Broker |
| Frontend | Plain HTML + JavaScript (no framework, no build step) |

---

## Architecture

```
SQL Server LocalDB (IotPoc)
    └── SensorReadings table
         └── Service Broker (change notifications)
              └── SqlDependency callback
                   └── SqlChangeListenerService (IHostedService)
                        └── IHubContext<SensorHub>
                             └── SignalR Hub (/hubs/sensor)
                                  └── WebSocket connection
                                       └── index.html (plain JS)
```

### Data flow on a manual SQL UPDATE

1. User runs `UPDATE dbo.SensorReadings SET Value=99 WHERE SensorId='SNS-004'`
2. Service Broker detects the change and fires the registered `SqlDependency` callback in the .NET process
3. `SqlChangeListenerService.OnDataChanged` checks `e.Type == SqlNotificationType.Change`
4. If Change: re-queries updated rows and pushes via `hubContext.Clients.All.SendAsync("SensorUpdated", reading)`
5. Always (regardless of notification type): re-registers the SqlDependency to keep the listener alive
6. All connected browser tabs receive the push and update the matching sensor card in-place

---

## Database

**Instance:** `(localdb)\MSSQLLocalDB`
**Database:** `IotPoc`
**Table:** `dbo.SensorReadings`

Service Broker must be enabled:
```sql
ALTER DATABASE IotPoc SET ENABLE_BROKER WITH ROLLBACK IMMEDIATE;
```

---

## Backend — Project: `IotPocApi`

### NuGet packages
- `Microsoft.Data.SqlClient` (ADO.NET for SqlDependency)
- SignalR and CORS are built into .NET 8 — no extra packages

### Project structure
```
IotPocApi/
├── Hubs/
│   └── SensorHub.cs
├── Services/
│   └── SqlChangeListenerService.cs
├── Models/
│   └── SensorReading.cs
├── Controllers/
│   └── SensorController.cs
└── Program.cs
```

### Components

**`SensorReading.cs`**
Plain C# record matching all DB columns. No ORM.

**`SensorHub.cs`**
Thin SignalR hub. No server-side methods — all communication is server → client push. Clients connect and listen for `"SensorUpdated"` events.

**`SqlChangeListenerService.cs`** — `IHostedService`
- `StartAsync`: calls `SqlDependency.Start(connectionString)`, then calls `RegisterDependency()`
- `RegisterDependency()`: opens a `SqlConnection`, creates a `SqlCommand` with the explicit-column query (see constraint below), attaches `OnDataChanged` callback, executes the reader to arm the dependency
- `OnDataChanged`:
  - If `e.Type == SqlNotificationType.Change`: re-query all rows, push the full array to SignalR in a single `SendAsync("SensorUpdated", allRows)` call
  - Always (unconditional): call `RegisterDependency()` to re-arm the listener
- `StopAsync`: calls `SqlDependency.Stop(connectionString)`

**`SensorController.cs`**
- `GET /api/sensor` — queries all rows, returns JSON array (initial page load)
- `POST /api/sensor/simulate` — selects 3 rows by random index from the 20 seeded rows; randomizes Value within a realistic range for that SensorType (e.g. Temperature 20–110°C, Pressure 1–15 bar, Vibration 0.1–3.5g, Humidity 20–95%RH, RPM 0–3000); randomizes Status to Normal/Warning/Critical; returns 200; triggers SqlDependency automatically; frontend ignores response body

**`Program.cs`**
- Add SignalR services
- Add CORS (allow all origins — POC only, HTML file opened from disk)
- Register `SqlChangeListenerService` as hosted service
- Map SignalR hub to `/hubs/sensor`
- Map controllers

### SqlDependency query constraints (hard rules)

The registration query MUST follow these rules or SqlDependency fires `e.Info = Invalid` immediately, silently killing the listener:

```sql
SELECT Id, SensorId, MachineName, Zone, SensorType, Value, Unit, Status, LastUpdated
FROM dbo.SensorReadings
```

Rules:
- Explicit column list — **no `SELECT *`**
- Two-part table name — **`dbo.SensorReadings`**, not `SensorReadings`
- No `JOIN`, no subquery, no `TOP`, no `ORDER BY`

### Re-registration invariant

```
OnDataChanged fires
  ├── e.Type == Change  → push rows to SignalR clients
  ├── e.Type == Subscribe or Error → skip push
  └── always → call RegisterDependency() unconditionally
```

Re-registration is always last and always unconditional. This prevents tight loops on housekeeping fires while guaranteeing the listener never silently dies.

---

## Frontend — `index.html`

Single file. No build step. Open directly in browser (`file://` or a local dev server).

### Layout

- **Header**: title + "Simulate Changes" button + SignalR connection indicator pill
- **Summary bar**: live counts of Normal / Warning / Critical / Offline sensors
- **Sensor grid**: 20 cards, grouped by Zone (5 zones × ~4 sensors each)

Each card displays: SensorId, MachineName, SensorType, Value + Unit, Status badge.

Status badge colors:
| Status | Color |
|--------|-------|
| Normal | Green |
| Warning | Amber |
| Critical | Red |
| Offline | Grey |

### SignalR connection indicator

Small pill in the header — three states:

| State | Color | Trigger |
|-------|-------|---------|
| Connected | 🟢 Green | `connection.onreconnected` / initial open |
| Reconnecting | 🟡 Amber | `connection.onreconnecting` |
| Disconnected | 🔴 Red | `connection.onclose` — prompt user to refresh |

Makes the WebSocket transport layer visible during the demo. A silent drop otherwise looks like a broken POC.

### JavaScript behavior

1. **On load**: `fetch('GET /api/sensor')` populates all 20 cards
2. **SignalR init**: `new signalR.HubConnectionBuilder().withUrl('/hubs/sensor').withAutomaticReconnect().build()` — `withAutomaticReconnect()` must be present for `onreconnecting` / `onreconnected` callbacks to fire; without it, a dropped connection goes straight to `onclose`
3. **On `"SensorUpdated"`**: receives one message containing the full array of all 20 rows; iterates the array and updates each card by `SensorId` in-place — no full grid re-render
4. **On "Simulate" click**: `fetch('POST /api/sensor/simulate')` — ignore response body; wait for SignalR push to deliver the actual update

### Key demo talking point

The "Simulate" button's HTTP response and the card update on screen are completely decoupled:
- HTTP `POST /api/sensor/simulate` → confirms the SQL write happened
- SignalR `"SensorUpdated"` → delivers the new data

These travel through entirely separate channels. This decoupling is what the POC proves.

---

## CORS

Configured to allow all origins (POC only). Required because `index.html` may be opened as a `file://` URL, which browsers treat as a distinct origin from `http://localhost`.

---

## Endpoints

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/sensor` | Return all sensor rows (initial load) |
| POST | `/api/sensor/simulate` | Update 3 random rows, trigger SqlDependency |
| WS | `/hubs/sensor` | SignalR hub endpoint |

---

## Out of scope (POC constraints)

- Authentication / authorization
- Persistent connection tracking per client
- Historical data / charting
- Production CORS policy
- Error UI beyond the connection indicator
