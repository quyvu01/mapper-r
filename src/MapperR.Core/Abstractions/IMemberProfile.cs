using System.Linq.Expressions;

namespace MapperR.Core.Abstractions;

public interface IMemberProfile
{
    LambdaExpression Selector { get; }
    IMemberMapInfo MemberMapInfo { get; }
}