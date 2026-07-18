namespace Pyramid.Security;

public interface IDatabaseCredentialProvider
{
    string? GetPassword(string credentialId);

    void SavePassword(string credentialId, string password);
}
