using System.Linq.Expressions;
using MapperR.Core.Entities;

namespace MapperR.Core.Abstractions;

public interface IMemberMapInfo
{
    bool IsIgnored { get; }
    LambdaExpression MemberPath { get; }
    LambdaExpression PreConditionPredicate { get; }
    MapClassifier Classifier { get; }
}