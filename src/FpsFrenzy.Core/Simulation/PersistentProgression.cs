using FpsFrenzy.Core.Data;

namespace FpsFrenzy.Core.Simulation;

public sealed record RolledAffix
{
    public required string AffixId { get; init; }
    public float Value { get; init; }
}

public sealed record EquipmentInstance
{
    public required string Id { get; init; }
    public string? EquipmentBaseId { get; init; }
    public string? WeaponBaseId { get; init; }
    public required string DisplayName { get; init; }
    public EquipmentSlot PrimarySlot { get; init; }
    public ItemRarity Rarity { get; init; }
    public int ItemPower { get; init; } = 1;
    public List<RolledAffix> Affixes { get; init; } = [];
    public string? UniqueEffectId { get; init; }
    public bool IsFavorite { get; init; }
    public bool IsLocked { get; init; }
    public bool IsRunBound { get; init; }

    public bool IsWeapon => !string.IsNullOrWhiteSpace(WeaponBaseId);
}

public sealed record StarterWeaponReference
{
    public required string WeaponBaseId { get; init; }
    public string? ItemInstanceId { get; init; }
    public bool IsArmoryIssue => string.IsNullOrWhiteSpace(ItemInstanceId);

    public static StarterWeaponReference Issue(string weaponBaseId) => new()
    {
        WeaponBaseId = weaponBaseId,
    };
}

public sealed class WeaponSetLoadout
{
    public StarterWeaponReference? RightHand { get; init; }
    public StarterWeaponReference? LeftHand { get; init; }

    public bool IsEmpty => RightHand is null && LeftHand is null;

    public WeaponSetLoadout Clone() => new()
    {
        RightHand = RightHand is null ? null : RightHand with { },
        LeftHand = LeftHand is null ? null : LeftHand with { },
    };
}

public sealed class WeaponPresetSlot
{
    public StarterWeaponReference? RightHand { get; init; }
    public StarterWeaponReference? LeftHand { get; init; }

    public bool IsEmpty => RightHand is null && LeftHand is null;

    public WeaponPresetSlot Clone() => new()
    {
        RightHand = RightHand is null ? null : RightHand with { },
        LeftHand = LeftHand is null ? null : LeftHand with { },
    };

    public WeaponSetLoadout ToWeaponSet() => new()
    {
        RightHand = RightHand is null ? null : RightHand with { },
        LeftHand = LeftHand is null ? null : LeftHand with { },
    };

    public static WeaponPresetSlot FromWeaponSet(WeaponSetLoadout? set) => new()
    {
        RightHand = set?.RightHand is null ? null : set.RightHand with { },
        LeftHand = set?.LeftHand is null ? null : set.LeftHand with { },
    };
}

public sealed class WeaponQuickbarLoadout
{
    public const int SlotCount = 10;

    public static IReadOnlyList<WeaponFamily> FamilyOrder { get; } =
    [
        WeaponFamily.Pulse,
        WeaponFamily.SMG,
        WeaponFamily.Burst,
        WeaponFamily.Scatter,
        WeaponFamily.Precision,
        WeaponFamily.Beam,
        WeaponFamily.Plasma,
        WeaponFamily.Arc,
        WeaponFamily.Heavy,
        WeaponFamily.Experimental,
    ];

    public List<WeaponPresetSlot> Slots { get; init; } = CreateEmptySlots();

    public WeaponPresetSlot this[int index] => index is >= 0 and < SlotCount && index < Slots.Count
        ? Slots[index]
        : new WeaponPresetSlot();

    public WeaponQuickbarLoadout Clone() => new()
    {
        Slots = Enumerable.Range(0, SlotCount).Select(index => this[index].Clone()).ToList(),
    };

    public static WeaponQuickbarLoadout FromLegacy(WeaponSetLoadout? setA, WeaponSetLoadout? setB)
    {
        List<WeaponPresetSlot> slots = CreateEmptySlots();
        slots[0] = WeaponPresetSlot.FromWeaponSet(setA);
        slots[1] = WeaponPresetSlot.FromWeaponSet(setB);
        return new WeaponQuickbarLoadout { Slots = slots };
    }

    public static WeaponFamily FamilyForSlot(int slotIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slotIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slotIndex, SlotCount);
        return FamilyOrder[slotIndex];
    }

    public static int SlotForFamily(WeaponFamily family)
    {
        for (int index = 0; index < FamilyOrder.Count; index++)
        {
            if (FamilyOrder[index] == family)
            {
                return index;
            }
        }
        throw new ArgumentOutOfRangeException(nameof(family), family,
            "Only playable weapon families have quickbar slots.");
    }

    private static List<WeaponPresetSlot> CreateEmptySlots() =>
        Enumerable.Range(0, SlotCount).Select(_ => new WeaponPresetSlot()).ToList();
}

public enum CraftingMaterialType
{
    Scrap,
    Components,
    Cores,
}

public readonly record struct CraftingMaterialBundle(int Scrap, int Components, int Cores)
{
    public static CraftingMaterialBundle Zero => new(0, 0, 0);
}

public sealed class CraftingWallet
{
    public int Scrap { get; set; }
    public int Components { get; set; }
    public int Cores { get; set; }

    public CraftingWallet Clone() => new()
    {
        Scrap = Math.Max(0, Scrap),
        Components = Math.Max(0, Components),
        Cores = Math.Max(0, Cores),
    };

    public bool CanAfford(CraftingMaterialBundle cost) =>
        Scrap >= cost.Scrap && Components >= cost.Components && Cores >= cost.Cores;

    public bool TrySpend(CraftingMaterialBundle cost)
    {
        if (!CanAfford(cost))
        {
            return false;
        }

        Scrap -= cost.Scrap;
        Components -= cost.Components;
        Cores -= cost.Cores;
        return true;
    }

    public void Add(CraftingMaterialBundle value)
    {
        Scrap = Math.Max(0, Scrap + value.Scrap);
        Components = Math.Max(0, Components + value.Components);
        Cores = Math.Max(0, Cores + value.Cores);
    }
}

public sealed record ItemUpgradeQuote
{
    public required string TargetItemId { get; init; }
    public required string DonorItemId { get; init; }
    public int CurrentItemPower { get; init; }
    public int ResultItemPower { get; init; }
    public CraftingMaterialBundle Cost { get; init; }
}

public sealed record CharacterStatSnapshot
{
    public int Level { get; init; } = 1;
    public int Experience { get; init; }
    public int ExperienceToNextLevel { get; init; }
    public int UnspentTalentPoints { get; init; }
    public float MaximumHealth { get; init; } = 100f;
    public float Armor { get; init; }
    public float ArmorMitigation { get; init; }
    public float DamageMultiplier { get; init; } = 1f;
    public float IncomingDamageMultiplier { get; init; } = 1f;
    public float MovementSpeedMultiplier { get; init; } = 1f;
    public float CooldownRecoveryMultiplier { get; init; } = 1f;
    public int PassiveCapacity { get; init; } = 10;
    public Dictionary<string, float> Contributions { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public static class EquipmentCrafting
{
    public static CraftingMaterialBundle GetDismantleYield(EquipmentInstance item, float yieldMultiplier = 1f)
    {
        ArgumentNullException.ThrowIfNull(item);
        int band = Math.Clamp((item.ItemPower + 9) / 10, 1, 10);
        CraftingMaterialBundle baseline = item.Rarity switch
        {
            ItemRarity.Common => new(2 * band, 0, 0),
            ItemRarity.Uncommon => new(3 * band, 1, 0),
            ItemRarity.Rare => new(4 * band, 2, 0),
            ItemRarity.Epic => new(5 * band, 3, 1),
            ItemRarity.Legendary => new(6 * band, 4, 2),
            _ => CraftingMaterialBundle.Zero,
        };
        float multiplier = float.IsFinite(yieldMultiplier) ? Math.Clamp(yieldMultiplier, 0f, 10f) : 1f;
        return new CraftingMaterialBundle(
            Math.Max(0, (int)MathF.Round(baseline.Scrap * multiplier)),
            Math.Max(0, (int)MathF.Round(baseline.Components * multiplier)),
            Math.Max(0, (int)MathF.Round(baseline.Cores * multiplier)));
    }

    public static bool TryCreateInfusionQuote(
        EquipmentInstance target,
        EquipmentInstance donor,
        ThreatTier highestUnlockedTier,
        IReadOnlySet<string> reservedItemIds,
        out ItemUpgradeQuote? quote,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(donor);
        ArgumentNullException.ThrowIfNull(reservedItemIds);
        if (target.Id.Equals(donor.Id, StringComparison.OrdinalIgnoreCase) || !SameBase(target, donor))
        {
            quote = null;
            error = "Infusion requires a different item instance of the exact same base.";
            return false;
        }
        if (donor.IsRunBound || donor.IsLocked || donor.IsFavorite || reservedItemIds.Contains(donor.Id))
        {
            quote = null;
            error = "The donor must be committed, unlocked, unfavorited, unequipped, and unassigned.";
            return false;
        }

        int cap = RpgProgressionMath.MaximumItemPower(highestUnlockedTier);
        if (target.ItemPower >= cap)
        {
            quote = null;
            error = "The target is already at the unlocked Item Power cap.";
            return false;
        }

        int nextBandCeiling = Math.Min(100, ((Math.Max(1, target.ItemPower) / 10) + 1) * 10);
        int resultPower = Math.Min(cap, Math.Max(donor.ItemPower, nextBandCeiling));
        int destinationBand = Math.Clamp((resultPower + 9) / 10, 1, 10);
        int rarityIndex = (int)target.Rarity;
        int cores = target.Rarity switch
        {
            ItemRarity.Epic => (destinationBand + 2) / 3,
            ItemRarity.Legendary => (destinationBand + 1) / 2,
            _ => 0,
        };
        quote = new ItemUpgradeQuote
        {
            TargetItemId = target.Id,
            DonorItemId = donor.Id,
            CurrentItemPower = target.ItemPower,
            ResultItemPower = resultPower,
            Cost = new CraftingMaterialBundle(
                5 * destinationBand,
                Math.Max(0, rarityIndex - 1) * destinationBand,
                cores),
        };
        error = null;
        return true;
    }

    public static bool TryInfuse(
        PlayerProgressionState progression,
        string targetItemId,
        string donorItemId,
        IReadOnlySet<string> reservedItemIds,
        out ItemUpgradeQuote? applied,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(progression);
        error = null;
        EquipmentInstance? target = progression.Stash.FirstOrDefault(item =>
            item.Id.Equals(targetItemId, StringComparison.OrdinalIgnoreCase));
        EquipmentInstance? donor = progression.Stash.FirstOrDefault(item =>
            item.Id.Equals(donorItemId, StringComparison.OrdinalIgnoreCase));
        if (target is null || donor is null || !TryCreateInfusionQuote(
                target, donor, progression.HighestUnlockedThreatTier, reservedItemIds, out ItemUpgradeQuote? quote, out error))
        {
            applied = null;
            error ??= "The target or donor is no longer in the committed stash.";
            return false;
        }
        if (!progression.Materials.TrySpend(quote!.Cost))
        {
            applied = null;
            error = "Not enough crafting materials.";
            return false;
        }

        int targetIndex = progression.Stash.FindIndex(item => item.Id.Equals(target.Id, StringComparison.OrdinalIgnoreCase));
        progression.Stash[targetIndex] = target with { ItemPower = quote.ResultItemPower };
        progression.Stash.RemoveAll(item => item.Id.Equals(donor.Id, StringComparison.OrdinalIgnoreCase));
        applied = quote;
        error = null;
        return true;
    }

    public static bool TryDismantle(
        PlayerProgressionState progression,
        string itemId,
        IReadOnlySet<string> reservedItemIds,
        bool confirmLegendary,
        float yieldMultiplier,
        out CraftingMaterialBundle yield,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(progression);
        EquipmentInstance? item = progression.Stash.FirstOrDefault(candidate =>
            candidate.Id.Equals(itemId, StringComparison.OrdinalIgnoreCase));
        if (item is null || item.IsRunBound || item.IsLocked || item.IsFavorite || reservedItemIds.Contains(item.Id))
        {
            yield = CraftingMaterialBundle.Zero;
            error = "The item is protected, pending, equipped, assigned, or unavailable.";
            return false;
        }
        if (item.Rarity == ItemRarity.Legendary && !confirmLegendary)
        {
            yield = CraftingMaterialBundle.Zero;
            error = "Legendary dismantling requires confirmation.";
            return false;
        }

        yield = GetDismantleYield(item, yieldMultiplier);
        progression.Stash.RemoveAll(candidate => candidate.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase));
        progression.Materials.Add(yield);
        error = null;
        return true;
    }

    private static bool SameBase(EquipmentInstance left, EquipmentInstance right) =>
        (!string.IsNullOrWhiteSpace(left.WeaponBaseId) && left.WeaponBaseId.Equals(
            right.WeaponBaseId, StringComparison.OrdinalIgnoreCase)) ||
        (!string.IsNullOrWhiteSpace(left.EquipmentBaseId) && left.EquipmentBaseId.Equals(
            right.EquipmentBaseId, StringComparison.OrdinalIgnoreCase));
}

public sealed class EquipmentLoadout
{
    public Dictionary<EquipmentSlot, string> EquippedItemIds { get; init; } = [];

    public string? this[EquipmentSlot slot] => EquippedItemIds.GetValueOrDefault(slot);

    public bool TryEquip(
        EquipmentInstance item,
        EquipmentSlot slot,
        IReadOnlyDictionary<string, EquipmentInstance> inventory,
        ContentCatalog catalog,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(catalog);

        if (!inventory.ContainsKey(item.Id))
        {
            error = "The item must be in the stash before it can be equipped.";
            return false;
        }

        if (item.IsWeapon)
        {
            if (!catalog.Weapons.TryGetValue(item.WeaponBaseId!, out WeaponDefinition? weapon))
            {
                error = $"Weapon base '{item.WeaponBaseId}' is missing.";
                return false;
            }

            if (slot is not (EquipmentSlot.RightHand or EquipmentSlot.LeftHand))
            {
                error = "Weapons can only be equipped in a hand slot.";
                return false;
            }

            RemoveItem(item.Id);
            if (weapon.Handedness == Handedness.TwoHanded)
            {
                Unequip(EquipmentSlot.RightHand);
                Unequip(EquipmentSlot.LeftHand);
                EquippedItemIds[EquipmentSlot.RightHand] = item.Id;
                EquippedItemIds[EquipmentSlot.LeftHand] = item.Id;
            }
            else if (weapon.Handedness == Handedness.OneHanded)
            {
                string? otherHandItemId = this[OtherHand(slot)];
                if (otherHandItemId is not null && inventory.TryGetValue(otherHandItemId, out EquipmentInstance? other) &&
                    other.IsWeapon && catalog.Weapons.TryGetValue(other.WeaponBaseId!, out WeaponDefinition? otherWeapon) &&
                    otherWeapon.Handedness == Handedness.TwoHanded)
                {
                    RemoveItem(other.Id);
                }

                Unequip(slot);
                EquippedItemIds[slot] = item.Id;
            }
            else
            {
                error = $"Weapon '{weapon.Id}' has invalid handedness.";
                return false;
            }
        }
        else
        {
            if (item.EquipmentBaseId is null ||
                !catalog.EquipmentBases.TryGetValue(item.EquipmentBaseId, out EquipmentBaseDefinition? definition))
            {
                error = $"Equipment base '{item.EquipmentBaseId}' is missing.";
                return false;
            }

            if (!definition.CompatibleSlots.Contains(slot))
            {
                error = $"{item.DisplayName} is not compatible with {slot}.";
                return false;
            }

            RemoveItem(item.Id);
            Unequip(slot);
            EquippedItemIds[slot] = item.Id;
        }

        error = null;
        return true;
    }

    public void Unequip(EquipmentSlot slot)
    {
        if (EquippedItemIds.TryGetValue(slot, out string? itemId))
        {
            RemoveItem(itemId);
        }
    }

    public void RemoveItem(string itemId)
    {
        foreach (EquipmentSlot slot in EquippedItemIds
            .Where(pair => string.Equals(pair.Value, itemId, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Key)
            .ToArray())
        {
            EquippedItemIds.Remove(slot);
        }
    }

    public EquipmentLoadout Clone() => new()
    {
        EquippedItemIds = new Dictionary<EquipmentSlot, string>(EquippedItemIds),
    };

    private static EquipmentSlot OtherHand(EquipmentSlot slot) => slot == EquipmentSlot.RightHand
        ? EquipmentSlot.LeftHand
        : EquipmentSlot.RightHand;
}

public sealed record WeaponProficiencyProgress
{
    public int Rank { get; init; } = 1;
    public int Experience { get; init; }
    public bool MasteryUnlocked => Rank >= RpgProgressionMath.MaximumWeaponProficiencyRank;
}

public sealed class WeaponProficiencyState
{
    public Dictionary<WeaponFamily, WeaponProficiencyProgress> Families { get; init; } =
        Enum.GetValues<WeaponFamily>()
            .Where(family => family != WeaponFamily.None)
            .ToDictionary(family => family, _ => new WeaponProficiencyProgress());

    public WeaponProficiencyProgress Get(WeaponFamily family) =>
        Families.GetValueOrDefault(family) ?? new WeaponProficiencyProgress();

    public int AddExperience(WeaponFamily family, int amount)
    {
        if (family == WeaponFamily.None || amount <= 0)
        {
            return 0;
        }

        WeaponProficiencyProgress current = Get(family);
        int rank = Math.Clamp(current.Rank, 1, RpgProgressionMath.MaximumWeaponProficiencyRank);
        int experience = Math.Max(0, current.Experience) + amount;
        int ranksGained = 0;
        while (rank < RpgProgressionMath.MaximumWeaponProficiencyRank)
        {
            int needed = RpgProgressionMath.ExperienceToNextProficiencyRank(rank);
            if (experience < needed)
            {
                break;
            }

            experience -= needed;
            rank++;
            ranksGained++;
        }

        if (rank >= RpgProgressionMath.MaximumWeaponProficiencyRank)
        {
            experience = 0;
        }

        Families[family] = new WeaponProficiencyProgress { Rank = rank, Experience = experience };
        return ranksGained;
    }
}

public sealed record AbilityProgress
{
    public int AbilityPoints { get; init; }
    public bool IsMastered { get; init; }
}

public sealed class AbilityMasteryState
{
    public Dictionary<string, AbilityProgress> Abilities { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> EquippedActiveAbilityIds { get; init; } = [];
    public List<string> EquippedPassiveAbilityIds { get; init; } = [];

    public bool AddAbilityPoints(string abilityId, int amount, ContentCatalog catalog)
    {
        if (amount <= 0 || !catalog.Abilities.TryGetValue(abilityId, out EquipmentAbilityDefinition? definition))
        {
            return false;
        }

        AbilityProgress current = Abilities.GetValueOrDefault(abilityId) ?? new AbilityProgress();
        int points = Math.Min(definition.RequiredAbilityPoints, current.AbilityPoints + amount);
        bool mastered = current.IsMastered || points >= definition.RequiredAbilityPoints;
        Abilities[abilityId] = new AbilityProgress { AbilityPoints = points, IsMastered = mastered };
        return mastered && !current.IsMastered;
    }

    public bool IsMastered(string abilityId) => Abilities.TryGetValue(abilityId, out AbilityProgress? progress) &&
        progress.IsMastered;

    public bool TrySetLoadout(
        IEnumerable<string> activeAbilityIds,
        IEnumerable<string> passiveAbilityIds,
        int playerLevel,
        IReadOnlySet<string> currentlyTaughtAbilityIds,
        ContentCatalog catalog,
        out string? error)
    {
        string[] active = activeAbilityIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        string[] passive = passiveAbilityIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (active.Length > 2)
        {
            error = "Only two active abilities may be equipped.";
            return false;
        }

        foreach (string id in active.Concat(passive))
        {
            if (!catalog.Abilities.TryGetValue(id, out EquipmentAbilityDefinition? ability))
            {
                error = $"Unknown ability '{id}'.";
                return false;
            }

            if (!IsMastered(id) && !currentlyTaughtAbilityIds.Contains(id))
            {
                error = $"Ability '{id}' is neither mastered nor taught by equipped gear.";
                return false;
            }

            bool isActiveSelection = active.Contains(id, StringComparer.OrdinalIgnoreCase);
            if ((ability.Kind == AbilityKind.Active) != isActiveSelection)
            {
                error = $"Ability '{id}' is in the wrong loadout section.";
                return false;
            }
        }

        int capacity = passive.Sum(id => catalog.Abilities[id].CapacityCost);
        if (capacity > RpgProgressionMath.PassiveAbilityCapacity(playerLevel))
        {
            error = "The passive ability loadout exceeds available capacity.";
            return false;
        }

        EquippedActiveAbilityIds.Clear();
        EquippedActiveAbilityIds.AddRange(active);
        EquippedPassiveAbilityIds.Clear();
        EquippedPassiveAbilityIds.AddRange(passive);
        error = null;
        return true;
    }
}

public sealed class PlayerProgressionState
{
    public int Level { get; set; } = 1;
    public int Experience { get; set; }
    public int UnspentTalentPoints { get; set; }
    public Dictionary<string, int> TalentRanks { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public WeaponProficiencyState Proficiencies { get; init; } = new();
    public AbilityMasteryState AbilityMastery { get; init; } = new();
    public ThreatTier HighestUnlockedThreatTier { get; set; } = ThreatTier.TierI;
    public CraftingWallet Materials { get; init; } = new();
    public List<EquipmentInstance> Stash { get; init; } = [];
    public EquipmentLoadout Loadout { get; init; } = new();
    public HashSet<string> CommittedRewardIds { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public int AddExperience(int amount)
    {
        if (amount <= 0 || Level >= RpgProgressionMath.MaximumPlayerLevel)
        {
            return 0;
        }

        int level = Level;
        int experience = Math.Max(0, Experience) + amount;
        int gained = 0;
        while (level < RpgProgressionMath.MaximumPlayerLevel)
        {
            int needed = RpgProgressionMath.ExperienceToNextLevel(level);
            if (experience < needed)
            {
                break;
            }

            experience -= needed;
            level++;
            gained++;
        }

        Level = level;
        Experience = level >= RpgProgressionMath.MaximumPlayerLevel ? 0 : experience;
        UnspentTalentPoints += gained;
        return gained;
    }

    public bool TrySpendTalent(string talentId, ContentCatalog catalog, out string? error)
    {
        if (!catalog.Talents.TryGetValue(talentId, out TalentDefinition? talent))
        {
            error = $"Unknown talent '{talentId}'.";
            return false;
        }

        int currentRank = TalentRanks.GetValueOrDefault(talentId);
        int branchPoints = catalog.Talents.Values
            .Where(candidate => candidate.Branch == talent.Branch)
            .Sum(candidate => TalentRanks.GetValueOrDefault(candidate.Id));
        if (UnspentTalentPoints <= 0 || currentRank >= talent.MaximumRanks ||
            branchPoints < talent.RequiredBranchPoints)
        {
            error = "Talent points, rank cap, or branch prerequisites are not satisfied.";
            return false;
        }

        TalentRanks[talentId] = currentRank + 1;
        UnspentTalentPoints--;
        error = null;
        return true;
    }

    public void RespecFromMainMenu()
    {
        int refunded = TalentRanks.Values.Sum();
        TalentRanks.Clear();
        UnspentTalentPoints += refunded;
    }
}

public sealed class PendingRunProgression
{
    public string CommitId { get; init; } = string.Empty;
    public int Experience { get; set; }
    public Dictionary<WeaponFamily, int> ProficiencyExperience { get; init; } = [];
    public Dictionary<string, int> AbilityPoints { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<EquipmentInstance> Equipment { get; init; } = [];
    public CraftingMaterialBundle DismantledMaterials { get; set; } = CraftingMaterialBundle.Zero;

    public bool Commit(PlayerProgressionState progression, ContentCatalog catalog)
    {
        if (string.IsNullOrWhiteSpace(CommitId) || !progression.CommittedRewardIds.Add(CommitId))
        {
            return false;
        }

        progression.AddExperience(Experience);
        foreach ((WeaponFamily family, int amount) in ProficiencyExperience)
        {
            progression.Proficiencies.AddExperience(family, amount);
        }

        foreach ((string abilityId, int amount) in AbilityPoints)
        {
            progression.AbilityMastery.AddAbilityPoints(abilityId, amount, catalog);
        }

        progression.Materials.Add(DismantledMaterials);

        HashSet<string> existingIds = progression.Stash.Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        progression.Stash.AddRange(Equipment.Where(item => existingIds.Add(item.Id)));
        return true;
    }
}

public sealed class RecoveryCache
{
    public List<EquipmentInstance> Items { get; init; } = [];

    public void Gather(IEnumerable<EquipmentInstance> items)
    {
        HashSet<string> existing = Items.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Items.AddRange(items.Where(item => existing.Add(item.Id)));
    }

    public bool Take(string itemId, PendingRunProgression pending)
    {
        int index = Items.FindIndex(item => string.Equals(item.Id, itemId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return false;
        }

        EquipmentInstance item = Items[index];
        Items.RemoveAt(index);
        if (!pending.Equipment.Any(existing => string.Equals(existing.Id, item.Id, StringComparison.OrdinalIgnoreCase)))
        {
            pending.Equipment.Add(item);
        }

        return true;
    }

    public void TakeAll(PendingRunProgression pending)
    {
        foreach (string id in Items.Select(item => item.Id).ToArray())
        {
            Take(id, pending);
        }
    }
}

public static class LootGenerator
{
    public static EquipmentInstance Generate(
        int runSeed,
        uint simulationTick,
        int sourceEntity,
        int dropSerial,
        ThreatTier threatTier,
        ContentCatalog catalog,
        WeaponProficiencyState? proficiencies = null,
        ItemRarity? minimumRarity = null,
        float rarityLuck = 0f,
        WeaponFamily? requiredWeaponFamily = null)
    {
        ulong state = Seed(runSeed, simulationTick, sourceEntity, dropSerial);
        int itemPower = NextInt(ref state, RpgProgressionMath.MinimumItemPower(threatTier),
            RpgProgressionMath.MaximumItemPower(threatTier) + 1);
        ItemRarity rarity = RollRarity(ref state, threatTier, rarityLuck);
        if (minimumRarity.HasValue && rarity < minimumRarity.Value)
        {
            rarity = minimumRarity.Value;
        }

        if (requiredWeaponFamily.HasValue &&
            !WeaponQuickbarLoadout.FamilyOrder.Contains(requiredWeaponFamily.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(requiredWeaponFamily));
        }
        bool weaponDrop = requiredWeaponFamily.HasValue || NextFloat(ref state) < 0.35f;
        WeaponDefinition? weapon = null;
        EquipmentBaseDefinition? equipment = null;
        if (weaponDrop)
        {
            WeaponDefinition[] eligibleWeapons = catalog.Weapons.Values
                .Where(candidate => candidate.Family != WeaponFamily.None &&
                    (!requiredWeaponFamily.HasValue || candidate.Family == requiredWeaponFamily.Value) &&
                    candidate.BaseTier is >= 1 and <= 5)
                .OrderBy(candidate => candidate.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (eligibleWeapons.Length > 0)
            {
                weapon = eligibleWeapons[NextInt(ref state, 0, eligibleWeapons.Length)];
            }
        }

        if (weapon is null)
        {
            EquipmentBaseDefinition[] eligibleEquipment = catalog.EquipmentBases.Values
                .OrderBy(candidate => candidate.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (eligibleEquipment.Length == 0)
            {
                throw new InvalidOperationException("The RPG catalog has no equipment bases.");
            }

            equipment = eligibleEquipment[NextInt(ref state, 0, eligibleEquipment.Length)];
        }

        EquipmentSlot primarySlot = weapon is not null
            ? EquipmentSlot.RightHand
            : equipment!.CompatibleSlots[NextInt(ref state, 0, equipment.CompatibleSlots.Count)];
        AffixDefinition[] eligibleAffixes = catalog.Affixes.Values
            .Where(affix => rarity >= affix.MinimumRarity && affix.AllowedSlots.Contains(primarySlot))
            .OrderBy(affix => affix.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        List<RolledAffix> affixes = [];
        for (int index = 0; index < RpgProgressionMath.AffixCount(rarity) && eligibleAffixes.Length > 0; index++)
        {
            int selected = NextInt(ref state, 0, eligibleAffixes.Length);
            AffixDefinition affix = eligibleAffixes[selected];
            if (affixes.Any(existing => string.Equals(existing.AffixId, affix.Id, StringComparison.OrdinalIgnoreCase)))
            {
                index--;
                if (affixes.Count >= eligibleAffixes.Length)
                {
                    break;
                }
                continue;
            }

            affixes.Add(new RolledAffix
            {
                AffixId = affix.Id,
                Value = float.Lerp(affix.MinimumValue, affix.MaximumValue, NextFloat(ref state)),
            });
        }

        string baseId = weapon?.Id ?? equipment!.Id;
        return new EquipmentInstance
        {
            Id = $"loot-{state:x16}-{dropSerial:x8}",
            WeaponBaseId = weapon?.Id,
            EquipmentBaseId = equipment?.Id,
            DisplayName = weapon?.DisplayName ?? equipment!.DisplayName,
            PrimarySlot = primarySlot,
            Rarity = rarity,
            ItemPower = itemPower,
            Affixes = affixes,
            UniqueEffectId = rarity == ItemRarity.Legendary
                ? $"legendary-{(weapon is null ? primarySlot.ToString() : weapon.Family.ToString()).ToLowerInvariant()}-{baseId}"
                : null,
        };
    }

    private static ItemRarity RollRarity(ref ulong state, ThreatTier tier, float rarityLuck)
    {
        float value = MathF.Max(0f, NextFloat(ref state) -
            (Math.Clamp(rarityLuck, 0f, 0.5f) * 0.25f));
        float cumulative = 0f;
        float[] probabilities = RpgProgressionMath.RarityProbabilities(tier);
        for (int index = 0; index < probabilities.Length; index++)
        {
            cumulative += probabilities[index];
            if (value <= cumulative)
            {
                return (ItemRarity)index;
            }
        }

        return ItemRarity.Legendary;
    }

    private static ulong Seed(int runSeed, uint simulationTick, int sourceEntity, int dropSerial)
    {
        ulong value = 14695981039346656037UL;
        Mix(ref value, unchecked((uint)runSeed));
        Mix(ref value, simulationTick);
        Mix(ref value, unchecked((uint)sourceEntity));
        Mix(ref value, unchecked((uint)dropSerial));
        return value == 0 ? 0x9e3779b97f4a7c15UL : value;
    }

    private static void Mix(ref ulong hash, uint value)
    {
        for (int shift = 0; shift < 32; shift += 8)
        {
            hash ^= (byte)(value >> shift);
            hash *= 1099511628211UL;
        }
    }

    private static ulong Next(ref ulong state)
    {
        state += 0x9e3779b97f4a7c15UL;
        ulong value = state;
        value = (value ^ (value >> 30)) * 0xbf58476d1ce4e5b9UL;
        value = (value ^ (value >> 27)) * 0x94d049bb133111ebUL;
        return value ^ (value >> 31);
    }

    private static float NextFloat(ref ulong state) => (Next(ref state) >> 40) / 16777216f;

    private static int NextInt(ref ulong state, int minimum, int maximumExclusive)
    {
        if (maximumExclusive <= minimum)
        {
            return minimum;
        }

        return minimum + (int)(Next(ref state) % (uint)(maximumExclusive - minimum));
    }
}
