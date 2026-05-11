using System;
using Godot;
using KaoyanEnglishMod.Vocab;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;

namespace KaoyanEnglishMod.UI;

public sealed partial class KaoyanQuestionPopup : Control, IScreenContext
{
    private static readonly string[] OptionLabels = ["A", "B", "C", "D"];

    private KaoyanQuestion? _question;
    private Action<int?>? _onAnswered;
    private bool _answered;

    public Control? DefaultFocusedControl => null;

    public static bool ShowQuestion(KaoyanQuestion question, Action<int?> onAnswered)
    {
        var modalContainer = NModalContainer.Instance;
        if (modalContainer == null)
        {
            Log.Warn("[KaoyanEnglishMod] NModalContainer.Instance was null; question popup skipped.");
            return false;
        }

        if (modalContainer.OpenModal != null)
        {
            Log.Warn("[KaoyanEnglishMod] Another modal is already open; question popup skipped.");
            return false;
        }

        var popup = new KaoyanQuestionPopup
        {
            _question = question,
            _onAnswered = onAnswered,
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
            Log.Error("[KaoyanEnglishMod] Failed to build question popup.");
            Log.Error(exception.ToString());
            NModalContainer.Instance?.Clear();
        }
    }

    private void BuildUi()
    {
        if (_question == null)
        {
            Log.Warn("[KaoyanEnglishMod] Question popup had no question data.");
            return;
        }

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        Log.Warn("[KaoyanEnglishMod] Textured question popup UI enabled.");
        var frame = KaoyanUiStyle.CreateImageFrame(new Vector2(840f, 630f), KaoyanUiStyle.QuestionPopupBackgroundPath);
        center.AddChild(frame);

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        KaoyanUiStyle.ApplyMargins(margin, 110, 82, 110, 78);
        frame.AddChild(margin);

        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 12);
        margin.AddChild(content);

        content.AddChild(KaoyanUiStyle.CreateLabel("\u8003\u7814\u8bcd\u5178", 29, KaoyanUiStyle.GoldText));
        content.AddChild(KaoyanUiStyle.CreateLabel(_question.Word.Word, 42, KaoyanUiStyle.BodyText));
        content.AddChild(KaoyanUiStyle.CreateLabel("\u9009\u62e9\u6b63\u786e\u91ca\u4e49", 18, KaoyanUiStyle.MutedText));

        for (var index = 0; index < _question.Options.Count && index < OptionLabels.Length; index++)
        {
            var answerIndex = index;
            var button = KaoyanUiStyle.CreateImageButton(
                $"{OptionLabels[index]}. {_question.Options[index]}",
                false,
                new Vector2(0f, 52f),
                18,
                () => HandleAnswer(answerIndex));
            content.AddChild(button);
        }

        var skipButton = KaoyanUiStyle.CreateImageButton(
            "\u8df3\u8fc7",
            true,
            new Vector2(0f, 48f),
            17,
            () => HandleAnswer(null));
        content.AddChild(skipButton);
    }

    private void HandleAnswer(int? answerIndex)
    {
        if (_answered)
        {
            return;
        }

        _answered = true;

        try
        {
            NModalContainer.Instance?.Clear();
        }
        catch (Exception exception)
        {
            Log.Error("[KaoyanEnglishMod] Failed to close question popup.");
            Log.Error(exception.ToString());
        }

        try
        {
            _onAnswered?.Invoke(answerIndex);
        }
        catch (Exception exception)
        {
            Log.Error("[KaoyanEnglishMod] Failed to handle question popup answer.");
            Log.Error(exception.ToString());
        }
    }
}
