using Lanting.Core.Island;
using Microsoft.Extensions.Time.Testing;

namespace Lanting.Core.Tests;

public class IslandControllerTests
{
    private readonly FakeTimeProvider _time = new();

    [Fact]
    public void Starts_empty()
    {
        using var controller = new IslandController(_time);

        Assert.Null(controller.Current);
    }

    [Fact]
    public void Higher_priority_wins_regardless_of_order()
    {
        using var controller = new IslandController(_time);
        var battery = new IslandContent("battery", "mouse", IslandPriority.Alert);
        var song = new IslandContent("media", "now-playing", IslandPriority.Ambient);

        controller.Show(battery);
        controller.Show(song);

        Assert.Same(battery, controller.Current);
    }

    [Fact]
    public void Same_priority_newest_wins()
    {
        using var controller = new IslandController(_time);
        var first = new IslandContent("a", "x", IslandPriority.Normal);
        var second = new IslandContent("b", "x", IslandPriority.Normal);

        controller.Show(first);
        controller.Show(second);

        Assert.Same(second, controller.Current);
    }

    [Fact]
    public void Withdraw_falls_back_to_next_content()
    {
        using var controller = new IslandController(_time);
        var song = new IslandContent("media", "now-playing", IslandPriority.Ambient);
        var battery = new IslandContent("battery", "mouse", IslandPriority.Alert);
        controller.Show(song);
        controller.Show(battery);

        controller.Withdraw("battery", "mouse");

        Assert.Same(song, controller.Current);
    }

    [Fact]
    public void Showing_same_id_replaces_instead_of_stacking()
    {
        using var controller = new IslandController(_time);
        controller.Show(new IslandContent("media", "now-playing", IslandPriority.Ambient, "Lagu A"));
        var updated = new IslandContent("media", "now-playing", IslandPriority.Ambient, "Lagu B");
        controller.Show(updated);
        Assert.Same(updated, controller.Current);

        // Satu Withdraw cukup karena konten lama sudah diganti, bukan ditumpuk.
        controller.Withdraw("media", "now-playing");
        Assert.Null(controller.Current);
    }

    [Fact]
    public void Timed_content_expires_and_returns_to_previous()
    {
        using var controller = new IslandController(_time);
        var song = new IslandContent("media", "now-playing", IslandPriority.Ambient);
        var toast = new IslandContent("battery", "low", IslandPriority.Alert, Duration: TimeSpan.FromSeconds(3));
        controller.Show(song);
        controller.Show(toast);

        _time.Advance(TimeSpan.FromSeconds(2.9));
        Assert.Same(toast, controller.Current);

        _time.Advance(TimeSpan.FromSeconds(0.2));
        Assert.Same(song, controller.Current);
    }

    [Fact]
    public void Reshowing_timed_content_restarts_its_timer()
    {
        using var controller = new IslandController(_time);
        var toast = new IslandContent("battery", "low", IslandPriority.Alert, Duration: TimeSpan.FromSeconds(3));
        controller.Show(toast);
        _time.Advance(TimeSpan.FromSeconds(2));

        controller.Show(toast with { Payload = "5%" });
        _time.Advance(TimeSpan.FromSeconds(2));

        Assert.NotNull(controller.Current);
        _time.Advance(TimeSpan.FromSeconds(1.1));
        Assert.Null(controller.Current);
    }

    [Fact]
    public void CurrentChanged_fires_only_when_the_winner_changes()
    {
        using var controller = new IslandController(_time);
        var changes = new List<IslandContent?>();
        controller.CurrentChanged += (_, c) => changes.Add(c);
        var battery = new IslandContent("battery", "mouse", IslandPriority.Alert);

        controller.Show(battery);
        controller.Show(new IslandContent("media", "now-playing", IslandPriority.Ambient));
        controller.Withdraw("unknown", "key");
        controller.Withdraw("battery", "mouse");

        Assert.Equal(2, changes.Count);
        Assert.Same(battery, changes[0]);
        Assert.Equal("media", changes[1]?.ModuleId);
    }
}
