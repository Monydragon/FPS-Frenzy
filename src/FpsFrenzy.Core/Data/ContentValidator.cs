namespace FpsFrenzy.Core.Data;

public static class ContentValidator
{
    private static readonly string[] RequiredAlienClips = ["idle", "walk", "attack", "hit", "death"];

    public static ContentValidationResult Validate(ContentCatalog catalog)
    {
        List<string> errors = [];

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
            Require(enemy.SchemaVersion == 1, $"Enemy '{enemy.Id}' has an unsupported schema version.", errors);
            Require(enemy.MaxHealth > 0, $"Enemy '{enemy.Id}' health must be positive.", errors);
            Require(enemy.MoveSpeed > 0, $"Enemy '{enemy.Id}' move speed must be positive.", errors);
            Require(enemy.ColliderRadius > 0 && enemy.ColliderHeight > 0,
                $"Enemy '{enemy.Id}' collider must be positive.", errors);
            Require(enemy.AttackDamage > 0f && enemy.AttackCooldownSeconds > 0f,
                $"Enemy '{enemy.Id}' attack values must be positive.", errors);
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
            foreach (string clip in RequiredAlienClips)
            {
                Require(enemy.AnimationClips.ContainsKey(clip), $"Enemy '{enemy.Id}' is missing the '{clip}' animation alias.", errors);
            }
        }

        foreach (WaveSetDefinition waveSet in catalog.WaveSets.Values)
        {
            Require(waveSet.SchemaVersion == 1, $"Wave set '{waveSet.Id}' has an unsupported schema version.", errors);
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
            Require(arena.SchemaVersion == 1, $"Arena '{arena.Id}' has an unsupported schema version.", errors);
            Require(catalog.WaveSets.ContainsKey(arena.WaveSetId), $"Arena '{arena.Id}' references missing wave set '{arena.WaveSetId}'.", errors);
            Require(arena.BoundsMin.X < arena.BoundsMax.X && arena.BoundsMin.Z < arena.BoundsMax.Z,
                $"Arena '{arena.Id}' has invalid bounds.", errors);
            Require(IsInside(arena, arena.PlayerSpawn), $"Arena '{arena.Id}' player spawn is outside its bounds.", errors);
            Require(arena.EnemySpawns.Count > 0, $"Arena '{arena.Id}' needs enemy spawns.", errors);
            Require(arena.NavigationCellSize > 0, $"Arena '{arena.Id}' navigation cell size must be positive.", errors);
            Require(arena.FogStart >= 0f && arena.FogEnd > arena.FogStart,
                $"Arena '{arena.Id}' has invalid fog distances.", errors);
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
        }

        return new ContentValidationResult(errors);
    }

    private static bool IsInside(ArenaDefinition arena, System.Numerics.Vector3 point) =>
        point.X >= arena.BoundsMin.X && point.X <= arena.BoundsMax.X &&
        point.Y >= arena.BoundsMin.Y && point.Y <= arena.BoundsMax.Y &&
        point.Z >= arena.BoundsMin.Z && point.Z <= arena.BoundsMax.Z;

    private static void Require(bool condition, string message, List<string> errors)
    {
        if (!condition)
        {
            errors.Add(message);
        }
    }
}
