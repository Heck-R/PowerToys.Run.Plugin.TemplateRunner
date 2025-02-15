
namespace Community.PowerToys.Run.Plugin.TemplateRunner.Util {

    public class IconLoader {
        /// <summary>
        /// Project path to the folder containing the theme folders
        /// </summary>
        private string ThemesRootPath { get; set; } = "Images";

        /// <summary>
        /// Name of the theme, and at the same time the folder containing the theme
        /// </summary>
        public string ThemeName { get; set; }

        /// <summary>
        /// Gets an icon from the theme set in this class
        /// </summary>
        /// <param name="themeIconPath">Subpath to an icon inside the theme folder</param>
        /// <returns></returns>
        private string GetThemedIcon(string themeIconPath) {
            return $"{this.ThemesRootPath}/{this.ThemeName}/{themeIconPath}";
        }

        /// <summary>
        /// Main Plugin icon
        /// </summary>
        public string MAIN { get => this.GetThemedIcon("main.png"); }

        /// <summary>
        /// Warning icon
        /// </summary>
        public string WARNING { get => this.GetThemedIcon("warning.png"); }
    }

}
