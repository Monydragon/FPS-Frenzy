namespace FpsFrenzy.Core.Input;

public readonly record struct ContextualSecondaryAction(bool FireLeft, bool Focus);

public static class ContextualWeaponInput
{
    public static ContextualSecondaryAction Resolve(
        bool secondaryTriggerPressed,
        bool dedicatedFocusPressed,
        bool dualWielding) => new(
            FireLeft: dualWielding && secondaryTriggerPressed,
            Focus: dedicatedFocusPressed || (!dualWielding && secondaryTriggerPressed));
}
