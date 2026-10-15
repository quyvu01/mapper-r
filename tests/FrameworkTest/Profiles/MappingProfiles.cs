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
            .ForMember(x => x.Name,
                opt => opt.MapFrom(p => $"{p.Name}-SomeOtherValue"))
            .ForMember(x => x.Alias,
                opt => opt.MapFrom((p, ps) => $"{p.Name} -- {ps.Alias}"))
            .ForMember(x => x.Addresses, opt => opt.Ignore());
    }
}