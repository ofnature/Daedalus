using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Daedalus.Services.Beastmaster;

/// <summary>Reading and clicking the Crucible's game windows: the autopilot's and the loop's shared plumbing.</summary>
internal static unsafe class CrucibleAddons
{
    /// <summary>The window by name when it is on screen, else null.</summary>
    public static AtkUnitBase* Visible(string name)
    {
        var manager = RaptureAtkUnitManager.Instance();
        var unit = manager == null ? null : manager->GetAddonByName(name);
        return unit != null && unit->IsVisible ? unit : null;
    }

    /// <summary>A window value as text (line breaks shown as " / "), "" when it is not a string.</summary>
    public static string Text(AtkUnitBase* unit, int index)
    {
        if (index >= unit->AtkValuesCount)
            return "";
        var value = unit->AtkValues[index];
        return value.String.Value == null
            ? ""
            : Dalamud.Memory.MemoryHelper.ReadSeStringNullTerminated((nint)value.String.Value).TextValue.Replace("\n", " / ");
    }

    /// <summary>The text of one of the window's text elements, by node id; "" when there is none.</summary>
    public static string NodeText(AtkUnitBase* unit, uint nodeId)
    {
        var node = unit->UldManager.SearchNodeById(nodeId);
        var text = node == null ? null : node->GetAsAtkTextNode();
        return text == null ? "" : text->NodeText.ToString().Trim();
    }

    /// <summary>A window value as a flag, false when it is not one.</summary>
    public static bool Bool(AtkUnitBase* unit, int index) =>
        index < unit->AtkValuesCount && unit->AtkValues[index].Type == AtkValueType.Bool && unit->AtkValues[index].Byte != 0;

    /// <summary>A window value as a number, 0 when it is not one.</summary>
    public static long Number(AtkUnitBase* unit, int index)
    {
        if (index >= unit->AtkValuesCount)
            return 0;
        var value = unit->AtkValues[index];
        return value.Type switch
        {
            AtkValueType.UInt => value.UInt,
            AtkValueType.Int => value.Int,
            _ => 0,
        };
    }

    /// <summary>Send the window the same click the player's would (integer arguments).</summary>
    public static void Fire(AtkUnitBase* unit, params int[] ints)
    {
        var values = stackalloc AtkValue[ints.Length];
        for (var i = 0; i < ints.Length; i++)
        {
            values[i] = default;
            values[i].Type = AtkValueType.Int;
            values[i].Int = ints[i];
        }

        unit->FireCallback((uint)ints.Length, values, true);
    }
}
