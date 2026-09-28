using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace MapperR.Generator.Printing;

/// <summary>
/// Prints the expression trees produced by <c>TypeMapExpressionBuilder</c> as C# source that will be
/// compiled into <paramref name="targetAssembly"/>. Output is compact (single line) — callers format it.
/// Anything that cannot be reproduced faithfully (closures, inaccessible types/members, unknown nodes)
/// throws <see cref="UnsupportedExpressionException"/>. Use one instance per printed lambda.
/// </summary>
/// <param name="targetAssembly">
/// The assembly the generated code is compiled into; <c>internal</c> types and members are only
/// accessible when they belong to it. <c>null</c> means only <c>public</c> ones are accessible.
/// </param>
internal sealed class CSharpExpressionPrinter(Assembly targetAssembly)
{
    private static readonly Dictionary<Type, string> Keywords = new()
    {
        [typeof(bool)] = "bool", [typeof(byte)] = "byte", [typeof(sbyte)] = "sbyte", [typeof(char)] = "char",
        [typeof(decimal)] = "decimal", [typeof(double)] = "double", [typeof(float)] = "float",
        [typeof(int)] = "int", [typeof(uint)] = "uint", [typeof(long)] = "long", [typeof(ulong)] = "ulong",
        [typeof(short)] = "short", [typeof(ushort)] = "ushort", [typeof(object)] = "object",
        [typeof(string)] = "string"
    };

    private readonly Dictionary<ParameterExpression, string> _parameterNames = [];

    /// <summary>Declares the lambda's parameters and prints its body.</summary>
    public string PrintBody(LambdaExpression lambda)
    {
        foreach (var parameter in lambda.Parameters) DeclareParameter(parameter);
        return Print(lambda.Body);
    }

    public string GetParameterName(ParameterExpression parameter) => Identifier(_parameterNames[parameter]);

    public string PrintType(Type type)
    {
        if (!IsAccessible(type))
            throw new UnsupportedExpressionException($"type '{type}' is not accessible from generated code");
        return FormatType(type);
    }

    internal string Print(Expression node) => node switch
    {
        ParameterExpression parameter => PrintParameter(parameter),
        ConstantExpression constant => PrintConstant(constant.Value),
        DefaultExpression defaultExpression => $"default({PrintType(defaultExpression.Type)})",
        MemberExpression member => PrintMember(member),
        MemberInitExpression memberInit => PrintMemberInit(memberInit),
        NewExpression newExpression => PrintNew(newExpression, omitEmptyArguments: false),
        NewArrayExpression { NodeType: ExpressionType.NewArrayInit } newArray =>
            $"new {PrintType(newArray.Type.GetElementType()!)}[] {{ {string.Join(", ", newArray.Expressions.Select(Print))} }}",
        ConditionalExpression conditional =>
            $"{Wrap(conditional.Test)} ? {Wrap(conditional.IfTrue)} : {Wrap(conditional.IfFalse)}",
        BinaryExpression binary => PrintBinary(binary),
        UnaryExpression unary => PrintUnary(unary),
        TypeBinaryExpression { NodeType: ExpressionType.TypeIs } typeIs =>
            $"{Wrap(typeIs.Expression)} is {PrintType(typeIs.TypeOperand)}",
        MethodCallExpression call => PrintCall(call),
        LambdaExpression lambda => PrintLambda(lambda),
        _ => throw new UnsupportedExpressionException($"expression node '{node.NodeType}' is not supported")
    };

    private string Wrap(Expression node)
    {
        var printed = Print(node);
        var needsParentheses = !IsPrimary(node) || (node is ConstantExpression && printed.StartsWith('-'));
        return needsParentheses ? $"({printed})" : printed;
    }

    private static bool IsPrimary(Expression node) =>
        node is ParameterExpression or MemberExpression or MethodCallExpression or DefaultExpression
            or NewExpression or MemberInitExpression or NewArrayExpression or ConstantExpression
            or UnaryExpression
            {
                NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.TypeAs
                or ExpressionType.ArrayLength or ExpressionType.NegateChecked
            };

    private string DeclareParameter(ParameterExpression parameter)
    {
        var baseName = !string.IsNullOrEmpty(parameter.Name) && SyntaxFacts.IsValidIdentifier(parameter.Name)
            ? parameter.Name
            : "p";
        var name = baseName;
        for (var suffix = 1; _parameterNames.ContainsValue(name); suffix++) name = $"{baseName}{suffix}";

        _parameterNames[parameter] = name;
        return Identifier(name);
    }

    private string PrintParameter(ParameterExpression parameter) =>
        _parameterNames.TryGetValue(parameter, out var name)
            ? Identifier(name)
            : throw new UnsupportedExpressionException($"parameter '{parameter.Name}' is not bound by any lambda");

    private string PrintConstant(object value)
    {
        if (value is null) return "null";

        var type = value.GetType();
        if (type.IsEnum)
        {
            var underlying = Convert.ChangeType(value, Enum.GetUnderlyingType(type), CultureInfo.InvariantCulture);
            return $"(({PrintType(type)})({PrintConstant(underlying)}))";
        }

        return value switch
        {
            string text => SymbolDisplay.FormatLiteral(text, quote: true),
            char character => SymbolDisplay.FormatLiteral(character, quote: true),
            bool boolean => boolean ? "true" : "false",
            int number => number.ToString(CultureInfo.InvariantCulture),
            long number => number.ToString(CultureInfo.InvariantCulture) + "L",
            uint number => number.ToString(CultureInfo.InvariantCulture) + "U",
            ulong number => number.ToString(CultureInfo.InvariantCulture) + "UL",
            decimal number => number.ToString(CultureInfo.InvariantCulture) + "M",
            float number => float.IsFinite(number)
                ? number.ToString("R", CultureInfo.InvariantCulture) + "F"
                : $"float.{NonFiniteName(float.IsNaN(number), number > 0)}",
            double number => double.IsFinite(number)
                ? number.ToString("R", CultureInfo.InvariantCulture) + "D"
                : $"double.{NonFiniteName(double.IsNaN(number), number > 0)}",
            byte or sbyte or short or ushort =>
                $"(({PrintType(type)})({Convert.ToString(value, CultureInfo.InvariantCulture)}))",
            _ => throw new UnsupportedExpressionException(
                $"constant of type '{type}' cannot be written as a literal")
        };
    }

    private static string NonFiniteName(bool isNaN, bool isPositive) =>
        isNaN ? "NaN" : isPositive ? "PositiveInfinity" : "NegativeInfinity";

    private string PrintMember(MemberExpression member)
    {
        if (member.Expression is ConstantExpression)
            throw new UnsupportedExpressionException(
                $"'{member.Member.Name}' is read from a captured variable (closure), which cannot be reproduced as source");

        EnsureReadable(member.Member);
        var target = member.Expression is null ? PrintType(member.Member.DeclaringType!) : Wrap(member.Expression);
        return $"{target}.{Identifier(member.Member.Name)}";
    }

    private string PrintMemberInit(MemberInitExpression memberInit)
    {
        var bindings = memberInit.Bindings.Select(binding => binding is MemberAssignment assignment
            ? $"{Identifier(EnsureWritable(assignment.Member).Name)} = {Print(assignment.Expression)}"
            : throw new UnsupportedExpressionException($"member binding '{binding.BindingType}' is not supported"));

        return $"{PrintNew(memberInit.NewExpression, omitEmptyArguments: true)} {{ {string.Join(", ", bindings)} }}";
    }

    private string PrintNew(NewExpression newExpression, bool omitEmptyArguments)
    {
        if (newExpression.Constructor is { } constructor && !IsAccessible(constructor))
            throw new UnsupportedExpressionException(
                $"constructor of '{newExpression.Type}' is not accessible from generated code");

        var type = PrintType(newExpression.Type);
        return newExpression.Arguments.Count == 0 && omitEmptyArguments
            ? $"new {type}"
            : $"new {type}({string.Join(", ", newExpression.Arguments.Select(Print))})";
    }

    private string PrintBinary(BinaryExpression binary)
    {
        if (binary.NodeType == ExpressionType.ArrayIndex) return $"{Wrap(binary.Left)}[{Print(binary.Right)}]";
        if (binary is { NodeType: ExpressionType.Coalesce, Conversion: not null })
            throw new UnsupportedExpressionException("coalesce with a conversion lambda is not supported");

        var (symbol, isChecked) = binary.NodeType switch
        {
            ExpressionType.Add => ("+", false), ExpressionType.AddChecked => ("+", true),
            ExpressionType.Subtract => ("-", false), ExpressionType.SubtractChecked => ("-", true),
            ExpressionType.Multiply => ("*", false), ExpressionType.MultiplyChecked => ("*", true),
            ExpressionType.Divide => ("/", false), ExpressionType.Modulo => ("%", false),
            ExpressionType.And => ("&", false), ExpressionType.Or => ("|", false),
            ExpressionType.ExclusiveOr => ("^", false),
            ExpressionType.AndAlso => ("&&", false), ExpressionType.OrElse => ("||", false),
            ExpressionType.Equal => ("==", false), ExpressionType.NotEqual => ("!=", false),
            ExpressionType.LessThan => ("<", false), ExpressionType.LessThanOrEqual => ("<=", false),
            ExpressionType.GreaterThan => (">", false), ExpressionType.GreaterThanOrEqual => (">=", false),
            ExpressionType.LeftShift => ("<<", false), ExpressionType.RightShift => (">>", false),
            ExpressionType.Coalesce => ("??", false),
            _ => throw new UnsupportedExpressionException($"binary operator '{binary.NodeType}' is not supported")
        };

        var printed = $"{Wrap(binary.Left)} {symbol} {Wrap(binary.Right)}";
        return isChecked ? $"checked({printed})" : printed;
    }

    private string PrintUnary(UnaryExpression unary)
    {
        if (unary.Method is { } method && method.Name is not ("op_Implicit" or "op_Explicit")
            && unary.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked)
            throw new UnsupportedExpressionException($"conversion through method '{method.Name}' is not supported");

        return unary.NodeType switch
        {
            ExpressionType.Convert => $"(({PrintType(unary.Type)})({Print(unary.Operand)}))",
            ExpressionType.ConvertChecked => $"checked(({PrintType(unary.Type)})({Print(unary.Operand)}))",
            ExpressionType.TypeAs => $"({Wrap(unary.Operand)} as {PrintType(unary.Type)})",
            ExpressionType.Not when unary.Operand.Type == typeof(bool) || unary.Operand.Type == typeof(bool?) =>
                $"!{Wrap(unary.Operand)}",
            ExpressionType.Not or ExpressionType.OnesComplement => $"~{Wrap(unary.Operand)}",
            ExpressionType.Negate => $"-{Wrap(unary.Operand)}",
            ExpressionType.NegateChecked => $"checked(-{Wrap(unary.Operand)})",
            ExpressionType.UnaryPlus => $"+{Wrap(unary.Operand)}",
            ExpressionType.ArrayLength => $"{Wrap(unary.Operand)}.Length",
            _ => throw new UnsupportedExpressionException($"unary operator '{unary.NodeType}' is not supported")
        };
    }

    /// <summary>
    /// Calls are always printed in static/explicit form (e.g. <c>Enumerable.Select&lt;A, B&gt;(x, ...)</c>
    /// rather than <c>x.Select(...)</c>) so no <c>using</c> directive or user-defined extension method in
    /// scope can change which overload binds.
    /// </summary>
    private string PrintCall(MethodCallExpression call)
    {
        var method = call.Method;
        if (!IsAccessible(method) || !IsAccessible(method.DeclaringType!))
            throw new UnsupportedExpressionException(
                $"method '{method.DeclaringType?.Name}.{method.Name}' is not accessible from generated code");
        if (method.GetParameters().Any(parameter => parameter.ParameterType.IsByRef))
            throw new UnsupportedExpressionException($"method '{method.Name}' has ref/out parameters");

        var target = call.Object is null ? PrintType(method.DeclaringType!) : Wrap(call.Object);
        var arguments = string.Join(", ", call.Arguments.Select(Print));

        if (method.IsSpecialName)
            return method.Name == "get_Item" && call.Object is not null
                ? $"{target}[{arguments}]"
                : throw new UnsupportedExpressionException($"special method '{method.Name}' is not supported");

        var typeArguments = method.IsGenericMethod
            ? $"<{string.Join(", ", method.GetGenericArguments().Select(PrintType))}>"
            : "";
        return $"{target}.{Identifier(method.Name)}{typeArguments}({arguments})";
    }

    private string PrintLambda(LambdaExpression lambda)
    {
        var parameters = string.Join(", ",
            lambda.Parameters.Select(parameter => $"{PrintType(parameter.Type)} {DeclareParameter(parameter)}"));
        return $"({parameters}) => {Print(lambda.Body)}";
    }

    private void EnsureReadable(MemberInfo member)
    {
        var readable = member switch
        {
            PropertyInfo property => property.GetIndexParameters().Length == 0
                                     && property.GetGetMethod(nonPublic: true) is { } getter && IsAccessible(getter),
            FieldInfo field => IsAccessible(field),
            _ => false
        };

        if (!readable || member.Name.Contains('<') || !IsAccessible(member.DeclaringType!))
            throw new UnsupportedExpressionException(
                $"'{member.DeclaringType?.Name}.{member.Name}' cannot be read from generated code");
    }

    private MemberInfo EnsureWritable(MemberInfo member)
    {
        var writable = member switch
        {
            PropertyInfo property => property.GetSetMethod(nonPublic: true) is { } setter && IsAccessible(setter),
            FieldInfo field => !field.IsInitOnly && !field.IsLiteral && IsAccessible(field),
            _ => false
        };

        if (!writable || member.Name.Contains('<') || !IsAccessible(member.DeclaringType!))
            throw new UnsupportedExpressionException(
                $"'{member.DeclaringType?.Name}.{member.Name}' cannot be assigned from generated code " +
                "(its setter is not public/internal)");

        return member;
    }

    private bool IsAccessible(Type type)
    {
        if (type.IsByRef || type.IsPointer || type.IsGenericParameter) return false;
        if (type.HasElementType) return IsAccessible(type.GetElementType()!);
        if (type is { IsGenericType: true, IsGenericTypeDefinition: false } && !type.GetGenericArguments().All(IsAccessible))
            return false;

        if (type.IsNested)
            return (type.IsNestedPublic || (IsTargetAssembly(type.Assembly) && (type.IsNestedAssembly || type.IsNestedFamORAssem)))
                   && IsAccessible(type.DeclaringType!);

        return type.IsPublic || (IsTargetAssembly(type.Assembly) && type.IsNotPublic);
    }

    private bool IsAccessible(MethodBase method) =>
        method.IsPublic || (IsTargetAssembly(method.Module.Assembly) && (method.IsAssembly || method.IsFamilyOrAssembly));

    private bool IsAccessible(FieldInfo field) =>
        field.IsPublic || (IsTargetAssembly(field.Module.Assembly) && (field.IsAssembly || field.IsFamilyOrAssembly));

    private bool IsTargetAssembly(Assembly assembly) => targetAssembly is not null && assembly == targetAssembly;

    private static string FormatType(Type type)
    {
        if (Keywords.TryGetValue(type, out var keyword)) return keyword;
        if (type.IsArray) return $"{FormatType(type.GetElementType()!)}[{new string(',', type.GetArrayRank() - 1)}]";
        if (Nullable.GetUnderlyingType(type) is { } underlying) return $"{FormatType(underlying)}?";
        if (type.Name.Contains('<'))
            throw new UnsupportedExpressionException($"type '{type}' is compiler-generated and has no source name");

        var typeArguments = type.IsGenericType ? type.GetGenericArguments() : Type.EmptyTypes;
        var chain = new List<Type>();
        for (var current = type; current is not null; current = current.DeclaringType) chain.Insert(0, current);

        var builder = new StringBuilder("global::");
        if (!string.IsNullOrEmpty(chain[0].Namespace)) builder.Append(chain[0].Namespace).Append('.');

        var argumentIndex = 0;
        for (var i = 0; i < chain.Count; i++)
        {
            if (i > 0) builder.Append('.');

            var name = chain[i].Name;
            var tick = name.IndexOf('`');
            builder.Append(Identifier(tick < 0 ? name : name[..tick]));
            if (tick < 0) continue;

            var arity = int.Parse(name[(tick + 1)..], CultureInfo.InvariantCulture);
            builder.Append('<')
                .Append(string.Join(", ", typeArguments.Skip(argumentIndex).Take(arity).Select(FormatType)))
                .Append('>');
            argumentIndex += arity;
        }

        return builder.ToString();
    }

    private static string Identifier(string name) =>
        SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;
}
