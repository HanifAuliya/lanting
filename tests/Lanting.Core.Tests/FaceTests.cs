using Lanting.Core.Face;
using Microsoft.Extensions.Time.Testing;

namespace Lanting.Core.Tests;

public class FaceTests
{
    private static readonly DateTime Afternoon = new(2026, 11, 2, 14, 0, 0);

    private readonly FakeTimeProvider _time = new();

    [Theory]
    [InlineData(80, false, 0, 14, Expression.Neutral)]
    [InlineData(18, false, 0, 14, Expression.Worried)]
    [InlineData(9, false, 0, 14, Expression.Alarm)]
    [InlineData(9, true, 0, 14, Expression.Neutral)]
    [InlineData(100, true, 0, 14, Expression.Full)]
    [InlineData(80, false, 6, 14, Expression.Sleepy)]
    [InlineData(80, false, 25, 14, Expression.Asleep)]
    [InlineData(80, false, 0, 23, Expression.Sleepy)]
    [InlineData(15, false, 25, 14, Expression.Worried)]
    public void Base_expression_follows_battery_then_idle_then_time(int percent, bool plugged, int idleMinutes, int hour, Expression expected)
    {
        var state = new FaceState(percent, plugged, TimeSpan.FromMinutes(idleMinutes), Afternoon.Date.AddHours(hour));

        Assert.Equal(expected, FaceController.Base(state));
    }

    [Fact]
    public void Pc_without_battery_never_worries()
    {
        Assert.Equal(Expression.Neutral, FaceController.Base(new FaceState(null, false, TimeSpan.Zero, Afternoon)));
    }

    [Fact]
    public void Plugging_in_the_charger_is_celebrated_briefly()
    {
        var face = new FaceController(_time);
        var seen = Record(face);
        face.Update(new FaceState(50, false, TimeSpan.Zero, Afternoon));

        face.Update(new FaceState(50, true, TimeSpan.Zero, Afternoon));
        _time.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal([Expression.Charging, Expression.Neutral], seen);
    }

    [Fact]
    public void Coming_back_after_a_nap_surprises_then_pleases()
    {
        var face = new FaceController(_time);
        var seen = Record(face);
        face.Update(new FaceState(80, false, TimeSpan.FromMinutes(25), Afternoon));

        face.Update(new FaceState(80, false, TimeSpan.FromSeconds(3), Afternoon));
        _time.Advance(TimeSpan.FromSeconds(1.5));
        _time.Advance(TimeSpan.FromSeconds(2.5));

        Assert.Equal([Expression.Asleep, Expression.Surprised, Expression.Happy, Expression.Neutral], seen);
    }

    [Fact]
    public void Cheer_does_not_hide_a_battery_warning()
    {
        var face = new FaceController(_time);
        face.Update(new FaceState(8, false, TimeSpan.Zero, Afternoon));

        face.Cheer();

        Assert.Equal(Expression.Alarm, face.Current);
    }

    private static List<Expression> Record(FaceController face)
    {
        var seen = new List<Expression>();
        face.Changed += (_, e) => seen.Add(e);
        return seen;
    }
}
