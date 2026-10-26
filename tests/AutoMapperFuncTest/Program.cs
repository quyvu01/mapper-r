// See https://aka.ms/new-console-template for more information

using AutoMapper;
using AutoMapperFuncTest.Dtos;
using AutoMapperFuncTest.Entities;
using AutoMapperFuncTest.Profiles;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddAutoMapper(cfg => cfg.AddProfile<MappingProfiles>());
var serviceProvider = services.BuildServiceProvider();
var mapper = serviceProvider.GetRequiredService<IMapper>();
var r = mapper.Map<List<AddressResponse>>(new Address());
Console.WriteLine($"Response: {r.Capacity}");

var k = mapper.Map<List<Address>>(new AddressResponse());

Console.WriteLine("Hello, World!");