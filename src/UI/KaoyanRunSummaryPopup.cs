using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;

namespace KaoyanEnglishMod.UI;

public sealed record KaoyanRunSummaryWord(
    string Word,
    string Meaning,
    int Rank,
    int TimesShown,
    int WrongAnswers,
    int CorrectAnswers,
    bool MasteredAfterMistake);

public sealed partial class KaoyanRunSummaryPopup : Control, IScreenContext
{
    private const double ShowDelaySeconds = 0.8d;
    private const float PopupWidth = 960f;
    private const float PopupHeight = 700f;
    private const float ContentWidth = 820f;
    private const float RowWidth = 760f;
    private const float RowHeight = 112f;
    private const string BackgroundPath = "res://KaoyanEnglishMod/images/ui/kaoyan/run_summary_background.png";
    private static readonly Color CyanText = new(0.47f, 0.95f, 1f, 1f);
    private static readonly Color GoldText = new(0.96f, 0.73f, 0.32f, 1f);
    private IReadOnlyList<KaoyanRunSummaryWord> _wrongWords = [];
    private bool _isVictory;
    private bool _closed;

    public Control? DefaultFocusedControl => null;

    public static void ShowRunSummary(IReadOnlyList<KaoyanRunSummaryWord> wrongWords, bool isVictory)
    {
        if (wrongWords.Count == 0)
        {
            return;
        }

        _ = ShowRunSummaryDelayedAsync(wrongWords.ToList(), isVictory);
    }

    public override void _Ready()
    {
        try
        {
            BuildUi();
        }
        catch (Exception exception)
        {
            Log.Error("[KaoyanEnglishMod] Failed to build run summary popup.");
            Log.Error(exception.ToString());
            NModalContainer.Instance?.Clear();
        }
    }

    private static async Task ShowRunSummaryDelayedAsync(
        IReadOnlyList<KaoyanRunSummaryWord> wrongWords,
        bool isVictory)
    {
        var modalContainer = NModalContainer.Instance;
        if (modalContainer == null)
        {
            Log.Warn("[KaoyanEnglishMod] NModalContainer.Instance was null; run summary popup skipped.");
            return;
        }

        try
        {
            await modalContainer.ToSignal(
                modalContainer.GetTree().CreateTimer(ShowDelaySeconds),
                SceneTreeTimer.SignalName.Timeout);
        }
        catch (Exception exception)
        {
            Log.Error("[KaoyanEnglishMod] Failed while waiting to show run summary popup.");
            Log.Error(exception.ToString());
            return;
        }

        modalContainer = NModalContainer.Instance;
        if (modalContainer == null)
        {
            Log.Warn("[KaoyanEnglishMod] NModalContainer.Instance was null after delay; run summary popup skipped.");
            return;
        }

        if (modalContainer.OpenModal != null)
        {
            Log.Warn("[KaoyanEnglishMod] Another modal is already open; run summary popup skipped.");
            return;
        }

        var popup = new KaoyanRunSummaryPopup
        {
            _wrongWords = wrongWords,
            _isVictory = isVictory,
        };
        popup.SetAnchorsPreset(LayoutPreset.FullRect);
        modalContainer.Add(popup, true);
        Log.Warn("[KaoyanEnglishMod] Run summary popup shown.");
    }

    private void BuildUi()
    {
        AddBackground();

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var shell = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(PopupWidth, PopupHeight),
        };
        shell.AddThemeConstantOverride("separation", 18);
        center.AddChild(shell);

        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 14);

        shell.AddChild(CreateTitle());

        var bodyPanel = new PanelContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(PopupWidth, 492f),
        };
        bodyPanel.AddThemeStyleboxOverride("panel", CreateMainPanelStyle());
        shell.AddChild(bodyPanel);

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        KaoyanUiStyle.ApplyMargins(margin, 64, 32, 64, 26);
        bodyPanel.AddChild(margin);
        margin.AddChild(content);

        content.AddChild(KaoyanUiStyle.CreateLabel(
            BuildSubtitle(),
            18,
            KaoyanUiStyle.BodyText));

        var listFrame = new PanelContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(ContentWidth, 402f),
        };
        listFrame.AddThemeStyleboxOverride("panel", CreateListFrameStyle());
        content.AddChild(listFrame);

        var listMargin = new MarginContainer();
        KaoyanUiStyle.ApplyMargins(listMargin, 22, 18, 30, 18);
        listFrame.AddChild(listMargin);

        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(ContentWidth - 52f, 360f),
        };
        listMargin.AddChild(scroll);

        var list = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(RowWidth, 0f),
        };
        list.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(list);

        foreach (var word in _wrongWords)
        {
            list.AddChild(CreateWordRow(word));
        }

        var buttonWrap = new CenterContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, 84f),
        };
        buttonWrap.AddChild(KaoyanUiStyle.CreateImageButton(
            "关闭",
            true,
            new Vector2(620f, 72f),
            22,
            ClosePopup));
        shell.AddChild(buttonWrap);
    }

    private string BuildSubtitle()
    {
        var resultText = _isVictory ? "胜利" : "失败";
        return $"{resultText}后总结：本局共有 {_wrongWords.Count} 个错词。";
    }

    private static Control CreateWordRow(KaoyanRunSummaryWord word)
    {
        var panel = new PanelContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(RowWidth, RowHeight),
        };

        panel.AddThemeStyleboxOverride("panel", CreateRowStyle(word.MasteredAfterMistake));

        var rowMargin = new MarginContainer();
        KaoyanUiStyle.ApplyMargins(rowMargin, 32, 14, 28, 14);
        panel.AddChild(rowMargin);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 24);
        rowMargin.AddChild(row);

        row.AddChild(CreateWordIcon(word.MasteredAfterMistake));

        var textColumn = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(600f, 0f),
        };
        textColumn.AddThemeConstantOverride("separation", 10);
        row.AddChild(textColumn);

        var title = CreateInlineWordTitle(word);
        textColumn.AddChild(title);

        var meta = new HBoxContainer();
        meta.AddThemeConstantOverride("separation", 42);
        textColumn.AddChild(meta);

        meta.AddChild(CreateMetaLabel($"Rank {word.Rank}", GoldText));
        meta.AddChild(CreateMetaLabel($"答错 {word.WrongAnswers} 次", KaoyanUiStyle.BodyText));
        meta.AddChild(CreateMetaLabel($"出现 {word.TimesShown} 次", KaoyanUiStyle.BodyText));

        if (word.MasteredAfterMistake)
        {
            meta.AddChild(CreateMetaLabel("已订正", CyanText));
        }

        return panel;
    }

    private void AddBackground()
    {
        var texture = KaoyanUiStyle.TryLoadTexture(BackgroundPath);
        if (texture == null)
        {
            var fallback = new ColorRect
            {
                Color = new Color(0.006f, 0.016f, 0.019f, 0.94f),
            };
            fallback.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(fallback);
            return;
        }

        var background = new TextureRect
        {
            Texture = texture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        background.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(background);

        var veil = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.26f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        veil.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(veil);
    }

    private static Control CreateTitle()
    {
        var wrap = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(0f, 96f),
        };
        wrap.AddThemeConstantOverride("separation", 4);

        wrap.AddChild(CreateDivider());

        var label = KaoyanUiStyle.CreateLabel("本局错词回顾", 42, KaoyanUiStyle.GoldText);
        label.CustomMinimumSize = new Vector2(0f, 64f);
        label.VerticalAlignment = VerticalAlignment.Center;
        wrap.AddChild(label);

        wrap.AddChild(CreateDivider());
        return wrap;
    }

    private static Control CreateDivider()
    {
        var center = new CenterContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, 12f),
        };

        var line = new ColorRect
        {
            Color = new Color(0.82f, 0.58f, 0.20f, 0.78f),
            CustomMinimumSize = new Vector2(360f, 2f),
        };
        center.AddChild(line);
        return center;
    }

    private static Control CreateInlineWordTitle(KaoyanRunSummaryWord word)
    {
        var row = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(600f, 38f),
        };
        row.AddThemeConstantOverride("separation", 10);

        var english = new Label
        {
            Text = word.Word,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(176f, 38f),
            ClipText = true,
        };
        english.AddThemeFontSizeOverride("font_size", 24);
        english.AddThemeColorOverride("font_color", CyanText);
        row.AddChild(english);

        var meaning = new Label
        {
            Text = $" - {word.Meaning}",
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        meaning.AddThemeFontSizeOverride("font_size", 20);
        meaning.AddThemeColorOverride("font_color", KaoyanUiStyle.BodyText);
        row.AddChild(meaning);

        return row;
    }

    private static Label CreateMetaLabel(string text, Color color)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(118f, 28f),
        };
        label.AddThemeFontSizeOverride("font_size", 18);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private static Control CreateWordIcon(bool mastered)
    {
        var holder = new PanelContainer
        {
            CustomMinimumSize = new Vector2(64f, 64f),
        };

        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.025f, 0.18f, 0.20f, 0.95f),
            BorderColor = mastered ? GoldText : new Color(0.17f, 0.79f, 0.84f, 0.95f),
        };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(4);
        holder.AddThemeStyleboxOverride("panel", style);

        var icon = KaoyanUiStyle.CreateLabel(mastered ? "订" : "错", 22, mastered ? GoldText : CyanText);
        icon.VerticalAlignment = VerticalAlignment.Center;
        holder.AddChild(icon);
        return holder;
    }

    private static StyleBoxFlat CreateMainPanelStyle()
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.012f, 0.044f, 0.049f, 0.82f),
            BorderColor = new Color(0.13f, 0.73f, 0.78f, 0.95f),
            ShadowColor = new Color(0f, 0f, 0f, 0.62f),
            ShadowSize = 24,
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 10,
            ContentMarginBottom = 10,
        };
        style.SetBorderWidthAll(3);
        style.SetCornerRadiusAll(6);
        return style;
    }

    private static StyleBoxFlat CreateListFrameStyle()
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0f, 0.012f, 0.014f, 0.18f),
            BorderColor = new Color(0.10f, 0.48f, 0.54f, 0.22f),
            ContentMarginLeft = 8,
            ContentMarginRight = 8,
            ContentMarginTop = 8,
            ContentMarginBottom = 8,
        };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(4);
        return style;
    }

    private static StyleBoxFlat CreateRowStyle(bool mastered)
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.025f, 0.118f, 0.128f, 0.84f),
            BorderColor = mastered
                ? new Color(0.80f, 0.58f, 0.22f, 0.86f)
                : new Color(0.14f, 0.58f, 0.63f, 0.86f),
            ShadowColor = new Color(0f, 0f, 0f, 0.36f),
            ShadowSize = 5,
        };
        style.SetBorderWidthAll(mastered ? 2 : 1);
        style.SetCornerRadiusAll(6);
        return style;
    }

    private void ClosePopup()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;

        try
        {
            NModalContainer.Instance?.Clear();
        }
        catch (Exception exception)
        {
            Log.Error("[KaoyanEnglishMod] Failed to close run summary popup.");
            Log.Error(exception.ToString());
        }
    }
}
