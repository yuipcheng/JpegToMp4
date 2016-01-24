namespace JpegToMp4Service
{
    partial class ProjectInstaller
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.JpegToMp4ServiceProcessInstaller = new System.ServiceProcess.ServiceProcessInstaller();
            this.JpegToMp4ServiceInstaller = new System.ServiceProcess.ServiceInstaller();
            // 
            // JpegToMp4ServiceProcessInstaller
            // 
            this.JpegToMp4ServiceProcessInstaller.Account = System.ServiceProcess.ServiceAccount.LocalSystem;
            this.JpegToMp4ServiceProcessInstaller.Password = null;
            this.JpegToMp4ServiceProcessInstaller.Username = null;
            // 
            // JpegToMp4ServiceInstaller
            // 
            this.JpegToMp4ServiceInstaller.ServiceName = "JpegToMp4Service";
            this.JpegToMp4ServiceInstaller.StartType = System.ServiceProcess.ServiceStartMode.Automatic;
            // 
            // ProjectInstaller
            // 
            this.Installers.AddRange(new System.Configuration.Install.Installer[] {
            this.JpegToMp4ServiceProcessInstaller,
            this.JpegToMp4ServiceInstaller});

        }

        #endregion

        private System.ServiceProcess.ServiceProcessInstaller JpegToMp4ServiceProcessInstaller;
        private System.ServiceProcess.ServiceInstaller JpegToMp4ServiceInstaller;
    }
}