using System;
using System.IO;
using System.Collections.Specialized;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.Gui;

namespace TranslationEplAddIn.Tool
{
    public static class BackupService
    {
        private static readonly string BackupFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EPLAN_TranslationAddIn",
            "Backups"
        );

        private static readonly System.Collections.Generic.List<Tuple<string, string>> LastBackups = new System.Collections.Generic.List<Tuple<string, string>>();

        public static bool CreateBackup(string projectPath)
        {
            if (string.IsNullOrEmpty(projectPath)) return false;

            try
            {
                if (!Directory.Exists(BackupFolder))
                {
                    Directory.CreateDirectory(BackupFolder);
                }

                string projectName = Path.GetFileNameWithoutExtension(projectPath);
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string backupFileName = $"{projectName}_{timestamp}.zw1";
                string backupFilePath = Path.Combine(BackupFolder, backupFileName);

                CommandLineInterpreter interpreter = new CommandLineInterpreter();
                ActionCallingContext context = new ActionCallingContext();
                
                context.AddParameter("TYPE", "PROJECT");
                context.AddParameter("PROJECTNAME", projectPath);
                context.AddParameter("ARCHIVENAME", backupFileName);
                context.AddParameter("DESTINATIONPATH", BackupFolder);
                context.AddParameter("BACKUPMETHOD", "BACKUP");
                context.AddParameter("BACKUPMEDIA", "DISK");

                bool success = interpreter.Execute("backup", context);
                if (success)
                {
                    LastBackups.Add(new Tuple<string, string>(backupFilePath, projectPath));
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to create backup.");
            }
            return false;
        }

        public static bool RestoreLastBackup()
        {
            if (LastBackups.Count == 0)
            {
                return false;
            }

            try
            {
                // We must close the projects before restoring to prevent file lock errors
                foreach (var backup in LastBackups)
                {
                    CloseProject(backup.Item2);
                }

                System.Threading.Thread.Sleep(500);

                bool allSuccess = true;

                foreach (var backup in LastBackups)
                {
                    if (!File.Exists(backup.Item1))
                    {
                        allSuccess = false;
                        continue;
                    }

                    bool restoreSuccess = false;

                    CommandLineInterpreter interpreter = new CommandLineInterpreter();
                    ActionCallingContext context = new ActionCallingContext();
                    context.AddParameter("TYPE", "PROJECT");
                    context.AddParameter("ARCHIVENAME", backup.Item1);
                    context.AddParameter("PROJECTNAME", backup.Item2);
                    context.AddParameter("UNPACKPROJECT", "1"); // 1 = Unpack and load

                    restoreSuccess = interpreter.Execute("restore", context);

                    if (restoreSuccess)
                    {
                        try
                        {
                            using (var pm = new ProjectManager())
                            {
                                pm.OpenProject(backup.Item2);
                            }
                        }
                        catch (Exception openEx)
                        {
                            Logger.Error(openEx, "Failed to open restored project.");
                        }
                    }
                    else
                    {
                        allSuccess = false;
                    }
                }

                LastBackups.Clear();
                return allSuccess;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to restore projects.");
            }
            return false;
        }

        private static void CloseProject(string projectPath)
        {
            try
            {
                CommandLineInterpreter interpreter = new CommandLineInterpreter();
                ActionCallingContext context = new ActionCallingContext();
                context.AddParameter("PROJECTNAME", projectPath);
                interpreter.Execute("ProjectClose", context);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "CloseProject failed.");
            }
        }
    }
}