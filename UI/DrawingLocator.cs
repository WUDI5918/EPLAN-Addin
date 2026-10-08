using System;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.Gui;
using Eplan.EplApi.HEServices;

namespace TranslationEplAddIn.Tool.UI
{
    public static class DrawingLocator
    {
        public static bool LocateAndHighlight(string objectId)
        {
            if (string.IsNullOrEmpty(objectId)) return false;

            try
            {
                // Retrieve active project from selection set (with locking disabled)
                SelectionSet selectionSet = new SelectionSet();
                selectionSet.LockProjectByDefault = false;
                selectionSet.LockSelectionByDefault = false;
                Project activeProject = null;
                try
                {
                    activeProject = selectionSet.GetCurrentProject(false);
                }
                catch { }

                if (activeProject == null)
                {
                    try
                    {
                        ProjectManager pm = new ProjectManager();
                        pm.LockProjectByDefault = false;
                        activeProject = pm.CurrentProject;
                    }
                    catch { }
                }

                if (activeProject == null)
                {
                    try
                    {
                        ProjectManager pm = new ProjectManager();
                        pm.LockProjectByDefault = false;
                        var openProjects = pm.OpenProjects;
                        if (openProjects != null && openProjects.Length > 0)
                        {
                            activeProject = openProjects[0];
                        }
                    }
                    catch { }
                }

                if (activeProject == null) return false;

                // Align database ID to active project
                uint currentDbId = activeProject.DatabaseIdentifier;
                string updatedObjectId = objectId;
                int firstSlash = objectId.IndexOf('/');
                if (firstSlash > 0)
                {
                    updatedObjectId = currentDbId.ToString() + objectId.Substring(firstSlash);
                }

                // Find EPLAN object by string identifier
                StorableObject storableObject = null;
                try
                {
                    storableObject = StorableObject.FromStringIdentifier(updatedObjectId);
                }
                catch { }

                // Fallback search for PlcIO if FromStringIdentifier returned null
                if (storableObject == null)
                {
                    try
                    {
                        PlcIO[] plcIOs = PlcIO.GetAll(activeProject);
                        if (plcIOs != null)
                        {
                            foreach (var p in plcIOs)
                            {
                                if (p != null && p.ToStringIdentifier() == updatedObjectId)
                                {
                                    storableObject = p;
                                    break;
                                }
                            }
                        }
                    }
                    catch { }
                }

                if (storableObject != null && storableObject.IsValid)
                {
                    // If it's an ArticleReference, resolve its parent object (typically a Function)
                    if (storableObject is ArticleReference artRef)
                    {
                        try
                        {
                            storableObject = artRef.ParentObject;
                        }
                        catch { }
                    }

                    // If it's a Connection, resolve to its placement representation (ConnectionDefPoints or StartSymbolReference)
                    if (storableObject is Connection connection)
                    {
                        try
                        {
                            if (connection.ConnectionDefPoints != null && connection.ConnectionDefPoints.Length > 0)
                            {
                                storableObject = connection.ConnectionDefPoints[0];
                            }
                            else if (connection.StartSymbolReference != null)
                            {
                                storableObject = connection.StartSymbolReference;
                            }
                        }
                        catch { }
                    }

                    Edit edit = new Edit();
                    if (storableObject is Placement placement)
                    {
                        edit.OpenPageWithPlacement(placement);
                        return true;
                    }
                    else if (storableObject is Page page)
                    {
                        edit.OpenPageWithName(page.Project.ProjectFullName, page.Name);
                        return true;
                    }
                    else if (storableObject is PlcIO plcIO)
                    {
                        // Try to find the booked terminal in the active project by address
                        string targetAddress = plcIO.Address?.Trim();
                        if (!string.IsNullOrEmpty(targetAddress))
                        {
                            foreach (Page pg in activeProject.Pages)
                            {
                                if (pg == null) continue;
                                foreach (Placement pl in pg.AllPlacements)
                                {
                                    if (pl is Function func)
                                    {
                                        if ((func.FunctionCategory == Eplan.EplApi.Base.Enums.FunctionCategory.PLCTerminal || 
                                             func.FunctionCategory == Eplan.EplApi.Base.Enums.FunctionCategory.PLCBox) &&
                                            func.Properties.Exists(20400))
                                        {
                                            string addr = func.Properties[20400].ToString()?.Trim();
                                            if (string.Equals(addr, targetAddress, StringComparison.OrdinalIgnoreCase))
                                            {
                                                edit.OpenPageWithPlacement(func);
                                                return true;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to locate drawing element: " + ex.Message);
            }
            return false;
        }
    }
}
