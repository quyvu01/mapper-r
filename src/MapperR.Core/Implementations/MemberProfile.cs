using System.Linq.Expressions;
using MapperR.Core.Abstractions;

namespace MapperR.Core.Implementations;

internal sealed class MemberProfile(
    string memberName,
    Type sourceMemberType,
    Type destinationMemberType,
    LambdaExpression selector,
    LambdaExpression path)
    : IMemberProfile
{
    public string MemberName { get; } = memberName;
    public Type SourceMemberType { get; } = sourceMemberType;
    public Type DestinationMemberType { get; } = destinationMemberType;
    public LambdaExpression Selector { get; } = selector;
    public LambdaExpression Path { get; } = path;
}