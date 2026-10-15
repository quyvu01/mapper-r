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
    }
}