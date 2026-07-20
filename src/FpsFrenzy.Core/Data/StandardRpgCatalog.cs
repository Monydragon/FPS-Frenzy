namespace FpsFrenzy.Core.Data;

public static class StandardRpgCatalog
{
    private static readonly string[] ActiveAbilityIds =
    [
        "barrier-pulse", "repair-drone", "overclock", "gravity-well",
        "emp-burst", "arc-nova", "plasma-mine", "decoy-beacon",
        "ammo-synthesizer", "target-mark", "coolant-vent", "phase-guard",
    ];

    private static readonly (string Id, string Name, AffixEffectType Type, float Value)[] PassiveSpecs =
    [
        ("pulse-discipline", "Pulse Discipline", AffixEffectType.WeaponDamage, 1.08f),
        ("burst-discipline", "Burst Discipline", AffixEffectType.WeaponDamage, 1.08f),
        ("scatter-discipline", "Scatter Discipline", AffixEffectType.WeaponDamage, 1.08f),
        ("beam-discipline", "Beam Discipline", AffixEffectType.WeaponDamage, 1.08f),
        ("plasma-discipline", "Plasma Discipline", AffixEffectType.WeaponDamage, 1.08f),
        ("arc-discipline", "Arc Discipline", AffixEffectType.WeaponDamage, 1.08f),
        ("smg-discipline", "SMG Discipline", AffixEffectType.WeaponDamage, 1.08f),
        ("precision-discipline", "Precision Discipline", AffixEffectType.WeaponDamage, 1.08f),
        ("heavy-discipline", "Heavy Discipline", AffixEffectType.WeaponDamage, 1.08f),
        ("experimental-discipline", "Experimental Discipline", AffixEffectType.WeaponDamage, 1.08f),
        ("rapid-loader", "Rapid Loader", AffixEffectType.ReloadAndRecovery, 1.10f),
        ("expanded-reserves", "Expanded Reserves", AffixEffectType.Capacity, 1.12f),
        ("steady-hands", "Steady Hands", AffixEffectType.Stability, 0.88f),
        ("reinforced-frame", "Reinforced Frame", AffixEffectType.MaximumHealth, 12f),
        ("reactive-plating", "Reactive Plating", AffixEffectType.Armor, 12f),
        ("phase-dampener", "Phase Dampener", AffixEffectType.IncomingDamage, 0.94f),
        ("kinetic-servos", "Kinetic Servos", AffixEffectType.MovementSpeed, 1.06f),
        ("ability-amplifier", "Ability Amplifier", AffixEffectType.AbilityPower, 1.10f),
        ("cooldown-loop", "Cooldown Loop", AffixEffectType.CooldownRecovery, 1.10f),
        ("salvage-magnet", "Salvage Magnet", AffixEffectType.PickupRadius, 1.35f),
        ("combat-learning", "Combat Learning", AffixEffectType.ExperienceGain, 1.10f),
        ("ability-study", "Ability Study", AffixEffectType.AbilityPointGain, 1.10f),
        ("field-proficiency", "Field Proficiency", AffixEffectType.ProficiencyGain, 1.10f),
        ("scavenger-protocol", "Scavenger Protocol", AffixEffectType.LootChance, 1.12f),
        ("rarity-analysis", "Rarity Analysis", AffixEffectType.RarityLuck, 1.10f),
        ("efficient-salvage", "Efficient Salvage", AffixEffectType.SalvageYield, 1.15f),
        ("emergency-repair", "Emergency Repair", AffixEffectType.MaximumHealth, 8f),
        ("combat-flow", "Combat Flow", AffixEffectType.FireInterval, 0.94f),
    ];

    public static IReadOnlyList<EquipmentAbilityDefinition> Abilities { get; } = BuildAbilities();
    public static IReadOnlyList<EquipmentBaseDefinition> Equipment { get; } = BuildEquipment();
    public static IReadOnlyList<AffixDefinition> Affixes { get; } = BuildAffixes();
    public static IReadOnlyList<TalentDefinition> Talents { get; } = BuildTalents();
    public static LootTableDefinition StandardLootTable { get; } = new() { Id = "standard-equipment" };

    private static List<EquipmentAbilityDefinition> BuildAbilities()
    {
        List<EquipmentAbilityDefinition> abilities = [];
        for (int index = 0; index < ActiveAbilityIds.Length; index++)
        {
            string id = ActiveAbilityIds[index];
            abilities.Add(new EquipmentAbilityDefinition
            {
                Id = id,
                DisplayName = Display(id),
                Description = $"Activate {Display(id)} through the equipped ability loadout.",
                Kind = AbilityKind.Active,
                CooldownSeconds = 14f + ((index % 5) * 3f),
                RequiredAbilityPoints = 35 + (index * 10),
            });
        }

        for (int passiveIndex = 0; passiveIndex < PassiveSpecs.Length; passiveIndex++)
        {
            (string id, string name, AffixEffectType type, float value) = PassiveSpecs[passiveIndex];
            abilities.Add(new EquipmentAbilityDefinition
            {
                Id = id,
                DisplayName = name,
                Description = $"Permanently learn {name} from compatible equipment.",
                Kind = AbilityKind.Passive,
                CapacityCost = 1 + (abilities.Count % 5),
                RequiredAbilityPoints = 20 + ((abilities.Count % 5) * 30),
                Effects = [new AffixEffectDefinition
                {
                    Type = type,
                    Value = value,
                    WeaponFamily = passiveIndex < 6 ? (WeaponFamily)(passiveIndex + 1) : WeaponFamily.None,
                }],
            });
        }

        return abilities;
    }

    private static List<EquipmentBaseDefinition> BuildEquipment()
    {
        List<EquipmentBaseDefinition> definitions = [];
        EquipmentSlot[] armorSlots =
            [EquipmentSlot.Head, EquipmentSlot.Chest, EquipmentSlot.Hands, EquipmentSlot.Legs, EquipmentSlot.Feet];
        string[] archetypes = ["Scout", "Assault", "Bulwark"];
        int abilityIndex = 6;
        foreach (EquipmentSlot slot in armorSlots)
        {
            foreach (string archetype in archetypes)
            {
                string slotName = slot.ToString().ToLowerInvariant();
                string archetypeName = archetype.ToLowerInvariant();
                definitions.Add(new EquipmentBaseDefinition
                {
                    Id = $"{archetypeName}-{slotName}",
                    DisplayName = $"{archetype} {slot}",
                    Archetype = archetype,
                    CompatibleSlots = [slot],
                    ModelAsset = "Models/Enemies/Robots/Leela",
                    IconAsset = "Textures/UI/menu-emblem",
                    TaughtAbilityId = PassiveSpecs[abilityIndex++ % PassiveSpecs.Length].Id,
                    BaseArmor = archetype switch { "Bulwark" => 12f, "Assault" => 8f, _ => 5f },
                    BaseMaximumHealth = slot == EquipmentSlot.Chest ?
                        archetype switch { "Bulwark" => 15f, "Assault" => 9f, _ => 5f } : 0f,
                });
            }
        }

        for (int index = 0; index < 8; index++)
        {
            definitions.Add(new EquipmentBaseDefinition
            {
                Id = $"utility-module-{index + 1}",
                DisplayName = $"Utility Module {index + 1}",
                Archetype = "Utility",
                CompatibleSlots = [EquipmentSlot.Accessory1, EquipmentSlot.Accessory2],
                ModelAsset = "Models/Enemies/Robots/Mike",
                IconAsset = "Textures/UI/menu-emblem",
                TaughtAbilityId = ActiveAbilityIds[index],
            });
            definitions.Add(new EquipmentBaseDefinition
            {
                Id = $"circuit-ring-{index + 1}",
                DisplayName = $"Circuit Ring {index + 1}",
                Archetype = "Circuit",
                CompatibleSlots = [EquipmentSlot.Ring1, EquipmentSlot.Ring2],
                ModelAsset = "Models/Enemies/Robots/Stan",
                IconAsset = "Textures/UI/menu-emblem",
                TaughtAbilityId = PassiveSpecs[index].Id,
            });
        }

        return definitions;
    }

    private static AffixDefinition[] BuildAffixes()
    {
        EquipmentSlot[] all = Enum.GetValues<EquipmentSlot>();
        (string Id, AffixEffectType Type, float Min, float Max)[] specs =
        [
            ("calibrated", AffixEffectType.WeaponDamage, 1.03f, 1.12f),
            ("rapid", AffixEffectType.FireInterval, 0.90f, 0.98f),
            ("loaded", AffixEffectType.ReloadAndRecovery, 1.04f, 1.18f),
            ("expanded", AffixEffectType.Capacity, 1.05f, 1.22f),
            ("stable", AffixEffectType.Stability, 0.78f, 0.95f),
            ("reinforced", AffixEffectType.MaximumHealth, 4f, 18f),
            ("armored", AffixEffectType.Armor, 4f, 18f),
            ("dampened", AffixEffectType.IncomingDamage, 0.90f, 0.98f),
            ("kinetic", AffixEffectType.MovementSpeed, 1.02f, 1.08f),
            ("amplified", AffixEffectType.AbilityPower, 1.04f, 1.16f),
            ("recursive", AffixEffectType.CooldownRecovery, 1.04f, 1.15f),
            ("magnetic", AffixEffectType.PickupRadius, 1.10f, 1.50f),
            ("learned", AffixEffectType.ExperienceGain, 1.04f, 1.15f),
            ("studious", AffixEffectType.AbilityPointGain, 1.04f, 1.15f),
            ("practiced", AffixEffectType.ProficiencyGain, 1.04f, 1.15f),
            ("scavenging", AffixEffectType.LootChance, 1.04f, 1.15f),
            ("fortunate", AffixEffectType.RarityLuck, 1.04f, 1.15f),
            ("reclaiming", AffixEffectType.SalvageYield, 1.05f, 1.25f),
        ];
        return specs.Select((spec, index) => new AffixDefinition
        {
            Id = spec.Id,
            DisplayName = Display(spec.Id),
            IsPrefix = index % 2 == 0,
            AllowedSlots = [.. all],
            EffectType = spec.Type,
            MinimumValue = spec.Min,
            MaximumValue = spec.Max,
        }).ToArray();
    }

    private static List<TalentDefinition> BuildTalents()
    {
        Dictionary<TalentBranch, (AffixEffectType Type, float Value)[]> branchEffects = new()
        {
            [TalentBranch.Arsenal] =
            [
                (AffixEffectType.WeaponDamage, 0.005f), (AffixEffectType.ReloadAndRecovery, 0.02f),
                (AffixEffectType.Capacity, 0.03f), (AffixEffectType.Stability, 0.03f),
                (AffixEffectType.AbilityPower, 0.02f), (AffixEffectType.CooldownRecovery, 0.02f),
                (AffixEffectType.ProficiencyGain, 0.05f), (AffixEffectType.FireInterval, 0.01f),
                (AffixEffectType.WeaponDamage, 0.005f), (AffixEffectType.AbilityPower, 0.03f),
            ],
            [TalentBranch.Bulwark] =
            [
                (AffixEffectType.MaximumHealth, 2f), (AffixEffectType.Armor, 2f),
                (AffixEffectType.IncomingDamage, 0.01f), (AffixEffectType.MaximumHealth, 2f),
                (AffixEffectType.Armor, 2f), (AffixEffectType.IncomingDamage, 0.01f),
                (AffixEffectType.MovementSpeed, 0.005f), (AffixEffectType.MaximumHealth, 3f),
                (AffixEffectType.Armor, 3f), (AffixEffectType.IncomingDamage, 0.01f),
            ],
            [TalentBranch.Salvage] =
            [
                (AffixEffectType.LootChance, 0.02f), (AffixEffectType.RarityLuck, 0.02f),
                (AffixEffectType.ExperienceGain, 0.03f), (AffixEffectType.AbilityPointGain, 0.03f),
                (AffixEffectType.ProficiencyGain, 0.03f), (AffixEffectType.PickupRadius, 0.05f),
                (AffixEffectType.SalvageYield, 0.04f), (AffixEffectType.ReloadAndRecovery, 0.01f),
                (AffixEffectType.RarityLuck, 0.02f), (AffixEffectType.LootChance, 0.02f),
            ],
        };
        List<TalentDefinition> talents = [];
        foreach ((TalentBranch branch, (AffixEffectType Type, float Value)[] effects) in branchEffects)
        {
            for (int index = 0; index < effects.Length; index++)
            {
                int tier = (index / 2) + 1;
                talents.Add(new TalentDefinition
                {
                    Id = $"{branch.ToString().ToLowerInvariant()}-{index + 1}",
                    DisplayName = $"{branch} Protocol {index + 1}",
                    Description = $"Tier {tier} {branch} specialization.",
                    Branch = branch,
                    Tier = tier,
                    RequiredBranchPoints = (tier - 1) * 10,
                    EffectType = effects[index].Type,
                    ValuePerRank = effects[index].Value,
                });
            }
        }

        return talents;
    }

    private static string Display(string id) => string.Join(' ', id.Split('-').Select(word =>
        char.ToUpperInvariant(word[0]) + word[1..]));
}
