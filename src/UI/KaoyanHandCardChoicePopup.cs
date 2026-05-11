using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;

namespace KaoyanEnglishMod.UI;

public sealed partial class KaoyanHandCardChoicePopup : Control, IScreenContext
{
    private IReadOnlyList<CardModel>? _cards;
    private string _title = string.Empty;
    private string _body = string.Empty;
    private Action<CardModel>? _onChosen;
    private bool _chosen;

    public Control? DefaultFocusedControl => null;

    public static bool ShowHandCardChoice(
        IReadOnlyList<CardModel> cards,
        string title,
        string body,
        Action<CardModel> onChosen)
    {
        var modalContainer = NModalContainer.Instance;
        if (modalContainer == null)
        {
            Log.Warn("[KaoyanEnglishMod] NModalContainer.Instance was null; hand card choice popup skipped.");
            return false;
        }

        if (modalContainer.OpenModal != null)
        {
            Log.Warn("[KaoyanEnglishMod] Another modal is already open; hand card choice popup skipped.");
            return false;
        }

        var popup = new KaoyanHandCardChoicePopup
        {
            _cards = cards,
            _title = title,
            _body = body,
            _onChosen = onChosen,
        };
        popup.SetAnchorsPreset(LayoutPreset.FullRect);
        modalContainer.Add(popup, true);
        return true;
    }

    public override void _Ready()
    {
        try
        {
            BuildUi();
        }
        catch (Exception exception)
        {
            Log.Error("[KaoyanEnglishMod] Failed to build hand card choice popup.");
            Log.Error(exception.ToString());
            NModalContainer.Instance?.Clear();
        }
    }

    private void BuildUi()
    {
        if (_cards == null || _cards.Count == 0)
        {
            Log.Warn("[KaoyanEnglishMod] Hand card choice popup had no cards.");
            return;
        }

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(680f, 420f),
        };
        center.AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 28);
        margin.AddThemeConstantOverride("margin_top", 24);
        margin.AddThemeConstantOverride("margin_right", 28);
        margin.AddThemeConstantOverride("margin_bottom", 24);
        panel.AddChild(margin);

        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 12);
        margin.AddChild(content);

        content.AddChild(CreateLabel(_title, 24));
        content.AddChild(CreateLabel(_body, 18));

        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, 260f),
        };
        content.AddChild(scroll);

        var list = new VBoxContainer();
        list.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(list);

        foreach (var card in _cards)
        {
            var selectedCard = card;
            var button = new Button
            {
                Text = GetCardLabel(selectedCard),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0f, 42f),
            };
            button.Pressed += () => HandleChoice(selectedCard);
            list.AddChild(button);
        }
    }

    private static Label CreateLabel(string text, int fontSize)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        return label;
    }

    private void HandleChoice(CardModel card)
    {
        if (_chosen)
        {
            return;
        }

        _chosen = true;

        try
        {
            NModalContainer.Instance?.Clear();
        }
        catch (Exception exception)
        {
            Log.Error("[KaoyanEnglishMod] Failed to close hand card choice popup.");
            Log.Error(exception.ToString());
        }

        try
        {
            _onChosen?.Invoke(card);
        }
        catch (Exception exception)
        {
            Log.Error("[KaoyanEnglishMod] Failed to handle hand card choice.");
            Log.Error(exception.ToString());
        }
    }

    private static string GetCardLabel(CardModel card)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(card.Title))
            {
                return card.Title;
            }
        }
        catch (Exception)
        {
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(card.Id.Entry))
            {
                return card.Id.Entry;
            }
        }
        catch (Exception)
        {
        }

        return card.GetType().Name;
    }
}
