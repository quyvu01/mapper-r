namespace MapperR.Core.Abstractions;

public interface IWireProfilesProvider
{
    IWireProfile[] WireProfiles { get; }
}