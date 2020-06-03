using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ArtifactBuilder.Artifacts
{
    public class DowloadSiteArtifact : Artifact
    {
        private const string SourceShaFileName = "checksum.sha256";

        public string Version { get; }
        public string ShaDirectory { get; }

        public DowloadSiteArtifact(string configuration) : base("DownloadSite")
        {
            OutputDirectory = $@"{SourceDirectory}\Build\BuildArtifacts\{Name}";
            ShaDirectory = OutputDirectory + @"\SHA256";
            var agentComponents = AgentComponents.GetAgentComponents(AgentType.Framework, configuration, "x64", SourceDirectory);
            Version = agentComponents.Version;
        }

        protected override void InternalBuild()
        {
            Directory.CreateDirectory(OutputDirectory);
            Directory.CreateDirectory(ShaDirectory);

            List<string> platforms = new List<string>()
            {
                "x86",
                "x64"
            };

            //Msi Installer
            foreach (var platform in platforms)
            {
                CopyFileAndChecksum($@"{SourceDirectory}\Build\BuildArtifacts\MsiInstaller-{platform}", "*.msi", OutputDirectory,
                    $@"newrelic-agent-win-{platform}-{Version}.msi");
            }

            //Scriptable Installer
            CopyFileAndChecksum($@"{SourceDirectory}\Build\BuildArtifacts\ScriptableInstaller", "*.zip", OutputDirectory, $@"newrelic-agent-win-{Version}-scriptable-installer.zip");

            //Core Scriptable Installer
            CopyFileAndChecksum($@"{SourceDirectory}\Build\BuildArtifacts\ZipArchiveCoreInstaller", "*.zip", OutputDirectory, $@"newrelic-netcore20-agent-win-{Version}-scriptable-installer.zip");

            //Core Zip files
            foreach (var platform in platforms)
            {
                CopyFileAndChecksum($@"{SourceDirectory}\Build\BuildArtifacts\ZipArchiveCore-{platform}", "*.zip", OutputDirectory, $@"newrelic-netcore20-agent-win-{platform}-{Version}.zip");
            }

            //Framework Zip files
            foreach (var platform in platforms)
            {
                CopyFileAndChecksum($@"{SourceDirectory}\Build\BuildArtifacts\ZipArchiveFramework-{platform}", "*.zip", OutputDirectory, $@"newrelic-agent-win-{platform}-{Version}.zip");
            }

            //Linux packages
            CopyFileAndChecksum($@"{SourceDirectory}\Build\BuildArtifacts\LinuxDeb", "*.deb", OutputDirectory);
            CopyFileAndChecksum($@"{SourceDirectory}\Build\BuildArtifacts\LinuxRpm", "*.rpm", OutputDirectory);
            CopyFileAndChecksum($@"{SourceDirectory}\Build\BuildArtifacts\LinuxTar", "*.tar.gz", OutputDirectory);

            //Copying Readme.txt file
            FileHelpers.CopyFile($@"{PackageDirectory}\Readme.txt", $@"{OutputDirectory}");
        }

        private void CopyFileAndChecksum(string sourceDirectory, string sourceFileSearchPattern, string destinationDirectory, string destinationFileName = null)
        {
            var filePath = Directory.GetFiles(sourceDirectory, sourceFileSearchPattern).First();

            if (destinationFileName == null)
            {
                var fileInfo = new FileInfo(filePath);
                destinationFileName = fileInfo.Name;
            }

            File.Copy(filePath, $@"{destinationDirectory}\{destinationFileName}");
            File.Copy($@"{sourceDirectory}\{SourceShaFileName}", $@"{ShaDirectory}\{destinationFileName}.sha256");
        }
    }
}