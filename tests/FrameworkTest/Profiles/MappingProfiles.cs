using FrameworkTest.Dtos;
using FrameworkTest.Entities;
using MapperR.Core.Abstractions;

namespace FrameworkTest.Profiles;

public class MappingProfiles : Profile
{
    public override void AddProfiles()
    {
        // CreateMap<Address, AddressResponse>();

        CreateMap<Person, PersonResponse>()
            .ForMember(x => x.Age, opt => opt.MapFrom(x => x.Age + 1));
        // .ForMember(x => x.Name,
        //     opt =>
        //     {
        //         opt.PreCondition(x => x.Name.Length > 2);
        //         opt.MapFrom(p => $"{p.Name}-SomeOtherValue");
        //     })
        // .ForMember(x => x.Alias,
        //     opt => opt.MapFrom((p, ps) => $"{p.Name} -- {ps.Alias}"));
    }
}