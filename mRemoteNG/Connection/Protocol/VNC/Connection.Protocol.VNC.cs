using System;
using System.ComponentModel;
using mRemoteNG.App;
using mRemoteNG.Resources.Language;
using mRemoteNG.Tools;
using System.Runtime.Versioning;

// ReSharper disable ArrangeAccessorOwnerBody


namespace mRemoteNG.Connection.Protocol.VNC
{
    // sysprep fork: VNC (and ARD, built on it) is not part of this build - the VncSharpCore library is gone, the
    // training labs don't use VNC. The class stays for its enums, which connection files and imports still use;
    // a VNC/ARD connection only reports that the protocol is not available.
    [SupportedOSPlatform("windows")]
    public class ProtocolVNC : ProtocolBase
    {
        #region Public Methods

        public override bool Connect()
        {
            Runtime.MessageCollector.AddMessage(Messages.MessageClass.ErrorMsg,
                                                "VNC is not part of the sysprep.io build of mRemoteNG.", true);
            return false;
        }

        public void SendSpecialKeys(SpecialKeys Keys)
        {
        }

        public void StartChat()
        {
            throw new NotImplementedException();
        }

        public void StartFileTransfer()
        {
            throw new NotImplementedException();
        }

        public void RefreshScreen()
        {
        }

        #endregion
        #region Enums

        public enum Defaults
        {
            Port = 5900
        }

        public enum SpecialKeys
        {
            CtrlAltDel,
            CtrlEsc
        }

        public enum Compression
        {
            [LocalizedAttributes.LocalizedDescription(nameof(Language.NoCompression))]
            CompNone = 99,
            [Description("0")] Comp0 = 0,
            [Description("1")] Comp1 = 1,
            [Description("2")] Comp2 = 2,
            [Description("3")] Comp3 = 3,
            [Description("4")] Comp4 = 4,
            [Description("5")] Comp5 = 5,
            [Description("6")] Comp6 = 6,
            [Description("7")] Comp7 = 7,
            [Description("8")] Comp8 = 8,
            [Description("9")] Comp9 = 9
        }

        public enum Encoding
        {
            [Description("Raw")] EncRaw,
            [Description("RRE")] EncRRE,
            [Description("CoRRE")] EncCorre,
            [Description("Hextile")] EncHextile,
            [Description("Zlib")] EncZlib,
            [Description("Tight")] EncTight,
            [Description("ZlibHex")] EncZLibHex,
            [Description("ZRLE")] EncZRLE
        }

        public enum AuthMode
        {
            [LocalizedAttributes.LocalizedDescription(nameof(Language.Vnc))]
            AuthVNC,

            [LocalizedAttributes.LocalizedDescription(nameof(Language.Windows))]
            AuthWin
        }

        public enum ProxyType
        {
            [LocalizedAttributes.LocalizedDescription(nameof(Language.None))]
            ProxyNone,

            [LocalizedAttributes.LocalizedDescription(nameof(Language.Http))]
            ProxyHTTP,

            [LocalizedAttributes.LocalizedDescription(nameof(Language.Socks5))]
            ProxySocks5,

            [LocalizedAttributes.LocalizedDescription(nameof(Language.UltraVncRepeater))]
            ProxyUltra
        }

        public enum Colors
        {
            [LocalizedAttributes.LocalizedDescription(nameof(Language.Normal))]
            ColNormal,
            [Description("8-bit")] Col8Bit
        }

        public enum SmartSizeMode
        {
            [LocalizedAttributes.LocalizedDescription(nameof(Language.NoSmartSize))]
            SmartSNo,

            [LocalizedAttributes.LocalizedDescription(nameof(Language.Free))]
            SmartSFree,

            [LocalizedAttributes.LocalizedDescription(nameof(Language.Aspect))]
            SmartSAspect
        }

        #endregion
    }
}
