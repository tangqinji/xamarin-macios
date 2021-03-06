using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.DotNet.XHarness.iOS.Shared.Execution;
using Microsoft.DotNet.XHarness.iOS.Shared.Logging;

namespace Xharness.Jenkins.TestTasks {
	class MSBuildTask : BuildProjectTask
	{
		public ILog BuildLog;

		protected virtual string ToolName => Harness.XIBuildPath;

		protected virtual List<string> ToolArguments => 
				MSBuild.GetToolArguments (ProjectPlatform, ProjectConfiguration, ProjectFile, BuildLog);

		Xharness.TestTasks.MSBuildTask MSBuild => buildToolTask as Xharness.TestTasks.MSBuildTask;

		public MSBuildTask (Jenkins jenkins, TestProject testProject, IProcessManager processManager)
			: base (jenkins, testProject, processManager) { }

		protected override void InitializeTool () => 
			buildToolTask = new Xharness.TestTasks.MSBuildTask (
				msbuildPath: ToolName,
				processManager: ProcessManager,
				resourceManager: Jenkins,
				eventLogger: this,
				envManager: this,
				errorKnowledgeBase: Jenkins);

		protected override async Task ExecuteAsync ()
		{
			using var resource = await NotifyAndAcquireDesktopResourceAsync ();
			BuildLog = Logs.Create ($"build-{Platform}-{Timestamp}.txt", LogType.BuildLog.ToString ());
			(ExecutionResult, KnownFailure) = await MSBuild.ExecuteAsync (
				projectPlatform: ProjectPlatform,
				projectConfiguration: ProjectConfiguration,
				projectFile: ProjectFile,
				resource: resource,
				dryRun: Harness.DryRun,
				buildLog: BuildLog,
				mainLog: Jenkins.MainLog);

			BuildLog.Dispose ();
		}

		public override Task CleanAsync () =>
			MSBuild.CleanAsync (
				projectPlatform: ProjectPlatform,
				projectConfiguration: ProjectConfiguration,
				projectFile: ProjectFile,
				cleanLog: Logs.Create ($"clean-{Platform}-{Timestamp}.txt", "Clean log"),
				mainLog: Jenkins.MainLog);
	}
}
