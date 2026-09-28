using System.Linq.Expressions;

namespace MapperR.Core.Extensions;

/// <summary>
/// Provides extension methods for extracting member metadata from selector expressions.
/// </summary>
public static class ExpressionExtensions
{
    extension<TSource, TProp>(Expression<Func<TSource, TProp>> expression)
    {
        /// <summary>
        /// Gets the name of the member accessed directly on the lambda parameter (e.g. <c>x => x.Name</c>).
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Thrown when the expression is not a simple member access on the parameter itself,
        /// such as a nested path (<c>x => x.Address.Name</c>), a method call, or any other shape.
        /// </exception>
        public string GetMemberName()
        {
            ArgumentNullException.ThrowIfNull(expression);

            var body = expression.Body;
            if (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
                body = unary.Operand;

            if (body is not MemberExpression { Expression: ParameterExpression } member)
                throw new ArgumentException(
                    $"Expression '{expression}' must be a direct member access on the parameter " +
                    "(e.g. 'x => x.Name'), not a nested path (e.g. 'x => x.Address.Name') or any other shape.",
                    nameof(expression));

            return member.Member.Name;
        }
    }
}
