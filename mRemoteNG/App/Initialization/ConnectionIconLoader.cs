using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using mRemoteNG.Connection;


namespace mRemoteNG.App.Initialization
{
    [SupportedOSPlatform("windows")]
    public class ConnectionIconLoader
    {
        private readonly string _path;

        public ConnectionIconLoader(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath))
                throw new ArgumentException($"{nameof(folderPath)} must be a valid folder path.");

            _path = folderPath;
        }

        public void GetConnectionIcons()
        {
            if (Directory.Exists(_path) == false)
                return;

            // sysprep fork: the icons are sorted into sets (Icons\mRemoteNG, Icons\azure2\<category>, ...) - the
            // list offers each with its path below Icons, which is the value ConnectionIcon.FromString loads.
            // With the bare file name, picking an icon from a subfolder left the connection without one.
            string root = Path.GetFullPath(_path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string[] names = Directory.GetFiles(root, "*.ico", SearchOption.AllDirectories)
                .Select(f => Path.ChangeExtension(f.Substring(root.Length), null))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            ConnectionIcon.Icons = [.. ConnectionIcon.Icons, .. names];
        }
    }
}