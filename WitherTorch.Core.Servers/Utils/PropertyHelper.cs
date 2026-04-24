using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

using WitherTorch.Core.Property;

namespace WitherTorch.Core.Servers.Utils
{
    internal static class PropertyHelper
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetString(JsonObject obj, string propertyName, out string? result)
        {
            if (!obj.TryGetPropertyValue(propertyName, out JsonNode? node) || node is not JsonValue valueNode)
            {
                result = null;
                return false;
            }
            return TryGetString(valueNode, out result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetString<T>(T obj, string propertyName, out string? result) where T : PropertyFileBase<JsonNode>
        {
            if (obj[propertyName] is not JsonValue valueNode)
            {
                result = null;
                return false;
            }
            return TryGetString(valueNode, out result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetString(JavaPropertyFile obj, string propertyName, [NotNullWhen(true)] out string? result)
        {
            result = obj[propertyName];
            return result is not null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryGetString(JsonValue value, out string? result)
        {
            switch (value.GetValueKind())
            {
                case JsonValueKind.String:
                    result = value.GetValue<string>();
                    return true;
                case JsonValueKind.Null:
                    result = null;
                    return true;
                default:
                    result = null;
                    return false;
            }
        }
    }
}
