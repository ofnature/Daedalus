using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Daedalus.Windows;

/// <summary>
/// Standalone shell for the Meld Optimizer (/daedalus meld). The actual UI lives in
/// <see cref="MeldOptimizerPanel"/>, which is ALSO hosted in the Analytics window's Melding tab
/// — same panel instance, so sweep results and overlays are shared between both hosts.
/// </summary>
public sealed class MeldOptimizerWindow : Window
{
    private readonly MeldOptimizerPanel _panel;

    public MeldOptimizerWindow(MeldOptimizerPanel panel)
        : base("Meld Optimizer")
    {
        _panel = panel;
        Size = new Vector2(980, 760);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(760, 560),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw() => _panel.Draw();
}
