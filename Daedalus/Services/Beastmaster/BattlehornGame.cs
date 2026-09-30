using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Daedalus.Services.Beastmaster;

/// <summary>
/// Live game reads and clicks for <see cref="BattlehornAssigner"/>: the horns off
/// <c>ActionManager.BeastmasterPets</c> (Bestiary numbers, verified 2026-09-29), and the Bestiary's
/// and the context menu's own callbacks (docs/battlehorn-assignment.md §4).
/// </summary>
public sealed unsafe class BattlehornGame : IBattlehornGame
{
    private const string Bestiary = "XBMMonsterNotebook";
    private const string Menu = "ContextMenu";

    public int[]? ReadHorns()
    {
        try
        {
            var manager = ActionManager.Instance();
            if (manager is null)
                return null;
            var pets = manager->BeastmasterPets;
            return [pets[0], pets[1], pets[2]];
        }
        catch
        {
            return null;
        }
    }

    public bool IsBestiaryOpen() => Visible(Bestiary) != null;

    public bool FireBestiary(int command, int value) => Fire(Visible(Bestiary), command, value);

    public IReadOnlyList<string> ContextMenuEntries()
    {
        var unit = Visible(Menu);
        if (unit == null || unit->AtkValuesCount == 0)
            return [];

        // Entry count at AtkValues[0], entry i's text at AtkValues[8 + i].
        var count = (int)unit->AtkValues[0].UInt;
        var entries = new List<string>(count);
        for (var i = 0; i < count && 8 + i < unit->AtkValuesCount; i++)
        {
            var value = unit->AtkValues[8 + i];
            entries.Add(value.Type is AtkValueType.String or AtkValueType.ManagedString or AtkValueType.ConstString
                        && value.String.Value != null
                ? Dalamud.Memory.MemoryHelper.ReadSeStringNullTerminated((nint)value.String.Value).TextValue
                : string.Empty);
        }
        return entries;
    }

    public bool PickContextMenu(int index) => Fire(Visible(Menu), 0, index, 0);

    public bool OpenBestiary()
    {
        try
        {
            var id = BestiaryMainCommand();
            var ui = UIModule.Instance();
            if (id == 0 || ui == null || !ui->IsMainCommandUnlocked(id))
                return false;
            ui->ExecuteMainCommand(id);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void CloseBestiary()
    {
        var unit = Visible(Bestiary);
        if (unit != null)
            unit->Close(true);
    }

    private static uint _bestiaryMainCommand;

    /// <summary>
    /// The MainCommand row behind the Character menu's "Master's Bestiary", looked up by its English
    /// name (whatever the client language) rather than hard-coding a row id a patch could move.
    /// </summary>
    private static uint BestiaryMainCommand()
    {
        if (_bestiaryMainCommand != 0)
            return _bestiaryMainCommand;
        var sheet = Daedalus.Localization.GameDataLocalizer.Instance?.DataManager
            .GetExcelSheet<Lumina.Excel.Sheets.MainCommand>(Dalamud.Game.ClientLanguage.English);
        if (sheet == null)
            return 0;
        foreach (var row in sheet)
            if (row.Name.ExtractText() == "Master's Bestiary")
                return _bestiaryMainCommand = row.RowId;
        return 0;
    }

    private static AtkUnitBase* Visible(string name)
    {
        try
        {
            var manager = RaptureAtkUnitManager.Instance();
            if (manager == null)
                return null;
            var unit = manager->GetAddonByName(name);
            return unit != null && unit->IsVisible ? unit : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool Fire(AtkUnitBase* unit, params int[] ints)
    {
        if (unit == null)
            return false;

        var values = stackalloc AtkValue[ints.Length];
        for (var i = 0; i < ints.Length; i++)
        {
            values[i] = default;
            values[i].Type = AtkValueType.Int;
            values[i].Int = ints[i];
        }
        unit->FireCallback((uint)ints.Length, values);
        return true;
    }
}
