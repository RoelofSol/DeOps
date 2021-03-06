using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

using DeOps.Implementation;
using DeOps.Implementation.Protocol;

namespace DeOps.Services.Storage
{
    public enum WorkingChange { Created, Updated, Removed };

    public class WorkingStorage
    {
        OpCore Core;
        G2Protocol Protocol;
        StorageService Storages;

        public uint ProjectID;

        public bool Modified;
        public bool PeriodicSave;

        FileSystemWatcher FileWatcher;
        FileSystemWatcher FolderWatcher;

        public LocalFolder RootFolder;
        public string RootPath;


        public WorkingStorage(StorageService storages, uint project)
        {
            Storages = storages;
            Core = Storages.Core;
            Protocol = storages.Protocol;

            RootPath = Storages.GetRootPath(Core.UserID, project);
            ProjectID = project;

           
            StorageFolder packet = new StorageFolder();
            packet.Name = Core.Trust.GetProjectName(project) + " Files";

            RootFolder = new LocalFolder(null, packet);

            LoadWorking();
        }

        public void SaveWorking()
        {
            try
            {
                string tempPath = Core.GetTempPath();
                using (IVCryptoStream stream = IVCryptoStream.Save(tempPath, Storages.LocalFileKey))
                {
                    Protocol.WriteToFile(new StorageRoot(ProjectID), stream);

                    WriteWorkingFile(stream, RootFolder, false); // record all so working can be browsed while locked

                    stream.FlushFinalBlock();
                }
                
                byte[] hash = null;
                long size = 0;
                Utilities.HashTagFile(tempPath, Protocol, ref hash, ref size);

                string finalPath = Storages.GetWorkingPath(ProjectID);

                File.Delete(finalPath);
                File.Move(tempPath, finalPath);
            }
            catch (Exception ex)
            {
                Core.Network.UpdateLog("Storage", "Error saving headers " + ex.Message);
            }
        }


        private void LoadWorking()
        {
            OpStorage local = Storages.GetStorage(Core.UserID);

            if (local == null)
                return;

            // load working
            try
            {
                string path = Storages.GetWorkingPath(ProjectID);
                byte[] key = Storages.LocalFileKey;

                if (File.Exists(path)) // use locally committed storage file
                {
                    Modified = true; // working file present on startup, meaning there are changes lingering to be committed
                }
                else
                {
                    path = Storages.GetFilePath(local);

                    if (!File.Exists(path))
                        return;

                    key = local.File.Header.FileKey;
                }

                using (TaggedStream source = new TaggedStream(path, Protocol))
                using (IVCryptoStream crypto = IVCryptoStream.Load(source, key))
                {
                    PacketStream stream = new PacketStream(crypto, Protocol, FileAccess.Read);

                    G2Header header = null;
                    bool readingProject = false;

                    LocalFolder CurrentFolder = RootFolder;
                    string CurrentPath = RootPath;

                    while (stream.ReadPacket(ref header))
                    {
                        if (header.Name == StoragePacket.Root)
                        {
                            StorageRoot root = StorageRoot.Decode(header);

                            readingProject = (root.ProjectID == ProjectID);
                        }

                        if (readingProject)
                        {
                            if (header.Name == StoragePacket.Folder)
                            {
                                StorageFolder folder = StorageFolder.Decode(header);

                                bool added = false;

                                while (!added)
                                {
                                    if (CurrentFolder.Info.UID == folder.ParentUID)
                                    {
                                        // tracked, so need to add multiple folders (archives) with same UIDs
                                        CurrentFolder = CurrentFolder.AddFolderInfo(folder);

                                        added = true;
                                    }
                                    else if (CurrentFolder.Parent == null) // error
                                        break;
                                    else if (CurrentFolder.Parent.GetType() == typeof(LocalFolder))
                                        CurrentFolder = CurrentFolder.Parent;
                                    else
                                        break;
                                }

                                if (!added)
                                {
                                    Debug.Assert(false);
                                    throw new Exception("Error loading CFS");
                                }
                            }

                            if (header.Name == StoragePacket.File)
                            {
                                StorageFile file = StorageFile.Decode(header);

                                CurrentFolder.AddFileInfo(file);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Core.Network.UpdateLog("Storage", "Error loading headers " + ex.Message);
            }
        }

        public void StartWatchers()
        {
            try
            {
                if (FileWatcher == null)
                {
                    FileWatcher = new FileSystemWatcher(RootPath, "*");

                    FileWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite;

                    FileWatcher.IncludeSubdirectories = true;

                    FileWatcher.Changed += new FileSystemEventHandler(OnFileChanged);
                    FileWatcher.Created += new FileSystemEventHandler(OnFileChanged);
                    FileWatcher.Deleted += new FileSystemEventHandler(OnFileChanged);
                    FileWatcher.Renamed += new RenamedEventHandler(OnFileRenamed);
                    
                    FileWatcher.EnableRaisingEvents = true;

                }

                if (FolderWatcher == null)
                {
                    FolderWatcher = new FileSystemWatcher(RootPath, "*");

                    FolderWatcher.NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.LastWrite;

                    FolderWatcher.IncludeSubdirectories = true;

                    FolderWatcher.Created += new FileSystemEventHandler(OnFolderChanged);
                    FolderWatcher.Deleted += new FileSystemEventHandler(OnFolderChanged);
                    FolderWatcher.Renamed += new RenamedEventHandler(OnFolderRenamed);
                    
                    FolderWatcher.EnableRaisingEvents = true;
                }
            }
            catch { }
        }

        public void StopWatchers()
        {
            FileWatcher.EnableRaisingEvents = false;
            FileWatcher.Dispose();
            FileWatcher = null;

            FolderWatcher.EnableRaisingEvents = false;
            FolderWatcher.Dispose();
            FolderWatcher = null;

        }

        public bool FileExists(string path)
        {
            string name = Path.GetFileName(path);
            string dir = Utilities.StripOneLevel(path);

            LocalFolder folder = GetLocalFolder(dir);

            if (folder == null)
                return false;

            LocalFile file = folder.GetFile(name);

            if (file == null)
                return false;

            return true;
        }

        public bool TrackFile(string path)
        {
            string name = Path.GetFileName(path);
            string dir = Utilities.StripOneLevel(path);

            LocalFolder folder = GetLocalFolder(dir);
            LocalFile file = folder.GetFile(name);

            if (file != null)
                return false;

            file = CreateNewFile(name);
            file.Info.SetFlag(StorageFlags.Modified);
            file.Info.SetFlag(StorageFlags.Unlocked);

            folder.AddFile(file);

            Storages.MarkforHash(file, RootPath + dir + Path.DirectorySeparatorChar + name, ProjectID, dir);
            Modified = true;
            PeriodicSave = true;

            Storages.CallFileUpdate(ProjectID, folder.GetPath(), file.Info.UID, WorkingChange.Created);

            return true;
        }

        public void TrackFile(string path, StorageFile track)
        {
            LocalFolder folder = GetLocalFolder(path);

            if (folder == null)
                return;

            // increase references
            OpFile commonFile = null;
            if (Storages.FileMap.SafeTryGetValue(track.HashID, out commonFile))
                commonFile.References++;

            LocalFile file = new LocalFile(track);
            file.Info.SetFlag(StorageFlags.Modified);
            file.Archived.SafeAddFirst(track);

            folder.AddFile(file);

            Modified = true;
            PeriodicSave = true;

            Storages.CallFileUpdate(ProjectID, folder.GetPath(), file.Info.UID, WorkingChange.Created);
        }

        public void ReplaceFile(string path, StorageFile replacement, List<LockError> errors)
        {
            string name = Path.GetFileName(path);
            string dir = Utilities.StripOneLevel(path);

            LocalFolder folder = GetLocalFolder(dir);
            LocalFile file = folder.GetFile(name);

            ReplaceFile(file, replacement, dir, true, errors);

            Storages.CallFileUpdate(ProjectID, folder.GetPath(), file.Info.UID, WorkingChange.Updated);
        }

        void ReplaceFile(LocalFile file, StorageFile replacement, string dir, bool setModified, List<LockError> errors)
        {
            // this will lock file if it is unlocked
            bool unlocked = Storages.IsFileUnlocked(Core.UserID, ProjectID, dir, file.Info, false);

            Storages.LockFile(Core.UserID, ProjectID, dir, file.Info, false);

            if (setModified)
                ReadyChange(file, replacement);
            else
            {
                file.Info = replacement;
                file.Archived.SafeAddFirst(replacement);
            }

            if (unlocked)
                Storages.UnlockFile(Core.UserID, ProjectID, dir, file.Info, false, errors);
        }

        public void ReplaceFolder(string path, StorageFolder replacement, bool setModified)
        {
            LocalFolder folder = GetLocalFolder(path);

            string oldPath = RootPath + path;

            // only create new entry for un-modified file
            if (setModified)
                ReadyChange(folder, replacement);
            else
            {
                folder.Info = replacement;
                folder.Archived.SafeAddFirst(replacement);
            }

            // rename folder on drive if it exists to match new folder
            try
            {
                if (Directory.Exists(oldPath))
                    if (Path.GetFileName(oldPath) != folder.Info.Name)
                    {
                        Directory.Move(oldPath, RootPath + folder.GetPath());
                        folder.Info.SetFlag(StorageFlags.Unlocked);
                    }
            }
            catch { }

            Storages.CallFolderUpdate(ProjectID, folder.Parent.GetPath(), folder.Info.UID, WorkingChange.Updated);
        }

        public void TrackFolder(string path)
        {
            Debug.Assert(path[path.Length - 1] != Path.DirectorySeparatorChar);

            string name = Path.GetFileName(path);

            string parentPath = Utilities.StripOneLevel(path);

            LocalFolder folder = GetLocalFolder(path);

            if (folder != null)
                return;

            LocalFolder parent = GetLocalFolder(parentPath);

            if(parent == null)
                return;

            folder = CreateNewFolder(parent, name);
            folder.Info.SetFlag(StorageFlags.Modified);

            parent.AddFolder(folder);

            if (Directory.Exists(RootPath + parentPath + Path.DirectorySeparatorChar + name))
                folder.Info.SetFlag(StorageFlags.Unlocked);

            Modified = true;
            PeriodicSave = true;

            Storages.CallFolderUpdate(ProjectID, parentPath, folder.Info.UID, WorkingChange.Created);   
        }

        public void TrackFolder(string path, StorageFolder track)
        {
            string parentPath = Utilities.StripOneLevel(path);

            LocalFolder folder = GetLocalFolder(path);

            if (folder != null)
                return;

            LocalFolder parent = GetLocalFolder(parentPath);

            if (parent == null)
                return;

            folder = new LocalFolder(parent, track);
            folder.Archived.SafeAddFirst(track);
            folder.Info.SetFlag(StorageFlags.Modified);

            parent.AddFolder(folder);

            if (Directory.Exists(RootPath + parentPath + Path.DirectorySeparatorChar + folder.Info.Name))
                folder.Info.SetFlag(StorageFlags.Unlocked);

            Modified = true;
            PeriodicSave = true;

            Storages.CallFolderUpdate(ProjectID, parentPath, folder.Info.UID, WorkingChange.Created);
        }

        // Define the event handlers.
        private void OnFileChanged(object source, FileSystemEventArgs e)
        {
            if (Core.InvokeRequired)
            {
                Core.RunInCoreAsync(delegate() { OnFileChanged(source, e); });
                return;
            }

            string directory = Path.GetDirectoryName(e.FullPath);
            string filename = Path.GetFileName(e.FullPath); // e.Name is path from watch root

            directory = directory.Replace(RootPath, "");

            try
            {
                if (Directory.Exists(e.FullPath)) // filter out pure folder changes, will be handled by folder functions
                    return;

                LocalFolder folder = GetLocalFolder(directory);

                if (folder == null)
                    return;

                LocalFile file = folder.GetFile(filename);


                if (e.ChangeType == WatcherChangeTypes.Created)
                {
                    //Debug.WriteLine("File Created " + e.Name);
                }

                if (e.ChangeType == WatcherChangeTypes.Changed)
                {
                    //Debug.WriteLine("File Changed " + e.Name);

                    if (file != null)
                        Storages.MarkforHash(file, e.FullPath, ProjectID, directory);
                }

                if (e.ChangeType == WatcherChangeTypes.Deleted)
                {
                    //Debug.WriteLine("File Deleted " + e.Name);

                    if (file != null)
                        file.Info.RemoveFlag(StorageFlags.Unlocked);
                }

                Storages.CallFileUpdate(ProjectID, directory, file != null ? file.Info.UID : 0, WorkingChange.Updated);
            }
            catch (Exception ex)
            {
                Core.Network.UpdateLog("Storage", "OnFileChanged: " + ex.Message);
            }
        }

        private void OnFileRenamed(object source, RenamedEventArgs e)
        {
            //Debug.WriteLine("File Renamed " + e.OldName + " to " + e.Name);
            if (Core.InvokeRequired)
            {
                Core.RunInCoreAsync(delegate() { OnFileRenamed(source, e); });
                return;
            }

            try
            {
                string oldName = Path.GetFileName(e.OldFullPath);
                string newName = Path.GetFileName(e.FullPath);

                LocalFolder folder = GetLocalFolder(Path.GetDirectoryName(e.OldFullPath));

                if (folder == null)
                    return;

                
                LocalFile file = folder.GetFile(oldName);

                if (file != null)
                {
                    ReadyChange(file);
                    file.Info.Name = newName;
                    file.Info.Note = "from " + oldName + " to " + newName;
                }

                // when visual studio saves a file, it creates a temp, writes to it, deletes the original file and renames the temp to the original
                // as opposed to files being created with the same name while their couterparts remain locked; renaming triggers the file to be
                // unlocked and rehashed
                else
                {
                    file = folder.GetFile(newName);

                    if (file != null)
                    {
                        file.Info.SetFlag(StorageFlags.Unlocked);
                        Storages.MarkforHash(file, e.FullPath, ProjectID, folder.GetPath());
                    }
                }

                Storages.CallFileUpdate(ProjectID, folder.GetPath(), file != null ? file.Info.UID : 0, WorkingChange.Updated);
            }
            catch (Exception ex)
            {
                Core.Network.UpdateLog("Storage", "OnFileRenamed: " + ex.Message);
            }
        }

        private void OnFolderChanged(object source, FileSystemEventArgs e)
        {
            if (Core.InvokeRequired)
            {
                Core.RunInCoreAsync(delegate() { OnFolderChanged(source, e); });
                return;
            }

            try
            {
                string directory = e.FullPath.Replace(RootPath, "");
                string dirname = Path.GetFileName(e.FullPath);

                string parent = Utilities.StripOneLevel(directory);

                // no matter what happens here its an update, create / delete only happens when working directly changed

                LocalFolder folder = null;

                if (e.ChangeType == WatcherChangeTypes.Created)
                {
                }

                if (e.ChangeType == WatcherChangeTypes.Deleted)
                {
                    folder = GetLocalFolder(e.FullPath);

                    if (folder != null)
                    {
                        List<LockError> errors = new List<LockError>();
                        LockFolder(folder.GetPath(), folder, true, errors);
                    }
                }

                Storages.CallFolderUpdate(ProjectID, parent, folder != null ? folder.Info.UID : 0, WorkingChange.Updated);
            }
            catch (Exception ex)
            {
                Core.Network.UpdateLog("Storage", "OnFolderChanged: " + ex.Message);
            }
        }

        private void OnFolderRenamed(object source, RenamedEventArgs e)
        {
            if (Core.InvokeRequired)
            {
                Core.RunInCoreAsync(delegate() { OnFolderRenamed(source, e); });
                return;
            }

            try
            {
                string newName = Path.GetFileName(e.FullPath);
                string oldName = Path.GetFileName(e.OldFullPath);

                string parent = e.OldFullPath.Replace(RootPath, "");
                parent = Utilities.StripOneLevel(parent);

                LocalFolder folder = GetLocalFolder(e.OldFullPath);

                if (folder != null)
                {
                    ReadyChange(folder);
                    folder.Info.Name = newName;
                    folder.Info.Note = "from " + oldName + " to " + newName;
                }

                Storages.CallFolderUpdate(ProjectID, parent, folder.Info.UID, WorkingChange.Updated);
            }
            catch (Exception ex)
            {
                Core.Network.UpdateLog("Storage", "OnFolderRenamed: " + ex.Message);
            }
        }

        public LocalFolder GetLocalFolder(string path)
        {
            path = path.Replace(RootPath, "");


            // path: \a\b\c

            string[] folders = path.Split(Path.DirectorySeparatorChar);

            LocalFolder current = RootFolder;

            foreach (string name in folders)
                if (name != "")
                {
                    bool found = false;

                    current.Folders.LockReading(delegate()
                    {
                        foreach (LocalFolder sub in current.Folders.Values)
                            if (string.Compare(name, sub.Info.Name, true) == 0)
                            {
                                found = true;
                                current = sub;
                                break;
                            }
                    });

                    if (!found)
                        return null;
                }

            return current;
        }


        public LocalFile GetLocalFile(string path)
        {
            string name = Path.GetFileName(path);

            path = Utilities.StripOneLevel(path);

            LocalFolder folder = GetLocalFolder(path);

            if (folder == null)
                return null;

            return folder.GetFile(name);
        }

        public void ReadyChange(LocalFile file)
        {
            Modified = true;
            PeriodicSave = true;

            file.Modify(Core.TimeNow, file.Info.Clone());
        }

        public void ReadyChange(LocalFile file, StorageFile newInfo)
        {
            Modified = true;
            PeriodicSave = true;

            file.Modify(Core.TimeNow, newInfo);
        }

        public void ReadyChange(LocalFolder folder)
        {
            Modified = true;
            PeriodicSave = true;

            folder.Modify(Core.TimeNow, folder.Info.Clone());
        }

        public void ReadyChange(LocalFolder folder, StorageFolder newInfo)
        {
            Modified = true;
            PeriodicSave = true;

            folder.Modify(Core.TimeNow, newInfo);
        }

        private LocalFolder CreateNewFolder(LocalFolder parent, string dirname )
        {
            StorageFolder info = new StorageFolder();
            LocalFolder folder = new LocalFolder(parent, info);

            info.UID = Utilities.StrongRandUInt64(Core.StrongRndGen);
            info.ParentUID = parent.Info.UID;
            info.Name = dirname;
            info.Date = Core.TimeNow.ToUniversalTime();
            info.Revs = 5;

            folder.Archived.SafeAddFirst(info);

            return folder;
        }

        private LocalFile CreateNewFile(string name)
        {
            StorageFile info = new StorageFile();
            LocalFile file = new LocalFile(info);

            info.UID = Utilities.StrongRandUInt64(Core.StrongRndGen);
            info.Name = name;
            info.Date = Core.TimeNow.ToUniversalTime();
            info.Revs = 5;

            file.Archived.SafeAddFirst(info);

            return file;
        }

        public void WriteWorkingFile(CryptoStream stream, LocalFolder folder, bool commit)
        {
            // write files and all archives if tracked
            folder.Files.LockReading(delegate()
            {
                foreach (LocalFile file in folder.Files.Values)
                {
                    // history
                    int count = 0;
                    file.Archived.LockReading(delegate()
                    {
                        foreach (StorageFile archive in file.Archived)
                        {
                            if (file.Info.HashID == 0 || file.Info.Hash == null)
                                continue; // happens if file is still being hashed and auto-save is called

                            if (commit)
                            {
                                archive.RemoveFlag(StorageFlags.Modified);

                                if (count == file.Info.Revs)
                                    break;
                            }

                            Protocol.WriteToFile(archive, stream);
                            count++;
                        }
                    });

                    // integrated
                    file.Integrated.LockReading(delegate()
                    {
                        foreach (ulong who in file.Integrated.Keys)
                        {
                            StorageFile integrated = (StorageFile)file.Integrated[who];

                            if (commit)
                                integrated.RemoveFlag(StorageFlags.Modified);

                            integrated.IntegratedID = who;
                            Protocol.WriteToFile(integrated, stream);
                        }
                    });
                }
            });

            // foreach tracked folder, recurse
            folder.Folders.LockReading(delegate()
            {
                foreach (LocalFolder sub in folder.Folders.Values)
                {
                    // history
                    int count = 0;
                    sub.Archived.LockReading(delegate()
                    {
                        foreach (StorageFolder archive in sub.Archived)
                        {
                            if (commit)
                            {
                                archive.RemoveFlag(StorageFlags.Modified);

                                if (count == sub.Info.Revs)
                                    break;
                            }

                            Protocol.WriteToFile(archive, stream);
                            count++;
                        }
                    });

                    // integrated
                    sub.Integrated.LockReading(delegate()
                    {
                        foreach (StorageFile change in sub.Integrated.Values)
                        {
                            if (commit)
                                change.RemoveFlag(StorageFlags.Modified);

                            Protocol.WriteToFile(change, stream);
                        }
                    });

                    WriteWorkingFile(stream, sub, commit);
                }
            });
        }

        public void LockAll(List<LockError> errors )
        {
            // make sure files/folder we care about are deleted

            LockFolder("", RootFolder, true, errors);
        }

        public void LockFolder(string dirpath, LocalFolder folder, bool subs, List<LockError> errors )
        {
            folder.Files.LockReading(delegate()
            {
                foreach (LocalFile file in folder.Files.Values)
                    Storages.LockFileCompletely(Core.UserID, ProjectID, dirpath, file.Archived, errors);
            });

            folder.Info.RemoveFlag(StorageFlags.Unlocked);

            if (subs)
                folder.Folders.LockReading(delegate()
                {
                    foreach (LocalFolder subfolder in folder.Folders.Values)
                        LockFolder(dirpath + Path.DirectorySeparatorChar + subfolder.Info.Name, subfolder, subs, errors);
                });

            try
            {
                string path = RootPath + dirpath;

                if (Directory.Exists(path) &&
                    Directory.GetDirectories(path).Length == 0 &&
                    Directory.GetFiles(path).Length == 0)
                    Directory.Delete(path, true);
            }
            catch { }

            Storages.CallFolderUpdate(ProjectID, Utilities.StripOneLevel(dirpath), folder.Info.UID, WorkingChange.Updated);
        }


        public void SetFileDetails(string path, string newName, Dictionary<ulong, short> scope)
        {
            if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) != -1)
                throw new Exception("Name Contains Invalid Characters");

            // check that scope includes self
            if (scope.Count > 0 && !Core.Trust.IsInScope(scope, Core.UserID, ProjectID))
                throw new Exception("Visibility Must Include Yourself");
                

            string oldName = Path.GetFileName(path);
            path = Utilities.StripOneLevel(path);

            // get local file / folder
            LocalFolder folder = GetLocalFolder(path);
            LocalFile file = folder.GetFile(oldName);

            bool renamed = file.Info.Name.CompareTo(newName) != 0;
            bool scopeChanged = Storages.ScopeChanged(scope, file.Info.Scope);

            // if unlocked try to rename
            if (renamed && file.Info.IsFlagged(StorageFlags.Unlocked))
                File.Move(RootPath + Path.DirectorySeparatorChar + path + Path.DirectorySeparatorChar + oldName, RootPath + Path.DirectorySeparatorChar + path + Path.DirectorySeparatorChar + newName);

            
            if (!renamed && !scopeChanged)
                return;

            // apply changes
            ReadyChange(file);

            if (renamed)
            {
                file.Info.Note = "from " + file.Info.Name + " to " + newName;
                file.Info.Name = newName;
            }

            if (scopeChanged)
                file.Info.Scope = scope;

            Storages.CallFileUpdate(ProjectID, path, file.Info.UID, WorkingChange.Updated);
        }


        public void SetFolderDetails(string path, string newName, Dictionary<ulong, short> scope)
        {
            if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) != -1)
                throw new Exception("Name Contains Invalid Characters");

            // check that scope includes self
            if (scope.Count > 0 && !Core.Trust.IsInScope(scope, Core.UserID, ProjectID))
                throw new Exception("Visibility Must Include Yourself");

            string oldName = Path.GetFileName(path);
            string parentPath = Utilities.StripOneLevel(path);
       
            // get local file / folder
            LocalFolder folder = GetLocalFolder(path);

            bool renamed = folder.Info.Name.CompareTo(newName) != 0;
            bool scopeChanged = Storages.ScopeChanged(scope, folder.Info.Scope);

            // if unlocked try to rename
            if (renamed && folder.Info.IsFlagged(StorageFlags.Unlocked))
                Directory.Move(RootPath + Path.DirectorySeparatorChar + parentPath + Path.DirectorySeparatorChar + oldName, RootPath + Path.DirectorySeparatorChar + parentPath + Path.DirectorySeparatorChar + newName);

            if (!renamed && !scopeChanged)
                return;

            // apply changes
            ReadyChange(folder);
           
            if (renamed)
            {
                folder.Info.Note = "from " + folder.Info.Name + " to " + newName;
                folder.Info.Name = newName;
            }

            if (scopeChanged)
                folder.Info.Scope = scope;

            Storages.CallFolderUpdate(ProjectID, parentPath, folder.Info.UID, WorkingChange.Updated);
        }

        public void ArchiveFile(string path, string name)
        {
            LocalFolder folder = GetLocalFolder(path);
            LocalFile file = folder.GetFile(name);
 
            // remove unlocked flag
            file.Info.RemoveFlag(StorageFlags.Unlocked);

            ReadyChange(file);
            file.Info.SetFlag(StorageFlags.Archived);

            Storages.CallFileUpdate(ProjectID, path, file.Info.UID, WorkingChange.Updated);
        }

        public void IntegrateFile(string path, ulong who, StorageFile change)
        {
            // put file in local's integration map for this user id

            LocalFolder folder = GetLocalFolder(path);
            LocalFile file = null;
            
            if(!folder.Files.SafeTryGetValue(change.UID, out file))
                return;

            file.Integrated.SafeAdd(who, change.Clone()); 
            // dont set file.info modified, because hash hasn't changed

            Modified = true;
            PeriodicSave = true;

            Storages.CallFileUpdate(ProjectID, path, file.Info.UID, WorkingChange.Updated);
        }

        public void UnintegrateFile(string path, ulong who, StorageFile change)
        {
            // put file in local's integration map for this user id

            LocalFolder folder = GetLocalFolder(path);
            LocalFile file = null;

            if (!folder.Files.SafeTryGetValue(change.UID, out file))
                return;

            file.Integrated.SafeRemove(who);

            Modified = true;
            PeriodicSave = true;

            Storages.CallFileUpdate(ProjectID, path, file.Info.UID, WorkingChange.Updated);
        }


        public void DeleteFile(string path, string name)
        {
            LocalFolder folder = GetLocalFolder(path);
            LocalFile file = folder.GetFile(name);

            folder.Files.SafeRemove(file.Info.UID);

            Modified = true;
            PeriodicSave = true;

            Storages.CallFileUpdate(ProjectID, path, file.Info.UID, WorkingChange.Removed);
        }

        public void ArchiveFolder(string path)
        {
            LocalFolder folder = GetLocalFolder(path);

            // remove unlocked flag
            folder.Info.RemoveFlag(StorageFlags.Unlocked);

            ReadyChange(folder);
            folder.Info.SetFlag(StorageFlags.Archived);

            Storages.CallFolderUpdate(ProjectID, Utilities.StripOneLevel(path), folder.Info.UID, WorkingChange.Updated);
        }

        public void DeleteFolder(string path)
        {
            LocalFolder folder = GetLocalFolder(path);

            folder.Parent.Folders.SafeRemove(folder.Info.UID);

            Modified = true;
            PeriodicSave = true;

            Storages.CallFolderUpdate(ProjectID, Utilities.StripOneLevel(path), folder.Info.UID, WorkingChange.Removed);
        }

        public void RestoreFolder(string path)
        {
            LocalFolder folder = GetLocalFolder(path);

            // remove unlocked flag
            folder.Info.RemoveFlag(StorageFlags.Unlocked);

            ReadyChange(folder);
            folder.Info.RemoveFlag(StorageFlags.Archived);

            Storages.CallFolderUpdate(ProjectID, Utilities.StripOneLevel(path), folder.Info.UID, WorkingChange.Updated);
        }

        public void RestoreFile(string path, string name)
        {
            LocalFolder folder = GetLocalFolder(path);
            LocalFile file = folder.GetFile(name);

            // remove unlocked flag
            file.Info.RemoveFlag(StorageFlags.Unlocked);

            ReadyChange(file);
            file.Info.RemoveFlag(StorageFlags.Archived);

            Storages.CallFileUpdate(ProjectID, path, file.Info.UID, WorkingChange.Updated);
        }

        public void SetRevs(string path, bool isFile, byte revs)
        {
            // file
            if (isFile)
            {
                string name = Path.GetFileName(path);
                path = Utilities.StripOneLevel(path);
                LocalFolder folder = GetLocalFolder(path);
                LocalFile file = folder.GetFile(name);

                ReadyChange(file);
                file.Info.Revs = revs;

                Storages.CallFileUpdate(ProjectID, path, file.Info.UID, WorkingChange.Updated);  
            }

            // folder
            else
            {
                LocalFolder folder = GetLocalFolder(path);
                ReadyChange(folder);
                folder.Info.Revs = revs;

                Storages.CallFolderUpdate(ProjectID, Utilities.StripOneLevel(path), folder.Info.UID, WorkingChange.Updated);
            }
        }

        public void SetNote(string path, StorageItem targetItem, bool isFile, string note)
        {
            note = (note.Length == 0) ? null : note;

            LinkedList<StorageItem> archived = null;

            string name = "";

            // file
            if (isFile)
            {
                name = Path.GetFileName(path);
                path = Utilities.StripOneLevel(path);
                LocalFolder folder = GetLocalFolder(path);
                LocalFile file = folder.GetFile(name);

                archived = file.Archived;
            }

            // folder
            else
            {
                LocalFolder folder = GetLocalFolder(path);

                archived = folder.Archived;
            }

            // update
            foreach (StorageItem item in archived)
                if (item == targetItem)
                {
                    item.Note = note;
                    item.SetFlag(StorageFlags.Modified);

                    Modified = true;
                    PeriodicSave = true;
                }

            if(isFile)
                Storages.CallFileUpdate(ProjectID, path, targetItem.UID, WorkingChange.Updated);
            else
                Storages.CallFolderUpdate(ProjectID, Utilities.StripOneLevel(path), targetItem.UID, WorkingChange.Updated);
        }



        public bool RefreshHigherChanges(ulong id)
        {
            // first remove changes from this id
            RemoveHigherChanges(RootFolder, id);


            bool save = false;

            OpStorage storage = Storages.GetStorage(id);

            if (storage == null)
                return save;

            // this is the first step in auto-integration
            // go through this id's storage file, and add any changes or updates to our own system
            string path = Storages.GetFilePath(storage);

            if (!File.Exists(path))
                return save;

            try
            {
                using (TaggedStream filex = new TaggedStream(path, Protocol))
                using (IVCryptoStream crypto = IVCryptoStream.Load(filex, storage.File.Header.FileKey))
                {
                    PacketStream stream = new PacketStream(crypto, Protocol, FileAccess.Read);

                    ulong currentUID = 0;
                    LocalFolder currentFolder = RootFolder;
                    LocalFile currentFile = null;
                    bool ignoreCurrent = false;
                    bool readingProject = false;

                    G2Header header = null;

                    while (stream.ReadPacket(ref header))
                    {
                        if (header.Name == StoragePacket.Root)
                        {
                            StorageRoot root = StorageRoot.Decode(header);

                            readingProject = (root.ProjectID == ProjectID);
                        }

                        if (readingProject)
                        {
                            if (header.Name == StoragePacket.Folder)
                            {
                                StorageFolder readFolder = StorageFolder.Decode(header);


                                // if new uid
                                if (currentUID != readFolder.UID)
                                {
                                    // if only 1 entry in changes for previous file, remove it, its probably a dupe of local
                                    // and integration needs more than one file to happen
                                    if (currentFolder.HigherChanges.ContainsKey(id) && currentFolder.HigherChanges[id].Count == 1)
                                        currentFolder.HigherChanges.Remove(id);

                                    // set new id
                                    currentUID = readFolder.UID;

                                    // check scope
                                    ignoreCurrent = false;
                                    if (readFolder.Scope.Count > 0 && !Core.Trust.IsInScope(readFolder.Scope, Core.UserID, ProjectID))
                                        ignoreCurrent = true;


                                    bool added = false;

                                    while (!added)
                                    {
                                        if (currentFolder.Info.UID == readFolder.ParentUID)
                                        {
                                            LocalFolder subFolder = null;
                                            if (currentFolder.Folders.SafeTryGetValue(currentUID, out subFolder))
                                                currentFolder = subFolder;

                                            else
                                            {
                                                // if ignoring, add folder so we can traverse id's file, but dont save changes to local storage mapping
                                                if (ignoreCurrent)
                                                {
                                                    currentFolder = new LocalFolder(currentFolder, readFolder);
                                                    break;
                                                }

                                                // check for conflicting name
                                                currentFolder.Folders.LockReading(delegate()
                                                {
                                                    foreach (LocalFolder subfolder in currentFolder.Folders.Values)
                                                        if (!subfolder.Info.IsFlagged(StorageFlags.Archived) && subfolder.Info.Name == readFolder.Name)
                                                            subfolder.Info.Name = subfolder.Info.Name + ".fix";
                                                });

                                                // if not found, create folder
                                                currentFolder = currentFolder.AddFolderInfo(readFolder);
                                                save = true;
                                            }

                                            added = true;
                                        }

                                        else if (currentFolder.Parent == null)
                                            break; // error, couldn't find parent of folder that was read

                                        else if (currentFolder.Parent.GetType() == typeof(LocalFolder))
                                            currentFolder = currentFolder.Parent;

                                        else
                                            break;
                                    }
                                }

                                // if file does not equal null
                                if (currentFolder != null && !ignoreCurrent)
                                {
                                    // log change if file newer than ours
                                    // if if not in higher's history 
                                    // or if file integrated by higher by a node we would have inherited from

                                    // we look for our own file in higher changes, if there then we can auto integrate

                                    if (readFolder.Date >= currentFolder.Info.Date)
                                        if (readFolder.IntegratedID == 0 ||
                                            readFolder.IntegratedID == Core.UserID ||
                                            Core.Trust.IsAdjacent(readFolder.IntegratedID, ProjectID))
                                            currentFolder.AddHigherChange(id, readFolder);
                                }

                            }

                            if (header.Name == StoragePacket.File)
                            {
                                StorageFile readFile = StorageFile.Decode(header);

                                // if new uid
                                if (currentUID != readFile.UID)
                                {
                                    // if only 1 entry in changes for previous file, remove it, its probably a dupe of local
                                    // and integration needs more than one file to happen
                                    if (currentFile != null && currentFile.HigherChanges.ContainsKey(id) && currentFile.HigherChanges[id].Count == 1)
                                        currentFile.HigherChanges.Remove(id);

                                    // set new id
                                    currentUID = readFile.UID;

                                    currentFile = null;

                                    // check scope
                                    ignoreCurrent = false;
                                    if (readFile.Scope.Count > 0 && !Core.Trust.IsInScope(readFile.Scope, Core.UserID, ProjectID))
                                    {
                                        ignoreCurrent = true;
                                        continue;
                                    }

                                    // if file exists with UID, else add file as temp, mark as changed
                                    if (!currentFolder.Files.SafeTryGetValue(currentUID, out currentFile))
                                    {
                                        // check for conflicting name
                                        currentFolder.Files.LockReading(delegate()
                                        {
                                            foreach (LocalFile checkFile in currentFolder.Files.Values)
                                                if (!checkFile.Info.IsFlagged(StorageFlags.Archived) && checkFile.Info.Name == readFile.Name)
                                                    checkFile.Info.Name = checkFile.Info.Name + ".fix";
                                        });

                                        currentFile = currentFolder.AddFileInfo(readFile);
                                        save = true;

                                        if (!Storages.FileExists(currentFile.Info))
                                            Storages.DownloadFile(id, currentFile.Info);
                                    }
                                }

                                // if file does not equal null
                                if (currentFile != null && !ignoreCurrent)
                                {
                                    if (readFile.Date >= currentFile.Info.Date)
                                        if (readFile.IntegratedID == 0 ||
                                            readFile.IntegratedID == Core.UserID ||
                                            Core.Trust.IsAdjacent(readFile.IntegratedID, ProjectID))
                                            currentFile.AddHigherChange(id, readFile);
                                }

                            }
                        }
                    }
                }
            }
            catch
            {

            }

            return save;
        }

        public void RemoveAllHigherChanges()
        {
            RemoveHigherChanges(RootFolder, 0);
        }

        private void RemoveHigherChanges(LocalFolder folder, ulong id)
        {
            folder.Files.LockReading(delegate()
            {
                foreach (LocalFile file in folder.Files.Values)
                {
                    if (id == 0)
                        file.HigherChanges.Clear();

                    else if (file.HigherChanges.ContainsKey(id))
                        file.HigherChanges.Remove(id);
                }
            });

            folder.Folders.LockReading(delegate()
            {
                foreach (LocalFolder subfolder in folder.Folders.Values)
                {
                    if (id == 0)
                        subfolder.HigherChanges.Clear();

                    else if (subfolder.HigherChanges.ContainsKey(id))
                        subfolder.HigherChanges.Remove(id);

                    RemoveHigherChanges(subfolder, id);
                }
            });
        }

        List<ulong> InheritIDs;

        public void AutoIntegrate(bool doSave)
        {
            // only 'save' if file system in a saved state
			// still merge with unsaved file system, just won't be made permanent till save is clicked

            // we don't inherit from all nodes in loop because infinite loop could form where we continually
            // inherit different changes, it is just asking for problems, diffs still show up for user
            // with all changes in loop, but only auto-integrate from our own parent in the loop

            InheritIDs = new List<ulong>();
            InheritIDs.Add(Core.UserID);
            InheritIDs.AddRange(Core.Trust.GetAutoInheritIDs(Core.UserID, ProjectID));


            List<LockError> errors = new List<LockError>();

            if (AutoIntegrate(RootFolder, errors) || doSave)
                if (!Modified)
                    Storages.SaveLocal(ProjectID); // triggers permanent save of system for publish on the network
                else
                    PeriodicSave = true; // triggers save of working file system
        }

        private bool AutoIntegrate(LocalFolder folder, List<LockError> errors)
        {
            bool save = false;

            if (!folder.Info.IsFlagged(StorageFlags.Modified))
            {
                StorageFolder latestFolder = folder.Info;

                // this will process will find higher has integrated my file
                // and highest has integrated his file, and return latest

                // from self to highest
                foreach (ulong id in InheritIDs)
                    if (folder.HigherChanges.ContainsKey(id))
                        // higherChanges consists of files that are newer than local
                        foreach (StorageFolder changeFolder in folder.HigherChanges[id])
                            if (changeFolder.Date >= latestFolder.Date && Storages.ItemDiff(latestFolder, changeFolder) == StorageActions.None)
                            {
                                latestFolder = (StorageFolder)folder.HigherChanges[id][0]; // first element is newest file
                                break;
                            }

                // if current file/folder is not our own (itemdiff)
                if (Storages.ItemDiff(latestFolder, folder.Info) != StorageActions.None)
                {
                    ReplaceFolder(folder.GetPath(), latestFolder, false);

                    save = true;
                }
            }

            string dir = folder.GetPath();

            folder.Files.LockReading(delegate()
            {
                foreach (LocalFile file in folder.Files.Values)
                    if (AutoIntegrate(file, dir, errors))
                        save = true;
            });

            folder.Folders.LockReading(delegate()
            {
                foreach (LocalFolder subfolder in folder.Folders.Values)
                    if (AutoIntegrate(subfolder, errors))
                        save = true;
            });

            return save;
        }

        private bool AutoIntegrate(LocalFile file, string dir, List<LockError> errors)
        {
            // If file/folder not flagged as modified
            if(file.Info.IsFlagged(StorageFlags.Modified))
                return false;

            StorageFile latestFile = file.Info;
            List<StorageFile> inheritIntegrated = new List<StorageFile>();

            ulong directHigher = (InheritIDs.Count >= 2) ? InheritIDs[1] : 0;


            // this process will find higher has integrated my file
            // and highest has integrated his file, and return latest
            
            // from self to highest
            foreach (ulong id in InheritIDs)
                if (file.HigherChanges.ContainsKey(id))
                    // higherChanges consists of files that are newer than local
                    foreach (StorageFile changeFile in file.HigherChanges[id])
                    {
                        if (changeFile.Date >= latestFile.Date && Storages.ItemDiff(latestFile, changeFile) == StorageActions.None)
                        {
                            latestFile = (StorageFile)file.HigherChanges[id][0]; // first element is newest file

                            if (id != directHigher)
                                break;
                        }

                        if (id == directHigher &&
                            changeFile.IntegratedID != 0 &&
                            Core.Trust.IsAdjacent(changeFile.IntegratedID, ProjectID))
                            inheritIntegrated.Add(changeFile);
                    }

            // if current file/folder is not our own (itemdiff)
            bool save = false;

            if (Storages.ItemDiff(latestFile, file.Info) != StorageActions.None)
            {
                ReplaceFile(file, latestFile, dir , false, errors);

                save = true;
            }

            // merges integration list for nodes adjacent to ourselves
            // works even if higher integrates more files, but doesn't necessarily change the file's hash
            StorageItem prevIntegrated = null;

            foreach (StorageFile inherited in inheritIntegrated)
            {
                file.Integrated.SafeTryGetValue(inherited.IntegratedID, out prevIntegrated);

                if (prevIntegrated == null || inherited.Date > prevIntegrated.Date)
                {
                    file.Integrated.SafeAdd(inherited.IntegratedID, inherited);
                    save = true;
                }
            }

            return save;
        }


        public void MoveFile(string sourcePath, string destPath, List<string> errors)
        {
            // get source folder
            string name = Path.GetFileName(sourcePath);

            sourcePath = Utilities.StripOneLevel(sourcePath);

            LocalFolder sourceFolder = GetLocalFolder(sourcePath);
            LocalFolder destFolder = GetLocalFolder(destPath);

            if (sourceFolder == null || destFolder == null || sourceFolder == destFolder)
                return;

            // get source file
            LocalFile sourceFile = sourceFolder.GetFile(name);

            if (sourceFile == null)
                return;

            // if name exists with diff uid, return error
            bool exists = false;

            destFolder.Files.LockReading(delegate()
            {
                foreach (LocalFile check in destFolder.Files.Values)
                    if (check.Info.UID != sourceFile.Info.UID &&
                        String.Compare(check.Info.Name, sourceFile.Info.Name, true) == 0)
                    {
                        errors.Add("File with same name exists at " + destPath);
                        exists = true;
                    }
            });

            if (exists)
                return;

            // if uid exists in destination, merge histories with diff hashes
            WorkingChange destChange = WorkingChange.Created;
            WorkingChange sourceChange = WorkingChange.Updated;

            LocalFile mergeFile = null;

            if (destFolder.Files.SafeTryGetValue(sourceFile.Info.UID, out mergeFile))
            {
                sourceFile.MergeFile(mergeFile);
                destFolder.Files.SafeAdd(sourceFile.Info.UID, sourceFile);
                destChange = WorkingChange.Updated;
            }
            else
                destFolder.AddFile(sourceFile);

            // make note file was moved at source in destination
            LocalFile ghost = new LocalFile(sourceFile.Info.Clone()); // do before modified/new date set
            ReadyChange(sourceFile);
            sourceFile.Info.Note = "Moved from " + (sourcePath == "" ? Path.DirectorySeparatorChar.ToString() : sourcePath);

            sourceFolder.Files.SafeRemove(sourceFile.Info.UID);
            
            // only leave a ghost if this file has a committed history
            if (sourceFile.Archived.SafeCount > 1 || !sourceFile.Info.IsFlagged(StorageFlags.Modified))
            {
                ghost.Archived.SafeAddFirst(ghost.Info);
                sourceFolder.AddFile(ghost);
                ReadyChange(ghost);
                ghost.Info.Note = "Moved to " + (destPath == "" ? Path.DirectorySeparatorChar.ToString() : destPath);
                ghost.Info.SetFlag(StorageFlags.Archived);
            }
            else
                sourceChange = WorkingChange.Removed;

            // move actual file if unlocked on disk, create new folder if need be
            if (File.Exists(RootPath + sourcePath + Path.DirectorySeparatorChar + name))
            {
                Directory.CreateDirectory(RootPath + destPath);

                // exceptions handled by caller
                File.Move(  RootPath + sourcePath + Path.DirectorySeparatorChar + name,
                            RootPath + destPath + Path.DirectorySeparatorChar + name);
            }

            // file created at destination, updated at source
            Storages.CallFileUpdate(ProjectID, destPath, sourceFile.Info.UID,  destChange);
            Storages.CallFileUpdate(ProjectID, sourcePath, sourceFile.Info.UID, sourceChange);
        }


        public void MoveFolder(LocalFolder sourceFolder, string destPath, List<string> errors)
        {
            // cant move root folder ;)
            if (sourceFolder.Parent == null)
                return;

            LocalFolder parentFolder = sourceFolder.Parent;

            LocalFolder destFolder = GetLocalFolder(destPath);

            if (destFolder == null || destFolder == parentFolder)
                return;

            // prevent folder from being moved inside of itself
            LocalFolder oneUp = destFolder;
            while (oneUp != null)
            {
                if (oneUp == sourceFolder)
                    return;

                oneUp = oneUp.Parent;
            }


            // if name exists with diff uid, return error
            destFolder.Folders.LockReading(delegate()
            {
                foreach (LocalFolder check in destFolder.Folders.Values)
                    if (check.Info.UID != sourceFolder.Info.UID &&
                        String.Compare(check.Info.Name, sourceFolder.Info.Name, true) == 0)
                    {
                        errors.Add("Folder with same name exists at " + destPath);
                        return;
                    }
            });

            // if uid exists in destination, merge histories with diff hashes
            WorkingChange destChange = WorkingChange.Created;
            WorkingChange sourceChange = WorkingChange.Updated;

            if (destFolder.Folders.SafeContainsKey(sourceFolder.Info.UID))
            {
                destFolder.Folders.SafeAdd(sourceFolder.Info.UID, sourceFolder);
                destChange = WorkingChange.Updated;
            }
            else
                destFolder.AddFolder(sourceFolder);


            // make note file was moved at source in destination
            LocalFolder ghost = new LocalFolder(parentFolder, sourceFolder.Info.Clone());

            ReadyChange(sourceFolder);
            sourceFolder.Parent = destFolder;
            string parentPath = parentFolder.GetPath();
            sourceFolder.Info.Note = "Moved from " + (parentPath == "" ? Path.DirectorySeparatorChar.ToString() : parentPath);
            sourceFolder.Info.ParentUID = destFolder.Info.UID;

            parentFolder.Folders.SafeRemove(sourceFolder.Info.UID);

            // only leave a ghost if this file has a committed history
            if (sourceFolder.Archived.SafeCount > 1 || !sourceFolder.Info.IsFlagged(StorageFlags.Modified))
            {
                ghost.Archived.SafeAddFirst(ghost.Info);
                parentFolder.AddFolder(ghost);
                ReadyChange(ghost); // created ghost needs to have 2 entries because applydiff() looks for date of previous file to apply change
                ghost.Info.Note = "Moved to " + (destPath == "" ? Path.DirectorySeparatorChar.ToString() : destPath);
                ghost.Info.SetFlag(StorageFlags.Archived);
            }
            else
                sourceChange = WorkingChange.Removed;

            // move actual file if unlocked on disk, create new folder if need be
            string name = sourceFolder.Info.Name;
            if (File.Exists(RootPath + parentFolder.GetPath() + Path.DirectorySeparatorChar + name))
            {
                Directory.CreateDirectory(RootPath + destPath);

                // exceptions handled by caller
                File.Move(RootPath + parentFolder.GetPath() + Path.DirectorySeparatorChar + name,
                            RootPath + destPath + Path.DirectorySeparatorChar + name);
            }

            // file created at destination, updated at source
            Storages.CallFolderUpdate(ProjectID, destPath, sourceFolder.Info.UID, destChange);
            Storages.CallFolderUpdate(ProjectID, parentFolder.GetPath(), sourceFolder.Info.UID, sourceChange);
        }


        public void CreateDirectory(string path)
        {
            string[] destDirs = path.Split(Path.DirectorySeparatorChar);

            LocalFolder destFolder = RootFolder;

            foreach (string dir in destDirs)
            {
                bool notFound = true;

                foreach (LocalFolder folder in destFolder.Folders.Values)
                    if (folder.Info.Name == dir)
                    {
                        destFolder = folder;
                        notFound = false;
                        break;
                    }

                if (notFound)
                {
                    LocalFolder newFolder = CreateNewFolder(destFolder, dir);
                    destFolder.AddFolder(newFolder);
                    destFolder = newFolder;
                }
            }
        }



        public void ImportFiles(string source, string destination, List<string> errors)
        {
            if (Core.InvokeRequired)
            {
                Core.RunInCoreAsync(() => ImportFiles(source, destination, errors));
                return;
            }

            try
            {
                // move secure folder or file
                if (source.StartsWith(RootPath))
                {
                    string securePath = source.Replace(RootPath, "");

                    LocalFolder folder = GetLocalFolder(securePath);

                    // secure folder
                    if (folder != null)
                    {
                        if (folder.Info.IsFlagged(StorageFlags.Archived))
                            return;

                        MoveFolder(folder, destination, errors);
                    }

                    // secure file
                    LocalFile file = GetLocalFile(securePath);

                    if (file != null)
                    {
                        if (file.Info.IsFlagged(StorageFlags.Archived))
                            return;

                        MoveFile(securePath, destination, errors);
                    }
                }

                // move local folder or file
                string finalPath = destination + Path.DirectorySeparatorChar + Path.GetFileName(source);

                if (Directory.Exists(source))
                {
                    //crit allow overwrite if files exist in spot
                    // if on file sys erased
                    // new entries made in file item's history

                    string fullDestination = RootPath + destination;

                    if (!fullDestination.Contains(source)) // dont let folder move into itself
                        CopyDiskDirectory(source, finalPath, errors);
                }
                else if (File.Exists(source))
                {
                    Directory.CreateDirectory(RootPath + destination);

                    CopyDiskFile(source, finalPath, errors);
                }
            }
            catch (Exception ex)
            {
                errors.Add("Exception " + ex.Message + " " + source);
            }

            return;
        }

        public void CopyDiskDirectory(string source, string destination, List<string> errors)
        {
            // create folder to copy to
            if (!Directory.Exists(RootPath + destination))
                Directory.CreateDirectory(RootPath + destination);

            TrackFolder(destination); // if already there simply returns

            // add folders and files
            String[] sourceFiles = Directory.GetFileSystemEntries(source);

            foreach (string diskFile in sourceFiles)
            {
                string finalPath = destination + Path.DirectorySeparatorChar + Path.GetFileName(diskFile);

                if (Directory.Exists(diskFile))
                    CopyDiskDirectory(diskFile, finalPath, errors);

                else if (File.Exists(diskFile))
                    CopyDiskFile(diskFile, finalPath, errors);
            }
        }

        public void CopyDiskFile(string source, string destination, List<string> errors)
        {
            if (FileExists(RootPath + destination))
            {
                errors.Add("File " + destination + " already exists in files");
                return;
            }

            Storages.CopyFiles.Enqueue(() =>
            {
                File.Copy(source, RootPath + destination, false);
                
                Core.RunInCoreAsync(() => TrackFile(destination));
            });
        }
    }

    public class LocalFolder
    {
        // keep track of untracked because when locked, this is the only way those files would be seen
        public StorageFolder Info;
        public ThreadedLinkedList<StorageItem> Archived = new ThreadedLinkedList<StorageItem>(); 
        public ThreadedDictionary<ulong, StorageItem> Integrated = new ThreadedDictionary<ulong, StorageItem>();
        public Dictionary<ulong, List<StorageItem>> HigherChanges = new Dictionary<ulong, List<StorageItem>>();

        public LocalFolder Parent;

        public ThreadedDictionary<ulong, LocalFolder> Folders = new ThreadedDictionary<ulong, LocalFolder>();
        public ThreadedDictionary<ulong, LocalFile> Files = new ThreadedDictionary<ulong, LocalFile>(); 

       
        public LocalFolder(LocalFolder parent, StorageFolder info)
        {
            Parent = parent;

            Info = info;
        }

        public void Modify(DateTime time, StorageFolder newInfo)
        {
            if (Info.IsFlagged(StorageFlags.Modified))
            {
                Info = newInfo;
                Archived.SafeRemoveFirst();
                Archived.SafeAddFirst(newInfo);
            }
            else
            {
                Archived.SafeAddFirst(newInfo);
                Info = newInfo;
            }

            Info.SetFlag(StorageFlags.Modified);
            Info.Date = time.ToUniversalTime();
        }

        public string GetPath()
        {
            string path = "";

            LocalFolder up = this;

            while (up.Parent != null)
            {
                path = Path.DirectorySeparatorChar + up.Info.Name + path;
                up = up.Parent;
            }

            return path;
        }

        public LocalFile GetFile(string name)
        {
            LocalFile result = null;

            Files.LockReading(delegate()
            {
                foreach (LocalFile file in Files.Values)
                    if (string.Compare(name, file.Info.Name, true) == 0)
                    {
                        result = file;
                        break;
                    }
            });

            return result;
        }

        public void AddFolder(LocalFolder folder)
        {
            ulong uid = folder.Info.UID;

            Debug.Assert(!Folders.SafeContainsKey(uid));

            Folders.SafeAdd(uid, folder);
        }

        public LocalFolder AddFolderInfo(StorageFolder info)
        {
            LocalFolder folder = null;

            if (!Folders.SafeTryGetValue(info.UID, out folder))
            {
                folder = new LocalFolder(this, info);
                Folders.SafeAdd(info.UID, folder);
            }
     
            // if this is integration info, add to integration map
            if (info.IntegratedID != 0)
                folder.Integrated.SafeAdd(info.IntegratedID, info);

            // if history info, add to files history
            else
                folder.Archived.SafeAddLast(info);   

            return folder;
        }

        public void AddFile(LocalFile file)
        {
            ulong uid = file.Info.UID;

            Debug.Assert(!Files.SafeContainsKey(uid));

            Files.SafeAdd(uid, file);
        }

        public LocalFile AddFileInfo(StorageFile info)
        {
            LocalFile file = null;

            if (!Files.SafeTryGetValue(info.UID, out file))
            {
                file = new LocalFile(info);
                Files.SafeAdd(info.UID, file);
            }
            
            if(info.IntegratedID != 0)
                file.Integrated.SafeAdd(info.IntegratedID, info); 
            else
                file.Archived.SafeAddLast(info);

            return file;
        }

        public override string ToString()
        {
            return Info.Name;
        }

        public void AddHigherChange(ulong id, StorageFolder change)
        {
            if (!HigherChanges.ContainsKey(id))
                HigherChanges[id] = new List<StorageItem>();

            HigherChanges[id].Add(change);
        }
    }

    public class LocalFile
    {
        public StorageFile Info;
        public ThreadedLinkedList<StorageItem> Archived = new ThreadedLinkedList<StorageItem>(); 
        public ThreadedDictionary<ulong, StorageItem> Integrated = new ThreadedDictionary<ulong, StorageItem>();
        public Dictionary<ulong, List<StorageItem>> HigherChanges = new Dictionary<ulong, List<StorageItem>>();

        public LocalFile(StorageFile info)
        {
            Info = info;
        }

        public void Modify(DateTime time, StorageFile newInfo)
        {
            // Details is mirror of file in working folder, it can change multiple times before the user commits, archive the
            //   original info when the user starts messing with stuff, only once, dont want to archive stuff not accessible on the network


            if (Info.IsFlagged(StorageFlags.Modified))
            {
                Info = newInfo;
                Archived.SafeRemoveFirst();
                Archived.SafeAddFirst(newInfo);
            }
            else
            {
                Archived.SafeAddFirst(newInfo);
                Info = newInfo;
            }

            Info.SetFlag(StorageFlags.Modified);
            Info.Date = time.ToUniversalTime();
        }

        public override string ToString()
        {
            return Info.Name;
        }

        public void AddHigherChange(ulong id, StorageFile change)
        {
            if (!HigherChanges.ContainsKey(id))
                HigherChanges[id] = new List<StorageItem>();

            HigherChanges[id].Add(change);
        }


        public void MergeFile(LocalFile file)
        {
            // if there are any unique files in the merge that don't exist locally, add them

            file.Archived.LockReading(delegate()
            {
                foreach (StorageFile merge in file.Archived)
                {
                    // check if exists
                    bool fileExists = false;

                    Archived.LockReading(delegate()
                    {
                        foreach (StorageFile item in Archived)
                            if (Utilities.MemCompare(item.InternalHash, merge.InternalHash))
                            {
                                fileExists = true;
                                break;
                            }
                    });

                    // add file if doesnt exist
                    if (!fileExists)
                    {
                        bool added = false;
                        LinkedListNode<StorageItem> item = null;

                        Archived.LockReading(delegate()
                        {
                            for (item = Archived.First; item != null; item = item.Next)
                                if (item.Value.Date > merge.Date) // loop until item is no longer the lowest (oldest)
                                {
                                    added = true;

                                    break;
                                }
                        });

                        if (added && item != null)
                        {
                            if (item.Value == Info)
                                Archived.SafeAddAfter(item, merge); // even if newest, the file being moved is the one at top
                            else
                                Archived.SafeAddBefore(item, merge); // put file in right spot
                        }
                        else
                            Archived.SafeAddLast(merge); // oldest, at end

                    }
                }
            });
        }
    }

    public enum LockErrorType
    {
        Temp, Blocked,  // lock errors
        Unexpected, Existing, Missing
    }; // unlock errors

    public class LockError
    {
        public string Path;
        public string Message;
        public bool IsFile;
        public LockErrorType Type;

        // special case
        public StorageFile File;
        public bool History;
        public bool Subs;


        public LockError(string path, string message, bool isFile, LockErrorType type)
        {
            Path = path;
            Message = message;
            IsFile = isFile;
            Type = type;
        }

        // used for unexpected (file) and existing errors
        public LockError(string path, string message, bool isFile, LockErrorType type, StorageFile file, bool history)
        {
            Path = path;
            Message = message;
            IsFile = isFile;
            Type = type;
            File = file;
            History = history;
        }
    }

}
