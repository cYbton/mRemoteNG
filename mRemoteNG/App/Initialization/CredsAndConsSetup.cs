using System.IO;
using System.Runtime.Versioning;
using mRemoteNG.Config.Connections;
using mRemoteNG.Properties;

namespace mRemoteNG.App.Initialization
{
    [SupportedOSPlatform("windows")]
    public class CredsAndConsSetup
    {
        public void LoadCredsAndCons()
        {
            new SaveConnectionsOnEdit(Runtime.ConnectionsService);

            if (Properties.App.Default.FirstStart && !Properties.OptionsBackupPage.Default.LoadConsFromCustomLocation && !File.Exists(Runtime.ConnectionsService.GetStartupConnectionFileName()))
                Runtime.ConnectionsService.NewConnectionsFile(Runtime.ConnectionsService.GetStartupConnectionFileName());

            // sysprep fork: the password-protected user profiles are opt-in (EnableUserProfiles) - without
            // them the connections load from the connections file as before, with no password prompt
            if (Properties.Settings.Default.EnableUserProfiles)
                Runtime.TryInitializeUserProfile();
            Runtime.LoadConnections();
        }
    }
}