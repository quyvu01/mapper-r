namespace FrameworkTest.Dtos;

public class PersonResponse
{
    public string Name { get; set; }
    public List<AddressResponse> Addresses { get; set; }
}