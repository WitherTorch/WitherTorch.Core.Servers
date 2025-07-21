using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.Linq;

namespace WitherTorch.Core.Servers.Utils
{
    internal static class MavenManifestExtractor
    {
        public static bool TryEnumerateVersionsFromXml(string? manifestXml,
            [NotNullWhen(true)] out string? latestVersion, [NotNullWhen(true)] out IEnumerable<string>? versions)
        {
            if (string.IsNullOrEmpty(manifestXml))
                goto Failed;
            XmlDocument document = new XmlDocument();
            try
            {
                document.LoadXml(manifestXml);
            }
            catch (XmlException)
            {
                goto Failed;
            }
            XmlNode? rootNode = document.SelectSingleNode("/metadata/versioning");
            if (rootNode is null || !TryGetNodeText(rootNode["latest"], out latestVersion) || rootNode["versions"] is not XmlElement versionsNode)
                goto Failed;
            versions = EnumerateXmlVersionTextNode(versionsNode);
            return true;

        Failed: // 將 Failed 路線整合為一條，以降低程式碼大小
            latestVersion = null;
            versions = null;
            return false;
        }

        private static IEnumerable<string> EnumerateXmlVersionTextNode(XmlNode parentNode)
        {
            foreach (XmlElement elementNode in parentNode.OfType<XmlElement>())
            {
                if (elementNode.Name != "version" || !TryGetNodeText(elementNode, out string? text))
                    continue;
                yield return text;
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryGetNodeText(XmlNode? node, [NotNullWhen(true)] out string? text)
        {
            if (node is not XmlElement)
                goto Failed;
            node = node.FirstChild;
            if (node is not XmlText textNode)
                goto Failed;

            text = textNode.Value;
            return !string.IsNullOrEmpty(text);
        
        Failed: // 將 Failed 路線整合為一條，以降低程式碼大小
            text = null;
            return false;
        }
    }
}
