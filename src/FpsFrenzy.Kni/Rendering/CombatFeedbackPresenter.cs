using FpsFrenzy.Core.Data;
using FpsFrenzy.Core.Simulation;
using Microsoft.Xna.Framework;
using CoreVector3 = System.Numerics.Vector3;

namespace FpsFrenzy.Kni.Rendering;

public sealed class CombatFeedbackPresenter(ContentCatalog catalog)
{
    private readonly Dictionary<string, Color> _weaponColors = catalog.Weapons.Values.ToDictionary(
        weapon => weapon.Id,
        weapon => new Color(weapon.ImpactColor.X, weapon.ImpactColor.Y, weapon.ImpactColor.Z),
        StringComparer.OrdinalIgnoreCase);
    private readonly List<FeedbackVisual> _visuals = [];

    public void Consume(IReadOnlyList<CombatEvent> events)
    {
        foreach (CombatEvent combatEvent in events)
        {
            Color color = GetColor(combatEvent);
            switch (combatEvent.Type)
            {
                case CombatEventType.WorldImpact:
                    AddBeam(combatEvent.SecondaryPosition, combatEvent.Position, color, 0.15f);
                    AddSpark(combatEvent.Position, color, 0.18f, 0.16f);
                    break;
                case CombatEventType.EnemyHit:
                    AddBeam(combatEvent.SecondaryPosition, combatEvent.Position, color, 0.18f);
                    AddSpark(combatEvent.Position, Color.White, 0.2f, 0.2f);
                    break;
                case CombatEventType.EnemyKilled:
                    AddSpark(combatEvent.Position, new Color(255, 88, 130), 0.42f, 0.5f);
                    break;
                case CombatEventType.EnemyTelegraph:
                    AddSpark(combatEvent.Position + new CoreVector3(0f, 0.45f, 0f), new Color(255, 180, 45),
                        MathF.Max(0.2f, combatEvent.Value), 0.42f);
                    break;
                case CombatEventType.BossPhaseChanged:
                    AddSpark(combatEvent.Position + new CoreVector3(0f, 1.5f, 0f), new Color(255, 55, 155), 0.8f, 1.2f);
                    break;
                case CombatEventType.SupportPulse:
                    AddPulse(combatEvent.Position, new Color(185, 105, 255), 0.55f, combatEvent.Value);
                    break;
            }
        }
    }

    public void Update(float deltaSeconds)
    {
        for (int index = _visuals.Count - 1; index >= 0; index--)
        {
            FeedbackVisual visual = _visuals[index];
            visual.RemainingSeconds -= deltaSeconds;
            if (visual.RemainingSeconds <= 0f)
            {
                _visuals.RemoveAt(index);
            }
        }
    }

    public void Draw(PrimitiveRenderer renderer)
    {
        foreach (FeedbackVisual visual in _visuals)
        {
            float life = Math.Clamp(visual.RemainingSeconds / visual.DurationSeconds, 0f, 1f);
            switch (visual.Kind)
            {
                case FeedbackVisualKind.Beam:
                    renderer.DrawBeam(visual.Start.ToXna(), visual.End.ToXna(), visual.Size * life, visual.Color);
                    break;
                case FeedbackVisualKind.Spark:
                    float sparkSize = MathF.Max(0.02f, visual.Size * life);
                    float sparkRadius = sparkSize * 0.5f;
                    float sparkThickness = MathF.Max(0.012f, sparkSize * 0.12f);
                    Vector3 sparkCenter = visual.End.ToXna();
                    renderer.DrawBeam(sparkCenter - (Vector3.Right * sparkRadius),
                        sparkCenter + (Vector3.Right * sparkRadius), sparkThickness, visual.Color);
                    renderer.DrawBeam(sparkCenter - (Vector3.Up * sparkRadius),
                        sparkCenter + (Vector3.Up * sparkRadius), sparkThickness, visual.Color);
                    renderer.DrawBeam(sparkCenter - (Vector3.Forward * sparkRadius),
                        sparkCenter + (Vector3.Forward * sparkRadius), sparkThickness, visual.Color);
                    break;
                case FeedbackVisualKind.Pulse:
                    float radius = visual.Size * (1f - life);
                    Vector3 center = visual.End.ToXna() + new Vector3(0f, 0.15f, 0f);
                    renderer.DrawBeam(center - (Vector3.Right * radius), center + (Vector3.Right * radius),
                        0.05f + (0.08f * life), visual.Color);
                    renderer.DrawBeam(center - (Vector3.Forward * radius), center + (Vector3.Forward * radius),
                        0.05f + (0.08f * life), visual.Color);
                    break;
            }
        }
    }

    private void AddBeam(CoreVector3 start, CoreVector3 end, Color color, float duration)
    {
        CoreVector3 delta = end - start;
        float length = delta.Length();
        if (length > 0.01f)
        {
            float muzzleOffset = MathF.Min(0.08f, length * 0.08f);
            start += (delta / length) * muzzleOffset;
        }

        _visuals.Add(new FeedbackVisual(FeedbackVisualKind.Beam, start, end, color, duration, 0.022f));
    }

    private void AddSpark(CoreVector3 position, Color color, float duration, float size) =>
        _visuals.Add(new FeedbackVisual(FeedbackVisualKind.Spark, position, position, color, duration, size));

    private void AddPulse(CoreVector3 position, Color color, float duration, float size) =>
        _visuals.Add(new FeedbackVisual(FeedbackVisualKind.Pulse, position, position, color, duration, size));

    private Color GetColor(CombatEvent combatEvent) =>
        combatEvent.CueId is not null && _weaponColors.TryGetValue(combatEvent.CueId, out Color color)
            ? color
            : new Color(110, 225, 255);

    private enum FeedbackVisualKind
    {
        Beam,
        Spark,
        Pulse,
    }

    private sealed class FeedbackVisual(
        FeedbackVisualKind kind,
        CoreVector3 start,
        CoreVector3 end,
        Color color,
        float durationSeconds,
        float size)
    {
        public FeedbackVisualKind Kind { get; } = kind;
        public CoreVector3 Start { get; } = start;
        public CoreVector3 End { get; } = end;
        public Color Color { get; } = color;
        public float DurationSeconds { get; } = durationSeconds;
        public float Size { get; } = size;
        public float RemainingSeconds { get; set; } = durationSeconds;
    }
}
