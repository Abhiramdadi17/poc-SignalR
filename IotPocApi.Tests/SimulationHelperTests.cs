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
