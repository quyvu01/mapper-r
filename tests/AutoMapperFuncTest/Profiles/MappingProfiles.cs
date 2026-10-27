using AutoMapper;
using AutoMapperFuncTest.Dtos;
using AutoMapperFuncTest.Entities;

namespace AutoMapperFuncTest.Profiles;

public class MappingProfiles : Profile
{
    public MappingProfiles()
    {
        CreateMap<Address, AddressResponse>();
        CreateMap<Person, PersonResponse>();
    }
}