using System;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace KaoyanEnglishMod.UI;

internal static class KaoyanUiStyle
{
    public const string QuestionPopupBackgroundPath = "res://KaoyanEnglishMod/images/ui/kaoyan/question_popup_bg.png";
    public const string RewardPopupBackgroundPath = "res://KaoyanEnglishMod/images/ui/kaoyan/reward_popup_bg.png";
    public const string ButtonNormalPath = "res://KaoyanEnglishMod/images/ui/kaoyan/button_normal.png";
    public const string ButtonHoverPath = "res://KaoyanEnglishMod/images/ui/kaoyan/button_hover.png";
    public const string ButtonPressedPath = "res://KaoyanEnglishMod/images/ui/kaoyan/button_pressed.png";
    public const string ButtonDisabledPath = "res://KaoyanEnglishMod/images/ui/kaoyan/button_disabled.png";
    public const string ButtonSkipNormalPath = "res://KaoyanEnglishMod/images/ui/kaoyan/button_skip_normal.png";
    public const string ButtonSkipHoverPath = "res://KaoyanEnglishMod/images/ui/kaoyan/button_skip_hover.png";

    public static readonly Color PanelBackground = new(0.045f, 0.075f, 0.08f, 0.94f);
    public static readonly Color PanelBorder = new(0.16f, 0.58f, 0.63f, 0.88f);
    public static readonly Color ButtonBackground = new(0.055f, 0.22f, 0.24f, 0.95f);
    public static readonly Color ButtonHoverBackground = new(0.075f, 0.31f, 0.34f, 0.98f);
    public static readonly Color ButtonPressedBackground = new(0.03f, 0.14f, 0.16f, 1f);
    public static readonly Color MutedButtonBackground = new(0.08f, 0.10f, 0.105f, 0.88f);
    public static readonly Color MutedButtonHoverBackground = new(0.13f, 0.15f, 0.16f, 0.95f);
    public static readonly Color GoldText = new(0.95f, 0.76f, 0.30f, 1f);
    public static readonly Color BodyText = new(0.94f, 0.93f, 0.86f, 1f);
    public static readonly Color MutedText = new(0.74f, 0.78f, 0.74f, 1f);

    public static void StylePanel(PanelContainer panel)
    {
        var style = new StyleBoxFlat
        {
            BgColor = PanelBackground,
            BorderColor = PanelBorder,
            ShadowColor = new Color(0f, 0f, 0f, 0.55f),
            ShadowSize = 18,
        };
        style.SetBorderWidthAll(3);
        style.SetCornerRadiusAll(8);
        panel.AddThemeStyleboxOverride("panel", style);
    }

    public static void StylePrimaryButton(Button button)
    {
        ApplyButtonStyle(button, ButtonBackground, ButtonHoverBackground, ButtonPressedBackground, BodyText, GoldText, 18, 2);
    }

    public static void StyleMutedButton(Button button)
    {
        ApplyButtonStyle(button, MutedButtonBackground, MutedButtonHoverBackground, ButtonPressedBackground, MutedText, BodyText, 17, 1);
    }

    public static Control CreateImageButton(string text, bool muted, Vector2 minimumSize, int fontSize, Action onPressed)
    {
        var normal = TryLoadTexture(muted ? ButtonSkipNormalPath : ButtonNormalPath);
        if (normal == null)
        {
            var fallback = new Button
            {
                Text = text,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = minimumSize,
            };
            if (muted)
            {
                StyleMutedButton(fallback);
            }
            else
            {
                StylePrimaryButton(fallback);
            }

            fallback.Pressed += onPressed;
            return fallback;
        }

        var hover = TryLoadTexture(muted ? ButtonSkipHoverPath : ButtonHoverPath) ?? normal;
        var pressed = TryLoadTexture(ButtonPressedPath) ?? hover;
        var root = new Control
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = minimumSize,
        };

        var image = new TextureRect
        {
            Texture = normal,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        image.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(image);

        var label = CreateLabel(text, fontSize, muted ? MutedText : BodyText);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        label.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(label);

        var clickLayer = new Button
        {
            Text = string.Empty,
            Flat = true,
        };
        clickLayer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        clickLayer.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        clickLayer.AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
        clickLayer.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
        clickLayer.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        clickLayer.MouseEntered += () => image.Texture = hover;
        clickLayer.MouseExited += () => image.Texture = normal;
        clickLayer.ButtonDown += () => image.Texture = pressed;
        clickLayer.ButtonUp += () => image.Texture = hover;
        clickLayer.Pressed += onPressed;
        root.AddChild(clickLayer);

        return root;
    }

    public static Control CreateImageFrame(Vector2 size, string texturePath)
    {
        var frame = new Control
        {
            CustomMinimumSize = size,
        };

        var texture = TryLoadTexture(texturePath);
        if (texture != null)
        {
            var textureRect = new TextureRect
            {
                Texture = texture,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            textureRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            frame.AddChild(textureRect);
            return frame;
        }

        var fallbackPanel = new PanelContainer();
        fallbackPanel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        StylePanel(fallbackPanel);
        frame.AddChild(fallbackPanel);
        return frame;
    }

    public static void StyleAnswerButton(Button button)
    {
        if (ApplyTextureButtonStyle(button, ButtonNormalPath, ButtonHoverPath, ButtonPressedPath, ButtonDisabledPath, BodyText, GoldText, 18))
        {
            return;
        }

        StylePrimaryButton(button);
    }

    public static void StyleSkipButton(Button button)
    {
        if (ApplyTextureButtonStyle(button, ButtonSkipNormalPath, ButtonSkipHoverPath, ButtonPressedPath, ButtonDisabledPath, MutedText, BodyText, 17))
        {
            return;
        }

        StyleMutedButton(button);
    }

    public static Label CreateLabel(string text, int fontSize, Color color, HorizontalAlignment alignment = HorizontalAlignment.Center)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = alignment,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    public static void ApplyMargins(MarginContainer margin, int left, int top, int right, int bottom)
    {
        margin.AddThemeConstantOverride("margin_left", left);
        margin.AddThemeConstantOverride("margin_top", top);
        margin.AddThemeConstantOverride("margin_right", right);
        margin.AddThemeConstantOverride("margin_bottom", bottom);
    }

    public static Texture2D? TryLoadTexture(string path)
    {
        if (!ResourceLoader.Exists(path))
        {
            Log.Warn($"[KaoyanEnglishMod] Missing UI texture: {path}");
            return null;
        }

        var texture = ResourceLoader.Load<Texture2D>(path);
        if (texture == null)
        {
            Log.Warn($"[KaoyanEnglishMod] Failed to load UI texture: {path}");
        }

        return texture;
    }

    private static bool ApplyTextureButtonStyle(
        Button button,
        string normalPath,
        string hoverPath,
        string pressedPath,
        string disabledPath,
        Color fontColor,
        Color hoverFontColor,
        int fontSize)
    {
        var normal = TryLoadTexture(normalPath);
        if (normal == null)
        {
            return false;
        }

        button.AddThemeStyleboxOverride("normal", CreateTextureButtonBox(normal));
        button.AddThemeStyleboxOverride("hover", CreateTextureButtonBox(TryLoadTexture(hoverPath) ?? normal));
        button.AddThemeStyleboxOverride("pressed", CreateTextureButtonBox(TryLoadTexture(pressedPath) ?? normal));
        button.AddThemeStyleboxOverride("disabled", CreateTextureButtonBox(TryLoadTexture(disabledPath) ?? normal));
        button.AddThemeColorOverride("font_color", fontColor);
        button.AddThemeColorOverride("font_hover_color", hoverFontColor);
        button.AddThemeColorOverride("font_pressed_color", BodyText);
        button.AddThemeColorOverride("font_disabled_color", MutedText);
        button.AddThemeFontSizeOverride("font_size", fontSize);
        return true;
    }

    private static void ApplyButtonStyle(
        Button button,
        Color normal,
        Color hover,
        Color pressed,
        Color fontColor,
        Color hoverFontColor,
        int fontSize,
        int borderWidth)
    {
        button.AddThemeStyleboxOverride("normal", CreateButtonBox(normal, borderWidth));
        button.AddThemeStyleboxOverride("hover", CreateButtonBox(hover, borderWidth + 1));
        button.AddThemeStyleboxOverride("pressed", CreateButtonBox(pressed, borderWidth));
        button.AddThemeColorOverride("font_color", fontColor);
        button.AddThemeColorOverride("font_hover_color", hoverFontColor);
        button.AddThemeColorOverride("font_pressed_color", BodyText);
        button.AddThemeFontSizeOverride("font_size", fontSize);
    }

    private static StyleBoxFlat CreateButtonBox(Color background, int borderWidth)
    {
        var style = new StyleBoxFlat
        {
            BgColor = background,
            BorderColor = PanelBorder,
            ContentMarginLeft = 18,
            ContentMarginRight = 18,
            ContentMarginTop = 10,
            ContentMarginBottom = 10,
        };
        style.SetBorderWidthAll(borderWidth);
        style.SetCornerRadiusAll(7);
        return style;
    }

    private static StyleBoxTexture CreateTextureButtonBox(Texture2D texture)
    {
        return new StyleBoxTexture
        {
            Texture = texture,
            DrawCenter = true,
            ContentMarginLeft = 28,
            ContentMarginRight = 28,
            ContentMarginTop = 10,
            ContentMarginBottom = 10,
        };
    }
}
