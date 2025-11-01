namespace CleanMate.Api.Contracts
{
    public record CleanerProfileUpdateDto(
    string FullName,
    string PhoneNumber,
    string AddressText,
    string Bio
    );
}
