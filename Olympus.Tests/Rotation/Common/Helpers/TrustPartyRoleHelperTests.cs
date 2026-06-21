using Moq;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Olympus.Data;
using Olympus.Rotation.Common.Helpers;
using Olympus.Tests.Mocks;
using Xunit;

namespace Olympus.Tests.Rotation.Common.Helpers;

/// <summary>
/// Trust role resolution for party buff targeting (AST cards, etc.).
/// </summary>
public sealed class TrustPartyRoleHelperTests
{
    [Fact]
    public void IsTank_UsesTankStanceWhenJobMissing()
    {
        var member = MockBuilders.CreateMockBattleChara(2);
        var partyList = MockBuilders.CreateMockPartyList(0);

        Assert.True(TrustPartyRoleHelper.IsTank(
            member.Object,
            partyList.Object,
            _ => true));

        Assert.False(TrustPartyRoleHelper.IsTank(
            member.Object,
            partyList.Object,
            _ => false));
    }

    [Fact]
    public void TrustCardTargeting_Documentation()
    {
        // Trust WAR/RDM/PCT expose ClassJob on IBattleChara in-game.
        // DPS cards must target IsDps allies only — never tank/healer fallbacks.
        Assert.True(JobRegistry.IsTank(JobRegistry.Warrior));
        Assert.True(JobRegistry.IsCasterDps(JobRegistry.RedMage));
        Assert.True(JobRegistry.IsCasterDps(JobRegistry.Pictomancer));
        Assert.True(JobRegistry.IsHealer(JobRegistry.Astrologian));
    }
}
