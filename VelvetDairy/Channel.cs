namespace VelvetDairy;

/// <summary>
/// Live/testing or the private dev channel. A dev-channel build (DEVCHANNEL) is a separate plugin: its own internal
/// name (so its own save and settings), its own chat command, and its own server, listed in network-dev.json and
/// dev.json on the devrelease branch. It never talks to the live server.
/// </summary>
public static class Channel
{
#if DEVCHANNEL
    public const bool Dev = true;
    public const string Command = "/milkersdev";
    public const string NetworkFileUrl = "https://raw.githubusercontent.com/sadoniaveletoria/velvetdairy/devrelease/network-dev.json";
    public const string RepoFileUrl = "https://raw.githubusercontent.com/sadoniaveletoria/velvetdairy/devrelease/dev.json";
    public const string TitleSuffix = " (dev)";
#else
    public const bool Dev = false;
    public const string Command = "/milkers";
    public const string NetworkFileUrl = "https://raw.githubusercontent.com/sadoniaveletoria/velvetdairy/main/network.json";
    public const string RepoFileUrl = "https://raw.githubusercontent.com/sadoniaveletoria/velvetdairy/main/repo.json";
    public const string TitleSuffix = "";
#endif
}
