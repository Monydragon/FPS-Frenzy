using Android.Hardware;
using FpsFrenzy.Kni.Input;
using CoreVector2 = System.Numerics.Vector2;

namespace FpsFrenzy.Android;

public sealed class AndroidGyroLookSource : Java.Lang.Object, ISensorEventListener, IPlatformLookSource
{
    private readonly object _gate = new();
    private readonly SensorManager _sensorManager;
    private readonly Sensor? _gyroscope;
    private float _pitchVelocity;
    private float _yawVelocity;

    public AndroidGyroLookSource(SensorManager sensorManager)
    {
        _sensorManager = sensorManager;
        _gyroscope = sensorManager.GetDefaultSensor(SensorType.Gyroscope);
    }

    public bool IsAvailable => _gyroscope is not null;

    public void Start()
    {
        if (_gyroscope is not null)
        {
            _sensorManager.RegisterListener(this, _gyroscope, SensorDelay.Game);
        }
    }

    public void Stop()
    {
        _sensorManager.UnregisterListener(this);
        lock (_gate)
        {
            _pitchVelocity = 0f;
            _yawVelocity = 0f;
        }
    }

    public CoreVector2 ConsumeLookDelta(float deltaSeconds)
    {
        lock (_gate)
        {
            const float fineAimScale = 0.75f;
            return new CoreVector2(
                -_yawVelocity * deltaSeconds * fineAimScale,
                -_pitchVelocity * deltaSeconds * fineAimScale);
        }
    }

    public void OnAccuracyChanged(Sensor? sensor, SensorStatus accuracy) { }

    public void OnSensorChanged(SensorEvent? e)
    {
        if (e?.Values is not { Count: >= 2 } values)
        {
            return;
        }

        lock (_gate)
        {
            _pitchVelocity = values[0];
            _yawVelocity = values[1];
        }
    }
}
