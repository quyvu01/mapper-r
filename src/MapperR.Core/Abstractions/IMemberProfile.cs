using System.Linq.Expressions;

namespace MapperR.Core.Abstractions;

public interface IMemberProfile
{
    string MemberName { get; }
    Type SourceMemberType { get; }
    Type DestinationMemberType { get; }

    /// <summary>The destination-side getter shape (e.g. <c>d => d.Name</c>), type-erased for non-generic consumers.</summary>
    LambdaExpression Selector { get; }

    /// <summary>The source-side value expression (e.g. <c>s => s.Name</c> or a computed value), type-erased for non-generic consumers.</summary>
    LambdaExpression Path { get; }
}