using AwesomeAssertions;

namespace LDOCE5ViewerX.ViewModels;

public sealed class HelpDialogViewModelTests
{
    [Fact]
    public void Shortcut_groups_include_representative_windows_application_shortcuts()
    {
        HelpDialogViewModel viewModel = new(useMacPrimaryModifier: false);

        viewModel.ShortcutGroups.Should().Contain(group => group.Title == "Navigation");
        viewModel.ShortcutGroups.Should().Contain(group =>
            group.Title == "Search"
            && group.Shortcuts.Any(shortcut => shortcut.Gesture == "Ctrl+L"));
        viewModel.ShortcutGroups.Should().Contain(group =>
            group.Title == "Application"
            && group.Shortcuts.Any(shortcut => shortcut.Gesture == "F1"));
    }

    [Fact]
    public void Shortcut_groups_use_cmd_for_primary_modifier_on_macos()
    {
        HelpDialogViewModel viewModel = new(useMacPrimaryModifier: true);

        viewModel.ShortcutGroups.Should().Contain(group =>
            group.Title == "Search"
            && group.Shortcuts.Any(shortcut => shortcut.Gesture == "Cmd+L"));
        viewModel.ShortcutGroups.Should().Contain(group =>
            group.Title == "Application"
            && group.Shortcuts.Any(shortcut => shortcut.Gesture == "Cmd+,"));
    }

    [Fact]
    public void Close_command_requests_window_close()
    {
        HelpDialogViewModel viewModel = new();
        bool closeRequested = false;
        viewModel.CloseRequested += (_, _) => closeRequested = true;

        viewModel.CloseCommand.Execute(null);

        closeRequested.Should().BeTrue();
    }
}
