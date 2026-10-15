// See https://aka.ms/new-console-template for more information

using FrameworkTest;
using FrameworkTest.Dtos;
using FrameworkTest.Entities;
using MapperR.Core.Abstractions;
using MapperR.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;

var serviceCollection = new ServiceCollection();
serviceCollection
    .AddMapperR(cfg => { cfg.AddProfilesFromAssembly(typeof(Program).Assembly); });
var serviceProvider = serviceCollection.BuildServiceProvider();
var mapper = serviceProvider.GetRequiredService<IMapper>();

var person = new Person { Name = "Abc", Addresses = [new Address { City = "Hanoi" }] };
var personResponse = mapper.Map<PersonResponse>(person);

Console.WriteLine(personResponse);