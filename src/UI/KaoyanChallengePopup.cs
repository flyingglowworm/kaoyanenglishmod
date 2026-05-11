using System;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;

namespace KaoyanEnglishMod.UI;

public sealed partial class KaoyanChallengePopup : Control, IScreenContext
{
    private NVerticalPopup? _verticalPopup;
    private Action<bool>? _onChoice;

    public Control? DefaultFocusedControl => null;

    public static void ShowChallenge(Action<bool> onChoice)
    {
        var modalContainer = NModalContainer.Instance;
        if (modalContainer == null)
        {
            Log.Warn("[KaoyanEnglishMod] NModalContainer.Instance was null; challenge popup skipped.");
            return;
        }

        if (modalContainer.OpenModal != null)
        {
            Log.Warn("[KaoyanEnglishMod] Another modal is already open; challenge popup skipped.");
            return;
        }

        var popup = new KaoyanChallengePopup
        {
            _onChoice = onChoice,
        };
        popup.SetAnchorsPreset(LayoutPreset.FullRect);
        modalContainer.Add(popup, true);
    }

    public override void _Ready()
    {
        _verticalPopup = SceneHelper.Instantiate<NVerticalPopup>("ui/vertical_popup");
        AddChild(_verticalPopup);
        Callable.From(ConfigurePopup).CallDeferred();
    }

    private void ConfigurePopup()
    {
        if (_verticalPopup == null)
        {
            Log.Error("[KaoyanEnglishMod] Challenge popup was not initialized.");
            return;
        }

        _verticalPopup.SetText("\u8003\u7814\u8bcd\u5178", "\u662f\u5426\u6311\u6218\u672c\u573a\u8003\u7814\u5355\u8bcd\uff1f");
        _verticalPopup.InitYesButton(new LocString("relics", "KaoyanLexicon.title"), _ =>
        {
            HandleChoice(true);
        });
        _verticalPopup.InitNoButton(new LocString("relics", "KaoyanLexicon.title"), _ =>
        {
            HandleChoice(false);
        });
        _verticalPopup.YesButton.SetText("\u6311\u6218");
        _verticalPopup.NoButton.SetText("\u4e0d\u6311\u6218");
    }

    private void HandleChoice(bool isChallengeSelected)
    {
        try
        {
            NModalContainer.Instance?.Clear();
        }
        catch (Exception exception)
        {
            Log.Error("[KaoyanEnglishMod] Failed to close challenge popup.");
            Log.Error(exception.ToString());
        }

        try
        {
            _onChoice?.Invoke(isChallengeSelected);
        }
        catch (Exception exception)
        {
            Log.Error("[KaoyanEnglishMod] Failed to handle challenge popup choice.");
            Log.Error(exception.ToString());
        }
    }
}
