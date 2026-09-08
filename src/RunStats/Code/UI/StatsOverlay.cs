using System;
using Godot;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using RunStats.Infrastructure;
using RunStats.Integration;
using RunStats.Models;

namespace RunStats.UI;

public sealed partial class StatsOverlay : Control, IOverlayScreen
{
    private readonly StatsViewModel _viewModel;
    private CenterContainer? _center;
    private PanelContainer? _panel;
    private TabContainer? _tabs;
    private Button? _closeButton;
    private bool _uiBuilt;
    private bool _closing;

    private StatsOverlay(StatsViewModel viewModel)
    {
        _viewModel = viewModel;
        Name = "RunStatsOverlay";
        MouseFilter = MouseFilterEnum.Stop;
        ZIndex = 1;
        GuiInput += HandleInput;
    }

    public NetScreenType ScreenType => NetScreenType.None;

    public bool UseSharedBackstop => true;

    public Control? DefaultFocusedControl => _closeButton;

    public static bool TryOpen(RunStatsSnapshot snapshot)
    {
        if (!StatsViewModel.TryCreate(snapshot, out var viewModel) || viewModel is null)
        {
            return false;
        }

        var stack = NOverlayStack.Instance;
        if (stack is null || !CanOpenOn(stack))
        {
            return false;
        }

        stack.Push(new StatsOverlay(viewModel));
        return true;
    }

    internal static bool CanOpenOn(NOverlayStack stack) =>
        stack.ScreenCount == 0 ||
        (stack.ScreenCount == 1 && stack.Peek() is NGameOverScreen);

    public override void _Ready()
    {
        EnsureUi();
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        HandleInput(inputEvent);
    }

    private void HandleInput(InputEvent inputEvent)
    {
        if (inputEvent.IsActionPressed(MegaInput.cancel) ||
            inputEvent.IsActionPressed(MegaInput.pauseAndBack) ||
            inputEvent.IsActionPressed(MegaInput.back))
        {
            Close();
            GetViewport()?.SetInputAsHandled();
        }
    }

    public void AfterOverlayOpened()
    {
        EnsureUi();
        Show();
        Callable.From(() => _closeButton?.GrabFocus()).CallDeferred();
    }

    public void AfterOverlayClosed()
    {
        QueueFree();
    }

    public void AfterOverlayShown()
    {
        EnsureUi();
        ApplyRootLayout();
        ResizePanel();
        Show();
        Callable.From(() => _closeButton?.GrabFocus()).CallDeferred();
    }

    public void AfterOverlayHidden()
    {
        Hide();
    }

    private void BuildUi()
    {
        _uiBuilt = true;
        _center = new CenterContainer
        {
            Name = "ContentCenter",
            MouseFilter = MouseFilterEnum.Pass
        };
        AddChild(_center);
        _center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _center.Position = Vector2.Zero;
        _center.Size = Size;

        _panel = new PanelContainer { Name = "StatsPanel" };
        _panel.AddThemeStyleboxOverride("panel", CreatePanelStyle());
        _center.AddChild(_panel);

        var content = new VBoxContainer
        {
            Name = "Content",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        content.AddThemeConstantOverride("separation", 14);
        _panel.AddChild(content);

        var title = new Label
        {
            Text = "RUN STATISTICS",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        title.AddThemeFontSizeOverride("font_size", 36);
        title.AddThemeColorOverride("font_color", new Color("f3d58a"));
        content.AddChild(title);

        var subtitle = new Label
        {
            Text = _viewModel.Subtitle,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        subtitle.AddThemeFontSizeOverride("font_size", 16);
        subtitle.AddThemeColorOverride("font_color", new Color("b9b4aa"));
        content.AddChild(subtitle);
        content.AddChild(new HSeparator());

        _tabs = new TabContainer
        {
            Name = "StatsTabs",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            FocusMode = FocusModeEnum.All
        };
        _tabs.AddThemeFontSizeOverride("font_size", 18);
        _tabs.GuiInput += HandleInput;
        content.AddChild(_tabs);

        for (var index = 0; index < _viewModel.Sections.Count; index++)
        {
            var section = _viewModel.Sections[index];
            var page = CreateSectionPage(section);
            _tabs.AddChild(page);
            _tabs.SetTabTitle(index, section.Title);
        }

        _closeButton = new Button
        {
            Name = "CloseButton",
            Text = "CLOSE",
            CustomMinimumSize = new Vector2(220, 52),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            FocusMode = FocusModeEnum.All
        };
        _closeButton.AddThemeFontSizeOverride("font_size", 20);
        _closeButton.Pressed += Close;
        _closeButton.GuiInput += HandleInput;
        content.AddChild(_closeButton);

        _tabs.FocusNeighborBottom = _closeButton.GetPath();
        _closeButton.FocusNeighborTop = _tabs.GetPath();
    }

    private Control CreateSectionPage(StatsSectionViewModel section)
    {
        var scroll = new ScrollContainer
        {
            Name = SafeNodeName(section.Title),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            FocusMode = FocusModeEnum.All
        };
        scroll.GuiInput += HandleInput;

        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 18);
        scroll.AddChild(body);

        var grid = new GridContainer
        {
            Name = SafeNodeName(section.Title) + "Grid",
            Columns = _viewModel.ColumnHeaders.Count + 1,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        grid.AddThemeConstantOverride("h_separation", 18);
        grid.AddThemeConstantOverride("v_separation", 4);
        body.AddChild(grid);

        grid.AddChild(CreateCell("STATISTIC", true, false));
        for (var index = 0; index < _viewModel.ColumnHeaders.Count; index++)
        {
            grid.AddChild(CreateCell(GetColumnHeader(index), true, true));
        }

        for (var rowIndex = 0; rowIndex < section.Rows.Count; rowIndex++)
        {
            var row = section.Rows[rowIndex];
            var shaded = rowIndex % 2 == 0;
            grid.AddChild(CreateCell(row.Label, false, false, shaded));
            foreach (var value in row.PlayerValues)
            {
                grid.AddChild(CreateCell(value, false, true, shaded));
            }

            if (row.TeamValue is not null)
            {
                grid.AddChild(CreateCell(row.TeamValue, false, true, shaded, true));
            }
        }

        if (section.Title == "CARDS")
        {
            body.AddChild(CreateMostPlayedCards());
        }

        return scroll;
    }

    private Control CreateMostPlayedCards()
    {
        var group = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var heading = new Label
        {
            Text = "MOST PLAYED CARD",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        heading.AddThemeFontSizeOverride("font_size", 20);
        heading.AddThemeColorOverride("font_color", new Color("d9c5a1"));
        group.AddChild(heading);

        var cards = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        cards.AddThemeConstantOverride("separation", 34);
        group.AddChild(cards);

        for (var index = 0; index < _viewModel.PlayerMostPlayedCardIds.Count; index++)
        {
            cards.AddChild(CreateCardDisplay(
                GetPlayerHeading(index),
                _viewModel.PlayerMostPlayedCardIds[index],
                _viewModel.PlayerNetIds[index]));
        }

        if (_viewModel.TeamMostPlayedCardId is not null)
        {
            cards.AddChild(CreateCardDisplay("TEAM", _viewModel.TeamMostPlayedCardId, null));
        }

        return group;
    }

    private static Control CreateCardDisplay(string heading, string? cardId, ulong? playerNetId)
    {
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(250, 390) };
        column.AddThemeConstantOverride("separation", 8);
        var label = new Label { Text = heading, HorizontalAlignment = HorizontalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", 17);
        label.AddThemeColorOverride("font_color", new Color("f3d58a"));
        column.AddChild(label);

        try
        {
            if (cardId is not null)
            {
                var model = playerNetId.HasValue
                    ? RunStatsRuntime.FindActiveDeckCard(playerNetId.Value, cardId)
                    : RunStatsRuntime.FindActiveDeckCard(cardId);
                var card = model is null ? null : NCard.Create(model, ModelVisibility.Visible);
                var holder = card is null ? null : NGridCardHolder.Create(card);
                if (model is not null && holder is not null)
                {
                    var frame = new Control { CustomMinimumSize = new Vector2(250, 340) };
                    frame.Resized += () => PositionCard(holder, frame);
                    card!.Ready += () => card.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
                    frame.AddChild(holder);
                    holder.Scale = holder.SmallScale * 0.72f;
                    PositionCard(holder, frame);
                    if (card.IsNodeReady())
                    {
                        card.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
                    }
                    column.AddChild(frame);
                    return column;
                }

                var canonical = ModelDb.GetByIdOrNull<CardModel>(ModelId.Deserialize(cardId));
                if (canonical is not null)
                {
                    column.AddChild(CreateEmptyCardLabel(canonical.Title));
                    return column;
                }
            }
        }
        catch (Exception exception)
        {
            RunStatsLog.Warn($"Could not render most-played card '{cardId}': {exception.Message}");
        }

        column.AddChild(CreateEmptyCardLabel("—"));
        return column;
    }

    private static void PositionCard(NGridCardHolder holder, Control frame)
    {
        holder.Position = new Vector2(frame.Size.X / 2f, 170f);
    }

    private string GetColumnHeader(int index)
    {
        if (index >= _viewModel.PlayerNetIds.Count)
        {
            return _viewModel.ColumnHeaders[index];
        }

        return GetPlayerHeading(index);
    }

    private string GetPlayerHeading(int index) =>
        RunStatsRuntime.GetPlayerDisplayName(_viewModel.PlayerNetIds[index]) ??
        _viewModel.ColumnHeaders[index];

    private static Label CreateEmptyCardLabel(string text)
    {
        var label = new Label
        {
            Text = text,
            CustomMinimumSize = new Vector2(245, 220),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        label.AddThemeFontSizeOverride("font_size", 20);
        return label;
    }

    private static string SafeNodeName(string value) =>
        value.Replace(" / ", "And", StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);

    private void EnsureUi()
    {
        if (_uiBuilt)
        {
            return;
        }

        ApplyRootLayout();
        BuildUi();
        Resized += ResizePanel;
        ResizePanel();
    }

    private static Control CreateCell(
        string text,
        bool header,
        bool centered,
        bool shaded = false,
        bool team = false)
    {
        var margin = new MarginContainer
        {
            CustomMinimumSize = new Vector2(centered ? 170 : 310, header ? 48 : 38),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Pass
        };
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_top", 6);
        margin.AddThemeConstantOverride("margin_bottom", 6);
        if (shaded)
        {
            var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
            panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(0.18f, 0.17f, 0.19f, 0.58f)
            });
            margin.AddChild(panel);
        }

        var label = new Label
        {
            Text = text,
            HorizontalAlignment = centered ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        label.AddThemeFontSizeOverride("font_size", header ? 19 : 17);
        label.AddThemeColorOverride(
            "font_color",
            team ? new Color("f3d58a") : header ? new Color("d9c5a1") : new Color("eee9df"));
        margin.AddChild(label);
        return margin;
    }

    private static StyleBoxFlat CreatePanelStyle()
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.075f, 0.07f, 0.09f, 0.98f),
            BorderColor = new Color("a77c3e")
        };
        style.SetBorderWidthAll(3);
        style.SetCornerRadiusAll(12);
        style.SetContentMarginAll(24);
        return style;
    }

    private void ResizePanel()
    {
        if (_panel is null)
        {
            return;
        }

        var panelSize = StatsLayout.CalculatePanelSize(Size.X, Size.Y);
        _panel.CustomMinimumSize = new Vector2(panelSize.Width, panelSize.Height);
    }

    private void ApplyRootLayout()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        if (GetParent() is Control parent)
        {
            Position = Vector2.Zero;
            Size = parent.Size;
        }

        if (_center is not null)
        {
            _center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            _center.Position = Vector2.Zero;
            _center.Size = Size;
        }
    }

    private void Close()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        var stack = NOverlayStack.Instance;
        if (stack is not null && stack.Peek() == this)
        {
            stack.Remove(this);
        }
        else
        {
            QueueFree();
        }
    }
}
