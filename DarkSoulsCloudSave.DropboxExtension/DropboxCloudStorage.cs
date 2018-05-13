﻿using DarkSoulsCloudSave.Core;
using Dropbox.Api;
using Dropbox.Api.Files;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;

namespace DarkSoulsCloudSave.DropboxExtension
{
    /// <summary>
    /// Implementation of cloud storage for the Dropbox platform.
    /// </summary>
    public class DropboxCloudStorage : ICloudStorage
    {
        private DropboxClient dropboxClient;

        private const string AppKey = "cwoecqgt2xtma0l";
        private const string AppSecret = "2a3si3j0kvgrush"; // <- not that secret in that case

        /// <summary>
        /// Initializes the Dropbox library, and ignites the authorization process if needed.
        /// </summary>
        /// <returns>Returns a task to be awaited until the initialization process is done.</returns>
        public async Task Initialize()
        {
            string accessToken;

            IReadOnlyDictionary<string, string> config = ConfigurationUtility.ReadConfigurationFile(GetType());

            if (config.TryGetValue("AccessToken", out accessToken) && string.IsNullOrWhiteSpace(accessToken) == false)
                accessToken = SecurityUtility.UnprotectString(accessToken, DataProtectionScope.CurrentUser);

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                Uri authorizeUri = DropboxOAuth2Helper.GetAuthorizeUri(AppKey, false);
                string url = authorizeUri.ToString();

                MessageBox.Show("After you click the OK button on this dialog, a web page asking you to allow the application will open, and then another one containing a code.\r\n\r\nOnce you see the code, please copy it to the clipboard by selecting it and pressing Ctrl+C, or right click and 'Copy' menu.", "Authorization", MessageBoxButton.OK, MessageBoxImage.Information);
                Process.Start(url);
                MessageBox.Show("Please proceed by closing this dialog once you copied the code.", "Authorization", MessageBoxButton.OK, MessageBoxImage.Information);

                string code = null;

                try
                {
                    code = Clipboard.GetText();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("An error occured:\r\n" + ex.Message, "Authorization Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                OAuth2Response response = await DropboxOAuth2Helper.ProcessCodeFlowAsync(code, AppKey, AppSecret);

                accessToken = response.AccessToken;

                ConfigurationUtility.CreateConfigurationFile(GetType(), new Dictionary<string, string>
                {
                    { "AccessToken", SecurityUtility.ProtectString(accessToken, DataProtectionScope.CurrentUser) },
                });

                MessageBox.Show("Authorization process succeeded.", "Authorization", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            dropboxClient = new DropboxClient(accessToken);
        }

        /// <summary>
        /// Lists the files available in the 'Apps/DarkSoulsCloudStrorage' remote folder on the Dropbox.
        /// </summary>
        /// <returns>Returns an array of remote filenames.</returns>
        public async Task<string[]> ListFiles()
        {
            if (dropboxClient == null)
                throw new InvalidOperationException("Not initialized");

            ListFolderResult list = await dropboxClient.Files.ListFolderAsync(string.Empty);

            return list.Entries
                .Where(e => e.IsFile)
                .Where(e => string.Equals(Path.GetExtension(e.Name), ".zip", StringComparison.InvariantCultureIgnoreCase))
                .Select(e => e.PathDisplay)
                .ToArray();
        }

        /// <summary>
        /// Downloads a remote file from Dropbox, as a readable stream.
        /// </summary>
        /// <param name="fileIdentifier">The full filename of the remote file to download from Dropbox.</param>
        /// <returns>Returns a readable stream representing the remote file to download.</returns>
        public async Task<Stream> Download(string fileIdentifier)
        {
            if (string.IsNullOrWhiteSpace(fileIdentifier))
                throw new ArgumentException($"Invalid '{nameof(fileIdentifier)}' argument.", nameof(fileIdentifier));

            if (dropboxClient == null)
                throw new InvalidOperationException("Not initialized");

            using (var response = await dropboxClient.Files.DownloadAsync(fileIdentifier))
                return new MemoryStream(await response.GetContentAsByteArrayAsync());
        }

        /// <summary>
        /// Uploads a local file to Dropbox.
        /// </summary>
        /// <param name="fileIdentifier">The full filename to be given to the remote file.</param>
        /// <param name="stream">A readable stream containing the local file content to upload to Dropbox.</param>
        /// <returns>Returns a task to be awaited until upload is done.</returns>
        public async Task<bool> Upload(string fileIdentifier, Stream stream)
        {
            if (string.IsNullOrWhiteSpace(fileIdentifier))
                throw new ArgumentException($"Invalid '{nameof(fileIdentifier)}' argument.", nameof(fileIdentifier));

            if (stream == null || stream.CanRead == false)
                throw new ArgumentException($"Invalid '{nameof(stream)}' argument. It must be a valid instance and being readable.", nameof(stream));

            if (dropboxClient == null)
                throw new InvalidOperationException("Not initialized");

            FileMetadata result = await dropboxClient.Files.UploadAsync(fileIdentifier, WriteMode.Overwrite.Instance, body: stream);

            return true;
        }

        /// <summary>
        /// Delete a remote file on Dropbox.
        /// </summary>
        /// <param name="fileIdentifier">The identifier of the remote file to delete.</param>
        /// <returns>Returns a task to be awaited until delteion is done, true meaning success and false meaning a failure occured.</returns>
        public async Task<bool> Delete(string fileIdentifier)
        {
            if (string.IsNullOrWhiteSpace(fileIdentifier))
                throw new ArgumentException($"Invalid '{nameof(fileIdentifier)}' argument.", nameof(fileIdentifier));

            if (dropboxClient == null)
                throw new InvalidOperationException("Not initialized");

            Metadata result = await dropboxClient.Files.DeleteAsync(fileIdentifier);

            return true;
        }

        /// <summary>
        /// Disposes the Dropbox library.
        /// </summary>
        public void Dispose()
        {
            if (dropboxClient != null)
            {
                dropboxClient.Dispose();
                dropboxClient = null;
            }
        }
    }
}
