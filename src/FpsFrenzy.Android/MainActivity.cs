using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Hardware;
using Android.OS;
using Android.Views;
using FpsFrenzy.Kni;

namespace FpsFrenzy.Android;

[Activity(
    Label = "FPS Frenzy",
    MainLauncher = true,
    Theme = "@android:style/Theme.Material.NoActionBar.Fullscreen",
    AlwaysRetainTaskState = true,
    LaunchMode = LaunchMode.SingleInstance,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.Keyboard |
        ConfigChanges.KeyboardHidden | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout |
        ConfigChanges.UiMode | ConfigChanges.SmallestScreenSize,
    ScreenOrientation = ScreenOrientation.SensorLandscape)]
public sealed class MainActivity : Microsoft.Xna.Framework.AndroidGameActivity
{
    private AndroidGyroLookSource? _gyro;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ApplyImmersiveMode();

        SensorManager sensorManager = (SensorManager)GetSystemService(SensorService)!;
        _gyro = new AndroidGyroLookSource(sensorManager);
        FpsFrenzyGame game = new(_gyro, fullScreen: true);
        bool useThirtyFpsFallback = GetPreferences(FileCreationMode.Private)?
            .GetBoolean("render-30-fps", false) ?? false;
        game.SetRenderFrameRate(useThirtyFpsFallback ? 30 : 60);
        SetContentView((View)game.Services.GetService(typeof(View))!);
        game.Run();
    }

    protected override void OnResume()
    {
        base.OnResume();
        _gyro?.Start();
    }

    protected override void OnPause()
    {
        _gyro?.Stop();
        base.OnPause();
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus)
        {
            ApplyImmersiveMode();
        }
    }

    private void ApplyImmersiveMode()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            Window?.InsetsController?.Hide(WindowInsets.Type.SystemBars());
            return;
        }

#pragma warning disable CA1422 // Required compatibility path for API 23-29.
        if (Window?.DecorView is View decorView)
        {
            decorView.SystemUiFlags = SystemUiFlags.Fullscreen |
                SystemUiFlags.HideNavigation | SystemUiFlags.ImmersiveSticky;
        }
#pragma warning restore CA1422
    }
}
