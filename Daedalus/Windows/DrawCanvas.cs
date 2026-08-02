using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Common.Component.BGCollision;
using Daedalus.Config;
using Daedalus.Data;
using Daedalus.Rotation;
using Daedalus.Rotation.Common;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Services.Drawing;
using Daedalus.Services.Positional;
using Daedalus.Services.Targeting;

namespace Daedalus.Windows;

/// <summary>
/// Fullscreen transparent overlay for world-space drawing (Pictomancy or 2D fallback).
/// Includes AoE test mode with mouse-to-world simulation and fake enemies.
/// </summary>
public sealed class DrawCanvas : Window
{
    private readonly DrawingService _drawing;
    private readonly Configuration _configuration;
    private readonly IObjectTable _objectTable;
    private readonly IClientState _clientState;
    private readonly ITargetManager _targetManager;
    private readonly IGameGui _gameGui;
    private readonly IPositionalService _positionalService;
    private readonly RotationManager _rotationManager;
    private readonly IPartyList _partyList;
    private readonly IDataManager _dataManager;

    // Coffer BaseId → scenery model id, resolved once per coffer type.
    private readonly Dictionary<uint, uint> _sceneryIdByBaseId = [];

    // AoE test mode state
    private Vector3 _simPlayerPos;
    private bool _hasSimPos;
    private float _optimalAngle;
    private int _maxHitCount;

    // Fake enemies
    public readonly List<FakeEnemy> FakeEnemies = [];

    // Test mode config (exposed for config UI)
    public bool TestModeEnabled;
    public bool UseRealPlayer;
    public TestShapeMode ShapeMode = TestShapeMode.Cone;
    public float ConeAngle = 90f;
    public float ConeRange = 8f;
    public float RectWidth = 4f;
    public float RectLength = 25f;
    public float SimHitboxRadius = 0.5f;

    // Melee job IDs
    private static readonly HashSet<uint> MeleeJobs = [1, 2, 3, 4, 19, 20, 21, 22, 29, 30, 32, 34, 37, 39, 41];
    // Ranged/caster job IDs
    private static readonly HashSet<uint> RangedJobs = [23, 24, 25, 27, 28, 31, 33, 35, 38, 40, 42];

    // Colors
    private const uint ColorSimPlayer = 0xC0FFFF00u;
    private const uint ColorFakeHitbox = 0xC000A5FFu;
    private const uint ColorShapeFill = 0x8000FF00u;
    private const uint ColorShapeOutline = 0xC000FF00u;

    // Max-melee debug ring colors (ABGR). Distinct hues so the rings are easy to tell apart.
    private const uint ColorRingHitbox = 0xC00000FFu;   // red — enemy hitbox
    private const uint ColorRingCombined = 0xC000FFFFu; // yellow — combined hitbox (inner melee bound)
    private const uint ColorRingMaxMelee = 0xC000FF00u; // green — max-melee stand ring (park here)
    private const uint ColorRingGrace = 0x6000FF00u;    // faint green — grace dead-band edges

    public DrawCanvas(
        DrawingService drawing,
        Configuration configuration,
        IObjectTable objectTable,
        IClientState clientState,
        ITargetManager targetManager,
        IGameGui gameGui,
        IPositionalService positionalService,
        RotationManager rotationManager,
        IPartyList partyList,
        IDataManager dataManager)
        : base("##DaedalusDrawCanvas",
            ImGuiWindowFlags.NoInputs
            | ImGuiWindowFlags.NoTitleBar
            | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoBackground
            | ImGuiWindowFlags.AlwaysUseWindowPadding
            | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoFocusOnAppearing
            | ImGuiWindowFlags.NoDocking)
    {
        _drawing = drawing;
        _configuration = configuration;
        _objectTable = objectTable;
        _clientState = clientState;
        _targetManager = targetManager;
        _gameGui = gameGui;
        _positionalService = positionalService;
        _rotationManager = rotationManager;
        _partyList = partyList;
        _dataManager = dataManager;

        IsOpen = true;
        RespectCloseHotkey = false;
    }

    private DrawHelperConfig Config => _configuration.DrawHelper;

    public override void PreDraw()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGuiHelpers.SetNextWindowPosRelativeMainViewport(Vector2.Zero);
        ImGui.SetNextWindowSize(ImGuiHelpers.MainViewport.Size);
    }

    public override bool DrawConditions()
    {
        if (!_clientState.IsLoggedIn) return false;
        if (!Config.DrawingEnabled && !TestModeEnabled && !_configuration.Nav.MaxMeleeDebugRings) return false;
        return _objectTable.LocalPlayer != null;
    }

    public override void Draw()
    {
        _drawing.BeginFrame();
        try
        {
            var player = _objectTable.LocalPlayer;
            if (player == null) return;

            // Standard drawing features
            if (Config.DrawingEnabled)
            {
                if (Config.ShowEnemyHitboxes)
                    DrawEnemyHitboxes(player);

                if (Config.ShowAstCardRange && JobRegistry.IsAstrologian(player.ClassJob.RowId))
                    DrawAstCardRange(player);

                // Guide lines are a navigation aid, not a combat overlay — they clutter a fight and
                // the loot isn't going anywhere. BOCCHI suppresses its radar in combat for the same reason.
                var inCombat = (player.StatusFlags & StatusFlags.InCombat) != 0;

                if (Config.ShowTreasureLines && !inCombat)
                    DrawGuideLines(player, WorldLineSelector.IsChestLineCandidate,
                        Config.TreasureLineMaxDistance, ChestLineColor);

                if (Config.ShowCarrotLines && !inCombat)
                    DrawGuideLines(player, WorldLineSelector.IsCarrotLineCandidate,
                        Config.CarrotLineMaxDistance, _ => Config.CarrotLineColor);

                if (Config.LabelWorldObjects)
                    DrawWorldObjectLabels(player);

                var target = _targetManager.Target;
                if (target != null)
                {
                    var jobId = player.ClassJob.RowId;

                    // Auto-detect: melee range for melee/tank, ranged range for ranged/caster
                    if (Config.ShowMeleeRange && MeleeJobs.Contains(jobId))
                        DrawMeleeRange(player, target);

                    if (Config.ShowRangedRange && RangedJobs.Contains(jobId))
                        DrawRangedRange(player, target);

                    if (Config.ShowPositionals && MeleeJobs.Contains(jobId) && target is IBattleChara targetBc)
                        DrawPositionals(player, targetBc);
                }
            }

            // Max-melee debug rings (independent of the general Draw Helper toggle; driven by Nav config).
            if (_configuration.Nav.MaxMeleeDebugRings)
            {
                var ringTarget = _targetManager.Target;
                if (ringTarget != null)
                    DrawMaxMeleeRings(player, ringTarget);
            }

            // AoE test mode
            if (TestModeEnabled)
                DrawTestMode(player);
        }
        finally
        {
            _drawing.EndFrame();
        }
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar();
    }

    // ── AoE Test Mode ──

    private void DrawTestMode(Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter player)
    {
        // Get simulated player position
        if (UseRealPlayer)
        {
            _simPlayerPos = player.Position;
            _hasSimPos = true;
        }
        else
        {
            var mousePos = ImGui.GetMousePos();
            if (!ImGui.GetIO().WantCaptureMouse && _gameGui.ScreenToWorld(mousePos, out var worldPos))
            {
                _simPlayerPos = worldPos;
                _hasSimPos = true;
            }
        }

        if (!_hasSimPos) return;

        // Collect enemies (real + fake)
        var enemies = CollectTestEnemies(player);
        if (enemies.Count == 0) return;

        // Draw enemy hitboxes (real ones handled by standard drawing, draw fakes here)
        foreach (var e in enemies)
        {
            if (e.IsFake)
            {
                _drawing.DrawCircle(e.Position, e.HitboxRadius, ColorFakeHitbox);
            }
            else if (!Config.DrawingEnabled || !Config.ShowEnemyHitboxes)
            {
                // Draw real enemy hitboxes if standard drawing isn't already doing it
                _drawing.DrawCircle(e.Position, e.HitboxRadius, Config.EnemyHitboxColor);
            }
        }

        // Draw simulated player hitbox
        _drawing.DrawCircleFilled(_simPlayerPos, SimHitboxRadius, ColorSimPlayer);

        // Find optimal angle and draw shape
        if (ShapeMode == TestShapeMode.Cone)
        {
            FindOptimalCone(enemies);
            DrawTestCone();
        }
        else
        {
            FindOptimalRect(enemies);
            DrawTestRect();
        }

        // Draw hit counter above simulated player position
        var textPos = _simPlayerPos with { Y = _simPlayerPos.Y + 1.5f };
        if (_gameGui.WorldToScreen(textPos, out var screenPos))
        {
            var text = $"Hits: {_maxHitCount}/{enemies.Count}";
            var textSize = ImGui.CalcTextSize(text);
            var dl = ImGui.GetBackgroundDrawList();
            var pos = screenPos - textSize * 0.5f;
            // Shadow
            dl.AddText(pos + new Vector2(1, 1), 0xFF000000, text);
            // Text — green if hitting, blue if 0
            dl.AddText(pos, _maxHitCount > 0 ? 0xFF00FF00u : 0xFF0000FFu, text);
        }
    }

    private void FindOptimalCone(List<TestEnemy> enemies)
    {
        var halfAngle = ConeAngle * 0.5f * MathF.PI / 180f;
        _maxHitCount = 0;
        _optimalAngle = 0f;

        // Pre-compute per-enemy angles and hitbox sizes
        var enemyAngles = new List<(float angle, float hitboxAngle)>();
        foreach (var e in enemies)
        {
            var dx = e.Position.X - _simPlayerPos.X;
            var dz = e.Position.Z - _simPlayerPos.Z;
            var dist = MathF.Sqrt(dx * dx + dz * dz);
            if (dist < 0.01f) continue;
            var angle = MathF.Atan2(dx, dz);
            var hbAngle = dist > 0.1f ? MathF.Asin(MathF.Min(1f, e.HitboxRadius / dist)) : 0f;
            enemyAngles.Add((angle, hbAngle));
        }

        // Candidate angles: per-enemy edge placements + per-pair midpoints
        var candidateAngles = new List<float>();
        foreach (var (angle, _) in enemyAngles)
        {
            candidateAngles.Add(angle);
            candidateAngles.Add(angle - halfAngle);
            candidateAngles.Add(angle + halfAngle);
        }

        // For every pair: test angles that place both enemies inside the cone
        for (var i = 0; i < enemyAngles.Count; i++)
        {
            for (var j = i + 1; j < enemyAngles.Count; j++)
            {
                var a = enemyAngles[i];
                var b = enemyAngles[j];
                // Place A at right edge → cone center = A - halfAngle
                // Place B at left edge → cone center = B + halfAngle
                // Midpoint splits the difference
                var mid1 = (a.angle - halfAngle + b.angle + halfAngle) * 0.5f;
                var mid2 = (b.angle - halfAngle + a.angle + halfAngle) * 0.5f;
                candidateAngles.Add(mid1);
                candidateAngles.Add(mid2);
                // Simple midpoint between centers
                candidateAngles.Add(a.angle + NormalizeAngle(b.angle - a.angle) * 0.5f);
            }
        }

        // When hit counts are tied, prefer the angle that centers the most on enemies
        var bestCenterScore = float.MaxValue;

        foreach (var testAngle in candidateAngles)
        {
            var count = 0;
            var centerScore = 0f; // lower = more centered on enemies
            foreach (var e in enemies)
            {
                var dx = e.Position.X - _simPlayerPos.X;
                var dz = e.Position.Z - _simPlayerPos.Z;
                var dist = MathF.Sqrt(dx * dx + dz * dz);
                if (dist - e.HitboxRadius > ConeRange) continue;
                if (dist < e.HitboxRadius) { count++; continue; }

                var angleToE = MathF.Atan2(dx, dz);
                var diff = NormalizeAngle(angleToE - testAngle);
                if (MathF.Abs(diff) <= halfAngle)
                {
                    count++;
                    centerScore += MathF.Abs(diff); // penalize off-center hits
                }
            }

            if (count > _maxHitCount || (count == _maxHitCount && centerScore < bestCenterScore))
            {
                _maxHitCount = count;
                _optimalAngle = testAngle;
                bestCenterScore = centerScore;
            }
        }
    }

    private void FindOptimalRect(List<TestEnemy> enemies)
    {
        var halfWidth = RectWidth * 0.5f;
        _maxHitCount = 0;
        _optimalAngle = 0f;

        // Build candidate angles: for each enemy, test angles that place it at the
        // left edge, right edge, and center of the rectangle
        var candidateAngles = new List<float>();
        foreach (var e in enemies)
        {
            var dx = e.Position.X - _simPlayerPos.X;
            var dz = e.Position.Z - _simPlayerPos.Z;
            var dist = MathF.Sqrt(dx * dx + dz * dz);
            if (dist < 0.01f) continue;

            var angleToCenter = MathF.Atan2(dx, dz);
            candidateAngles.Add(angleToCenter);

            // Angle offset that places this enemy at the lateral edge of the rect
            if (dist > 0.1f)
            {
                var edgeAngle = MathF.Asin(MathF.Min(1f, (halfWidth + e.HitboxRadius) / dist));
                candidateAngles.Add(angleToCenter + edgeAngle);
                candidateAngles.Add(angleToCenter - edgeAngle);
            }
        }

        var bestCenterScore = float.MaxValue;

        foreach (var testAngle in candidateAngles)
        {
            var sinH = MathF.Sin(testAngle);
            var cosH = MathF.Cos(testAngle);
            var count = 0;
            var centerScore = 0f;

            foreach (var e in enemies)
            {
                var dx = e.Position.X - _simPlayerPos.X;
                var dz = e.Position.Z - _simPlayerPos.Z;
                var forward = dx * sinH + dz * cosH;
                var lateral = dx * cosH - dz * sinH;

                if (forward >= -e.HitboxRadius
                    && forward <= RectLength + e.HitboxRadius
                    && MathF.Abs(lateral) <= halfWidth + e.HitboxRadius)
                {
                    count++;
                    centerScore += MathF.Abs(lateral); // prefer centered hits
                }
            }

            if (count > _maxHitCount || (count == _maxHitCount && centerScore < bestCenterScore))
            {
                _maxHitCount = count;
                _optimalAngle = testAngle;
                bestCenterScore = centerScore;
            }
        }
    }

    private void DrawTestCone()
    {
        var halfAngle = ConeAngle * 0.5f * MathF.PI / 180f;
        _drawing.DrawFanFilled(_simPlayerPos, 0f, ConeRange,
            _optimalAngle - halfAngle, _optimalAngle + halfAngle, ColorShapeFill);
        _drawing.DrawFan(_simPlayerPos, 0f, ConeRange,
            _optimalAngle - halfAngle, _optimalAngle + halfAngle, ColorShapeOutline);
    }

    private void DrawTestRect()
    {
        _drawing.DrawRect(_simPlayerPos, _optimalAngle, RectWidth * 0.5f, RectLength, ColorShapeFill, filled: true);
        _drawing.DrawRect(_simPlayerPos, _optimalAngle, RectWidth * 0.5f, RectLength, ColorShapeOutline, filled: false);
    }

    // ── Enemy collection for test mode ──

    private List<TestEnemy> CollectTestEnemies(Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter player)
    {
        var enemies = new List<TestEnemy>();

        foreach (var obj in _objectTable)
        {
            if (obj is not IBattleNpc npc) continue;
            var npcKind = (byte)npc.BattleNpcKind;
            if (npcKind == Daedalus.Compat.BattleNpcKinds.Pet
                || npcKind == Daedalus.Compat.BattleNpcKinds.Chocobo
                || npcKind == Daedalus.Compat.BattleNpcKinds.NpcPartyMember)
                continue;
            if ((npc.StatusFlags & StatusFlags.Hostile) == 0) continue;
            if (!EnemyAttackability.IsPlayerAttackable(npc)) continue;

            var dist = Vector3.Distance(_simPlayerPos, npc.Position);
            if (dist > 50f) continue;

            enemies.Add(new TestEnemy { Position = npc.Position, HitboxRadius = npc.HitboxRadius, IsFake = false });
        }

        foreach (var fake in FakeEnemies)
            enemies.Add(new TestEnemy { Position = fake.Position, HitboxRadius = fake.Radius, IsFake = true });

        return enemies;
    }

    // ── Standard drawing features ──

    private void DrawEnemyHitboxes(Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter player)
    {
        foreach (var obj in _objectTable)
        {
            if (obj is not IBattleNpc npc) continue;
            var npcKind = (byte)npc.BattleNpcKind;
            if (npcKind == Daedalus.Compat.BattleNpcKinds.Pet
                || npcKind == Daedalus.Compat.BattleNpcKinds.Chocobo
                || npcKind == Daedalus.Compat.BattleNpcKinds.NpcPartyMember)
                continue;
            if ((npc.StatusFlags & StatusFlags.Hostile) == 0) continue;
            if (!EnemyAttackability.IsPlayerAttackable(npc)) continue;

            var dist = Vector3.Distance(player.Position, npc.Position);
            if (dist > 50f) continue;

            _drawing.DrawCircle(npc.Position, npc.HitboxRadius, Config.EnemyHitboxColor);
        }
    }

    /// <summary>
    /// A line from the player to every object the filter accepts, plus a ring at its foot.
    /// Both ends are lifted off the ground so the line reads against the floor rather than clipping into it.
    /// </summary>
    private void DrawGuideLines(
        IPlayerCharacter player,
        Func<IGameObject, Vector3, float, bool> filter,
        float maxDistance,
        Func<IGameObject, uint> colorFor)
    {
        var origin = player.Position with { Y = player.Position.Y + 1f };

        foreach (var obj in _objectTable)
        {
            if (!filter(obj, player.Position, maxDistance)) continue;

            var color = colorFor(obj);
            var destination = SnapToFloor(obj.Position);
            _drawing.DrawLine(origin, destination with { Y = destination.Y + 0.5f }, color);
            _drawing.DrawCircle(destination, 1f, color);
        }
    }

    /// <summary>Coffer line colour by tier — bronze, silver, or purple when the model isn't recognised.</summary>
    private uint ChestLineColor(IGameObject chest) => ChestTier(chest) switch
    {
        TreasureTier.Bronze => Config.BronzeChestLineColor,
        TreasureTier.Silver => Config.SilverChestLineColor,
        TreasureTier.Gold => Config.GoldChestLineColor,
        _ => Config.UnknownChestLineColor,
    };

    /// <summary>
    /// Gold coffers are EventObj and carry no Treasure-sheet row, so they resolve by BaseId;
    /// everything else goes through the scenery model.
    /// </summary>
    private TreasureTier ChestTier(IGameObject chest) => chest.ObjectKind == ObjectKind.EventObj
        ? WorldLineSelector.TierFromCofferBaseId(chest.BaseId)
        : WorldLineSelector.TierFromSceneryId(ResolveSceneryId(chest.BaseId));

    /// <summary>
    /// Scenery (SGB) model id for a coffer, from the Treasure sheet. Cached — this runs per
    /// coffer per frame, and the mapping never changes within a session.
    /// </summary>
    private uint ResolveSceneryId(uint baseId)
    {
        if (_sceneryIdByBaseId.TryGetValue(baseId, out var cached)) return cached;

        var sceneryId = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Treasure>()?.GetRowOrDefault(baseId)?.SGB.RowId ?? 0u;
        _sceneryIdByBaseId[baseId] = sceneryId;
        return sceneryId;
    }

    /// <summary>
    /// Diagnostic: stamps ObjectKind + name over every nearby non-creature object. This is how you
    /// find out what kind a given chest actually reports — turn it on, stand next to one, read the label.
    /// </summary>
    private void DrawWorldObjectLabels(IPlayerCharacter player)
    {
        const float labelRange = 30f;
        var drawList = ImGui.GetBackgroundDrawList();

        foreach (var obj in _objectTable)
        {
            if (Vector3.DistanceSquared(player.Position, obj.Position) > labelRange * labelRange) continue;

            // Creatures are only labelled when the CLIENT has marked them — the nameplate icon
            // (quest gold icon, hunt-bill marks, and whatever that magenta triangle is). The id
            // is the point: it is how a marker seen in the world becomes a filter in code, the
            // same way the carrot BaseId and the Knowledge Crystal id were captured.
            string text;
            if (obj.ObjectKind is ObjectKind.BattleNpc or ObjectKind.EventNpc)
            {
                var iconId = ReadNamePlateIconId(obj);
                if (iconId == 0) continue;
                text = $"Icon#{iconId}: {obj.Name.TextValue}";
            }
            else
            {
                if (!WorldLineSelector.IsLabelCandidate(obj.ObjectKind)) continue;
                // BaseId and targetability are what a line filter can key on, so the label carries both.
                var targetable = obj.IsTargetable ? " [T]" : string.Empty;
                text = $"{obj.ObjectKind}#{obj.BaseId}{targetable}: {obj.Name.TextValue}";
            }

            if (!_gameGui.WorldToScreen(obj.Position with { Y = obj.Position.Y + 1f }, out var screenPos)) continue;
            drawList.AddText(screenPos + new Vector2(1, 1), 0xFF000000u, text);
            drawList.AddText(screenPos, 0xFFFFFFFFu, text);
        }
    }

    /// <summary>
    /// Max-melee positioning debug rings around the current target: enemy hitbox, combined hitbox
    /// (inner melee bound), the max-melee stand ring (where the toon parks), plus the vNav Flex grace
    /// dead-band edges. Purely additive — does not touch the existing range/positional ring draws.
    /// </summary>
    private void DrawMaxMeleeRings(Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter player, IGameObject target)
    {
        var center = SnapToFloor(target.Position);
        var combined = target.HitboxRadius + player.HitboxRadius;
        var standDistance = PositionalStandCalculator.MaxMeleeStandDistance(target.HitboxRadius, player.HitboxRadius);
        var flex = MathF.Max(0f, _configuration.Nav.VNavFlex);

        // Grace band edges first (faint, drawn under the solid rings).
        _drawing.DrawCircle(center, MathF.Max(0.05f, standDistance - flex), ColorRingGrace, 1f);
        _drawing.DrawCircle(center, standDistance + flex, ColorRingGrace, 1f);

        _drawing.DrawCircle(center, target.HitboxRadius, ColorRingHitbox, 2f);
        _drawing.DrawCircle(center, combined, ColorRingCombined, 2f);
        _drawing.DrawCircle(center, standDistance, ColorRingMaxMelee, 2.5f);
    }

    private void DrawMeleeRange(Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter player, IGameObject target)
    {
        const float meleeRange = 3f;
        var totalRadius = target.HitboxRadius + player.HitboxRadius + meleeRange;
        var ringCenter = SnapToFloor(target.Position);

        var dx = player.Position.X - target.Position.X;
        var dz = player.Position.Z - target.Position.Z;
        var edgeDist = MathF.Sqrt(dx * dx + dz * dz) - player.HitboxRadius - target.HitboxRadius;
        var inRange = edgeDist <= meleeRange;

        if (inRange && Config.MeleeRangeFade)
        {
            var buffer = meleeRange - edgeDist;
            var alpha = MathF.Max(0f, (1f - buffer) / 1f) * 0.75f;
            if (alpha <= 0.01f) return;
            var fadeColor = ((uint)(alpha * 255f) << 24) | (Config.MeleeRangeColor & 0x00FFFFFFu);
            _drawing.DrawCircle(ringCenter, totalRadius, fadeColor);
        }
        else if (inRange)
        {
            _drawing.DrawCircle(ringCenter, totalRadius, Config.MeleeRangeColor);
        }
        else
        {
            _drawing.DrawCircle(ringCenter, totalRadius, Config.MeleeRangeOutOfRangeColor);
        }
    }

    private void DrawRangedRange(Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter player, IGameObject target)
    {
        const float rangedRange = 25f; // All ranged/caster jobs use 25y
        var totalRadius = target.HitboxRadius + player.HitboxRadius + rangedRange;
        var ringCenter = SnapToFloor(target.Position);

        var dx = player.Position.X - target.Position.X;
        var dz = player.Position.Z - target.Position.Z;
        var edgeDist = MathF.Sqrt(dx * dx + dz * dz) - player.HitboxRadius - target.HitboxRadius;
        var inRange = edgeDist <= rangedRange;

        if (inRange)
        {
            var buffer = rangedRange - edgeDist;
            var alpha = MathF.Max(0f, (1.5f - buffer) / 1.5f) * 0.75f;
            if (alpha <= 0.01f) return;
            var fadeColor = ((uint)(alpha * 255f) << 24) | (Config.RangedRangeColor & 0x00FFFFFFu);
            _drawing.DrawCircle(ringCenter, totalRadius, fadeColor);
        }
        else
        {
            _drawing.DrawCircle(ringCenter, totalRadius, Config.RangedRangeOutOfRangeColor);
        }
    }

    private void DrawAstCardRange(IPlayerCharacter player)
    {
        var range = ASTActions.TheBalance.Range;
        var rangeSquared = range * range;
        var center = SnapToFloor(player.Position);

        _drawing.DrawCircleFilled(center, range, Config.AstCardRangeFillColor);
        _drawing.DrawCircle(center, range, Config.AstCardRangeColor);

        foreach (var ally in GetCardRangeAllies(player))
        {
            var distSquared = Vector3.DistanceSquared(player.Position, ally.Position);
            var inRange = distSquared <= rangeSquared;
            var markerColor = inRange ? Config.AstCardAllyInRangeColor : Config.AstCardAllyOutOfRangeColor;
            var markerRadius = MathF.Max(0.4f, ally.HitboxRadius * 0.5f);
            _drawing.DrawCircle(SnapToFloor(ally.Position), markerRadius, markerColor);
        }
    }

    private IEnumerable<IBattleChara> GetCardRangeAllies(IPlayerCharacter player)
    {
        if (_partyList.Length > 0)
        {
            foreach (var member in _partyList)
            {
                if (member.GameObject is not IBattleChara battleChara)
                    continue;
                if (battleChara.EntityId == player.EntityId || battleChara.IsDead)
                    continue;
                yield return battleChara;
            }

            yield break;
        }

        foreach (var obj in _objectTable)
        {
            if (!BasePartyHelper.IsValidTrustNpc(obj, out var npc, includeDead: false))
                continue;
            if (npc!.EntityId == player.EntityId)
                continue;
            yield return npc;
        }
    }

    private void DrawPositionals(Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter player, IBattleChara target)
    {
        if (_positionalService.HasPositionalImmunity(target)) return;

        // BossMod pattern: hitboxRadius + 3.5y
        var radius = target.HitboxRadius + 3.5f;
        var facing = target.Rotation;
        var pos = SnapToFloor(target.Position);
        var halfAngle = 45f * MathF.PI / 180f; // 45° half-angle = 90° sector

        // Get the required positional from the active rotation (if melee)
        PositionalType? required = null;
        var activeRotation = _rotationManager.ActiveRotation;
        if (activeRotation is IHasPositionals posRotation && posRotation.Positionals.HasTarget)
            required = posRotation.Positionals.RequiredPositional;

        // Check if player is at the correct positional
        var currentPos = _positionalService.GetPositional(player, target);
        var isCorrect = required == null || currentPos == required.Value;

        if (required != null)
        {
            // BossMod style: only draw the required zone, green if correct, red if wrong
            var color = isCorrect ? 0x8000FF00u : 0x800000FFu;
            var outlineColor = isCorrect ? 0xC000FF00u : 0xC00000FFu;

            switch (required.Value)
            {
                case PositionalType.Rear:
                    // Rear cone centered at facing + 180°
                    DrawPositionalCone(pos, radius, facing + MathF.PI, halfAngle, color, outlineColor);
                    break;
                case PositionalType.Flank:
                    // Two flank cones at facing ± 90°
                    DrawPositionalCone(pos, radius, facing + MathF.PI / 2f, halfAngle, color, outlineColor);
                    DrawPositionalCone(pos, radius, facing - MathF.PI / 2f, halfAngle, color, outlineColor);
                    break;
            }
        }
        else
        {
            // No required positional known — show rear + flanks equally
            DrawPositionalCone(pos, radius, facing + MathF.PI, halfAngle, Config.PositionalRearColor, ApplyAlpha(Config.PositionalRearColor, 0xC0));
            DrawPositionalCone(pos, radius, facing + MathF.PI / 2f, halfAngle, Config.PositionalFlankColor, ApplyAlpha(Config.PositionalFlankColor, 0xC0));
            DrawPositionalCone(pos, radius, facing - MathF.PI / 2f, halfAngle, Config.PositionalFlankColor, ApplyAlpha(Config.PositionalFlankColor, 0xC0));
        }
    }

    private void DrawPositionalCone(Vector3 center, float radius, float direction, float halfAngle, uint fillColor, uint outlineColor)
    {
        _drawing.DrawFanFilled(center, 0f, radius, direction - halfAngle, direction + halfAngle, fillColor);
        _drawing.DrawFan(center, 0f, radius, direction - halfAngle, direction + halfAngle, outlineColor);
    }

    private static uint ApplyAlpha(uint color, uint alpha)
    {
        return (alpha << 24) | (color & 0x00FFFFFFu);
    }

    /// <summary>
    /// The client's own marker over a mob — quest gold icon, hunt-bill tag, etc. Fail closed.
    /// </summary>
    private static unsafe uint ReadNamePlateIconId(IGameObject obj)
    {
        try
        {
            if (obj.Address == nint.Zero)
                return 0;
            var native = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)obj.Address;
            return native == null ? 0u : native->NamePlateIconId;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Snap position Y to ground via BGCollision raycast.
    /// Prevents rings floating in housing zones or on elevated targets.
    /// </summary>
    private static unsafe Vector3 SnapToFloor(Vector3 position, float maxDistance = 10f)
    {
        try
        {
            var direction = new Vector3(0, -1, 0);
            var origin = position with { Y = position.Y + 1f };
            if (BGCollisionModule.RaycastMaterialFilter(origin, direction, out var hit, maxDistance))
                return position with { Y = hit.Point.Y };
        }
        catch { /* BGCollision unavailable */ }
        return position;
    }

    // ── Helpers ──

    private static float NormalizeAngle(float angle)
    {
        while (angle > MathF.PI) angle -= 2f * MathF.PI;
        while (angle < -MathF.PI) angle += 2f * MathF.PI;
        return angle;
    }

    // ── Types ──

    public enum TestShapeMode { Cone, Rect }

    private struct TestEnemy
    {
        public Vector3 Position;
        public float HitboxRadius;
        public bool IsFake;
    }

    public struct FakeEnemy
    {
        public Vector3 Position;
        public float Radius;
        public string Name;
    }
}
