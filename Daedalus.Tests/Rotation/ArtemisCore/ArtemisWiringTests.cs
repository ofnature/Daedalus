using System.Linq;
using System.Reflection;
using Daedalus.Data;
using Daedalus.Rotation;
using Daedalus.Rotation.Common.Helpers;
using Xunit;

namespace Daedalus.Tests.Rotation.ArtemisCore;

/// <summary>
/// Artemis is registered and reachable. Every job in this plugin reaches the RotationManager through
/// exactly one route — a [Rotation] attribute discovered by reflection — so a job with a complete
/// action catalog and working modules is still entirely inert without it. Beastmaster sat in exactly
/// that state from the scaffold until 2026-09-08.
/// </summary>
public sealed class ArtemisWiringTests
{
    private static RotationAttribute Attribute() =>
        typeof(Artemis).GetCustomAttribute<RotationAttribute>()!;

    [Fact]
    public void ArtemisIsDiscoverableAsTheBeastmasterRotation()
    {
        var attr = Attribute();

        Assert.NotNull(attr);
        Assert.Equal("Artemis", attr.Name);
        Assert.Contains(JobRegistry.Beastmaster, attr.JobIds);
    }

    /// <summary>
    /// No other rotation may claim job 43, or the factory registers two competing factories for it
    /// and which one wins depends on reflection order.
    /// </summary>
    [Fact]
    public void OnlyArtemisClaimsBeastmaster()
    {
        var claimants = typeof(Artemis).Assembly
            .GetTypes()
            .Select(t => (t, attr: t.GetCustomAttribute<RotationAttribute>()))
            .Where(x => x.attr != null && x.attr.JobIds.Contains(JobRegistry.Beastmaster))
            .Select(x => x.t.Name)
            .ToList();

        Assert.Equal(["Artemis"], claimants);
    }

    /// <summary>
    /// Declared MeleeDps — the user's call, 2026-09-08. RotationRole is metadata and is read nowhere
    /// outside the attribute, so declaring it costs nothing behaviourally; the movement suppression
    /// is done by LimitedJobContentPolicy instead. Pinned together so the pairing is not separated.
    /// </summary>
    [Fact]
    public void BeastmasterIsMeleeButOptsOutOfAutoMovement()
    {
        Assert.Equal(RotationRole.MeleeDps, Attribute().Role);
        Assert.False(LimitedJobContentPolicy.AllowsAutoMovement(JobRegistry.Beastmaster));

        // Unlimited melee jobs are untouched by the new gate.
        Assert.True(LimitedJobContentPolicy.AllowsAutoMovement(JobRegistry.Samurai));
        Assert.True(LimitedJobContentPolicy.AllowsAutoMovement(JobRegistry.Reaper));

        // Blue Mage is the other limited job and is suppressed the same way.
        Assert.False(LimitedJobContentPolicy.AllowsAutoMovement(JobRegistry.BlueMage));
    }

    [Theory]
    [InlineData(44879u, 1)]   // Smash Axe     -> Axeblade Bite
    [InlineData(44883u, 2)]   // Axeblade Bite -> Shieldsplitter
    [InlineData(44885u, 0)]   // Shieldsplitter is the finisher; the chain restarts
    [InlineData(44884u, 0)]   // an instinctual skill is not part of the GCD chain at all
    public void ComboStepFollowsTheAxeChain(uint lastAction, int expected)
        => Assert.Equal(expected, Artemis.ComputeComboStep(lastAction, comboTimer: 10f));

    /// <summary>An expired combo timer means no chain, whatever the last action was.</summary>
    [Fact]
    public void ComboStepIsZeroOnceTheTimerLapses()
        => Assert.Equal(0, Artemis.ComputeComboStep(44879u, comboTimer: 0f));

    /// <summary>
    /// Beastmaster resolves a job icon. The lookup was bounded at job 42 (Pictomancer), so 43
    /// returned 0 and the sidebar drew a bare label with no icon — the one visible symptom of the
    /// job not being wired. The bound must move with every new job.
    /// </summary>
    [Fact]
    public void BeastmasterHasAJobIcon()
    {
        Assert.Equal(62043u, JobRegistry.GetJobIconId(JobRegistry.Beastmaster));

        // The neighbours still resolve, and an out-of-range id still yields 0 rather than a
        // plausible-looking icon for a job that does not exist.
        Assert.Equal(62042u, JobRegistry.GetJobIconId(JobRegistry.Pictomancer));
        Assert.Equal(0u, JobRegistry.GetJobIconId(JobRegistry.Beastmaster + 1));
        Assert.Equal(0u, JobRegistry.GetJobIconId(0));
    }
}
