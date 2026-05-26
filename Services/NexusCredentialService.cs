using Windows.Security.Credentials;

namespace LSMA.Services;

public sealed class NexusCredentialService
{
    private const string Resource = "LSMA.Nexus.ApiKey";
    private const string UserName = "NexusPersonalKey";
    private readonly PasswordVault _vault = new();

    public bool HasCredential => GetKey() is not null;

    public string? GetKey()
    {
        try
        {
            var credential = _vault.Retrieve(Resource, UserName);
            credential.RetrievePassword();
            return credential.Password;
        }
        catch
        {
            return null;
        }
    }

    public void Save(string apiKey)
    {
        Clear();
        _vault.Add(new PasswordCredential(Resource, UserName, apiKey));
    }

    public void Clear()
    {
        try
        {
            var credential = _vault.Retrieve(Resource, UserName);
            _vault.Remove(credential);
        }
        catch
        {
            // A missing credential already represents the desired state.
        }
    }
}
