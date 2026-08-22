using Android.Content;
using Android.Graphics;
using Android.Views;

namespace DeezerRpc.Android;

internal enum MonochromeLogo
{
    Discord,
    Deezer
}

internal sealed class MonochromeLogoView : View
{
    private readonly Paint _paint = new(PaintFlags.AntiAlias);
    private readonly MonochromeLogo _logo;

    public MonochromeLogoView(Context context, MonochromeLogo logo, Color tint) : base(context)
    {
        _logo = logo;
        SetTint(tint);
    }

    public void SetTint(Color tint)
    {
        _paint.Color = tint;
        Invalidate();
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        var width = Width;
        var height = Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (_logo == MonochromeLogo.Discord)
        {
            DrawDiscord(canvas, width, height);
        }
        else
        {
            DrawDeezer(canvas, width, height);
        }
    }

    private void DrawDiscord(Canvas canvas, float width, float height)
    {
        _paint.SetStyle(Paint.Style.Stroke);
        _paint.StrokeWidth = Math.Max(2F, Math.Min(width, height) * 0.09F);
        _paint.StrokeCap = Paint.Cap.Round;
        _paint.StrokeJoin = Paint.Join.Round;

        using var body = new global::Android.Graphics.Path();
        body.MoveTo(width * 0.23F, height * 0.31F);
        body.CubicTo(width * 0.37F, height * 0.20F, width * 0.63F, height * 0.20F, width * 0.77F, height * 0.31F);
        body.CubicTo(width * 0.86F, height * 0.45F, width * 0.90F, height * 0.66F, width * 0.84F, height * 0.76F);
        body.CubicTo(width * 0.78F, height * 0.83F, width * 0.69F, height * 0.72F, width * 0.63F, height * 0.65F);
        body.CubicTo(width * 0.55F, height * 0.70F, width * 0.45F, height * 0.70F, width * 0.37F, height * 0.65F);
        body.CubicTo(width * 0.31F, height * 0.72F, width * 0.22F, height * 0.83F, width * 0.16F, height * 0.76F);
        body.CubicTo(width * 0.10F, height * 0.66F, width * 0.14F, height * 0.45F, width * 0.23F, height * 0.31F);
        canvas.DrawPath(body, _paint);

        _paint.SetStyle(Paint.Style.Fill);
        var eyeRadius = Math.Min(width, height) * 0.075F;
        canvas.DrawCircle(width * 0.37F, height * 0.49F, eyeRadius, _paint);
        canvas.DrawCircle(width * 0.63F, height * 0.49F, eyeRadius, _paint);
    }

    private void DrawDeezer(Canvas canvas, float width, float height)
    {
        _paint.SetStyle(Paint.Style.Fill);
        var heights = new[] { 0.34F, 0.52F, 0.76F, 0.94F, 0.76F, 0.52F, 0.34F };
        var gap = width * 0.035F;
        var barWidth = (width - gap * (heights.Length - 1)) / heights.Length;
        var bottom = height * 0.86F;
        for (var index = 0; index < heights.Length; index++)
        {
            var barHeight = height * heights[index];
            var left = index * (barWidth + gap);
            canvas.DrawRoundRect(
                left,
                bottom - barHeight,
                left + barWidth,
                bottom,
                barWidth / 2F,
                barWidth / 2F,
                _paint);
        }
    }
}
