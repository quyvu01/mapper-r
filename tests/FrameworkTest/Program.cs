// See https://aka.ms/new-console-template for more information

using System.Linq.Expressions;
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
Expression<Func<int, long?>> k = x => x + 1;
var n = k.Compile().Invoke(1);

// var person = new Person { Name = "Abc", Addresses = [new Address { City = "Hanoi" }] };
var person = new Person { Age = 10 };
var personResponse = mapper.Map<PersonResponse>(person);
Console.WriteLine($"Response");