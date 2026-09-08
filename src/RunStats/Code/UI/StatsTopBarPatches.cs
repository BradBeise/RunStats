using System;
using System.Collections.Generic;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using RunStats.Infrastructure;
using RunStats.Integration;
using RunStats.Models;

namespace RunStats.UI;

[HarmonyPatch]
internal static class StatsTopBarPatches
{
    private const string ButtonName = "RunStatsStatsButton";
    private static readonly Dictionary<NTopBar, ButtonState> States = new();

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NTopBar), nameof(NTopBar._Ready))]
    private static void AfterReady(NTopBar __instance)
    {
        try
        {
            Install(__instance);
        }
        catch (Exception exception)
        {
            RunStatsLog.Error("The STATS top-bar button could not be installed.", exception);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NTopBar), "UpdateNavigation")]
    private static void AfterNavigationUpdated(NTopBar __instance)
    {
        if (States.TryGetValue(__instance, out var state))
        {
            RefreshNavigation(__instance, state);
        }
    }

    private static void Install(NTopBar topBar)
    {
        if (States.ContainsKey(topBar))
        {
            return;
        }

        var parent = topBar.FindChild("RightAlignedStuff", true, false) as Container;
        if (parent is null)
        {
            RunStatsLog.Error("The STATS top-bar button was skipped: RightAlignedStuff was not found.");
            return;
        }

        var button = new Button
        {
            Name = ButtonName,
            TooltipText = "View statistics for this run",
            CustomMinimumSize = new Vector2(58, 52),
            FocusMode = Control.FocusModeEnum.All,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            Flat = true,
            Icon = CreateChartIcon(),
            IconAlignment = HorizontalAlignment.Center
        };
        button.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        button.AddThemeStyleboxOverride("hover", CreateButtonFeedback(0.08f));
        button.AddThemeStyleboxOverride("focus", CreateButtonFeedback(0.05f, 2));
        button.AddThemeStyleboxOverride("pressed", CreateButtonFeedback(0.14f));

        parent.AddChild(button);
        var options = parent.FindChild("Options", false, false);
        if (options is not null)
        {
            parent.MoveChild(button, options.GetIndex());
        }

        var state = new ButtonState(button);
        States.Add(topBar, state);
        button.Resized += () => button.PivotOffset = button.Size / 2f;
        button.MouseEntered += () => AnimateHover(state, new Vector2(1.08f, 1.08f));
        button.MouseExited += () => AnimateHover(state, Vector2.One);
        button.Pressed += () => StatsOverlay.TryOpen(RunStatsRuntime.DisplaySnapshot);
        topBar.TreeExited += () => Remove(topBar);
        ActiveScreenContext.Instance.Updated += state.RefreshHandler = () => Refresh(topBar, state);
        RunStatsRuntime.DisplaySnapshotChanged += state.SnapshotRefreshHandler = () => Refresh(topBar, state);
        button.Visible = false;
        button.Disabled = true;
        Callable.From(() => Refresh(topBar, state)).CallDeferred();
    }

    private static void Remove(NTopBar topBar)
    {
        if (!States.Remove(topBar, out var state))
        {
            return;
        }

        ActiveScreenContext.Instance.Updated -= state.RefreshHandler;
        RunStatsRuntime.DisplaySnapshotChanged -= state.SnapshotRefreshHandler;
        state.HoverTween?.Kill();
    }

    private static void Refresh(NTopBar topBar, ButtonState state)
    {
        state.Button.Visible = CanInteract();
        state.Button.Disabled = !state.Button.Visible;
        if (!state.Button.Visible)
        {
            state.HoverTween?.Kill();
            state.Button.Scale = Vector2.One;
        }
        RefreshNavigation(topBar, state);
    }

    private static bool CanInteract()
    {
        NOverlayStack? overlayStack;
        try
        {
            overlayStack = NOverlayStack.Instance;
        }
        catch (NullReferenceException)
        {
            return false;
        }

        var lifecycle = RunStatsRuntime.DisplaySnapshot.Lifecycle;
        if (lifecycle is not (RunLifecycle.Active or RunLifecycle.Ended) ||
            overlayStack is null ||
            !StatsOverlay.CanOpenOn(overlayStack))
        {
            return false;
        }

        if (NModalContainer.Instance?.OpenModal is not null ||
            (lifecycle != RunLifecycle.Ended &&
             NCapstoneContainer.Instance?.CurrentCapstoneScreen is not null))
        {
            return false;
        }

        if (NMapScreen.Instance is not null && NMapScreen.Instance.IsOpen)
        {
            return false;
        }

        return lifecycle == RunLifecycle.Ended ||
            ActiveScreenContext.Instance.GetCurrentScreen() is not null;
    }

    private static void RefreshNavigation(NTopBar topBar, ButtonState state)
    {
        var button = state.Button;
        var parent = button.GetParent();
        if (parent is null)
        {
            return;
        }

        Control? previous = null;
        for (var index = button.GetIndex() - 1; index >= 0; index--)
        {
            if (parent.GetChild(index) is Control candidate && candidate.Visible)
            {
                previous = candidate;
                break;
            }
        }

        var pause = topBar.Pause;
        if (button.Visible)
        {
            button.FocusNeighborLeft = (previous ?? button).GetPath();
            button.FocusNeighborRight = pause.GetPath();
            button.FocusNeighborTop = button.GetPath();
            button.FocusNeighborBottom = topBar.ActiveScreenProxy.GetPath();
            pause.FocusNeighborLeft = button.GetPath();
            if (previous is not null)
            {
                previous.FocusNeighborRight = button.GetPath();
            }
        }
        else if (previous is not null)
        {
            previous.FocusNeighborRight = pause.GetPath();
            pause.FocusNeighborLeft = previous.GetPath();
        }
    }

    private static StyleBoxFlat CreateButtonFeedback(float opacity, int borderWidth = 0)
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.78f, 0.84f, 0.82f, opacity),
            BorderColor = new Color(0.78f, 0.84f, 0.82f, 0.7f)
        };
        style.SetBorderWidthAll(borderWidth);
        style.SetCornerRadiusAll(7);
        style.SetContentMarginAll(8);
        return style;
    }

    private static Texture2D CreateChartIcon()
    {
        var image = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
        image.Fill(Colors.Transparent);
        var color = new Color("c6d6d0");
        image.FillRect(new Rect2I(3, 4, 3, 25), color);
        image.FillRect(new Rect2I(3, 26, 26, 3), color);
        image.FillRect(new Rect2I(8, 19, 5, 7), color);
        image.FillRect(new Rect2I(16, 13, 5, 13), color);
        image.FillRect(new Rect2I(24, 6, 5, 20), color);
        return ImageTexture.CreateFromImage(image);
    }

    private static void AnimateHover(ButtonState state, Vector2 targetScale)
    {
        state.HoverTween?.Kill();
        state.Button.PivotOffset = state.Button.Size / 2f;
        state.HoverTween = state.Button.CreateTween();
        state.HoverTween.SetEase(Tween.EaseType.Out);
        state.HoverTween.SetTrans(Tween.TransitionType.Quad);
        state.HoverTween.TweenProperty(state.Button, "scale", targetScale, 0.1);
    }

    private sealed class ButtonState(Button button)
    {
        public Button Button { get; } = button;

        public Action RefreshHandler { get; set; } = null!;

        public Action SnapshotRefreshHandler { get; set; } = null!;

        public Tween? HoverTween { get; set; }
    }

}
