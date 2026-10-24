using System;
using System.Runtime.Versioning;
using mRemoteNG.App.Info;

namespace mRemoteNG.UI.Forms
{
    [SupportedOSPlatform("windows")]
    /// <summary>
    /// Interaction logic for FrmSplashScreenNew.xaml
    /// </summary>
    public partial class FrmSplashScreenNew
    {
        static FrmSplashScreenNew instance = null;
        public FrmSplashScreenNew()
        {
            InitializeComponent();
            LoadFont();
            lblLogoPartD.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center;
            // sysprep fork: the version names the build ("1.78.2 (sysprep.io build) x64"), no code name
            lblLogoPartD.Content = $@"v. {GeneralAppInfo.ApplicationVersion}";
        }
        public static FrmSplashScreenNew GetInstance()
        {
            //instance == null
            instance ??= new FrmSplashScreenNew();
            return instance;
        }

        void LoadFont()
        {
            lblLogoPartA.FontFamily = new System.Windows.Media.FontFamily(new Uri("pack://application:,,,/"), "./UI/Font/#HandelGotDBol");
            lblLogoPartB.FontFamily = new System.Windows.Media.FontFamily(new Uri("pack://application:,,,/"), "./UI/Font/#HandelGotDBol");
        }
    }
}
