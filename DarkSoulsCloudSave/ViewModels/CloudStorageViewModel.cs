﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DarkSoulsCloudSave.Core;

namespace DarkSoulsCloudSave.ViewModels
{
    public class CloudStorageViewModel : ViewModelBase
    {
        private readonly RootViewModel parent;

        public ICloudStorage CloudStorage { get; }

        public string UniqueId { get; }

        private bool isStoreTarget;
        public bool IsStoreTarget
        {
            get { return isStoreTarget; }
            set
            {
                if (SetValue(ref isStoreTarget, value))
                    parent.CloudStorageSelectionChanged();
            }
        }

        private bool isRestoreSource;
        public bool IsRestoreSource
        {
            get { return isRestoreSource; }
            set
            {
                if (SetValue(ref isRestoreSource, value))
                    parent.CloudStorageSelectionChanged();
            }
        }

        public string Name => CloudStorage.Name;

        public CloudStorageViewModel(ICloudStorage cloudStorage, RootViewModel parent)
        {
            if (cloudStorage == null)
                throw new ArgumentNullException(nameof(cloudStorage));

            this.parent = parent;

            CloudStorage = cloudStorage;

            UniqueId = MakeUniqueId(cloudStorage);
        }

        private string status;
        public string Status
        {
            get { return status; }
            private set { SetValue(ref status, value); }
        }

        private string detailedStatus;
        public string DetailedStatus
        {
            get { return detailedStatus; }
            private set { SetValue(ref detailedStatus, value); }
        }

        private bool isInitializing;
        private bool isInitialized;

        public async Task Initialize()
        {
            if (isInitialized || isInitializing)
                return;

            isInitializing = true;

            Status = "Initializing...";
            DetailedStatus = null;

            try
            {
                await CloudStorage.Initialize();

                Status = "Initialized";
                DetailedStatus = null;

                isInitialized = true;
            }
            catch (Exception ex)
            {
                Status = "Initialization failed";
                DetailedStatus = ex.Message;
            }
            finally
            {
                isInitializing = false;
            }
        }

        private bool isRestoring;

        public async Task Restore()
        {
            if (IsRestoreSource == false || isRestoring)
                return;

            isRestoring = true;

            try
            {
                await RestoreInternal();
            }
            catch (Exception ex)
            {
                Status = $"Error: {ex.Message}";
            }
            finally
            {
                isRestoring = false;
            }
        }

        private async Task RestoreInternal()
        {
            Status = "Retrieving save data list...";

            IList<IGrouping<DateTime, CloudStorageFileInfo>> fileGroups = RootViewModel.GroupArchives(await CloudStorage.ListFiles());

            if (fileGroups.Count == 0)
            {
                Status = "No save data";
                return;
            }

            foreach (CloudStorageFileInfo fileInfo in fileGroups[0])
            {
                Status = string.Format("Restoring {0}...", Path.GetFileNameWithoutExtension(fileInfo.LocalFilename));

                using (Stream archiveStream = await CloudStorage.Download(fileInfo))
                    await SaveDataUtility.ExtractSaveDataArchive(archiveStream);
            }

            Status = "Restore done";
        }

        private bool isStoring;

        public async Task Store(string timestamp, string[] directories, int revisionsToKeep)
        {
            if (IsStoreTarget == false || isStoring)
                return;

            isStoring = true;

            try
            {
                await StoreInternal(timestamp, directories, revisionsToKeep);
            }
            catch (Exception ex)
            {
                Status = $"Error: {ex.Message}";
            }
            finally
            {
                isStoring = false;
            }
        }

        private async Task StoreInternal(string timestamp, string[] directories, int revisionsToKeep)
        {
            Status = string.Format("Storing...");

            IList<Task> storeTasks = new List<Task>();

            foreach (string directory in directories)
            {
                string filename = Path.GetFileName(directory);

                Stream archiveStream = await SaveDataUtility.GetSaveDataArchive(directory);
                storeTasks.Add(CloudStorage.Upload($"/{timestamp}_{filename}.zip", archiveStream));
            }

            await Task.WhenAll(storeTasks);

            Status = "Cleaning up...";

            if (await CleanupOldRemoteSaves(revisionsToKeep) == false)
                Status = "Failed to cleanup";
            else
                Status = "Store done";
        }

        private async Task<bool> CleanupOldRemoteSaves(int revisionsToKeep)
        {
            IList<IGrouping<DateTime, CloudStorageFileInfo>> fileGroups = RootViewModel.GroupArchives(await CloudStorage.ListFiles());

            if (fileGroups.Count > revisionsToKeep)
            {
                IEnumerable<CloudStorageFileInfo> files = fileGroups
                    .Skip(revisionsToKeep)
                    .SelectMany(x => x);

                return await CloudStorage.DeleteMany(files);

                //IEnumerable<Task<bool>> deleteTasks = fileGroups
                //    .Skip(revisionsToKeep)
                //    .SelectMany(x => x)
                //    .Select(CloudStorage.Delete);

                //await Task.WhenAll(deleteTasks);

                //foreach (var t in deleteTasks)
                //{
                //    if ((await t) == false)
                //        return false;
                //}
            }

            return true;
        }

        public static string MakeUniqueId(ICloudStorage cloudStorage)
        {
            if (cloudStorage == null)
                throw new ArgumentNullException(nameof(cloudStorage));

            return cloudStorage.GetType().FullName;
        }
    }
}
