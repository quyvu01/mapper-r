using System.Linq.Expressions;

namespace MapperR.Core.Abstractions;

public interface IMemberProfile
{
    string MemberName { get; }
    Type SourceMemberType { get; }
    Type DestinationMemberType { get; }
    LambdaExpression Selector { get; }
    LambdaExpression Path { get; }
}