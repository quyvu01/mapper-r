using System.Text;

namespace MapperR.Core.Helpers;

internal static class TypeNames
{
    /// <summary>A readable name for messages: <c>List&lt;Person&gt;</c> rather than <c>List`1</c>.</summary>
    public static string Describe(Type type)
    {
        if (type.IsArray) return $"{Describe(type.GetElementType()!)}[{new string(',', type.GetArrayRank() - 1)}]";
        if (!type.IsGenericType) return type.Name;

        var name = type.Name;
        var builder = new StringBuilder(name[..name.IndexOf('`')]).Append('<');
        builder.AppendJoin(", ", type.GetGenericArguments().Select(Describe));
        return builder.Append('>').ToString();
    }
}
