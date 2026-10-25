using System;
using System.ComponentModel;
using System.Runtime.Versioning;
using mRemoteNG.App;
using mRemoteNG.App.Info;


namespace mRemoteNG.Connection
{
    [SupportedOSPlatform("windows")]
    public class ConnectionIcon : StringConverter
    {
        public static string[] Icons = { };

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
        {
            return new StandardValuesCollection(Icons);
        }

        public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context)
        {
            return true;
        }

        public override bool GetStandardValuesSupported(ITypeDescriptorContext? context)
        {
            return true;
        }

        public static System.Drawing.Icon? FromString(string iconName)
        {
            try
            {
                string iconPath = $"{GeneralAppInfo.HomePath}\\Icons\\{iconName}.ico";

                // sysprep fork: mRemoteNG's own icons are in Icons\mRemoteNG - connection files written for the
                // original layout name them without a folder ("Windows", the default "mRemoteNG")
                if (!System.IO.File.Exists(iconPath) && !iconName.Contains('\\') && !iconName.Contains('/'))
                    iconPath = $"{GeneralAppInfo.HomePath}\\Icons\\mRemoteNG\\{iconName}.ico";

                if (System.IO.File.Exists(iconPath))
                {
                    System.Drawing.Icon nI = new(iconPath);
                    return nI;
                }
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddMessage(Messages.MessageClass.ErrorMsg, $"Couldn't get Icon from String" + Environment.NewLine + ex.Message);
            }

            return null;
        }
    }
}