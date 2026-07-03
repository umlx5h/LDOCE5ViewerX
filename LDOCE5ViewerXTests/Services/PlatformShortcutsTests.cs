using Avalonia.Input;

using AwesomeAssertions;

namespace LDOCE5ViewerX.Services;

public sealed class PlatformShortcutsTests
{
    [Fact]
    public void ToPrimaryGesture_keeps_control_shortcut_for_windows()
    {
        KeyGesture gesture = PlatformShortcuts.ToPrimaryGesture(
            new KeyGesture(Key.F, KeyModifiers.Control),
            useMacPrimaryModifier: false);

        gesture.Key.Should().Be(Key.F);
        gesture.KeyModifiers.Should().Be(KeyModifiers.Control);
    }

    [Fact]
    public void ToPrimaryGesture_uses_meta_shortcut_for_macos()
    {
        KeyGesture gesture = PlatformShortcuts.ToPrimaryGesture(
            new KeyGesture(Key.F, KeyModifiers.Control),
            useMacPrimaryModifier: true);

        gesture.Key.Should().Be(Key.F);
        gesture.KeyModifiers.Should().Be(KeyModifiers.Meta);
    }
}
