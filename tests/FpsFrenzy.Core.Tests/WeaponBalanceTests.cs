using FpsFrenzy.Core.Data;
using FpsFrenzy.Core.Simulation;

namespace FpsFrenzy.Core.Tests;

public sealed class WeaponBalanceTests
{
    private static readonly string[] CrowdLeaders = ["plasma-launcher", "arc-cannon"];
    private static readonly string[] RangeLeaders = ["burst-carbine", "beam-rifle"];

    private static ContentCatalog LoadCatalog() => ContentCatalog.LoadFromDirectory(
        Path.Combine(AppContext.BaseDirectory, "Content", "Data"));

    [Fact]
    public void StandardWeaponsRetainDistinctSustainedRangeAndCrowdRoles()
    {
        ContentCatalog catalog = LoadCatalog();
        Dictionary<string, WeaponBalanceSample> samples = catalog.Weapons.Values.ToDictionary(
            weapon => weapon.Id,
            weapon => Measure(weapon, durationSeconds: 20f, distance: IntendedDistance(weapon)));

        Assert.All(samples.Values, sample => Assert.InRange(sample.SingleTargetDps, 18f, 120f));

        string singleTargetLeader = samples.MaxBy(pair => pair.Value.SingleTargetDps).Key;
        string crowdLeader = samples.MaxBy(pair => pair.Value.CrowdDps).Key;
        string rangeLeader = samples.MaxBy(pair => pair.Value.Range).Key;
        string uptimeLeader = samples.MaxBy(pair => pair.Value.Uptime).Key;

        Assert.Contains(crowdLeader, CrowdLeaders);
        Assert.Contains(rangeLeader, RangeLeaders);
        Assert.True(new[] { singleTargetLeader, crowdLeader, rangeLeader, uptimeLeader }
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 3);
        Assert.DoesNotContain(catalog.Weapons.Keys, weaponId =>
            string.Equals(weaponId, singleTargetLeader, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(weaponId, crowdLeader, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(weaponId, rangeLeader, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(weaponId, uptimeLeader, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PulseBaselineAndRepresentativeBossBuildMeetStandardTtkBands()
    {
        ContentCatalog catalog = LoadCatalog();
        WeaponDefinition pulse = catalog.Weapons["pulse-sidearm"];

        Assert.InRange(TimeToKill(pulse, 85f, modifiers: null), 0.6f, 1.0f);
        Assert.InRange(TimeToKill(pulse, 110f, modifiers: null), 0.8f, 1.3f);
        Assert.InRange(TimeToKill(pulse, 240f, modifiers: null), 1.8f, 3.0f);
        Assert.InRange(TimeToKill(pulse, 220f, modifiers: null), 1.8f, 3.0f);

        RunModifiers bossBuild = new(StandardUpgradeCatalog.All);
        Assert.True(bossBuild.Apply("pulse-capacitor"));
        Assert.True(bossBuild.Apply("calibrated-cells"));
        float bossSeconds = TimeToKill(pulse, catalog.Enemies["breach-walker"].MaxHealth, bossBuild);
        Assert.InRange(bossSeconds, 75f, 100f);
    }

    private static WeaponBalanceSample Measure(WeaponDefinition definition, float durationSeconds, float distance)
    {
        (float damage, int shots, float readySeconds) = Simulate(definition, durationSeconds, null, float.PositiveInfinity);
        float falloff = DamageFalloff(definition, distance);
        float singleDps = damage * falloff / durationSeconds;
        int crowdTargets = definition.ChainTargets > 0
            ? 1 + definition.ChainTargets
            : definition.SplashRadius > 0f
                ? 1 + Math.Min(3, (int)MathF.Ceiling(definition.SplashRadius))
                : 1;
        return new WeaponBalanceSample(
            singleDps,
            singleDps * crowdTargets,
            definition.Range,
            readySeconds / durationSeconds,
            shots);
    }

    private static float TimeToKill(
        WeaponDefinition definition,
        float targetHealth,
        RunModifiers? modifiers)
    {
        const float maximumSeconds = 180f;
        const float tickSeconds = 1f / 600f;
        WeaponState weapon = new(definition, modifiers);
        float health = targetHealth;
        for (float elapsed = 0f; elapsed <= maximumSeconds; elapsed += tickSeconds)
        {
            if (definition.AmmoMode == AmmoMode.MagazineReserve &&
                weapon.Magazine == 0 && weapon.Reserve == 0)
            {
                // The boss arena's ammo salvage sustains magazine weapons; reload time remains paid.
                weapon.AddAmmo(definition.ReserveCapacity);
            }

            PrepareBurst(weapon);
            if (weapon.TryFire())
            {
                health -= definition.Damage * definition.PelletCount *
                    (modifiers?.DamageMultiplier(definition.Id, 10f) ?? 1f);
                weapon.CompleteBurstShot();
                if (health <= 0f)
                {
                    return elapsed;
                }
            }

            weapon.Tick(tickSeconds);
        }

        return float.PositiveInfinity;
    }

    private static (float Damage, int Shots, float ReadySeconds) Simulate(
        WeaponDefinition definition,
        float durationSeconds,
        RunModifiers? modifiers,
        float stopAfterDamage)
    {
        const float tickSeconds = 1f / 600f;
        WeaponState weapon = new(definition, modifiers);
        float damage = 0f;
        int shots = 0;
        float readySeconds = 0f;
        for (float elapsed = 0f; elapsed < durationSeconds && damage < stopAfterDamage; elapsed += tickSeconds)
        {
            bool hasResource = definition.AmmoMode switch
            {
                AmmoMode.MagazineReserve => weapon.Magazine > 0,
                AmmoMode.RegeneratingEnergy => weapon.Energy >= definition.EnergyPerShot,
                AmmoMode.Heat => !weapon.IsOverheated && weapon.Heat + definition.HeatPerShot <= 1f,
                _ => false,
            };
            if (hasResource && !weapon.IsReloading)
            {
                readySeconds += tickSeconds;
            }

            PrepareBurst(weapon);
            if (weapon.TryFire())
            {
                shots++;
                damage += definition.Damage * definition.PelletCount *
                    (modifiers?.DamageMultiplier(definition.Id, 10f) ?? 1f);
                weapon.CompleteBurstShot();
            }

            weapon.Tick(tickSeconds);
        }

        return (damage, shots, readySeconds);
    }

    private static void PrepareBurst(WeaponState weapon)
    {
        if (weapon.Definition.TriggerMode == TriggerMode.Burst && weapon.BurstShotsRemaining == 0)
        {
            weapon.StartBurst();
        }
    }

    private static float DamageFalloff(WeaponDefinition weapon, float distance)
    {
        if (weapon.DamageFalloffStart <= 0f || distance <= weapon.DamageFalloffStart ||
            weapon.Range <= weapon.DamageFalloffStart)
        {
            return 1f;
        }

        float amount = Math.Clamp(
            (distance - weapon.DamageFalloffStart) / (weapon.Range - weapon.DamageFalloffStart),
            0f,
            1f);
        return float.Lerp(1f, weapon.MinimumDamageMultiplier, amount);
    }

    private static float IntendedDistance(WeaponDefinition weapon) => weapon.Id switch
    {
        "scatter-blaster" => 6f,
        "plasma-launcher" or "arc-cannon" => 16f,
        _ => 20f,
    };

    private readonly record struct WeaponBalanceSample(
        float SingleTargetDps,
        float CrowdDps,
        float Range,
        float Uptime,
        int Shots);
}
