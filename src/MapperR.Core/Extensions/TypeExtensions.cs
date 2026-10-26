using MapperR.Core.Helpers;

namespace MapperR.Core.Extensions;

public static class TypeExtensions
{
    extension(Type type)
    {
        public bool IsClosedConcreteType() =>
            type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false };

        public bool IsPrimitiveType() => GeneralHelpers.IsPrimitiveType(type);

        public bool IsScalar() => GeneralHelpers.IsScalar(type);

        public Type GetGenericBaseType(Type genericTypeDefinition)
        {
            var current = type;
            while (current != null && current != typeof(object))
            {
                if (current.IsGenericType && current.GetGenericTypeDefinition() == genericTypeDefinition)
                    return current;
                current = current.BaseType;
            }

            return null;
        }

        internal bool CanImplicitlyConvert(Type target)
        {
            if (type == target) return true;

            // Nullable<T> handling
            var sourceUnderlying = Nullable.GetUnderlyingType(type);
            var targetUnderlying = Nullable.GetUnderlyingType(target);

            if (targetUnderlying != null)
            {
                // T -> T?
                var actualSource = sourceUnderlying ?? type;
                return actualSource.CanImplicitlyConvert(targetUnderlying);
            }

            // Cannot implicitly convert nullable sang non-nullable
            if (sourceUnderlying != null) return false;

            // Todo: check later
            if (type.IsEnum || target.IsEnum) return false;

            // Implicit numeric conversions
            return Type.IsImplicitNumericConversion(type, target);
        }

        private static bool IsImplicitNumericConversion(Type source, Type target)
        {
            if (source == typeof(sbyte))
                return target == typeof(short) || target == typeof(int)
                                               || target == typeof(long) || target == typeof(float)
                                               || target == typeof(double) || target == typeof(decimal);

            if (source == typeof(byte))
                return target == typeof(short) || target == typeof(ushort)
                                               || target == typeof(int) || target == typeof(uint)
                                               || target == typeof(long) || target == typeof(ulong)
                                               || target == typeof(float) || target == typeof(double)
                                               || target == typeof(decimal);

            if (source == typeof(short))
                return target == typeof(int) || target == typeof(long)
                                             || target == typeof(float) || target == typeof(double)
                                             || target == typeof(decimal);

            if (source == typeof(ushort))
                return target == typeof(int) || target == typeof(uint)
                                             || target == typeof(long) || target == typeof(ulong)
                                             || target == typeof(float) || target == typeof(double)
                                             || target == typeof(decimal);

            if (source == typeof(int))
                return target == typeof(long) || target == typeof(float)
                                              || target == typeof(double) || target == typeof(decimal);

            if (source == typeof(uint))
                return target == typeof(long) || target == typeof(ulong)
                                              || target == typeof(float) || target == typeof(double)
                                              || target == typeof(decimal);

            if (source == typeof(long) || source == typeof(ulong))
                return target == typeof(float) || target == typeof(double)
                                               || target == typeof(decimal);

            if (source == typeof(char))
                return target == typeof(ushort) || target == typeof(int)
                                                || target == typeof(uint) || target == typeof(long)
                                                || target == typeof(ulong) || target == typeof(float)
                                                || target == typeof(double) || target == typeof(decimal);

            if (source == typeof(float))
                return target == typeof(double);

            return false;
        }
    }
}