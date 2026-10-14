using System;

/// <summary>Geometry only: no input, authentication, player data or runtime UI creation.</summary>
public static class MainMenuLayoutPolicy
{
    public static MainMenuLayoutFrame Build(float width, float height)
    {
        if (float.IsNaN(width) || float.IsInfinity(width) || width < 320f)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (float.IsNaN(height) || float.IsInfinity(height) || height < 320f)
            throw new ArgumentOutOfRangeException(nameof(height));

        float u = Math.Min(1f, Math.Min(width / 1600f, height / 900f));
        float margin = 32f * u;
        // Matches the existing Updates entry plus its NEW UPDATE badge. Reserve
        // this zone even when the badge is hidden; resize must not move actions over it.
        float updatesUnit = Math.Max(.55f, u);
        float updatesWidth = Math.Min(270f * updatesUnit, width - margin * 2f);
        float updatesHeight = Math.Min(126f * updatesUnit, height - margin * 2f);
        var updates = new MainMenuLayoutBox(width * .5f - 28f * updatesUnit - updatesWidth * .5f,
            -height * .5f + 28f * updatesUnit + updatesHeight * .5f, updatesWidth, updatesHeight);

        float menuWidth = Math.Min(1220f, width - margin * 2f);
        float bottomClearance = Math.Max(160f * u, 154f * updatesUnit + 24f * u);
        float menuHeight = Math.Min(800f, Math.Max(height * .32f, height - bottomClearance * 2f));
        var dropdown = new MainMenuLayoutBox(0f, 0f, menuWidth, menuHeight);
        float menuUnit = Math.Min(menuWidth / 1220f, menuHeight / 800f);

        bool landscape = width >= height * 1.15f;
        float splashWidth = Math.Min(680f, landscape ? width * .43f : width - margin * 2f);
        float splashHeight = Math.Min(740f, height - 152f * u - 154f * updatesUnit);
        float splashX = landscape ? -width * .5f + margin + splashWidth * .5f : 0f;
        float splashY = height * .5f - 128f * u - splashHeight * .5f;
        var splash = new MainMenuLayoutBox(splashX, splashY, splashWidth, splashHeight);
        var quit = new MainMenuLayoutBox(width * .5f - margin - 100f * u,
            height * .5f - margin - 30f * u, 200f * u, 60f * u);
        var version = new MainMenuLayoutBox(-width * .5f + margin + 160f * u,
            -height * .5f + margin + 19f * u, 320f * u, 38f * u);
        var title = new MainMenuLayoutBox(0f, height * .30f, Math.Min(960f, width - margin * 2f), 130f * u);
        var edition = new MainMenuLayoutBox(0f, height * .18f, Math.Min(720f, width - margin * 2f), 70f * u);
        var play = new MainMenuLayoutBox(0f, -height * .10f, Math.Min(600f, width - margin * 2f), 120f * u);
        return new MainMenuLayoutFrame(splash, dropdown, quit, version, updates,
            Math.Min(splashWidth / 680f, splashHeight / 740f), menuUnit, title, edition, play, u);
    }
}

public struct MainMenuLayoutBox
{
    public float X, Y, Width, Height;
    public float Left => X - Width * .5f;
    public float Right => X + Width * .5f;
    public float Bottom => Y - Height * .5f;
    public float Top => Y + Height * .5f;
    public MainMenuLayoutBox(float x, float y, float width, float height)
    { X = x; Y = y; Width = width; Height = height; }
}

public sealed class MainMenuLayoutFrame
{
    public readonly MainMenuLayoutBox Splash, Dropdown, Quit, Version, Updates;
    public readonly MainMenuLayoutBox Title, Edition, Play;
    public readonly float SplashUnit, MenuUnit;
    public readonly float IntroUnit;
    public MainMenuLayoutFrame(MainMenuLayoutBox splash, MainMenuLayoutBox dropdown,
        MainMenuLayoutBox quit, MainMenuLayoutBox version, MainMenuLayoutBox updates,
        float splashUnit, float menuUnit, MainMenuLayoutBox title, MainMenuLayoutBox edition,
        MainMenuLayoutBox play, float introUnit)
    {
        Splash = splash; Dropdown = dropdown; Quit = quit; Version = version;
        Updates = updates; SplashUnit = splashUnit; MenuUnit = menuUnit;
        Title = title; Edition = edition; Play = play; IntroUnit = introUnit;
    }
}
