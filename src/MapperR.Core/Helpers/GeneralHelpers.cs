using System.Reflection;

namespace MapperR.Core.Helpers;

public static class GeneralHelpers
{
    /// <summary>
    /// Determines if a type is a primitive or value type.
    /// </summary>
    /// <param name="objectType">The type to check.</param>
    /// <returns>True if the type is a primitive, string, DateTime, enum, decimal, or any value type.</returns>
    /// <remarks>
    /// This method is used to determine whether a type should be recursively processed
    /// for FxMap property mapping or treated as a leaf value.
    /// </remarks>
    public static bool IsPrimitiveType(Type objectType) =>
        objectType.IsPrimitive ||
        objectType == typeof(string) ||
        objectType == typeof(DateTime) ||
        objectType.IsEnum ||
        objectType == typeof(decimal) ||
        objectType.IsValueType; // Covers all value types (structs, etc.)

    private static readonly HashSet<Type> ScalarStructs =
    [
        typeof(decimal), typeof(DateTime), typeof(DateTimeOffset), typeof(DateOnly), typeof(TimeOnly),
        typeof(TimeSpan), typeof(Guid)
    ];

    /// <summary>
    /// Scalar types — single values that format meaningfully as text: primitives (including <c>bool</c> and
    /// <c>char</c>), <c>decimal</c>, enums, the date/time structs and <c>Guid</c>, plus their nullable forms.
    /// Unlike <see cref="IsPrimitiveType"/>, user-defined structs are not scalar: their <c>ToString()</c>
    /// usually returns the type name.
    /// </summary>
    public static bool IsScalar(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
        return underlyingType.IsPrimitive || underlyingType.IsEnum || ScalarStructs.Contains(underlyingType);
    }

    public static bool CanConvertNumeric(Type sourceProp, Type targetProp)
    {
        var sourceType = Nullable.GetUnderlyingType(sourceProp) ?? sourceProp;
        var targetType = Nullable.GetUnderlyingType(targetProp) ?? targetProp;

        // Verify both types are actually numeric
        if (!IsNumericType(sourceType) || !IsNumericType(targetType)) return false;

        // All built-in numeric types implement IConvertible and can convert to each other
        return typeof(IConvertible).IsAssignableFrom(sourceType) && typeof(IConvertible).IsAssignableFrom(targetType);
    }

    private static bool IsNumericType(Type type)
    {
        switch (Type.GetTypeCode(type))
        {
            case TypeCode.Byte:
            case TypeCode.SByte:
            case TypeCode.UInt16:
            case TypeCode.Int16:
            case TypeCode.UInt32:
            case TypeCode.Int32:
            case TypeCode.UInt64:
            case TypeCode.Int64:
            case TypeCode.Single:
            case TypeCode.Double:
            case TypeCode.Decimal:
                return true;
            default:
                return false;
        }
    }
}