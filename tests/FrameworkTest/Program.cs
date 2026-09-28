// See https://aka.ms/new-console-template for more information

using FrameworkTest;
using FrameworkTest.Dtos;
using FrameworkTest.Entities;
using MapperR.Core.Abstractions;
using MapperR.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;

var serviceCollection = new ServiceCollection();
serviceCollection
    .AddMapR(cfg => { cfg.AddProfilesFromAssembly(typeof(Program).Assembly); })
    .AddGeneratedMappers();
var serviceProvider = serviceCollection.BuildServiceProvider();
var mapper = serviceProvider.GetRequiredService<IMapper>();

var resolvedMapper = serviceProvider.GetRequiredService<IInternalMapper<Person, PersonResponse>>();
Console.WriteLine($"resolved mapper = {resolvedMapper.GetType().Name}");
var person = new Person { Name = "Abc", Address = new Address { City = "Hanoi" } };
var personResponse = mapper.Map<PersonResponse>(person);

Console.WriteLine($"personResponse.Name = {personResponse.Name}");
Console.WriteLine($"personResponse.Address.City = {personResponse.Address?.City}");

var personWithoutAddress = new Person { Name = "Xyz", Address = null };
var personWithoutAddressResponse = mapper.Map<PersonResponse>(personWithoutAddress);

Console.WriteLine($"personWithoutAddressResponse.Address = {personWithoutAddressResponse.Address}");