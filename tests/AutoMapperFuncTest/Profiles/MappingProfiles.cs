using AutoMapper;
using AutoMapperFuncTest.Dtos;
using AutoMapperFuncTest.Entities;

namespace AutoMapperFuncTest.Profiles;

public class MappingProfiles : Profile
{
    public MappingProfiles()
    {
        CreateMap<List<Address>, AddressResponse>()
            .ForMember(x => x.City, opt => opt.MapFrom(_ => "Hello"));
        CreateMap<Address, List<AddressResponse>>()
            .ForMember(x => x.Capacity, opt => opt.MapFrom(_ => 10));
        // CreateMap<List<AddressResponse>, List<Address>>()
        //     .ForMember(x => x, opt => opt.MapFrom(_ => new List<Address>()));
        // CreateMap<Address, AddressResponse>();
        //
        // CreateMap<Person, PersonResponse>()
        //     .ForMember(x => x.Name,
        //         opt => opt.MapFrom(p => $"{p.Name}-SomeOtherValue"))
        //     .ForMember(x => x.Alias,
        //         opt => opt.MapFrom((p, ps) => $"{p.Name} -- {ps.Alias}"));
    }
}