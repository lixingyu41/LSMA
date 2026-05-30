using Windows.Security.Credentials;

namespace LSMA.Services;

public sealed class NexusCredentialService
{
    private const string Resource = "LSMA.Nexus.ApiKey";
    private const string UserName = "NexusPersonalKey";
    private const string WebLoginResource = "LSMA.Nexus.WebLogin";
    private readonly PasswordVault _vault = new();

    public bool HasCredential => GetKey() is not null;
    public bool HasWebLogin => GetWebLogin() is not null;

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

    public NexusWebLoginCredential? GetWebLogin()
    {
        try
        {
            var credential = _vault.FindAllByResource(WebLoginResource).FirstOrDefault();
            if (credential is null)
            {
                return null;
            }

            credential.RetrievePassword();
            return new NexusWebLoginCredential(credential.UserName, credential.Password);
        }
        catch
        {
            return null;
        }
    }

    public void SaveWebLogin(string userName, string password)
    {
        ClearWebLogin();
        _vault.Add(new PasswordCredential(WebLoginResource, userName, password));
    }

    public void ClearWebLogin()
    {
        try
        {
            foreach (var credential in _vault.FindAllByResource(WebLoginResource).ToList())
            {
                _vault.Remove(credential);
            }
        }
        catch
        {
            // A missing credential already represents the desired state.
        }
    }
}

public sealed record NexusWebLoginCredential(string UserName, string Password);
