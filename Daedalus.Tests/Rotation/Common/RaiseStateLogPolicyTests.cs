using Daedalus.Rotation.Common.Modules;
using Xunit;

namespace Daedalus.Tests.Rotation.Common;

/// <summary>
/// Field 2026-08-14: "Raise: None needed ×5314" in one session. Two resting states are written
/// every frame from two different places, and the old dedupe stored them in the key before
/// skipping them — so every frame read as a transition and re-logged forever.
/// </summary>
public sealed class RaiseStateLogPolicyTests
{
    [Theory]
    [InlineData("None needed")]
    [InlineData("No target")]
    [InlineData("Disabled")]
    [InlineData("")]
    [InlineData(null)]
    public void RestingStates_NeverLog(string? state)
    {
        var (shouldLog, _) = RaiseStateLogPolicy.Decide(state, lastLogged: null);
        Assert.False(shouldLog);
        Assert.True(RaiseStateLogPolicy.IsResting(state));
    }

    /// <summary>
    /// The actual bug, reproduced: the execute pass writes "No target", the debug pass writes
    /// "None needed", every frame, forever. Neither may log, and — critically — neither may leave
    /// a key behind that makes the other look new.
    /// </summary>
    [Fact]
    public void AlternatingRestingStates_DoNotReLogForever()
    {
        string? key = null;
        var logs = 0;

        for (var frame = 0; frame < 1000; frame++)
        {
            foreach (var state in new[] { "No target", "None needed" })
            {
                var (shouldLog, next) = RaiseStateLogPolicy.Decide(state, key);
                key = next;
                if (shouldLog) logs++;
            }
        }

        Assert.Equal(0, logs);
    }

    /// <summary>A real raise need logs once, not once per frame.</summary>
    [Fact]
    public void InterestingState_LogsOnce_ThenDedupes()
    {
        string? key = null;
        var logs = 0;

        for (var frame = 0; frame < 100; frame++)
        {
            var (shouldLog, next) = RaiseStateLogPolicy.Decide("Dead member found", key);
            key = next;
            if (shouldLog) logs++;
        }

        Assert.Equal(1, logs);
    }

    /// <summary>
    /// Why resting CLEARS the key instead of storing it: somebody dies, gets raised, and dies
    /// again. The second death must log. Storing the resting state would work here too, but
    /// clearing is what makes it work without re-introducing the alternation bug.
    /// </summary>
    [Fact]
    public void RaiseNeed_AfterALull_LogsAgain()
    {
        string? key = null;
        var logs = 0;

        void Step(string state)
        {
            var (shouldLog, next) = RaiseStateLogPolicy.Decide(state, key);
            key = next;
            if (shouldLog) logs++;
        }

        Step("Dead member found");   // logs
        Step("None needed");         // lull
        Step("No target");           // lull
        Step("Dead member found");   // must log again

        Assert.Equal(2, logs);
    }

    /// <summary>Distinct interesting states each log.</summary>
    [Fact]
    public void DistinctInterestingStates_EachLog()
    {
        string? key = null;
        var logs = 0;

        foreach (var state in new[] { "Dead member found", "Reserved by other", "Dead member found" })
        {
            var (shouldLog, next) = RaiseStateLogPolicy.Decide(state, key);
            key = next;
            if (shouldLog) logs++;
        }

        Assert.Equal(3, logs);
    }
}
