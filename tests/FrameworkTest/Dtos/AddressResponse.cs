namespace FrameworkTest.Dtos;

public class AddressResponse
{
    public string City { get; set; }
    public string Location { get; set; }
    public StreetResponse Street { get; set; }
}