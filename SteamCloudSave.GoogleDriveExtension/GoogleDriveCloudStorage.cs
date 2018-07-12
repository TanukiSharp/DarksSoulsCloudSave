using System;
using SteamCloudSave.Core;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Upload;
using Data = Google.Apis.Drive.v3.Data;

namespace SteamCloudSave.GoogleDriveExtension
{
    /// <summary>
    /// Implementation of cloud storage for the Google Drive platform. (AppData access variant)
    /// </summary>
    public class GoogleDriveCloudStorage : ICloudStorage
    {
        private static readonly string[] Scopes = { DriveService.Scope.DriveAppdata };
        private static readonly string ApplicationName = "DarkSoulsCloudSave";

        private const string ClientId = "483903211848-lll6sv6teesjlvrnu2faobrgvse17h5e.apps.googleusercontent.com";
        private const string ClientSecret = "2qatYztspPDByeq4rh7KBi_I";

        private DriveService driveService;

        /// <summary>
        /// Gets the display name of the current <see cref="ICloudStorage"/> instance.
        /// </summary>
        public string Name => "Google Drive";

        /// <summary>
        /// Initializes the Google Drive library, and ignites the authorization process if needed.
        /// </summary>
        /// <returns>Returns a task to be awaited until the initialization process is done.</returns>
        public async Task Initialize()
        {
            string path = ConfigurationUtility.GetExtensionConfigurationFilePath(GetType());
            path = Path.GetDirectoryName(path);

            UserCredential credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                new ClientSecrets
                {
                    ClientId = ClientId,
                    ClientSecret = ClientSecret,
                },
                Scopes,
                "user",
                CancellationToken.None,
                new FileDataStore(path, true));

            driveService = new DriveService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName,
            });
        }

        /// <summary>
        /// Lists the files available in the remote app folder on the Google Drive.
        /// </summary>
        /// <returns>Returns an array of remote filenames.</returns>
        public async Task<IEnumerable<CloudStorageFileInfo>> ListFiles()
        {
            FilesResource.ListRequest request = driveService.Files.List();
            request.Spaces = "appDataFolder";
            request.Fields = "files(id, name, createdTime)";

            // getting all files, then filtering does not scale :(
            IList<Data.File> files = (await request.ExecuteAsync()).Files;

            var result = new List<CloudStorageFileInfo>();

            foreach (IGrouping<string, Data.File> fileGroup in files.GroupBy(x => x.Name))
            {
                Data.File newest = fileGroup.OrderByDescending(x => x.CreatedTime).FirstOrDefault();
                if (newest != null)
                {
                    string name = newest.OriginalFilename ?? newest.Name;
                    if (name == null)
                        continue;
                    name = name.TrimStart('/');
                    result.Add(CloudStorageFileInfo.ParseCloudStorageFileInfo(name, newest.Id));
                }
            }

            return result;
        }

        /// <summary>
        /// Downloads a remote file from Google Drive, as a readable stream.
        /// </summary>
        /// <param name="fileInfo">The file identifier representing the remote file to download from Google Drive.</param>
        /// <returns>Returns a readable stream representing the remote file to download.</returns>
        public async Task<Stream> Download(CloudStorageFileInfo fileInfo)
        {
            var stream = new MemoryStream();

            FilesResource.GetRequest request = driveService.Files.Get(fileInfo.RemoteFileIdentifier);

            await request.DownloadAsync(stream);
            stream.Position = 0;

            return stream;
        }

        /// <summary>
        /// Uploads a local file to Google Drive.
        /// </summary>
        /// <param name="fileIdentifier">The full filename to be given to the remote file.</param>
        /// <param name="stream">A readable stream containing the local file content to upload to Google Drive.</param>
        /// <returns>Returns a task to be awaited until upload is done.</returns>
        public async Task<bool> Upload(string fileIdentifier, Stream stream)
        {
            var fileMetadata = new Data.File
            {
                Name = fileIdentifier,
                Parents = new List<string>() { "appDataFolder" }
            };

            FilesResource.CreateMediaUpload request = driveService.Files.Create(fileMetadata, stream, "application/octet-stream");

            IUploadProgress result = await request.UploadAsync();

            if (result.Exception != null)
                throw result.Exception;

            return result.Status == UploadStatus.Completed;
        }

        /// <summary>
        /// Deletes a file from Google Drive.
        /// </summary>
        /// <param name="fileInfo">The file information representing the remote file to delete.</param>
        /// <returns>Returns a task to be awaited until delteion is done, true meaning success and false meaning a failure occured.</returns>
        public async Task<bool> Delete(CloudStorageFileInfo fileInfo)
        {
            FilesResource.DeleteRequest request = driveService.Files.Delete(fileInfo.RemoteFileIdentifier);

            string result = await request.ExecuteAsync();

            return result == string.Empty;
        }

        /// <summary>
        /// Deletes multiple remote files from Google Drive.
        /// </summary>
        /// <param name="fileInfo">The file information representing the remote files to delete.</param>
        /// <returns>Returns a task to be awaited until deletion is done, true meaning success and false meaning a failure occured.</returns>
        public async Task<bool> DeleteMany(IEnumerable<CloudStorageFileInfo> fileInfo)
        {
            bool[] results = await Task.WhenAll(fileInfo.Select(Delete));
            return results.All(x => x);
        }

        /// <summary>
        /// Disposes the Google Drive library.
        /// </summary>
        public void Dispose()
        {
            if (driveService != null)
            {
                driveService.Dispose();
                driveService = null;
            }
        }
    }
}