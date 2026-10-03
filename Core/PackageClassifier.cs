using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.IO.Compression;

namespace MAV.ProductionRegressionController
{
    public static class PackageClassifier
    {
        public static PackageInfo Analyze(string cpzPath, string configPath)
        {
            if (!LocalFileIO.Exists(cpzPath)) throw new FileNotFoundException("CPZ was not staged.", cpzPath);
            if (!LocalFileIO.Exists(configPath)) throw new FileNotFoundException("SystemDefinition/config file was not staged.", configPath);
            using (ZipArchive cpz = new ZipArchive(new MemoryStream(LocalFileIO.ReadAllBytes(cpzPath, 256L*1024L*1024L)), ZipArchiveMode.Read, false))
            {
                if (cpz.Entries.Count == 0) throw new InvalidOperationException("Selected CPZ is an empty archive.");
            }

            string text = LocalFileIO.ReadAllText(configPath, 8L*1024L*1024L);
            JObject root = JObject.Parse(text);
            string name = Read(root, "Name");
            string matrixName = root["MatrixSwitcher"] == null ? string.Empty : Convert.ToString(root["MatrixSwitcher"]["Name"]);

            MavPackageType type = MavPackageType.Unknown;
            string reason = "No authoritative package marker was found.";

            JToken programType = root["ProgramType"] ?? root["SystemType"];
            if (programType != null)
            {
                string explicitType = Convert.ToString(programType).Trim();
                if (explicitType.Equals("VTCFarm", StringComparison.OrdinalIgnoreCase) || explicitType.Equals("VTC Farm", StringComparison.OrdinalIgnoreCase))
                {
                    type = MavPackageType.VtcFarm;
                    reason = "Explicit ProgramType/SystemType identifies VTC Farm.";
                }
                else if (explicitType.Equals("Room", StringComparison.OrdinalIgnoreCase))
                {
                    type = MavPackageType.Room;
                    reason = "Explicit ProgramType/SystemType identifies Room.";
                }
            }

            if (type == MavPackageType.Unknown &&
                ((!string.IsNullOrEmpty(name) && name.IndexOf("VTC Farm", StringComparison.OrdinalIgnoreCase) >= 0) ||
                 (!string.IsNullOrEmpty(matrixName) && matrixName.IndexOf("VTC Farm", StringComparison.OrdinalIgnoreCase) >= 0)))
            {
                type = MavPackageType.VtcFarm;
                reason = "SystemDefinition name/topology identifies VTC Farm.";
            }

            if (type == MavPackageType.Unknown && !string.IsNullOrWhiteSpace(name))
            {
                if (root["Sources"] != null || root["Destinations"] != null || root["Displays"] != null || root["RoomCombine"] != null || root["VTC"] != null)
                {
                    type = MavPackageType.Room;
                    reason = "SystemDefinition contains room topology objects.";
                }
            }

            return new PackageInfo
            {
                CpzPath = cpzPath,
                ConfigPath = configPath,
                CpzFileName = Path.GetFileName(cpzPath),
                ConfigFileName = Path.GetFileName(configPath),
                CpzSha256 = FileHash.Sha256(cpzPath),
                ConfigSha256 = FileHash.Sha256(configPath),
                PackageType = type,
                SystemName = name,
                DetectionReason = reason
            };
        }

        private static string Read(JObject o, string name)
        {
            JToken t = o[name];
            return t == null ? string.Empty : Convert.ToString(t);
        }
    }
}
