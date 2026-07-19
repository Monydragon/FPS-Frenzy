namespace FpsFrenzy.Core.Data;

public static class ContentValidator
{
    private static readonly string[] RequiredAlienClips = ["idle", "walk", "attack", "hit", "death"];
    private static readonly string[] RequiredRobotStates =
        ["idle", "locomotion", "windup", "activeAttack", "recovery", "hitReaction", "death"];
    private static readonly string[] ReleaseWeaponIds =
        ["pulse-sidearm", "burst-carbine", "scatter-blaster", "beam-rifle", "plasma-launcher", "arc-cannon"];
    private static readonly HashSet<UpgradeEffectType> WeaponScopedUpgradeEffects =
    [
        UpgradeEffectType.WeaponDamage,
        UpgradeEffectType.WeaponAmmoCost,
        UpgradeEffectType.WeaponBurstRounds,
        UpgradeEffectType.WeaponSpread,
        UpgradeEffectType.WeaponFalloffStart,
        UpgradeEffectType.WeaponHeatGeneration,
        UpgradeEffectType.WeaponCooling,
        UpgradeEffectType.WeaponSplashRadius,
        UpgradeEffectType.WeaponProjectileSpeed,
        UpgradeEffectType.WeaponChainTargets,
        UpgradeEffectType.WeaponChainRadius,
    ];

    public static ContentValidationResult Validate(ContentCatalog catalog)
    {
        List<string> errors = [];
        bool hasReleaseCampaign = catalog.Arenas.Values.Any(arena => arena.SchemaVersion >= 2) &&
            ReleaseWeaponIds.All(catalog.Weapons.ContainsKey);

        foreach (WeaponDefinition weapon in catalog.Weapons.Values)
        {
            Require(weapon.SchemaVersion == 1, $"Weapon '{weapon.Id}' has an unsupported schema version.", errors);
            Require(weapon.Damage > 0, $"Weapon '{weapon.Id}' damage must be positive.", errors);
            Require(weapon.FireIntervalSeconds > 0, $"Weapon '{weapon.Id}' fire interval must be positive.", errors);
            Require(weapon.HipFieldOfViewDegrees > weapon.AdsFieldOfViewDegrees,
                $"Weapon '{weapon.Id}' ADS FOV must be lower than hip FOV.", errors);
            Require(!string.IsNullOrWhiteSpace(weapon.ModelAsset), $"Weapon '{weapon.Id}' needs a model asset.", errors);
            Require(weapon.PelletCount > 0, $"Weapon '{weapon.Id}' pellet count must be positive.", errors);
            Require(weapon.MinimumDamageMultiplier is > 0f and <= 1f,
                $"Weapon '{weapon.Id}' minimum damage multiplier must be in (0, 1].", errors);
            Require(weapon.Range > 0f, $"Weapon '{weapon.Id}' range must be positive.", errors);
            Require(weapon.SpreadDegrees >= 0f, $"Weapon '{weapon.Id}' spread cannot be negative.", errors);
            Require(weapon.DamageFalloffStart >= 0f && weapon.DamageFalloffStart <= weapon.Range,
                $"Weapon '{weapon.Id}' damage falloff must be inside its range.", errors);
            Require(weapon.SplashRadius >= 0f && weapon.ChainRadius >= 0f && weapon.ChainTargets >= 0,
                $"Weapon '{weapon.Id}' area-effect values cannot be negative.", errors);
            if (weapon.TriggerMode == TriggerMode.Burst)
            {
                Require(weapon.BurstCount > 1, $"Burst weapon '{weapon.Id}' needs at least two shots.", errors);
                Require(weapon.BurstRecoverySeconds >= weapon.FireIntervalSeconds,
                    $"Burst weapon '{weapon.Id}' recovery must not be shorter than its shot interval.", errors);
            }

            if (weapon.ShotMode == ShotMode.Projectile)
            {
                Require(weapon.ProjectileSpeed > 0f, $"Projectile weapon '{weapon.Id}' needs projectile speed.", errors);
                Require(weapon.ProjectileRadius > 0f, $"Projectile weapon '{weapon.Id}' needs projectile radius.", errors);
            }
            if (weapon.AmmoMode == AmmoMode.MagazineReserve)
            {
                Require(weapon.MagazineSize > 0, $"Weapon '{weapon.Id}' needs a magazine.", errors);
                Require(weapon.ReserveCapacity >= weapon.MagazineSize, $"Weapon '{weapon.Id}' reserve is too small.", errors);
                Require(weapon.ReloadSeconds > 0f, $"Weapon '{weapon.Id}' reload time must be positive.", errors);
            }
            else if (weapon.AmmoMode == AmmoMode.RegeneratingEnergy)
            {
                Require(weapon.EnergyCapacity > 0f && weapon.EnergyPerShot is > 0f &&
                    weapon.EnergyPerShot <= weapon.EnergyCapacity && weapon.EnergyRegenerationPerSecond > 0f,
                    $"Energy weapon '{weapon.Id}' has invalid capacity, cost, or regeneration.", errors);
            }
            else if (weapon.AmmoMode == AmmoMode.Heat)
            {
                Require(weapon.HeatPerShot is > 0f and <= 1f && weapon.HeatDissipationPerSecond > 0f,
                    $"Heat weapon '{weapon.Id}' has invalid heat generation or dissipation.", errors);
            }
        }

        foreach (EnemyDefinition enemy in catalog.Enemies.Values)
        {
            Require(enemy.SchemaVersion is 1 or 2, $"Enemy '{enemy.Id}' has an unsupported schema version.", errors);
            Require(enemy.MaxHealth > 0, $"Enemy '{enemy.Id}' health must be positive.", errors);
            Require(enemy.MoveSpeed > 0, $"Enemy '{enemy.Id}' move speed must be positive.", errors);
            Require(enemy.ColliderRadius > 0 && enemy.ColliderHeight > 0,
                $"Enemy '{enemy.Id}' collider must be positive.", errors);
            Require(enemy.AttackDamage > 0f && enemy.AttackCooldownSeconds > 0f,
                $"Enemy '{enemy.Id}' attack values must be positive.", errors);
            Require(enemy.AttackWindupSeconds >= 0f && enemy.AttackRecoverySeconds >= 0f,
                $"Enemy '{enemy.Id}' attack timings cannot be negative.", errors);
            Require(enemy.ThreatWeight > 0f,
                $"Enemy '{enemy.Id}' threat weight must be positive.", errors);
            if (enemy.SchemaVersion >= 2)
            {
                Require(!string.IsNullOrWhiteSpace(enemy.Visual.AlbedoAsset),
                    $"Release enemy '{enemy.Id}' needs an explicitly authored albedo asset.", errors);
                Require(enemy.Visual.TextureSampling == TextureSamplingMode.LinearMipmapped,
                    $"Release enemy '{enemy.Id}' must use linear mipmapped texture sampling.", errors);
                float groundedBaseHeight = 0.7f + enemy.Visual.GroundOffset;
                if (enemy.Behavior == EnemyBehavior.Spitter)
                {
                    Require(MathF.Abs(groundedBaseHeight) <= 0.1f && enemy.Visual.HoverOffset >= 0.5f,
                        $"Release hover enemy '{enemy.Id}' needs a grounded base calibration and visible hover offset.",
                        errors);
                }
                else
                {
                    Require(MathF.Abs(groundedBaseHeight) <= 0.1f && MathF.Abs(enemy.Visual.HoverOffset) <= 0.1f,
                        $"Release ground enemy '{enemy.Id}' must place its authored feet within 10 cm of the floor.",
                        errors);
                }
                Require(enemy.IsBoss ? enemy.AttackWindupSeconds >= 0.7f :
                        enemy.Behavior is not (EnemyBehavior.Skirmisher or EnemyBehavior.Spitter or EnemyBehavior.Warden) ||
                        enemy.AttackWindupSeconds is >= 0.45f and <= 0.65f,
                    $"Release enemy '{enemy.Id}' has an invalid attack telegraph duration.", errors);
                if (enemy.Behavior == EnemyBehavior.Charger)
                {
                    Require(enemy.ChargeWindupSeconds >= 0.9f,
                        $"Release charger '{enemy.Id}' needs at least a 0.9-second charge tell.", errors);
                }

                if (enemy.Behavior is EnemyBehavior.Charger or EnemyBehavior.Warden or EnemyBehavior.Boss)
                {
                    Require(!enemy.StaggerableDuringWindup,
                        $"Armored release enemy '{enemy.Id}' cannot be staggerable during windup by default.", errors);
                }
            }
            Require(float.IsFinite(enemy.Visual.TargetHeight) && enemy.Visual.TargetHeight > 0f,
                $"Enemy '{enemy.Id}' visual target height must be finite and positive.", errors);
            Require(float.IsFinite(enemy.Visual.GroundOffset) && float.IsFinite(enemy.Visual.HoverOffset) &&
                float.IsFinite(enemy.Visual.ForwardYawDegrees),
                $"Enemy '{enemy.Id}' visual calibration offsets must be finite.", errors);
            Require(float.IsFinite(enemy.Visual.CorpseLifetimeSeconds) && enemy.Visual.CorpseLifetimeSeconds >= 0f,
                $"Enemy '{enemy.Id}' corpse lifetime cannot be negative or non-finite.", errors);
            Require(float.IsFinite(enemy.Visual.SourceUnitScale) && enemy.Visual.SourceUnitScale > 0f,
                $"Enemy '{enemy.Id}' source unit scale must be finite and positive.", errors);
            Require(IsFinite(enemy.Visual.HealthBarAnchor) && IsFinite(enemy.Visual.EmissiveAccent) &&
                enemy.Visual.EmissiveAccent.X >= 0f && enemy.Visual.EmissiveAccent.Y >= 0f &&
                enemy.Visual.EmissiveAccent.Z >= 0f,
                $"Enemy '{enemy.Id}' visual anchors and emissive accent must be finite and non-negative.", errors);
            foreach ((string alias, AnimationClipBinding binding) in enemy.Visual.AnimationBindings)
            {
                Require(!string.IsNullOrWhiteSpace(alias) && !string.IsNullOrWhiteSpace(binding.ClipName),
                    $"Enemy '{enemy.Id}' has an animation binding without an alias or clip.", errors);
                Require(float.IsFinite(binding.PlaybackRate) && binding.PlaybackRate > 0f &&
                    float.IsFinite(binding.TransitionSeconds) && binding.TransitionSeconds >= 0f,
                    $"Enemy '{enemy.Id}' animation binding '{alias}' has invalid timing.", errors);
                Require(float.IsFinite(binding.StartNormalized) && float.IsFinite(binding.EndNormalized) &&
                    binding.StartNormalized >= 0f && binding.StartNormalized < binding.EndNormalized &&
                    binding.EndNormalized <= 1f,
                    $"Enemy '{enemy.Id}' animation binding '{alias}' has an invalid normalized clip window.", errors);
            }
            Require(enemy.HealthDropChance is >= 0f and <= 1f && enemy.AmmoDropChance is >= 0f and <= 1f &&
                enemy.HealthDropChance + enemy.AmmoDropChance <= 1f,
                $"Enemy '{enemy.Id}' drop chances must be probabilities with a combined maximum of one.", errors);
            if (enemy.Behavior is EnemyBehavior.Skirmisher or EnemyBehavior.Spitter or EnemyBehavior.Warden or EnemyBehavior.Boss)
            {
                Require(enemy.RangedAttackRange > enemy.AttackRange,
                    $"Ranged enemy '{enemy.Id}' needs a ranged attack distance.", errors);
                Require(enemy.ProjectileSpeed > 0f,
                    $"Ranged enemy '{enemy.Id}' needs projectile speed.", errors);
            }

            if (enemy.Behavior == EnemyBehavior.Charger)
            {
                Require(enemy.ChargeSpeed > enemy.MoveSpeed && enemy.ChargeRange > enemy.AttackRange,
                    $"Charger '{enemy.Id}' needs valid charge speed and range.", errors);
            }

            if (enemy.Behavior == EnemyBehavior.Warden)
            {
                Require(enemy.SupportRadius > 0f && enemy.SupportPulseSeconds > 0f && enemy.SupportHealAmount > 0f,
                    $"Warden '{enemy.Id}' needs a support pulse.", errors);
            }

            Require(enemy.IsBoss == (enemy.Behavior == EnemyBehavior.Boss),
                $"Enemy '{enemy.Id}' boss flag and behavior must agree.", errors);
            if (enemy.IsBoss)
            {
                Require(enemy.BossPhases.Count >= 3, $"Boss '{enemy.Id}' needs at least three phases.", errors);
                float previousThreshold = float.PositiveInfinity;
                foreach (BossPhaseDefinition phase in enemy.BossPhases)
                {
                    Require(phase.HealthThreshold is > 0f and <= 1f,
                        $"Boss phase '{phase.Id}' has an invalid health threshold.", errors);
                    Require(phase.HealthThreshold < previousThreshold,
                        $"Boss '{enemy.Id}' phase thresholds must descend.", errors);
                    Require(phase.MoveSpeedMultiplier > 0f && phase.AttackCooldownMultiplier > 0f &&
                        phase.DamageMultiplier > 0f && phase.ProjectileSpeedMultiplier > 0f,
                        $"Boss phase '{phase.Id}' multipliers must be positive.", errors);
                    if (phase.SummonCount > 0)
                    {
                        Require(phase.SummonEnemyId is not null && catalog.Enemies.ContainsKey(phase.SummonEnemyId),
                            $"Boss phase '{phase.Id}' has a missing summon enemy.", errors);
                    }

                    previousThreshold = phase.HealthThreshold;
                }
            }
            IEnumerable<string> requiredStates = enemy.SchemaVersion >= 2
                ? RequiredRobotStates
                : RequiredAlienClips;
            foreach (string clip in requiredStates)
            {
                Require(enemy.AnimationClips.ContainsKey(clip) || enemy.Visual.AnimationBindings.ContainsKey(clip),
                    $"Enemy '{enemy.Id}' is missing the '{clip}' animation alias.", errors);
            }
        }

        foreach (WaveSetDefinition waveSet in catalog.WaveSets.Values)
        {
            Require(waveSet.SchemaVersion is 1 or 2,
                $"Wave set '{waveSet.Id}' has an unsupported schema version.", errors);
            Require(waveSet.Difficulty == DifficultyMode.Standard,
                $"Wave set '{waveSet.Id}' must identify the shipped Standard difficulty.", errors);
            Require(waveSet.InterWaveDelaySeconds >= 0f,
                $"Wave set '{waveSet.Id}' has a negative inter-wave delay.", errors);
            Require(waveSet.Waves.Count > 0, $"Wave set '{waveSet.Id}' has no waves.", errors);
            foreach (WaveDefinition wave in waveSet.Waves.Append(waveSet.BossWave).OfType<WaveDefinition>())
            {
                Require(wave.SpawnGroups.Count > 0, $"Wave '{wave.Id}' has no spawn groups.", errors);
                Require(wave.MaximumConcurrentEnemies > 0,
                    $"Wave '{wave.Id}' concurrency must be positive.", errors);
                Require(wave.SpawnIntervalSeconds > 0f,
                    $"Wave '{wave.Id}' spawn interval must be positive.", errors);
                foreach (SpawnGroupDefinition group in wave.SpawnGroups)
                {
                    Require(catalog.Enemies.ContainsKey(group.EnemyId), $"Wave '{wave.Id}' references missing enemy '{group.EnemyId}'.", errors);
                    Require(group.Count > 0, $"Wave '{wave.Id}' contains a non-positive spawn count.", errors);
                }
            }

            if (waveSet.BossWave is not null)
            {
                Require(waveSet.BossWave.SpawnGroups.Any(group =>
                        catalog.Enemies.TryGetValue(group.EnemyId, out EnemyDefinition? enemy) && enemy.IsBoss),
                    $"Wave set '{waveSet.Id}' boss wave does not contain a boss enemy.", errors);
            }
        }

        foreach (ArenaDefinition arena in catalog.Arenas.Values)
        {
            Require(arena.SchemaVersion is 1 or 2, $"Arena '{arena.Id}' has an unsupported schema version.", errors);
            Require(catalog.WaveSets.ContainsKey(arena.WaveSetId), $"Arena '{arena.Id}' references missing wave set '{arena.WaveSetId}'.", errors);
            if (catalog.WaveSets.TryGetValue(arena.WaveSetId, out WaveSetDefinition? arenaWaveSet))
            {
                Require(arenaWaveSet.SchemaVersion == arena.SchemaVersion,
                    $"Arena '{arena.Id}' schema v{arena.SchemaVersion} must reference a v{arena.SchemaVersion} wave set.",
                    errors);
            }
            Require(arena.BoundsMin.X < arena.BoundsMax.X && arena.BoundsMin.Z < arena.BoundsMax.Z,
                $"Arena '{arena.Id}' has invalid bounds.", errors);
            Require(IsInside(arena, arena.PlayerSpawn), $"Arena '{arena.Id}' player spawn is outside its bounds.", errors);
            Require(arena.EnemySpawns.Count > 0, $"Arena '{arena.Id}' needs enemy spawns.", errors);
            Require(arena.NavigationCellSize > 0, $"Arena '{arena.Id}' navigation cell size must be positive.", errors);
            Require(arena.FogStart >= 0f && arena.FogEnd > arena.FogStart,
                $"Arena '{arena.Id}' has invalid fog distances.", errors);
            if (arena.SchemaVersion >= 2)
            {
                Require(arena.Sectors.Count >= 4,
                    $"Release arena '{arena.Id}' needs four authored sectors.", errors);
                Require(IsInside(arena, arena.BossArenaAnchor),
                    $"Release arena '{arena.Id}' boss anchor is outside its bounds.", errors);
                Require(float.IsFinite(arena.BossArenaHalfExtents.X) &&
                        float.IsFinite(arena.BossArenaHalfExtents.Z) &&
                        arena.BossArenaHalfExtents.X >= 8f && arena.BossArenaHalfExtents.Z >= 8f,
                    $"Release arena '{arena.Id}' boss arena half extents must be finite and at least 8m.", errors);
                Require(
                    arena.BossArenaAnchor.X - arena.BossArenaHalfExtents.X >= arena.BoundsMin.X &&
                    arena.BossArenaAnchor.X + arena.BossArenaHalfExtents.X <= arena.BoundsMax.X &&
                    arena.BossArenaAnchor.Z - arena.BossArenaHalfExtents.Z >= arena.BoundsMin.Z &&
                    arena.BossArenaAnchor.Z + arena.BossArenaHalfExtents.Z <= arena.BoundsMax.Z,
                    $"Release arena '{arena.Id}' boss arena exceeds the authored arena bounds.", errors);
                Require(arena.Sectors.Select(sector => sector.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() ==
                    arena.Sectors.Count,
                    $"Release arena '{arena.Id}' has duplicate sector ids.", errors);
            }
            for (int index = 0; index < arena.EnemySpawns.Count; index++)
            {
                Require(IsInside(arena, arena.EnemySpawns[index]), $"Arena '{arena.Id}' enemy spawn {index} is outside its bounds.", errors);
            }

            foreach (ArenaPrimitiveDefinition primitive in arena.Primitives)
            {
                Require(primitive.Size.X > 0f && primitive.Size.Y > 0f && primitive.Size.Z > 0f,
                    $"Arena '{arena.Id}' primitive '{primitive.Id}' has a non-positive size.", errors);
                if (!string.IsNullOrWhiteSpace(primitive.TextureAsset))
                {
                    Require(primitive.TextureMetersPerTile > 0f,
                        $"Arena '{arena.Id}' primitive '{primitive.Id}' has an invalid texture scale.", errors);
                }
            }

            foreach (ArenaPropDefinition prop in arena.Props)
            {
                Require(!string.IsNullOrWhiteSpace(prop.ModelAsset),
                    $"Arena '{arena.Id}' prop '{prop.Id}' needs a model asset.", errors);
                Require(prop.TargetSpan > 0f,
                    $"Arena '{arena.Id}' prop '{prop.Id}' has a non-positive target span.", errors);
                Require(IsInside(arena, prop.Position),
                    $"Arena '{arena.Id}' prop '{prop.Id}' is outside its bounds.", errors);
            }

            foreach (PickupSpawnDefinition pickup in arena.PickupSpawns)
            {
                Require(IsInside(arena, pickup.Position), $"Arena '{arena.Id}' has a pickup outside its bounds.", errors);
                Require(pickup.Amount > 0, $"Arena '{arena.Id}' has a non-positive pickup amount.", errors);
                Require(pickup.RespawnSeconds > 0f, $"Arena '{arena.Id}' has a non-positive pickup respawn.", errors);
                if (pickup.Type == PickupType.Weapon)
                {
                    Require(pickup.WeaponId is not null && catalog.Weapons.ContainsKey(pickup.WeaponId),
                        $"Arena '{arena.Id}' has a weapon pickup with a missing weapon reference.", errors);
                }
            }

            foreach (ArenaSectorDefinition sector in arena.Sectors)
            {
                Require(sector.BoundsMin.X < sector.BoundsMax.X && sector.BoundsMin.Z < sector.BoundsMax.Z,
                    $"Arena '{arena.Id}' sector '{sector.Id}' has invalid bounds.", errors);
                Require(IsInside(arena, sector.EntryPoint) && IsInside(arena, sector.ObjectiveAnchor),
                    $"Arena '{arena.Id}' sector '{sector.Id}' has an anchor outside arena bounds.", errors);
                Require(IsInside(sector, sector.EntryPoint) && IsInside(sector, sector.ObjectiveAnchor),
                    $"Arena '{arena.Id}' sector '{sector.Id}' has an anchor outside sector bounds.", errors);
                Require(sector.SpawnPortals.Count > 0,
                    $"Arena '{arena.Id}' sector '{sector.Id}' needs at least one spawn portal.", errors);
                Require(sector.SpawnPortals.Select(portal => portal.Id)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count() == sector.SpawnPortals.Count,
                    $"Arena '{arena.Id}' sector '{sector.Id}' has duplicate portal ids.", errors);
                Require(sector.EnergyGateIds.All(id => !string.IsNullOrWhiteSpace(id)) &&
                    sector.EnergyGateIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() ==
                    sector.EnergyGateIds.Count,
                    $"Arena '{arena.Id}' sector '{sector.Id}' has invalid energy gate ids.", errors);
                foreach (SpawnPortalDefinition portal in sector.SpawnPortals)
                {
                    Require(IsInside(arena, portal.Position),
                        $"Arena '{arena.Id}' portal '{portal.Id}' is outside arena bounds.", errors);
                    Require(IsInside(sector, portal.Position),
                        $"Arena '{arena.Id}' portal '{portal.Id}' is outside sector bounds.", errors);
                    Require(portal.TelegraphSeconds >= 0.75f,
                        $"Arena '{arena.Id}' portal '{portal.Id}' telegraph must be at least 0.75 seconds.", errors);
                    float entryDistance = System.Numerics.Vector3.Distance(sector.EntryPoint, portal.Position);
                    Require(entryDistance is >= 12f and <= 32f,
                        $"Arena '{arena.Id}' portal '{portal.Id}' must be 12-32m from sector entry.", errors);
                }
            }
        }

        foreach (UpgradeDefinition upgrade in catalog.Upgrades.Values)
        {
            Require(upgrade.SchemaVersion == 2,
                $"Upgrade '{upgrade.Id}' has an unsupported schema version.", errors);
            Require(!string.IsNullOrWhiteSpace(upgrade.DisplayName) && !string.IsNullOrWhiteSpace(upgrade.Description),
                $"Upgrade '{upgrade.Id}' needs player-facing text.", errors);
            Require(upgrade.Effects.Count > 0,
                $"Upgrade '{upgrade.Id}' needs at least one typed effect.", errors);
            foreach (UpgradeEffectDefinition effect in upgrade.Effects)
            {
                Require(float.IsFinite(effect.Value) && effect.Value > 0f,
                    $"Upgrade '{upgrade.Id}' has a non-positive effect value.", errors);
                if (hasReleaseCampaign && WeaponScopedUpgradeEffects.Contains(effect.Type))
                {
                    Require(effect.WeaponId is not null && catalog.Weapons.ContainsKey(effect.WeaponId),
                        $"Upgrade '{upgrade.Id}' has a weapon-scoped effect without a valid weapon.", errors);
                }
            }
        }

        if (hasReleaseCampaign)
        {
            Require(catalog.Upgrades.Count == 18,
                "The release campaign must expose exactly 18 non-stacking in-run upgrades.", errors);
        }

        return new ContentValidationResult(errors);
    }

    private static bool IsInside(ArenaDefinition arena, System.Numerics.Vector3 point) =>
        point.X >= arena.BoundsMin.X && point.X <= arena.BoundsMax.X &&
        point.Y >= arena.BoundsMin.Y && point.Y <= arena.BoundsMax.Y &&
        point.Z >= arena.BoundsMin.Z && point.Z <= arena.BoundsMax.Z;

    private static bool IsFinite(System.Numerics.Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsInside(ArenaSectorDefinition sector, System.Numerics.Vector3 point) =>
        point.X >= sector.BoundsMin.X && point.X <= sector.BoundsMax.X &&
        point.Y >= sector.BoundsMin.Y && point.Y <= sector.BoundsMax.Y &&
        point.Z >= sector.BoundsMin.Z && point.Z <= sector.BoundsMax.Z;

    private static void Require(bool condition, string message, List<string> errors)
    {
        if (!condition)
        {
            errors.Add(message);
        }
    }
}
