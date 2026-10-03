namespace MAV.ProductionRegressionController
{
    /// <summary>
    /// Canonical CP4N-local filesystem locations. Keep controller-local paths in the
    /// Crestron/Linux form used by CrestronIO and SFTP: /user/..., never \\User\\....
    /// </summary>
    internal static class LocalPaths
    {
        public const string Root = "/user/MAVRegression";
        public static readonly string Staging = Root + "/Staging";
        public static readonly string Evidence = Root + "/Evidence";
        public static readonly string Logs = Root + "/Logs";
        public static readonly string Console = Root + "/Console";
        public static readonly string CloudCache = Root + "/CloudCache";
        public static readonly string DropboxAuth = Root + "/DropboxAuth";
        public static readonly string OfflineBundles = Root + "/OfflineBundles";
        public static readonly string RuntimeWebAssets = Root + "/RuntimeWebAssets";

        public static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            string p = path.Replace('\\', '/');
            if (p.Equals("/User", System.StringComparison.OrdinalIgnoreCase)) return "/user";
            if (p.StartsWith("/User/", System.StringComparison.OrdinalIgnoreCase)) p = "/user/" + p.Substring(6);
            else if (p.Equals("User", System.StringComparison.OrdinalIgnoreCase)) p = "/user";
            else if (p.StartsWith("User/", System.StringComparison.OrdinalIgnoreCase)) p = "/user/" + p.Substring(5);
            return p;
        }
    }
}
