// See https://aka.ms/new-console-template for more information

using AutoMapper;
using AutoMapper.QueryableExtensions;
using AutoMapperFuncTest.Dtos;
using AutoMapperFuncTest.Entities;
using AutoMapperFuncTest.Profiles;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddAutoMapper(cfg => cfg.AddProfile<MappingProfiles>());
var serviceProvider = services.BuildServiceProvider();
var mapper = serviceProvider.GetRequiredService<IMapper>();
var person = new List<Person> { new() { Address = new Address { City = "HN", Location = "HoanKiem" } } }.AsQueryable();
var r = person.ProjectTo<PersonResponse>(mapper.ConfigurationProvider);
var x = r.Expression;
var k = mapper.Map<Address>(new AddressResponse());

Console.WriteLine("Hello, World!");