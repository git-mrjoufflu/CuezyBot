using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Automation.Desktop;

/// <summary>Shared collapsible category presentation for management libraries.</summary>
internal sealed class CategoryGroups(StackPanel parent, HashSet<string> collapsed, string uncategorized)
{
    private readonly Dictionary<string, (StackPanel Rows, TextBlock Title)> groups = new(StringComparer.OrdinalIgnoreCase);

    internal void Add(string category, Control row)
    {
        if (!groups.TryGetValue(category, out var group))
        {
            var title = new TextBlock { FontWeight = FontWeight.SemiBold, Foreground = Brush.Parse("#C9B4FF"), FontSize = 15 };
            var content = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 4) };
            var section = new Expander
            {
                Header = title, Content = content, IsExpanded = !collapsed.Contains(category),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Padding = new Thickness(0), Margin = new Thickness(0, 4, 0, 4)
            };
            section.Collapsed += (_, _) => collapsed.Add(category);
            section.Expanded += (_, _) => collapsed.Remove(category);
            parent.Children.Add(section); group = (content, title); groups.Add(category, group);
        }
        group.Rows.Children.Add(row);
        group.Title.Text = $"▱  {(category.Length == 0 ? uncategorized : category)}  ({group.Rows.Children.Count})";
    }
}
