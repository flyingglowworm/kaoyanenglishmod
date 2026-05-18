using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using KaoyanEnglishMod.Vocab;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;

namespace KaoyanEnglishMod.UI;

public sealed partial class KaoyanQuestionPopup : Control, IScreenContext
{
    private static readonly string[] OptionLabels = ["A", "B", "C", "D"];
    private static readonly Color CorrectAnswerTint = new(0.26f, 0.86f, 0.42f, 1f);
    private static readonly Color WrongAnswerTint = new(0.92f, 0.24f, 0.20f, 1f);
    private const double AnswerFeedbackSeconds = 0.65d;

    private KaoyanQuestion? _question;
    private Action<int?>? _onAnswered;
    private readonly List<AnswerOptionUi> _answerOptions = [];
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
            content.AddChild(CreateAnswerButton(index));
        }

        var skipButton = KaoyanUiStyle.CreateImageButton(
            "\u8df3\u8fc7",
            true,
            new Vector2(0f, 48f),
            17,
            () => HandleAnswer(null));
        content.AddChild(skipButton);
    }

    private Control CreateAnswerButton(int answerIndex)
    {
        var text = $"{OptionLabels[answerIndex]}. {_question!.Options[answerIndex]}";
        var normal = KaoyanUiStyle.TryLoadTexture(KaoyanUiStyle.ButtonNormalPath);
        if (normal == null)
        {
            var fallback = new Button
            {
                Text = text,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0f, 52f),
            };
            KaoyanUiStyle.StylePrimaryButton(fallback);
            fallback.Pressed += () => HandleAnswer(answerIndex);
            _answerOptions.Add(new AnswerOptionUi(fallback, fallback, null, null, null));
            return fallback;
        }

        var hover = KaoyanUiStyle.TryLoadTexture(KaoyanUiStyle.ButtonHoverPath) ?? normal;
        var pressed = KaoyanUiStyle.TryLoadTexture(KaoyanUiStyle.ButtonPressedPath) ?? hover;
        var root = new Control
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, 52f),
        };

        var image = new TextureRect
        {
            Texture = normal,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        image.SetAnchorsPreset(LayoutPreset.FullRect);
        root.AddChild(image);

        var label = KaoyanUiStyle.CreateLabel(text, 18, KaoyanUiStyle.BodyText);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.MouseFilter = MouseFilterEnum.Ignore;
        label.SetAnchorsPreset(LayoutPreset.FullRect);
        root.AddChild(label);

        var clickLayer = new Button
        {
            Text = string.Empty,
            Flat = true,
        };
        clickLayer.SetAnchorsPreset(LayoutPreset.FullRect);
        clickLayer.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        clickLayer.AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
        clickLayer.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
        clickLayer.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        clickLayer.MouseEntered += () =>
        {
            if (!_answered)
            {
                image.Texture = hover;
            }
        };
        clickLayer.MouseExited += () =>
        {
            if (!_answered)
            {
                image.Texture = normal;
            }
        };
        clickLayer.ButtonDown += () =>
        {
            if (!_answered)
            {
                image.Texture = pressed;
            }
        };
        clickLayer.ButtonUp += () =>
        {
            if (!_answered)
            {
                image.Texture = hover;
            }
        };
        clickLayer.Pressed += () => HandleAnswer(answerIndex);
        root.AddChild(clickLayer);

        _answerOptions.Add(new AnswerOptionUi(clickLayer, root, image, label, normal));
        return root;
    }

    private async void HandleAnswer(int? answerIndex)
    {
        if (_answered)
        {
            return;
        }

        _answered = true;

        if (answerIndex != null)
        {
            ShowAnswerFeedback(answerIndex.Value);
            await WaitForAnswerFeedbackAsync();
        }

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

    private void ShowAnswerFeedback(int answerIndex)
    {
        if (_question == null)
        {
            return;
        }

        for (var index = 0; index < _answerOptions.Count; index++)
        {
            var option = _answerOptions[index];
            option.ClickLayer.Disabled = true;
            option.ClickLayer.MouseFilter = MouseFilterEnum.Ignore;

            if (index == _question.CorrectIndex)
            {
                ApplyAnswerFeedback(option, CorrectAnswerTint);
            }
            else if (index == answerIndex)
            {
                ApplyAnswerFeedback(option, WrongAnswerTint);
            }
        }
    }

    private static void ApplyAnswerFeedback(AnswerOptionUi option, Color tint)
    {
        if (option.Image != null)
        {
            option.Image.Texture = option.NormalTexture;
            option.Image.SelfModulate = tint;
        }
        else
        {
            option.Root.SelfModulate = tint;
        }

        option.Label?.AddThemeColorOverride("font_color", Colors.White);
    }

    private async Task WaitForAnswerFeedbackAsync()
    {
        var tree = GetTree();
        if (tree == null)
        {
            return;
        }

        await ToSignal(tree.CreateTimer(AnswerFeedbackSeconds), SceneTreeTimer.SignalName.Timeout);
    }

    private sealed record AnswerOptionUi(
        Button ClickLayer,
        Control Root,
        TextureRect? Image,
        Label? Label,
        Texture2D? NormalTexture);
}
