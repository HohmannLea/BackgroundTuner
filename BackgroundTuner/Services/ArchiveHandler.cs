using System.IO;
using System.IO.Compression;


namespace BackgroundTuner.Services
{
    public class ArchiveHandler
    {
        private ZipArchive? archive;

        public void OpenArchive(string archivePath)
        {
            if (archive != null)
            {
                archive.Dispose();
            }
            archive = ZipFile.Open(archivePath, ZipArchiveMode.Read);
        }

        public Stream? GetFileStream(string fileName)
        {
            if (archive == null)
            {
                throw new InvalidOperationException("Archive is not opened.");
            }
            var entry = archive.GetEntry(fileName);
            return entry?.Open();
        }

        public void CloseArchive() {
            if (archive != null)
            {
                archive.Dispose();
                archive = null;
            }
        }
}

}
