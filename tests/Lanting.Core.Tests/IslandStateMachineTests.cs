using Lanting.Core.Island;
using Microsoft.Extensions.Time.Testing;

namespace Lanting.Core.Tests;

public class IslandStateMachineTests
{
    private readonly FakeTimeProvider _time = new();
    // Tanpa jeda buka supaya tes lain sederhana; jeda buka dites terpisah di bawah.
    private readonly IslandOptions _options = new() { CollapseDelay = TimeSpan.FromMilliseconds(300), ExpandDelay = TimeSpan.Zero };

    [Fact]
    public void Idle_shows_compact_pill_by_default()
    {
        using var machine = new IslandStateMachine(_options, _time);

        Assert.Equal(IslandState.Compact, machine.State);
    }

    [Fact]
    public void Idle_is_hidden_when_idle_pill_disabled()
    {
        using var machine = new IslandStateMachine(_options with { ShowWhenIdle = false }, _time);

        Assert.Equal(IslandState.Hidden, machine.State);

        machine.OnContentChanged(new IslandContent("media", "now-playing", IslandPriority.Ambient));
        Assert.Equal(IslandState.Compact, machine.State);
    }

    [Fact]
    public void Hover_expands_and_leaving_collapses_after_delay()
    {
        using var machine = new IslandStateMachine(_options, _time);

        machine.OnPointerEntered();
        Assert.Equal(IslandState.Expanded, machine.State);

        machine.OnPointerExited();
        _time.Advance(TimeSpan.FromMilliseconds(299));
        Assert.Equal(IslandState.Expanded, machine.State);

        _time.Advance(TimeSpan.FromMilliseconds(2));
        Assert.Equal(IslandState.Compact, machine.State);
    }

    [Fact]
    public void Resting_on_the_pill_opens_it_after_the_expand_delay()
    {
        using var machine = new IslandStateMachine(_options with { ExpandDelay = TimeSpan.FromMilliseconds(250) }, _time);

        machine.OnPointerEntered();
        _time.Advance(TimeSpan.FromMilliseconds(249));
        Assert.Equal(IslandState.Compact, machine.State);

        _time.Advance(TimeSpan.FromMilliseconds(2));
        Assert.Equal(IslandState.Expanded, machine.State);
    }

    [Fact]
    public void Passing_over_the_pill_quickly_does_not_open_it()
    {
        using var machine = new IslandStateMachine(_options with { ExpandDelay = TimeSpan.FromMilliseconds(250) }, _time);
        var states = new List<IslandState>();
        machine.StateChanged += (_, s) => states.Add(s);

        machine.OnPointerEntered();
        _time.Advance(TimeSpan.FromMilliseconds(100));
        machine.OnPointerExited();
        _time.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(IslandState.Compact, machine.State);
        Assert.Empty(states);
    }

    [Fact]
    public void Coming_back_before_delay_keeps_it_expanded()
    {
        using var machine = new IslandStateMachine(_options, _time);
        machine.OnPointerEntered();

        machine.OnPointerExited();
        _time.Advance(TimeSpan.FromMilliseconds(200));
        machine.OnPointerEntered();
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(IslandState.Expanded, machine.State);
    }

    [Fact]
    public void Timed_alert_shows_as_notification_even_when_hovered()
    {
        using var machine = new IslandStateMachine(_options, _time);
        machine.OnPointerEntered();

        machine.OnContentChanged(new IslandContent("battery", "low", IslandPriority.Alert, Duration: TimeSpan.FromSeconds(3)));

        Assert.Equal(IslandState.Notification, machine.State);
    }

    [Fact]
    public void Fullscreen_hides_everything_and_restores_after()
    {
        using var machine = new IslandStateMachine(_options, _time);
        machine.OnContentChanged(new IslandContent("media", "now-playing", IslandPriority.Ambient));

        machine.OnFullscreenChanged(true);
        Assert.Equal(IslandState.Hidden, machine.State);

        machine.OnFullscreenChanged(false);
        Assert.Equal(IslandState.Compact, machine.State);
    }

    [Fact]
    public void StateChanged_fires_once_per_real_change()
    {
        using var machine = new IslandStateMachine(_options, _time);
        var states = new List<IslandState>();
        machine.StateChanged += (_, s) => states.Add(s);

        machine.OnPointerEntered();
        machine.OnPointerEntered();
        machine.OnPointerExited();
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal([IslandState.Expanded, IslandState.Compact], states);
    }
}
