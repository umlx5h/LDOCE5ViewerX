using System;
using System.Collections.Generic;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using LDOCE5ViewerX.Services;

namespace LDOCE5ViewerX.ViewModels;

/// <summary>
/// View model for the keyboard shortcut help window.
/// </summary>
public partial class HelpDialogViewModel : ObservableObject
{
    /// <summary>
    /// Creates a help dialog view model with the built-in shortcut cheat sheet.
    /// </summary>
    public HelpDialogViewModel()
        : this(PlatformShortcuts.IsMacOS)
    {
    }

    internal HelpDialogViewModel(bool useMacPrimaryModifier)
    {
        ShortcutGroups =
            ConvertShortcutGroupsForPlatform(CreateDefaultShortcutGroups(), useMacPrimaryModifier);
    }

    private static IReadOnlyList<ShortcutGroupViewModel> CreateDefaultShortcutGroups()
    {
        return
        [
            new(
                "Search",
                [
                    new("Ctrl+L", "Focus the search box"),
                    new("Enter", "Run search or open the selected result"),
                    new("Up / Down", "Move through visible search results"),
                    new("Ctrl+J / Ctrl+N", "Move to the next search result"),
                    new("Ctrl+K / Ctrl+P", "Move to the previous search result"),
                    new("Ctrl+C", "Copy the current entry word"),
                ]),
            new(
                "Navigation",
                [
                    new("Alt+Left", "Go back in entry history"),
                    new("Alt+Right", "Go forward in entry history"),
                    new("Mouse Back", "Go back in entry history"),
                    new("Mouse Forward", "Go forward in entry history"),
                ]),
            new(
                "Find In Entry",
                [
                    new("Ctrl+F", "Open find in the current entry"),
                    new("Enter", "Move to the next find match"),
                    new("Shift+Enter", "Move to the previous find match"),
                    new("Esc", "Close the find bar"),
                ]),
            new(
                "Spelling Suggestions",
                [
                    new("Up / Down", "Move through spelling suggestions"),
                    new("Enter", "Apply the selected suggestion"),
                    new("Esc", "Close spelling suggestions"),
                ]),
            new(
                "Audio And Zoom",
                [
                    new("Ctrl+S", "Play pronunciation for the current entry"),
                    new("Ctrl+Plus", "Zoom in"),
                    new("Ctrl+Minus", "Zoom out"),
                    new("Ctrl+0", "Reset zoom"),
                    new("Ctrl+Wheel", "Zoom content in or out"),
                ]),
            new(
                "Application",
                [
                    new("Ctrl+,", "Open settings"),
                    new("F1", "Open this help window"),
                ]),
        ];
    }

    private static IReadOnlyList<ShortcutGroupViewModel> ConvertShortcutGroupsForPlatform(
        IReadOnlyList<ShortcutGroupViewModel> shortcutGroups,
        bool useMacPrimaryModifier)
    {
        if (!useMacPrimaryModifier)
        {
            return shortcutGroups;
        }

        List<ShortcutGroupViewModel> convertedGroups = [];
        foreach (ShortcutGroupViewModel group in shortcutGroups)
        {
            List<ShortcutItemViewModel> convertedShortcuts = [];
            foreach (ShortcutItemViewModel shortcut in group.Shortcuts)
            {
                convertedShortcuts.Add(shortcut with
                {
                    Gesture = ConvertPrimaryGestureLabel(shortcut.Gesture, useMacPrimaryModifier),
                });
            }

            convertedGroups.Add(group with { Shortcuts = convertedShortcuts });
        }

        return convertedGroups;
    }

    private static string ConvertPrimaryGestureLabel(string gesture, bool useMacPrimaryModifier)
    {
        return useMacPrimaryModifier
            ? gesture.Replace("Ctrl+", "Cmd+", StringComparison.Ordinal)
            : gesture;
    }

    /// <summary>
    /// Raised when the help dialog should close.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Gets the shortcut groups displayed by the help dialog.
    /// </summary>
    public IReadOnlyList<ShortcutGroupViewModel> ShortcutGroups { get; }

    /// <summary>
    /// Closes the help dialog.
    /// </summary>
    public RelayCommand CloseCommand => field ??= new(() =>
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    });
}

/// <summary>
/// Represents a group of related shortcut entries.
/// </summary>
/// <param name="Title">Display title for the shortcut group.</param>
/// <param name="Shortcuts">Shortcut entries in the group.</param>
public sealed record ShortcutGroupViewModel(string Title, IReadOnlyList<ShortcutItemViewModel> Shortcuts);

/// <summary>
/// Represents one shortcut cheat-sheet row.
/// </summary>
/// <param name="Gesture">Shortcut gesture shown to the user.</param>
/// <param name="Description">Description of the shortcut behavior.</param>
public sealed record ShortcutItemViewModel(string Gesture, string Description);
