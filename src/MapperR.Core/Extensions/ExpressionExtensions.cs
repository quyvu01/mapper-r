using System.Linq.Expressions;
using MapperR.Core.Exceptions;

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
                throw MapperException.InvalidSelector(expression, nameof(expression));

            return member.Member.Name;
        }
    }

    extension(LambdaExpression expression)
    {
        internal Type GetBodyType() => expression.Body.Type;

        /// <summary>
        /// Removes the <c>Convert(x, object)</c> the compiler adds around a value type in a
        /// <c>Func&lt;..., object&gt;</c> lambda. A cast the user wrote (<c>(long)x.N</c>, <c>(string)x.Obj</c>) is kept.
        /// </summary>
        internal LambdaExpression UnwrapBoxing()
        {
            if (expression.Body is UnaryExpression
                {
                    NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked,
                    Type: var type
                } unary && type == typeof(object))
                return Expression.Lambda(unary.Operand, expression.Parameters);
            return expression;
        }
    }
}