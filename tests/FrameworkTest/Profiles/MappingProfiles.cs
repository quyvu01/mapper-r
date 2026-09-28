using FrameworkTest.Dtos;
using FrameworkTest.Entities;
using MapperR.Core.Abstractions;

namespace FrameworkTest.Profiles;

public class MappingProfiles : Profile
{
    public override void AddProfiles()
    {
        CreateMap<Address, AddressResponse>();

        CreateMap<Person, PersonResponse>()
            .ForMember(x => x.Name, p => $"{p.Name}-SomeOtherValue")
            .ForMember(x => x.Address, p => p.Address);
    }
}