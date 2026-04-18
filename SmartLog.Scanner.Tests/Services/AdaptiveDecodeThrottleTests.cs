using SmartLog.Scanner.Core.Services;

namespace SmartLog.Scanner.Tests.Services;

/// <summary>
/// EP0011: Unit tests for AdaptiveDecodeThrottle.
/// Verifies that the frame-skip count is correct for each camera count tier.
/// </summary>
public class AdaptiveDecodeThrottleTests
{
    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 5)]
    [InlineData(3, 8)]
    [InlineData(4, 8)]
    [InlineData(5, 10)]
    [InlineData(6, 12)]
    [InlineData(7, 13)]
    [InlineData(8, 15)]
    public void Calculate_ReturnsExpectedThrottleForCameraCount(int cameraCount, int expectedThrottle)
    {
        var result = AdaptiveDecodeThrottle.Calculate(cameraCount);

        Assert.Equal(expectedThrottle, result);
    }

    [Fact]
    public void Calculate_Zero_ReturnsMinimum()
    {
        // Edge: 0 cameras is invalid but should not throw
        var result = AdaptiveDecodeThrottle.Calculate(0);

        Assert.True(result >= 3, "Throttle should never go below minimum of 3");
    }

    [Fact]
    public void Calculate_Above8_ReturnsSameAs8()
    {
        var at8 = AdaptiveDecodeThrottle.Calculate(8);
        var at9 = AdaptiveDecodeThrottle.Calculate(9);
        var at16 = AdaptiveDecodeThrottle.Calculate(16);

        Assert.Equal(at8, at9);
        Assert.Equal(at8, at16);
    }

    [Fact]
    public void Calculate_NeverBelowMinimum()
    {
        for (var i = 0; i <= 20; i++)
        {
            var result = AdaptiveDecodeThrottle.Calculate(i);
            Assert.True(result >= 3, $"Throttle for {i} cameras ({result}) is below minimum of 3");
        }
    }

    [Fact]
    public void Calculate_IncreaseMonotonically_WithCameraCount()
    {
        // Throttle should not decrease as camera count increases
        var prev = 0;
        for (var i = 1; i <= 8; i++)
        {
            var current = AdaptiveDecodeThrottle.Calculate(i);
            Assert.True(current >= prev,
                $"Throttle decreased from {prev} (camera {i - 1}) to {current} (camera {i})");
            prev = current;
        }
    }
}
