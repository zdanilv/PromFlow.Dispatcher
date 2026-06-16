using Configurator.Desktop.Workspace.RouteMap.Models;
using Xunit;

namespace Configurator.Tests.Unit.RouteMap;

public sealed class RouteNodeRoleStateTransitionsTests
{
    [Fact]
    public void ToggleLoader_leaves_only_one_loader_enabled()
    {
        var states = new Dictionary<string, RouteNodeRoleState>
        {
            ["smes_32"] = new("smes_32", IsLoader: true, IsTarget: false),
            ["smes_22"] = new("smes_22", IsLoader: false, IsTarget: false),
            ["concrete_bucket"] = new("concrete_bucket", IsLoader: false, IsTarget: true),
        };

        var updated = RouteNodeRoleStateTransitions.ToggleLoader(states, "smes_22");

        Assert.False(updated["smes_32"].IsLoader);
        Assert.True(updated["smes_22"].IsLoader);
        Assert.Single(updated.Values, x => x.IsLoader);
        Assert.True(updated["concrete_bucket"].IsTarget);
    }

    [Fact]
    public void ToggleTarget_leaves_only_one_target_enabled()
    {
        var states = new Dictionary<string, RouteNodeRoleState>
        {
            ["smes_32"] = new("smes_32", IsLoader: true, IsTarget: false),
            ["smes_22"] = new("smes_22", IsLoader: false, IsTarget: false),
            ["concrete_bucket"] = new("concrete_bucket", IsLoader: false, IsTarget: true),
        };

        var updated = RouteNodeRoleStateTransitions.ToggleTarget(states, "smes_22");

        Assert.False(updated["concrete_bucket"].IsTarget);
        Assert.True(updated["smes_22"].IsTarget);
        Assert.False(updated["smes_22"].IsLoader);
        Assert.Single(updated.Values, x => x.IsTarget);
        Assert.True(updated["smes_32"].IsLoader);
    }

    [Fact]
    public void ToggleLoader_turns_global_loader_off_when_already_enabled()
    {
        var states = new Dictionary<string, RouteNodeRoleState>
        {
            ["smes_32"] = new("smes_32", IsLoader: true, IsTarget: false),
            ["concrete_bucket"] = new("concrete_bucket", IsLoader: false, IsTarget: true),
        };

        var updated = RouteNodeRoleStateTransitions.ToggleLoader(states, "smes_32");

        Assert.DoesNotContain(updated.Values, x => x.IsLoader);
        Assert.True(updated["concrete_bucket"].IsTarget);
    }

    [Fact]
    public void ToggleTarget_turns_global_target_off_when_already_enabled()
    {
        var states = new Dictionary<string, RouteNodeRoleState>
        {
            ["smes_32"] = new("smes_32", IsLoader: true, IsTarget: false),
            ["concrete_bucket"] = new("concrete_bucket", IsLoader: false, IsTarget: true),
        };

        var updated = RouteNodeRoleStateTransitions.ToggleTarget(states, "concrete_bucket");

        Assert.DoesNotContain(updated.Values, x => x.IsTarget);
        Assert.True(updated["smes_32"].IsLoader);
    }

    [Fact]
    public void ToggleTarget_enables_target_and_clears_loader()
    {
        var state = new RouteNodeRoleState("smes_32", IsLoader: true, IsTarget: false);

        var updated = RouteNodeRoleStateTransitions.ToggleTarget(state);

        Assert.False(updated.IsLoader);
        Assert.True(updated.IsTarget);
    }

    [Fact]
    public void ToggleLoader_enables_loader_and_clears_target()
    {
        var state = new RouteNodeRoleState("smes_32", IsLoader: false, IsTarget: true);

        var updated = RouteNodeRoleStateTransitions.ToggleLoader(state);

        Assert.True(updated.IsLoader);
        Assert.False(updated.IsTarget);
    }

    [Fact]
    public void ToggleTarget_turns_target_off_when_already_enabled()
    {
        var state = new RouteNodeRoleState("concrete_bucket", IsLoader: false, IsTarget: true);

        var updated = RouteNodeRoleStateTransitions.ToggleTarget(state);

        Assert.False(updated.IsLoader);
        Assert.False(updated.IsTarget);
    }

    [Fact]
    public void ToggleLoader_turns_loader_off_when_already_enabled()
    {
        var state = new RouteNodeRoleState("smes_32", IsLoader: true, IsTarget: false);

        var updated = RouteNodeRoleStateTransitions.ToggleLoader(state);

        Assert.False(updated.IsLoader);
        Assert.False(updated.IsTarget);
    }
}
