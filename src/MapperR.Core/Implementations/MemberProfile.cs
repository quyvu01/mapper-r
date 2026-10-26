using System.Linq.Expressions;
using MapperR.Core.Abstractions;

namespace MapperR.Core.Implementations;

internal sealed class MemberProfile(LambdaExpression selector, IMemberMapInfo memberMapInfo) : IMemberProfile
{
    public LambdaExpression Selector { get; } = selector;
    public IMemberMapInfo MemberMapInfo { get; } = memberMapInfo;
}