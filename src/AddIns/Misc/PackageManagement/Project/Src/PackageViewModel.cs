﻿// Copyright (c) 2014 AlphaSierraPapa for the SharpDevelop Team
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using ICSharpCode.PackageManagement;
using NuGet;

namespace ICSharpCode.PackageManagement
{
	public class PackageViewModel : ViewModelBase<PackageViewModel>
	{
		DelegateCommand addPackageCommand;
		DelegateCommand removePackageCommand;
		DelegateCommand managePackageCommand;
		
		PackageManagementSelectedProjects selectedProjects;
		IPackageManagementEvents packageManagementEvents;
		IPackageFromRepository package;
		IEnumerable<PackageOperation> packageOperations = new PackageOperation[0];
		PackageViewModelOperationLogger logger;
		IPackageActionRunner actionRunner;
		IPackageViewModelParent parent;
		
		public PackageViewModel(
			IPackageViewModelParent parent,
			IPackageFromRepository package,
			PackageManagementSelectedProjects selectedProjects,
			IPackageManagementEvents packageManagementEvents,
			IPackageActionRunner actionRunner,
			ILogger logger)
		{
			this.parent = parent;
			this.package = package;
			this.selectedProjects = selectedProjects;
			this.packageManagementEvents = packageManagementEvents;
			this.actionRunner = actionRunner;
			this.logger = CreateLogger(logger);
			
			CreateCommands();
		}
		
		public IPackageViewModelParent GetParent()
		{
			return parent;
		}
		
		protected virtual PackageViewModelOperationLogger CreateLogger(ILogger logger)
		{
			return new PackageViewModelOperationLogger(logger, package);
		}
		
		void CreateCommands()
		{
			addPackageCommand = new DelegateCommand(param => AddOrManagePackage());
			removePackageCommand = new DelegateCommand(param => RemoveOrManagePackage());
			managePackageCommand = new DelegateCommand(param => ManagePackage());
		}
	
		public ICommand AddPackageCommand {
			get { return addPackageCommand; }
		}
		
		public ICommand RemovePackageCommand {
			get { return removePackageCommand; }
		}
		
		public ICommand ManagePackageCommand {
			get { return managePackageCommand; }
		}
		
		public IPackage GetPackage()
		{
			return package;
		}
		
		public bool HasLicenseUrl {
			get { return LicenseUrl != null; }
		}
		
		public Uri LicenseUrl {
			get { return package.LicenseUrl; }
		}
		
		public bool HasProjectUrl {
			get { return ProjectUrl != null; }
		}
		
		public Uri ProjectUrl {
			get { return package.ProjectUrl; }
		}
		
		public bool HasReportAbuseUrl {
			get { return ReportAbuseUrl != null; }
		}
		
		public Uri ReportAbuseUrl {
			get { return package.ReportAbuseUrl; }
		}
		
		public bool IsAdded {
			get {
				if (selectedProjects.HasSingleProjectSelected()) {
					return selectedProjects.GetSingleProjectSelected(package.Repository).IsPackageInstalled(package)
						|| (IsPackageInstalled() && !IsProjectPackage(package));
				}
				return IsPackageInstalled();
			}
		}
		
		protected bool IsPackageInstalled()
		{
			return IsPackageInstalledInSolution(package);
		}
		
		public IEnumerable<PackageDependencySet> Dependencies {
			get { return package.DependencySets; }
		}
		
		public bool HasDependencies {
			get { return package.HasDependencies; }
		}
		
		public bool HasNoDependencies {
			get { return !HasDependencies; }
		}
		
		public IEnumerable<string> Authors {
			get { return package.Authors; }
		}
		
		public bool HasDownloadCount {
			get { return package.DownloadCount >= 0; }
		}
		
		public string Id {
			get { return package.Id; }
		}
		
		public string Name {
			get { return package.GetName(); }
		}
		
		public bool HasGalleryUrl {
			get { return GalleryUrl != null; }
		}
		
		public bool HasNoGalleryUrl {
			get { return !HasGalleryUrl; }
		}
		
		public Uri GalleryUrl {
			get { return package.GalleryUrl; }
		}
		
		public Uri IconUrl {
			get { return package.IconUrl; }
		}
		
		public string Summary {
			get { return package.SummaryOrDescription(); }
		}
		
		public SemanticVersion Version {
			get { return package.Version; }
		}
		
		public int DownloadCount {
			get { return package.DownloadCount; }
		}
		
		public string Description {
			get { return package.Description; }
		}
		
		public DateTimeOffset? LastPublished {
			get { return package.Published; }
		}
		
		public bool HasLastPublished {
			get { return package.Published.HasValue; }
		}

		/// <summary>
		/// Make this & IPackageExtensions.IsProjectPackage overridable/testable
		/// </summary>
		/// <param name="package"></param>
		/// <returns></returns>
		protected virtual bool IsProjectPackage(IPackage package) {
			return package.IsProjectPackage();
		}

		public void PackageChanged() {
			OnPropertyChanged(model => model.IsManaged);
			OnPropertyChanged(model => model.IsAdded);
		}

		public void AddOrManagePackage() {
			if (selectedProjects.HasMultipleProjects()) {
				if (IsProjectPackage(package)) {
					ManagePackage();
				}
				else {
					AddPackage();
				}
			}
			else {
				AddPackage();
			}
		}

		public void AddPackage()
		{
			ClearReportedMessages();
			logger.LogAddingPackage();
			
			using (IDisposable operation = StartOperation(package)) {
				if (IsProjectPackage(package)) {
					TryInstallingPackage();
				}
				else {
					TryInstallingSolutionPackage();
				}
			}
			
			logger.LogAfterPackageOperationCompletes();
		}
		
		protected virtual IDisposable StartOperation(IPackageFromRepository package)
		{
			return package.StartInstallOperation();
		}
		
		void ClearReportedMessages()
		{
			packageManagementEvents.OnPackageOperationsStarting();
		}
		
		void GetInstallPackageOperations()
		{
			IPackageManagementProject project = GetSingleProjectSelected();
			project.Logger = logger;
			InstallPackageAction installAction = project.CreateInstallPackageAction();
			installAction.AllowPrereleaseVersions = parent.IncludePrerelease;
			packageOperations = project.GetInstallPackageOperations(package, installAction);
		}
		
		IPackageManagementProject GetSingleProjectSelected()
		{
			return selectedProjects.GetSingleProjectSelected(package.Repository);
		}
		
		IEnumerable<IPackage> GetPackagesRequiringLicenseAcceptance()
		{
			IList<IPackage> packagesToBeInstalled = GetPackagesToBeInstalled();
			return GetPackagesRequiringLicenseAcceptance(packagesToBeInstalled);
		}
		
		IEnumerable<IPackage> GetPackagesRequiringLicenseAcceptance(IEnumerable<IPackage> packagesToBeInstalled)
		{
			return packagesToBeInstalled.Where(PackageRequiresLicenseAcceptance);
		}
		
		IList<IPackage> GetPackagesToBeInstalled()
		{
			var packages = new List<IPackage>();
			foreach (PackageOperation operation in packageOperations) {
				if (operation.Action == PackageAction.Install) {
					packages.Add(operation.Package);
				}
			}
			return packages;
		}

		bool PackageRequiresLicenseAcceptance(IPackage package)
		{
			return package.RequireLicenseAcceptance && !IsPackageInstalledInSolution(package);
		}
		
		bool IsPackageInstalledInSolution(IPackage package)
		{
			return selectedProjects.Solution.IsPackageInstalled(package);
		}

		void TryInstallingSolutionPackage()
		{
			try {
				var installAction = new InstallPackageAction(null, packageManagementEvents);
				installAction.IgnoreDependencies = false;
				installAction.AllowPrereleaseVersions = parent.IncludePrerelease;

				var solutionRepository = selectedProjects.Solution.CreateSolutionPackageRepository();

				var resolverFactory = new PackageOperationsResolverFactory();
				var resolver = resolverFactory.CreateInstallPackageOperationResolver(
					solutionRepository.Repository, 
					package.Repository, 
					logger, 
					installAction);
				packageOperations = resolver.ResolveOperations(package);

				if (LicensesAccepted()) {
					var packageManager = new PackageManager(
						package.Repository, 
						solutionRepository.PackagePathResolver, 
						solutionRepository.FileSystem, 
						solutionRepository.Repository);
					packageManager.InstallPackage(
						package.Id, 
						package.Version, 
						installAction.IgnoreDependencies, 
						installAction.AllowPrereleaseVersions);
					packageManagementEvents.OnParentPackageInstalled(package);
				}
			} catch (Exception ex) {
				ReportError(ex);
				logger.LogError(ex);
			}
		}		

		void TryInstallingPackage()
		{
			try {
				GetInstallPackageOperations();
				if (LicensesAccepted()) {
					InstallPackage();
				}
			} catch (Exception ex) {
				ReportError(ex);
				logger.LogError(ex);
			}
		}
		
		void InstallPackage()
		{
			InstallPackage(packageOperations);
		}
		
		void InstallPackage(IEnumerable<PackageOperation> packageOperations)
		{
			IPackageManagementProject project = GetSingleProjectSelected();
			ProcessPackageOperationsAction action = CreateInstallPackageAction(project);
			action.AllowPrereleaseVersions = parent.IncludePrerelease;
			action.Package = package;
			action.Operations = packageOperations;
			actionRunner.Run(action);
		}
		
		protected virtual ProcessPackageOperationsAction CreateInstallPackageAction(
			IPackageManagementProject project)
		{
			return project.CreateInstallPackageAction();
		}
		
		void ReportError(Exception ex)
		{
			packageManagementEvents.OnPackageOperationError(ex);
		}
		
		public void RemoveOrManagePackage() {
			if (selectedProjects.HasMultipleProjects()) {
				if (IsProjectPackage(package)) {
					ManagePackage();
				}
				else {
					RemovePackage();
				}
			}
			else {
				RemovePackage();
			}
		}

		public void RemovePackage()
		{
			ClearReportedMessages();
			logger.LogRemovingPackage();

			if (IsProjectPackage(package)) {
				TryUninstallingPackage();
			} else {
				TryUninstallingSolutionPackage();
			}

			logger.LogAfterPackageOperationCompletes();
		}
		
		void LogRemovingPackage()
		{
			logger.LogRemovingPackage();
		}

		void TryUninstallingSolutionPackage()
		{
			try {
				var solutionPackageRepository = PackageManagementServices.Solution.CreateSolutionPackageRepository();
				var packageManager = new NuGet.PackageManager(solutionPackageRepository.Repository, solutionPackageRepository.PackagePathResolver, solutionPackageRepository.FileSystem);
				packageManager.UninstallPackage(package.Id, package.Version);
				packageManagementEvents.OnParentPackageUninstalled(package);
			} catch (Exception ex) {
				ReportError(ex);
				logger.LogError(ex);
			}
		}		

		void TryUninstallingPackage()
		{
			try {
				IPackageManagementProject project = GetSingleProjectSelected();
				UninstallPackageAction action = project.CreateUninstallPackageAction();
				action.Package = package;
				actionRunner.Run(action);
			} catch (Exception ex) {
				ReportError(ex);
				logger.LogError(ex);
			}
		}
		
		public bool IsManaged {
			get {
				if (selectedProjects.HasSingleProjectSelected()) {
					// Single Project selected 
					// Project-level Package Management
					return false; // [Add]/[Remove]
				}
				// Solution selected 
				// Project-level/Solution-level Package Management
				if (IsAdded) {
					if (IsProjectPackage(package)) {
						return true; // [Manage] Button
					}
					return false; // [Remove] Button
				}
				// package.IsProjectPackage() is too slow on uninstalled packages
				// check ([Add] or [Manage]) after user press on [Add] Package Button
				return false; // [Add] Button -> redirect to [Manage] if (package.IsProjectPackage())
			}
		}
		
		public void ManagePackage()
		{
			List<IPackageManagementSelectedProject> projects = GetSelectedProjectsForPackage();
			if (packageManagementEvents.OnSelectProjects(projects)) {
				ManagePackagesForSelectedProjects(projects);
			}
		}
		
		List<IPackageManagementSelectedProject> GetSelectedProjectsForPackage()
		{
			return selectedProjects.GetProjects(package).ToList();
		}
		
		public void ManagePackagesForSelectedProjects(IEnumerable<IPackageManagementSelectedProject> projects)
		{
			ManagePackagesForSelectedProjects(projects.ToList());
		}
		
		void ManagePackagesForSelectedProjects(IList<IPackageManagementSelectedProject> projects)
		{
			ClearReportedMessages();
			logger.LogManagingPackage();
			
			using (IDisposable operation = StartOperation(package)) {
				TryManagePackagesForSelectedProjects(projects);
			}
			
			logger.LogAfterPackageOperationCompletes();
		}
		
		void TryManagePackagesForSelectedProjects(IList<IPackageManagementSelectedProject> projects)
		{
			try {
				packageOperations = new PackageOperation[0];
				if (!IsPackageInstalled()) {
					IPackageManagementSelectedProject firstSelectedProject = (
						from project in projects 
						where project.IsSelected 
						select project).FirstOrDefault();
					
					if (firstSelectedProject != null) {
						InstallPackageAction installAction = firstSelectedProject.Project.CreateInstallPackageAction();
						installAction.AllowPrereleaseVersions = parent.IncludePrerelease;
						packageOperations = firstSelectedProject.Project.GetInstallPackageOperations(package, installAction);
					}
				}
				IList<ProcessPackageAction> actions = GetProcessPackageActionsForSelectedProjects(projects);
				if (actions.Any() && LicensesAccepted()) {
					RunActionsIfAnyExist(actions);
				}
			} catch (Exception ex) {
				ReportError(ex);
				logger.LogError(ex);
			}
		}
		
		protected virtual bool AnyProjectsSelected(IList<IPackageManagementSelectedProject> projects)
		{
			return projects.Any(project => project.IsSelected);
		}
		
		public IList<ProcessPackageAction> GetProcessPackageActionsForSelectedProjects(
			IList<IPackageManagementSelectedProject> selectedProjects)
		{
			var actions = new List<ProcessPackageAction>();
			foreach (IPackageManagementSelectedProject selectedProject in selectedProjects) {
				ProcessPackageAction action = CreatePackageManageAction(selectedProject);
				if (action != null) {
					actions.Add(action);
				}
			}
			return actions;
		}
		
		protected virtual ProcessPackageAction CreatePackageManageAction(IPackageManagementSelectedProject selectedProject)
		{
			if (selectedProject.IsSelected) {
				return CreateInstallPackageManageActionForSelectedProject(selectedProject);
			}
			return CreateUninstallPackageManageActionForSelectedProject(selectedProject);
		}
		
		ProcessPackageAction CreateInstallPackageManageActionForSelectedProject(IPackageManagementSelectedProject selectedProject)
		{
			if (!selectedProject.Project.IsPackageInstalled(package)) {
				return CreateInstallPackageAction(selectedProject);
			}
			return null;
		}
		
		ProcessPackageAction CreateUninstallPackageManageActionForSelectedProject(IPackageManagementSelectedProject selectedProject)
		{
			if (selectedProject.Project.IsPackageInstalled(package)) {
				return CreateUninstallPackageAction(selectedProject);
			}
			return null;
		}
		
		protected ProcessPackageAction CreateUpdatePackageManageActionForSelectedProject(IPackageManagementSelectedProject selectedProject)
		{
			if (!selectedProject.Project.IsPackageInstalled(package)) {
				return CreateUpdatePackageAction(selectedProject);
			}
			return null;
		}
		
		bool LicensesAccepted()
		{
			IEnumerable<IPackage> licensedPackages = GetPackagesRequiringLicenseAcceptance();
			if (licensedPackages.Any()) {
				return packageManagementEvents.OnAcceptLicenses(licensedPackages);
			}
			return true;
		}
		
		protected ProcessPackageAction CreateInstallPackageAction(IPackageManagementSelectedProject selectedProject)
		{
			IPackageManagementProject project = selectedProject.Project;
			project.Logger = logger;
			
			ProcessPackageOperationsAction action = CreateInstallPackageAction(project);
			action.AllowPrereleaseVersions = parent.IncludePrerelease;
			action.Package = package;
			return action;
		}
		
		protected ProcessPackageAction CreateUninstallPackageAction(IPackageManagementSelectedProject selectedProject)
		{
			IPackageManagementProject project = selectedProject.Project;
			project.Logger = logger;
			
			ProcessPackageAction action = project.CreateUninstallPackageAction();
			action.Package = package;
			return action;
		}
		
		protected ProcessPackageAction CreateUpdatePackageAction(IPackageManagementSelectedProject selectedProject)
		{
			IPackageManagementProject project = selectedProject.Project;
			project.Logger = logger;
			
			ProcessPackageOperationsAction action = project.CreateUpdatePackageAction();
			action.AllowPrereleaseVersions = parent.IncludePrerelease;
			action.Package = package;
			return action;
		}
		
		void RunActionsIfAnyExist(IList<ProcessPackageAction> actions)
		{
			if (actions.Any()) {
				actionRunner.Run(actions);
			}
		}
	
	}
}
