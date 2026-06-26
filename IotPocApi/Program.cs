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
