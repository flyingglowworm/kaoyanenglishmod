using System;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;

namespace KaoyanEnglishMod.UI;

public enum KaoyanRewardChoice
{
    Replay,
    FreeThisTurn,
}

public sealed partial class KaoyanRewardChoicePopup : Control, IScreenContext
{
    private Action<KaoyanRewardChoice>? _onChosen;
    private bool _chosen;

    public Control? DefaultFocusedControl => null;

    public static bool ShowRewardChoice(Action<KaoyanRewardChoice> onChosen)
    {
        var modalContainer = NModalContainer.Instance;
        if (modalContainer == null)
        {
            Log.Warn("[KaoyanEnglishMod] NModalContainer.Instance was null; reward choice popup skipped.");
            return false;
        }

        if (modalContainer.OpenModal != null)
        {
            Log.Warn("[KaoyanEnglishMod] Another modal is already open; reward choice popup skipped.");
            return false;
        }

        var popup = new KaoyanRewardChoicePopup
        {
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
            Log.Error("[KaoyanEnglishMod] Failed to build reward choice popup.");
            Log.Error(exception.ToString());
            NModalContainer.Instance?.Clear();
        }
    }

    private void BuildUi()
    {
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        Log.Warn("[KaoyanEnglishMod] Textured reward choice popup UI enabled.");
        var frame = KaoyanUiStyle.CreateImageFrame(new Vector2(760f, 428f), KaoyanUiStyle.RewardPopupBackgroundPath);
        center.AddChild(frame);

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        KaoyanUiStyle.ApplyMargins(margin, 80, 64, 80, 58);
        frame.AddChild(margin);

        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 14);
        margin.AddChild(content);

        content.AddChild(KaoyanUiStyle.CreateLabel("\u56de\u7b54\u6b63\u786e\uff01", 32, KaoyanUiStyle.GoldText));
        content.AddChild(KaoyanUiStyle.CreateLabel("\u8bf7\u9009\u62e9\u4e00\u4e2a\u5956\u52b1", 20, KaoyanUiStyle.BodyText));
        content.AddChild(CreateButton(
            "\u91cd\u653e 1\n\u9009\u62e9\u4e00\u5f20\u624b\u724c\uff0c\u672c\u573a\u6218\u6597\u6dfb\u52a0\u91cd\u653e 1\u3002",
            KaoyanRewardChoice.Replay));
        content.AddChild(CreateButton(
            "\u672c\u56de\u5408\u514d\u8d39\n\u9009\u62e9\u4e00\u5f20\u624b\u724c\uff0c\u672c\u56de\u5408\u53ef\u4ee5\u514d\u8d39\u6253\u51fa\u3002",
            KaoyanRewardChoice.FreeThisTurn));
    }

    private Control CreateButton(string text, KaoyanRewardChoice choice)
    {
        return KaoyanUiStyle.CreateImageButton(
            text,
            false,
            new Vector2(0f, 78f),
            17,
            () => HandleChoice(choice));
    }

    private void HandleChoice(KaoyanRewardChoice choice)
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
            Log.Error("[KaoyanEnglishMod] Failed to close reward choice popup.");
            Log.Error(exception.ToString());
        }

        try
        {
            _onChosen?.Invoke(choice);
        }
        catch (Exception exception)
        {
            Log.Error("[KaoyanEnglishMod] Failed to handle reward choice.");
            Log.Error(exception.ToString());
        }
    }
}
