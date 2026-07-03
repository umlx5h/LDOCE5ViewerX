using AwesomeAssertions;

namespace LDOCE5ViewerX.ViewModels;

public sealed class AdvancedSearchDialogViewModelTests
{
    [Fact]
    public void Checking_coded_parent_filter_does_not_check_child_filters()
    {
        AdvancedSearchDialogViewModel viewModel = new();
        AdvancedSearchFilterNodeViewModel noun = FindNode(viewModel.FilterNodes, "noun");
        AdvancedSearchFilterNodeViewModel uncountable = FindNode(noun.Children, "uncountable");

        noun.IsChecked = true;

        noun.IsChecked.Should().BeTrue();
        uncountable.IsChecked.Should().BeFalse();
        noun.GetSelectedCodesByGroup().Should().ContainSingle().Which.Should().Be("341");
    }

    [Fact]
    public void Unchecking_coded_parent_filter_clears_child_filters()
    {
        AdvancedSearchDialogViewModel viewModel = new();
        AdvancedSearchFilterNodeViewModel noun = FindNode(viewModel.FilterNodes, "noun");
        AdvancedSearchFilterNodeViewModel uncountable = FindNode(noun.Children, "uncountable");
        noun.IsChecked = true;
        uncountable.IsChecked = true;

        noun.IsChecked = false;

        noun.IsChecked.Should().BeFalse();
        uncountable.IsChecked.Should().BeFalse();
        noun.GetSelectedCodesByGroup().Should().BeEmpty();
    }

    private static AdvancedSearchFilterNodeViewModel FindNode(
        IEnumerable<AdvancedSearchFilterNodeViewModel> nodes,
        string label)
    {
        foreach (AdvancedSearchFilterNodeViewModel node in nodes)
        {
            if (node.Label == label)
            {
                return node;
            }

            AdvancedSearchFilterNodeViewModel? match = TryFindNode(node.Children, label);
            if (match is not null)
            {
                return match;
            }
        }

        throw new InvalidOperationException($"Filter node '{label}' was not found.");
    }

    private static AdvancedSearchFilterNodeViewModel? TryFindNode(
        IEnumerable<AdvancedSearchFilterNodeViewModel> nodes,
        string label)
    {
        foreach (AdvancedSearchFilterNodeViewModel node in nodes)
        {
            if (node.Label == label)
            {
                return node;
            }

            AdvancedSearchFilterNodeViewModel? match = TryFindNode(node.Children, label);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }
}
