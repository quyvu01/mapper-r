namespace MapperR.Core.Abstractions;

internal interface IWireProfileProvider
{
    IWireProfile[] WireProfiles { get; }
    IWireProfile GetDependency(IMemberProfile member);
    IReadOnlyList<IWireProfile> GetDependencies(IWireProfile profile);
    IWireProfile Resolve<TSource, TDestination>();
    IWireProfile Resolve(Type sourceType, Type destinationType);
}