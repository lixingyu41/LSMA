namespace LSMA.Models;

public sealed record GameDirectoryState(string Path, bool HasSmapi, bool HasVanilla)
{
    public bool CanLaunch => HasSmapi || HasVanilla;
}
