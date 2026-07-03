using System;

using Avalonia.Input;

namespace LDOCE5ViewerX.Services;

internal static class PlatformShortcuts
{
    internal static bool IsMacOS { get; } = OperatingSystem.IsMacOS();

    internal static KeyModifiers PrimaryModifier { get; } = GetPrimaryModifier(IsMacOS);

    internal static KeyGesture PrimaryGesture(Key key)
    {
        return new KeyGesture(key, PrimaryModifier);
    }

    internal static bool IsPrimaryModifierOnly(KeyModifiers modifiers)
    {
        return modifiers == PrimaryModifier;
    }

    internal static bool HasPrimaryModifier(KeyModifiers modifiers)
    {
        return (modifiers & PrimaryModifier) != 0;
    }

    internal static KeyGesture ToPrimaryGesture(KeyGesture gesture)
    {
        return ToPrimaryGesture(gesture, IsMacOS);
    }

    internal static KeyGesture ToPrimaryGesture(KeyGesture gesture, bool useMacPrimaryModifier)
    {
        KeyModifiers modifiers = gesture.KeyModifiers;
        if ((modifiers & KeyModifiers.Control) == 0)
        {
            return gesture;
        }

        modifiers &= ~KeyModifiers.Control;
        modifiers |= GetPrimaryModifier(useMacPrimaryModifier);
        return new KeyGesture(gesture.Key, modifiers);
    }

    private static KeyModifiers GetPrimaryModifier(bool useMacPrimaryModifier)
    {
        return useMacPrimaryModifier ? KeyModifiers.Meta : KeyModifiers.Control;
    }
}
