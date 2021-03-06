﻿using EnvDTE;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EnvDTE80;

namespace MadsKristensen.EditorExtensions
{
    internal static class ProjectHelpers
    {
        public static IEnumerable<Project> GetAllProjects()
        {
            return EditorExtensionsPackage.DTE.Solution.Projects
                .Cast<Project>()
                .SelectMany(GetChildProjects);
        }
        private static IEnumerable<Project> GetChildProjects(Project parent)
        {
            if (!String.IsNullOrEmpty(parent.FullName))
                return new[] { parent };
            return parent.ProjectItems
                    .Cast<ProjectItem>()
                    .Where(p => p.SubProject != null)
                    .SelectMany(p => GetChildProjects(p.SubProject));
        }

        ///<summary>Indicates whether a Project is a Web Application or Web Site project.</summary>
        public static bool IsWebProject(this Project project)
        {
            // Web site project
            if (project.Kind.Equals("{E24C65DC-7377-472B-9ABA-BC803B73C61A}", StringComparison.OrdinalIgnoreCase))
                return true;

            // Check for Web Application projects.  See https://github.com/madskristensen/WebEssentials2013/pull/140#issuecomment-26679862
            return project.Properties.Item("WebApplication.UseIISExpress") != null;
        }

        ///<summary>Gets the base directory of a specific Project, or of the active project if no parameter is passed.</summary>
        public static string GetRootFolder(Project project = null)
        {
            try
            {
                project = project ?? GetActiveProject();

                if (project == null)
                {
                    var doc = EditorExtensionsPackage.DTE.ActiveDocument;
                    if (doc != null && !string.IsNullOrEmpty(doc.FullName))
                        return GetProjectFolder(doc.FullName);
                    return string.Empty;
                }
                if (string.IsNullOrEmpty(project.FullName))
                    return null;
                var fullPath = project.Properties.Item("FullPath").Value as string;

                if (String.IsNullOrEmpty(fullPath))
                    return "";

                if (Directory.Exists(fullPath))
                    return fullPath;
                if (File.Exists(fullPath))
                    return Path.GetDirectoryName(fullPath);

                return "";
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
                return string.Empty;
            }
        }

        internal static bool AddFileToActiveProject(string fileName, string itemType = null)
        {
            Project project = GetActiveProject();

            if (project != null)
            {
                string projectFilePath = project.Properties.Item("FullPath").Value.ToString();
                string projectDirPath = Path.GetDirectoryName(projectFilePath);

                if (fileName.StartsWith(projectDirPath, StringComparison.OrdinalIgnoreCase))
                {
                    ProjectItem item = project.ProjectItems.AddFromFile(fileName);

                    if (itemType != null && item != null && !project.FullName.Contains("://"))
                    {
                        try
                        {
                            item.Properties.Item("ItemType").Value = itemType;
                        }
                        catch
                        {
                        }
                    }
                }
            }

            return false;
        }

        ///<summary>Gets the currently active project (as reported by the Solution Explorer), if any.</summary>
        public static Project GetActiveProject()
        {
            try
            {
                Array activeSolutionProjects = EditorExtensionsPackage.DTE.ActiveSolutionProjects as Array;

                if (activeSolutionProjects != null && activeSolutionProjects.Length > 0)
                    return activeSolutionProjects.GetValue(0) as Project;
            }
            catch (Exception ex)
            {
                Logger.Log("Error getting the active project" + ex);
            }

            return null;
        }

        #region ToAbsoluteFilePath()
        ///<summary>Converts a relative URL to an absolute path on disk, as resolved from the specified file.</summary>
        public static string ToAbsoluteFilePath(string relativeUrl, string relativeToFile)
        {
            var file = EditorExtensionsPackage.DTE.Solution.FindProjectItem(relativeToFile);
            return ToAbsoluteFilePath(relativeUrl, file);
        }

        ///<summary>Converts a relative URL to an absolute path on disk, as resolved from the active file.</summary>
        public static string ToAbsoluteFilePathFromActiveFile(string relativeUrl)
        {
            return ToAbsoluteFilePath(relativeUrl, GetActiveFile());
        }

        ///<summary>Converts a relative URL to an absolute path on disk, as resolved from the specified file.</summary>
        public static string ToAbsoluteFilePath(string relativeUrl, ProjectItem file)
        {
            var baseFolder = file.Properties == null ? null : Path.GetDirectoryName(file.Properties.Item("FullPath").Value.ToString());
            return ToAbsoluteFilePath(relativeUrl, GetProjectFolder(file), baseFolder);
        }

        ///<summary>Converts a relative URL to an absolute path on disk, as resolved from the specified relative or base directory.</summary>
        ///<param name="relativeUrl">The URL to resolve.</param>
        ///<param name="projectRoot">The root directory to resolve absolute URLs from.</param>
        ///<param name="baseFolder">The source directory to resolve relative URLs from.</param>
        public static string ToAbsoluteFilePath(string relativeUrl, string projectRoot, string baseFolder)
        {
            string imageUrl = relativeUrl.Trim(new[] { '\'', '"' });
            var relUri = new Uri(imageUrl, UriKind.RelativeOrAbsolute);

            if (relUri.IsAbsoluteUri)
            {
                return relUri.LocalPath;
            }

            if (projectRoot == null && baseFolder == null)
                return "";

            if (relUri.OriginalString.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).StartsWith(new string(Path.DirectorySeparatorChar, 1)))
            {
                baseFolder = null;
                relUri = new Uri(relUri.OriginalString.Substring(1), UriKind.Relative);
            }

            var root = (baseFolder ?? projectRoot).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

            if (File.Exists(root))
            {
                root = Path.GetDirectoryName(root);
            }

            if (!root.EndsWith(new string(Path.DirectorySeparatorChar, 1)))
            {
                root += Path.DirectorySeparatorChar;
            }

            var rootUri = new Uri(root, UriKind.Absolute);

            try
            {
                return FixAbsolutePath(new Uri(rootUri, relUri).LocalPath);
            }
            catch (UriFormatException)
            {
                return string.Empty;
            }
        }
        #endregion

        ///<summary>Gets the primary TextBuffer for the active document.</summary>
        public static ITextBuffer GetCurentTextBuffer()
        {
            //TODO: Get active ProjectionBuffer
            return GetCurentTextView().TextBuffer;
        }

        ///<summary>Gets the TextView for the active document.</summary>
        public static IWpfTextView GetCurentTextView()
        {
            var componentModel = GetComponentModel();
            if (componentModel != null)
            {
                var editorAdapter = componentModel.GetService<IVsEditorAdaptersFactoryService>();
                var textManager = (IVsTextManager)ServiceProvider.GlobalProvider.GetService(typeof(SVsTextManager));

                IVsTextView activeView = null;
                textManager.GetActiveView(1, null, out activeView);

                return editorAdapter.GetWpfTextView(activeView);
            }

            return null;
        }

        public static IComponentModel GetComponentModel()
        {
            return (IComponentModel)ServiceProvider.GlobalProvider.GetService(typeof(SComponentModel));
        }

        ///<summary>Gets the full paths to the currently selected item(s) in the Solution Explorer.</summary>
        public static IEnumerable<string> GetSelectedItemPaths(DTE2 dte = null)
        {
            var items = (Array)(dte ?? EditorExtensionsPackage.DTE).ToolWindows.SolutionExplorer.SelectedItems;
            foreach (UIHierarchyItem selItem in items)
            {
                var item = selItem.Object as ProjectItem;
                if (item != null)
                {
                    yield return item.Properties.Item("FullPath").Value.ToString();
                }
            }
        }
        ///<summary>Gets the the currently selected project(s) in the Solution Explorer.</summary>
        public static IEnumerable<Project> GetSelectedProjects()
        {
            var items = (Array)EditorExtensionsPackage.DTE.ToolWindows.SolutionExplorer.SelectedItems;
            foreach (UIHierarchyItem selItem in items)
            {
                var item = selItem.Object as Project;
                if (item != null)
                {
                    yield return item;
                }
            }
        }

        public static bool CheckOutFileFromSourceControl(string fileName)
        {
            try
            {
                var dte = EditorExtensionsPackage.DTE;

                if (File.Exists(fileName) && dte.Solution.FindProjectItem(fileName) != null)
                {
                    if (dte.SourceControl.IsItemUnderSCC(fileName) && !dte.SourceControl.IsItemCheckedOut(fileName))
                    {
                        dte.SourceControl.CheckOutItem(fileName);
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
            }

            return false;
        }

        ///<summary>Gets the directory containing the active solution file.</summary>
        public static string GetSolutionFolderPath()
        {
            EnvDTE.Solution solution = EditorExtensionsPackage.DTE.Solution;

            if (solution == null || string.IsNullOrEmpty(solution.FullName))
                return null;

            return Path.GetDirectoryName(solution.FullName);
        }

        ///<summary>Gets the directory containing the project for the specified file.</summary>
        private static string GetProjectFolder(ProjectItem item)
        {
            if (item == null || item.ContainingProject == null || string.IsNullOrEmpty(item.ContainingProject.FullName)) // Solution items
                return null;

            return GetRootFolder(item.ContainingProject);
        }

        ///<summary>Gets the directory containing the project for the specified file.</summary>
        public static string GetProjectFolder(string fileNameOrFolder)
        {
            if (string.IsNullOrEmpty(fileNameOrFolder))
                return GetRootFolder();

            ProjectItem item = EditorExtensionsPackage.DTE.Solution.FindProjectItem(fileNameOrFolder);

            return GetProjectFolder(item);
        }

        ///<summary>Gets the the currently selected file(s) in the Solution Explorer.</summary>
        public static IEnumerable<ProjectItem> GetSelectedItems()
        {
            var items = (Array)EditorExtensionsPackage.DTE.ToolWindows.SolutionExplorer.SelectedItems;
            foreach (UIHierarchyItem selItem in items)
            {
                var item = selItem.Object as ProjectItem;
                if (item != null)
                {
                    yield return item;
                }
            }
        }

        public static string FixAbsolutePath(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return absolutePath;
            }

            var uniformlySeparated = absolutePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            var doubleSlash = new string(Path.DirectorySeparatorChar, 2);
            var prependSeparator = uniformlySeparated.StartsWith(doubleSlash);
            uniformlySeparated = uniformlySeparated.Replace(doubleSlash, new string(Path.DirectorySeparatorChar, 1));

            if (prependSeparator)
            {
                uniformlySeparated = Path.DirectorySeparatorChar + uniformlySeparated;
            }

            return uniformlySeparated;
        }

        ///<summary>Gets the Project containing the specified file.</summary>
        public static Project GetProject(string item)
        {
            var projectItem = EditorExtensionsPackage.DTE.Solution.FindProjectItem(item);
            if (projectItem == null)
            {
                return null;
            }

            return projectItem.ContainingProject;
        }

        public static ProjectItem GetActiveFile()
        {
            var doc = EditorExtensionsPackage.DTE.ActiveDocument;

            if (doc != null)
            {
                return doc.ProjectItem;
            }

            return null;
        }
    }
}
