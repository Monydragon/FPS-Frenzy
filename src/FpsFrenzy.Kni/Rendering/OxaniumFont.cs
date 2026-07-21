using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FpsFrenzy.Kni.Rendering;

/// <summary>
/// Responsive HUD typography backed entirely by compiled Oxanium SpriteFonts.
/// </summary>
public sealed class OxaniumFont
{
    private static OxaniumFont? _measurementSource;
    private readonly SpriteFont _hud;
    private readonly SpriteFont _body;
    private readonly SpriteFont _heading;

    public OxaniumFont(SpriteFont hud, SpriteFont body, SpriteFont heading)
    {
        _hud = hud ?? throw new ArgumentNullException(nameof(hud));
        _body = body ?? throw new ArgumentNullException(nameof(body));
        _heading = heading ?? throw new ArgumentNullException(nameof(heading));
        _measurementSource = this;
    }

    public static Vector2 Measure(ReadOnlySpan<char> text, int scale = 2)
    {
        if (_measurementSource is null)
        {
            // Layout-only tests run without a GraphicsDevice. These conservative Oxanium
            // metrics mirror the compiled sizes while runtime measurement still comes from
            // SpriteFont itself.
            float glyphWidth = scale switch
            {
                <= 1 => 7.6f,
                2 => 10.8f,
                3 => 13.2f,
                4 => 20.5f,
                5 => 24.5f,
                _ => 28.5f,
            };
            float lineHeight = scale switch
            {
                <= 1 => 13f,
                2 => 20f,
                3 => 24f,
                4 => 35f,
                5 => 42f,
                _ => 49f,
            };
            return new Vector2(text.Length * glyphWidth, lineHeight);
        }

        OxaniumFont source = _measurementSource;
        (SpriteFont font, float factor) = source.Select(scale);
        return font.MeasureString(text.ToString()) * factor;
    }

    public static Vector2 MeasureNumber(int value, int scale = 2)
    {
        Span<char> characters = stackalloc char[16];
        value.TryFormat(characters, out int written, default, CultureInfo.InvariantCulture);
        return Measure(characters[..written], scale);
    }

    public void Draw(SpriteBatch spriteBatch, ReadOnlySpan<char> text, Vector2 position, Color color, int scale = 2)
    {
        (SpriteFont font, float factor) = Select(scale);
        spriteBatch.DrawString(font, text.ToString(), position, color, 0f, Vector2.Zero, factor,
            SpriteEffects.None, 0f);
    }

    public void DrawNumber(SpriteBatch spriteBatch, int value, Vector2 position, Color color, int scale = 2)
    {
        Span<char> characters = stackalloc char[16];
        value.TryFormat(characters, out int written, default, CultureInfo.InvariantCulture);
        Draw(spriteBatch, characters[..written], position, color, scale);
    }

    private (SpriteFont Font, float Factor) Select(int scale) => scale switch
    {
        <= 1 => (_hud, 0.62f),
        2 => (_body, 0.88f),
        3 => (_body, 1.08f),
        4 => (_heading, 0.78f),
        5 => (_heading, 0.92f),
        _ => (_heading, 1.08f),
    };
}
