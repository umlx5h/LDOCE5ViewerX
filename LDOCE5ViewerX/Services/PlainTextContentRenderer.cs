using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LDOCE5ViewerX.Services;

/// <summary>
/// Produces a temporary plain-text display from raw LDOCE XML content.
/// </summary>
public static partial class PlainTextContentRenderer
{
    /// <summary>
    /// Extracts readable text from XML bytes while the full HTML transformer is not ported yet.
    /// </summary>
    /// <param name="data">Raw XML bytes from an archive item.</param>
    /// <returns>Whitespace-normalized plain text.</returns>
    public static string RenderXml(byte[] data)
    {
        string xml = Encoding.UTF8.GetString(data);
        XDocument document = XDocument.Parse(xml, LoadOptions.None);
        string text = string.Join(" ", document.DescendantNodes().OfType<XText>().Select(node => node.Value));
        return WhitespaceRegex().Replace(text, " ").Trim();
    }

    /// <summary>
    /// Collapses XML text-node whitespace into single spaces.
    /// </summary>
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
