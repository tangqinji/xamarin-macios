﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using Xamarin.Utils;

namespace xharness
{
	public class Jenkins
	{
		bool populating = true;

		public Harness Harness;
		public bool IncludeAll;
		public bool IncludeClassicMac = true;
		public bool IncludeBcl;
		public bool IncludeMac = true;
		public bool IncludeMac32 = true;
		public bool IncludeiOS = true;
		public bool IncludeiOSExtensions;
		public bool ForceExtensionBuildOnly;
		public bool BuildiOSExtensions;
		public bool IncludetvOS = true;
		public bool IncludewatchOS = true;
		public bool IncludeMmpTest;
		public bool IncludeiOSMSBuild = true;
		public bool IncludeMtouch;
		public bool IncludeBtouch;
		public bool IncludeMacBindingProject;
		public bool IncludeSimulator = true;
		public bool IncludeDevice;
		public bool IncludeXtro;

		public Log MainLog;
		public Log SimulatorLoadLog;
		public Log DeviceLoadLog;

		string log_directory;
		public string LogDirectory {
			get {
				if (string.IsNullOrEmpty (log_directory)) {
					log_directory = Path.Combine (Harness.JENKINS_RESULTS_DIRECTORY, "tests");
					if (IsServerMode)
						log_directory = Path.Combine (log_directory, Harness.Timestamp);
				}
				return log_directory;
			}
		}

		Logs logs;
		public Logs Logs {
			get {
				return logs ?? (logs = new Logs (LogDirectory));
			}
		}

		public Simulators Simulators = new Simulators ();
		public Devices Devices = new Devices ();

		List<TestTask> Tasks = new List<TestTask> ();
		Dictionary<string, MakeTask> DependencyTasks = new Dictionary<string, MakeTask> ();

		internal static Resource DesktopResource = new Resource ("Desktop", Environment.ProcessorCount);

		static Dictionary<string, Resource> device_resources = new Dictionary<string, Resource> ();
		internal static Resources GetDeviceResources (IEnumerable<Device> devices)
		{
			List<Resource> resources = new List<Resource> ();
			lock (device_resources) {
				foreach (var device in devices) {
					Resource res;
					if (!device_resources.TryGetValue (device.UDID, out res))
						device_resources.Add (device.UDID, res = new Resource (device.UDID, 1, device.Name));
					resources.Add (res);
				}
			}
			return new Resources (resources);
		}

		// Loads both simulators and devices in parallel
		Task LoadSimulatorsAndDevicesAsync ()
		{
			Simulators.Harness = Harness;
			Devices.Harness = Harness;

			if (SimulatorLoadLog == null)
				SimulatorLoadLog = Logs.Create ($"simulator-list-{Harness.Timestamp}.log", "Simulator Listing (in progress)");

			var simulatorLoadTask = Task.Run (async () => {
				try {
					await Simulators.LoadAsync (SimulatorLoadLog);
					SimulatorLoadLog.Description = "Simulator Listing (ok)";
				} catch (Exception e) {
					SimulatorLoadLog.WriteLine ("Failed to load simulators:");
					SimulatorLoadLog.WriteLine (e);
					SimulatorLoadLog.Description = $"Simulator Listing ({e.Message})";
				}
			});

			if (DeviceLoadLog == null)
				DeviceLoadLog = Logs.Create ($"device-list-{Harness.Timestamp}.log", "Device Listing (in progress)");
			var deviceLoadTask = Task.Run (async () => {
				try {
					await Devices.LoadAsync (DeviceLoadLog, removed_locked: true);
					DeviceLoadLog.Description = "Device Listing (ok)";
				} catch (Exception e) {
					DeviceLoadLog.WriteLine ("Failed to load devices:");
					DeviceLoadLog.WriteLine (e);
					DeviceLoadLog.Description = $"Device Listing ({e.Message})";
				}
			});

			return Task.CompletedTask;
		}

		IEnumerable<RunSimulatorTask> CreateRunSimulatorTaskAsync (XBuildTask buildTask)
		{
			var runtasks = new List<RunSimulatorTask> ();

			AppRunnerTarget [] targets;
			TestPlatform [] platforms;

			switch (buildTask.Platform) {
			case TestPlatform.tvOS:
				targets = new AppRunnerTarget [] { AppRunnerTarget.Simulator_tvOS };
				platforms = new TestPlatform [] { TestPlatform.tvOS };
				break;
			case TestPlatform.watchOS:
				targets = new AppRunnerTarget [] { AppRunnerTarget.Simulator_watchOS };
				platforms = new TestPlatform [] { TestPlatform.watchOS };
				break;
			case TestPlatform.iOS_Unified:
				targets = new AppRunnerTarget [] { AppRunnerTarget.Simulator_iOS32, AppRunnerTarget.Simulator_iOS64 };
				platforms = new TestPlatform [] { TestPlatform.iOS_Unified32, TestPlatform.iOS_Unified64 };
				break;
			default:
				throw new NotImplementedException ();
			}

			for (int i = 0; i < targets.Length; i++)
				runtasks.Add (new RunSimulatorTask (buildTask, Simulators.SelectDevices (targets [i], SimulatorLoadLog)) { Platform = platforms [i], Ignored = buildTask.Ignored });

			return runtasks;
		}

		bool IsIncluded (TestProject project)
		{
			if (!project.IsExecutableProject)
				return false;

			if (!IncludeBcl && project.IsBclTest)
				return false;

			if (!Harness.IncludeSystemPermissionTests && project.Name == "introspection")
				return false;

			return true;
		}

		class TestData
		{
			public string Variation;
			public string MTouchExtraArgs;
			public string MonoBundlingExtraArgs; // mmp
			public bool Debug;
			public bool Profiling;
			public string LinkMode;
			public string Defines;
			public string Undefines;
			public bool Ignored;
		}

		IEnumerable<TestData> GetTestData (RunTestTask test)
		{
			// This function returns additional test configurations (in addition to the default one) for the specific test

			switch (test.ProjectPlatform) {
			case "iPhone":
				/* we don't add --assembly-build-target=@all=staticobject because that's the default in all our test projects */
				yield return new TestData { Variation = "AssemblyBuildTarget: dylib (debug)", MTouchExtraArgs = "--assembly-build-target=@all=dynamiclibrary", Debug = true, Profiling = false };
				yield return new TestData { Variation = "AssemblyBuildTarget: SDK framework (debug)", MTouchExtraArgs = "--assembly-build-target=@sdk=framework=Xamarin.Sdk --assembly-build-target=@all=staticobject", Debug = true, Profiling = false };

				yield return new TestData { Variation = "AssemblyBuildTarget: dylib (debug, profiling)", MTouchExtraArgs = "--assembly-build-target=@all=dynamiclibrary", Debug = true, Profiling = true };
				yield return new TestData { Variation = "AssemblyBuildTarget: SDK framework (debug, profiling)", MTouchExtraArgs = "--assembly-build-target=@sdk=framework=Xamarin.Sdk --assembly-build-target=@all=staticobject", Debug = true, Profiling = true };

				yield return new TestData { Variation = "Release", MTouchExtraArgs = "", Debug = false, Profiling = false };
				yield return new TestData { Variation = "AssemblyBuildTarget: SDK framework (release)", MTouchExtraArgs = "--assembly-build-target=@sdk=framework=Xamarin.Sdk --assembly-build-target=@all=staticobject", Debug = false, Profiling = false };

				switch (test.TestName) {
				case "monotouch-test":
					yield return new TestData { Variation = "Release (all optimizations)", MTouchExtraArgs = "--registrar:static --optimize:all", Debug = false, Profiling = false, Defines = "OPTIMIZEALL" };
					yield return new TestData { Variation = "Debug (all optimizations)", MTouchExtraArgs = "--registrar:static --optimize:all", Debug = true, Profiling = false, Defines = "OPTIMIZEALL" };
					yield return new TestData { Variation = "Debug (interpreter)", MTouchExtraArgs = "--interpreter", Debug = true, Profiling = false, };
					yield return new TestData { Variation = "Debug (interpreter -mscorlib)", MTouchExtraArgs = "--interpreter=-mscorlib", Debug = true, Profiling = false, };
					break;
				case "mscorlib":
					yield return new TestData { Variation = "Debug (interpreter)", MTouchExtraArgs = "--interpreter", Debug = true, Profiling = false, Undefines = "FULL_AOT_RUNTIME" };
					yield return new TestData { Variation = "Debug (interpreter -mscorlib)", MTouchExtraArgs = "--interpreter=-mscorlib", Debug = true, Profiling = false, Ignored = true, Undefines = "FULL_AOT_RUNTIME" };
					break;
				case "mini":
					yield return new TestData { Variation = "Debug (interpreter)", MTouchExtraArgs = "--interpreter", Debug = true, Profiling = false, Undefines = "FULL_AOT_RUNTIME" };
					yield return new TestData { Variation = "Debug (interpreter -mscorlib)", MTouchExtraArgs = "--interpreter=-mscorlib", Debug = true, Profiling = false, Undefines = "FULL_AOT_RUNTIME" };
					break;
				}
				break;
			case "iPhoneSimulator":
				switch (test.TestName) {
				case "monotouch-test":
					// The default is to run monotouch-test with the dynamic registrar (in the simulator), so that's already covered
					yield return new TestData { Variation = "Debug (static registrar)", MTouchExtraArgs = "--registrar:static", Debug = true, Profiling = false };
					yield return new TestData { Variation = "Release (all optimizations)", MTouchExtraArgs = "--registrar:static --optimize:all", Debug = false, Profiling = false, LinkMode = "Full", Defines = "OPTIMIZEALL" };
					yield return new TestData { Variation = "Debug (all optimizations)", MTouchExtraArgs = "--registrar:static --optimize:all,-remove-uithread-checks", Debug = true, Profiling = false, LinkMode = "Full", Defines = "OPTIMIZEALL", Ignored = !IncludeAll };
					break;
				}
				break;
			case "AnyCPU":
			case "x86":
				switch (test.TestName) {
				case "xammac tests":
					switch (test.ProjectConfiguration) {
					case "Release":
						yield return new TestData { Variation = "Release (all optimizations)", MonoBundlingExtraArgs = "--registrar:static --optimize:all", Debug = false, LinkMode = "Full", Defines = "OPTIMIZEALL"};
						break;
					case "Debug":
						yield return new TestData { Variation = "Debug (all optimizations)", MonoBundlingExtraArgs = "--registrar:static --optimize:all,-remove-uithread-checks", Debug = true, LinkMode = "Full", Defines = "OPTIMIZEALL", Ignored = !IncludeAll };
						break;
					}
					break;
				}
				break;
			default:
				throw new NotImplementedException (test.ProjectPlatform);
			}
		}

		IEnumerable<T> CreateTestVariations<T> (IEnumerable<T> tests, Func<XBuildTask, T, T> creator) where T: RunTestTask
		{
			foreach (var task in tests) {
				if (string.IsNullOrEmpty (task.Variation))
					task.Variation = "Debug";
			}

			var rv = new List<T> (tests);
			foreach (var task in tests.ToArray ()) {
				foreach (var test_data in GetTestData (task)) {
					var variation = test_data.Variation;
					var mtouch_extra_args = test_data.MTouchExtraArgs;
					var bundling_extra_args = test_data.MonoBundlingExtraArgs;
					var configuration = test_data.Debug ? task.ProjectConfiguration : task.ProjectConfiguration.Replace ("Debug", "Release");
					var debug = test_data.Debug;
					var profiling = test_data.Profiling;
					var link_mode = test_data.LinkMode;
					var defines = test_data.Defines;
					var undefines = test_data.Undefines;
					var ignored = test_data.Ignored;

					var clone = task.TestProject.Clone ();
					var clone_task = Task.Run (async () => {
						await task.BuildTask.InitialTask; // this is the project cloning above
						await clone.CreateCopyAsync (task);

						var isMac = false;
						switch (task.Platform) {
						case TestPlatform.Mac:
						case TestPlatform.Mac_Classic:
						case TestPlatform.Mac_Unified:
						case TestPlatform.Mac_Unified32:
						case TestPlatform.Mac_UnifiedXM45:
						case TestPlatform.Mac_UnifiedXM45_32:
						case TestPlatform.Mac_UnifiedSystem:
							isMac = true;
							break;
						}

						if (!string.IsNullOrEmpty (mtouch_extra_args))
							clone.Xml.AddExtraMtouchArgs (mtouch_extra_args, task.ProjectPlatform, configuration);
						if (!string.IsNullOrEmpty (bundling_extra_args))
							clone.Xml.AddMonoBundlingExtraArgs (bundling_extra_args, task.ProjectPlatform, configuration);
						if (!string.IsNullOrEmpty (link_mode))
							clone.Xml.SetNode (isMac ? "LinkMode" : "MtouchLink", link_mode, task.ProjectPlatform, configuration);
						if (!string.IsNullOrEmpty (defines)) {
							clone.Xml.AddAdditionalDefines (defines, task.ProjectPlatform, configuration);
							if (clone.ProjectReferences != null) {
								foreach (var pr in clone.ProjectReferences) {
									pr.Xml.AddAdditionalDefines (defines, task.ProjectPlatform, configuration);
									pr.Xml.Save (pr.Path);
								}
							}
						}
						if (!string.IsNullOrEmpty (undefines)) {
							clone.Xml.RemoveDefines (undefines, task.ProjectPlatform, configuration);
							if (clone.ProjectReferences != null) {
								foreach (var pr in clone.ProjectReferences) {
									pr.Xml.RemoveDefines (undefines, task.ProjectPlatform, configuration);
									pr.Xml.Save (pr.Path);
								}
							}
						}
						clone.Xml.SetNode (isMac ? "Profiling" : "MTouchProfiling", profiling ? "True" : "False", task.ProjectPlatform, configuration);

						if (!debug && !isMac)
							clone.Xml.SetMtouchUseLlvm (true, task.ProjectPlatform, configuration);
						clone.Xml.Save (clone.Path);
					});

					var build = new XBuildTask {
						Jenkins = this,
						TestProject = clone,
						ProjectConfiguration = configuration,
						ProjectPlatform = task.ProjectPlatform,
						Platform = task.Platform,
						InitialTask = clone_task,
						TestName = clone.Name,
					};
					T newVariation = creator (build, task);
					newVariation.Variation = variation;
					newVariation.Ignored = task.Ignored || ignored;
					newVariation.BuildOnly = task.BuildOnly;
					rv.Add (newVariation);
				}
			}

			return rv;
		}

		IEnumerable<TestTask> CreateRunSimulatorTasks ()
		{
			var runSimulatorTasks = new List<RunSimulatorTask> ();

			foreach (var project in Harness.IOSTestProjects) {
				if (!project.IsExecutableProject)
					continue;

				bool ignored = !IncludeSimulator;
				if (!IsIncluded (project))
					ignored = true;

				var ps = new List<Tuple<TestProject, TestPlatform, bool>> ();
				if (!project.SkipiOSVariation)
					ps.Add (new Tuple<TestProject, TestPlatform, bool> (project, TestPlatform.iOS_Unified, ignored || !IncludeiOS));
				if (!project.SkiptvOSVariation)
					ps.Add (new Tuple<TestProject, TestPlatform, bool> (project.AsTvOSProject (), TestPlatform.tvOS, ignored || !IncludetvOS));
				if (!project.SkipwatchOSVariation)
					ps.Add (new Tuple<TestProject, TestPlatform, bool> (project.AsWatchOSProject (), TestPlatform.watchOS, ignored || !IncludewatchOS));
				
				var configurations = project.Configurations;
				if (configurations == null)
					configurations = new string [] { "Debug" };
				foreach (var config in configurations) {
					foreach (var pair in ps) {
						var derived = new XBuildTask () {
							Jenkins = this,
							ProjectConfiguration = config,
							ProjectPlatform = "iPhoneSimulator",
							Platform = pair.Item2,
							Ignored = pair.Item3,
							TestName = project.Name,
							Dependency = project.Dependency,
						};
						derived.CloneTestProject (pair.Item1);
						var simTasks = CreateRunSimulatorTaskAsync (derived);
						runSimulatorTasks.AddRange (simTasks);
						if (configurations.Length > 1) {
							foreach (var task in simTasks)
								task.Variation = config;
						}
					}
				}
			}

			var testVariations = CreateTestVariations (runSimulatorTasks, (buildTask, test) => new RunSimulatorTask (buildTask, test.Candidates)).ToList ();

			foreach (var taskGroup in testVariations.GroupBy ((RunSimulatorTask task) => task.Platform)) {
				yield return new AggregatedRunSimulatorTask (taskGroup) {
					Jenkins = this,
					TestName = $"Tests for {taskGroup.Key}",
				};
			}
		}

		IEnumerable<TestTask> CreateRunDeviceTasks ()
		{
			var rv = new List<RunDeviceTask> ();

			foreach (var project in Harness.IOSTestProjects) {
				if (!project.IsExecutableProject)
					continue;
				
				bool ignored = !IncludeDevice;
				if (!IsIncluded (project))
					ignored = true;

				if (!project.SkipiOSVariation) {
					var build64 = new XBuildTask {
						Jenkins = this,
						ProjectConfiguration = "Debug64",
						ProjectPlatform = "iPhone",
						Platform = TestPlatform.iOS_Unified64,
						TestName = project.Name,
					};
					build64.CloneTestProject (project);
					rv.Add (new RunDeviceTask (build64, Devices.Connected64BitIOS) { Ignored = ignored || !IncludeiOS, BuildOnly = project.BuildOnly });

					var build32 = new XBuildTask {
						Jenkins = this,
						ProjectConfiguration = "Debug32",
						ProjectPlatform = "iPhone",
						Platform = TestPlatform.iOS_Unified32,
						TestName = project.Name,
					};
					build32.CloneTestProject (project);
					rv.Add (new RunDeviceTask (build32, Devices.Connected32BitIOS) { Ignored = ignored || !IncludeiOS, BuildOnly = project.BuildOnly });

					var todayProject = project.AsTodayExtensionProject ();
					var buildToday = new XBuildTask {
						Jenkins = this,
						ProjectConfiguration = "Debug64",
						ProjectPlatform = "iPhone",
						Platform = TestPlatform.iOS_TodayExtension64,
						TestName = project.Name,
					};
					buildToday.CloneTestProject (todayProject);
					//rv.Add (new RunDeviceTask (buildToday, Devices.Connected64BitIOS) { Ignored = ignored || !IncludeiOSExtensions, BuildOnly = project.BuildOnly || ForceExtensionBuildOnly });
					var ignoreExtension = ignored || !IncludeiOSExtensions;
					if (buildToday.TestName == "fsharp")
						ignoreExtension = true; // https://github.com/xamarin/xamarin-macios/issues/3684
					rv.Add (new RunDeviceTask (buildToday, Devices.ConnectedDevices.Where ((dev) => dev.DevicePlatform == DevicePlatform.iOS && dev.Supports64Bit)) {
						Ignored = ignoreExtension,
						BuildOnly = BuildiOSExtensions,
					});
				}

				if (!project.SkiptvOSVariation) {
					var tvOSProject = project.AsTvOSProject ();
					var buildTV = new XBuildTask {
						Jenkins = this,
						ProjectConfiguration = "Debug",
						ProjectPlatform = "iPhone",
						Platform = TestPlatform.tvOS,
						TestName = project.Name,
					};
					buildTV.CloneTestProject (tvOSProject);
					rv.Add (new RunDeviceTask (buildTV, Devices.ConnectedTV) { Ignored = ignored || !IncludetvOS, BuildOnly = project.BuildOnly });
				}

				if (!project.SkipwatchOSVariation) {
					var watchOSProject = project.AsWatchOSProject ();
					var buildWatch = new XBuildTask {
						Jenkins = this,
						ProjectConfiguration = "Debug",
						ProjectPlatform = "iPhone",
						Platform = TestPlatform.watchOS,
						TestName = project.Name,
					};
					buildWatch.CloneTestProject (watchOSProject);
					rv.Add (new RunDeviceTask (buildWatch, Devices.ConnectedWatch) { Ignored = ignored || !IncludewatchOS, BuildOnly = project.BuildOnly });
				}
			}

			return CreateTestVariations (rv, (buildTask, test) => new RunDeviceTask (buildTask, test.Candidates));
		}

		static string AddSuffixToPath (string path, string suffix)
		{
			return Path.Combine (Path.GetDirectoryName (path), Path.GetFileNameWithoutExtension (path) + suffix + Path.GetExtension (path));
		}

		void SelectTests ()
		{
			int pull_request;

			if (!int.TryParse (Environment.GetEnvironmentVariable ("ghprbPullId"), out pull_request))
				MainLog.WriteLine ("The environment variable 'ghprbPullId' was not found, so no pull requests will be checked for test selection.");

			// First check if can auto-select any tests based on which files were modified.
			// This will only enable additional tests, never disable tests.
			if (pull_request > 0)
				SelectTestsByModifiedFiles (pull_request);
			
			// Then we check for labels. Labels are manually set, so those override
			// whatever we did automatically.
			SelectTestsByLabel (pull_request);

			DisableKnownFailingDeviceTests ();

			if (!Harness.INCLUDE_IOS) {
				MainLog.WriteLine ("The iOS build is diabled, so any iOS tests will be disabled as well.");
				IncludeiOS = false;
			}

			if (!Harness.INCLUDE_WATCH) {
				MainLog.WriteLine ("The watchOS build is disabled, so any watchOS tests will be disabled as well.");
				IncludewatchOS = false;
			}

			if (!Harness.INCLUDE_TVOS) {
				MainLog.WriteLine ("The tvOS build is disabled, so any tvOS tests will be disabled as well.");
				IncludetvOS = false;
			}

			if (!Harness.INCLUDE_MAC) {
				MainLog.WriteLine ("The macOS build is disabled, so any macOS tests will be disabled as well.");
				IncludeMac = false;
			}
		}

		void DisableKnownFailingDeviceTests ()
		{
			// https://github.com/xamarin/maccore/issues/1008
			ForceExtensionBuildOnly = true;

			// https://github.com/xamarin/maccore/issues/1011
			foreach (var mono in Harness.IOSTestProjects.Where (x => x.Name == "mini"))
				mono.BuildOnly = true;
		}

		void SelectTestsByModifiedFiles (int pull_request)
		{
			var files = GitHub.GetModifiedFiles (Harness, pull_request);

			MainLog.WriteLine ("Found {0} modified file(s) in the pull request #{1}.", files.Count (), pull_request);
			foreach (var f in files)
				MainLog.WriteLine ("    {0}", f);

			// We select tests based on a prefix of the modified files.
			// Add entries here to check for more prefixes.
			var mtouch_prefixes = new string [] {
				"tests/mtouch",
				"tests/common",
				"tools/mtouch",
				"tools/common",
				"tools/linker",
				"src/ObjCRuntime/Registrar.cs",
				"external/mono",
				"external/llvm",
				"msbuild",
			};
			var mmp_prefixes = new string [] {
				"tests/mmptest",
				"tests/common",
				"tools/mmp",
				"tools/common",
				"tools/linker",
				"src/ObjCRuntime/Registrar.cs",
				"external/mono",
				"msbuild",
			};
			var bcl_prefixes = new string [] {
				"tests/bcl-test",
				"tests/common",
				"external/mono",
				"external/llvm",
			};
			var btouch_prefixes = new string [] {
				"src/btouch.cs",
				"src/generator.cs",
				"src/generator-",
				"src/Makefile.generator",
				"tests/generator",
				"tests/common",
			};
			var mac_binding_project = new string [] {
				"msbuild",
				"tests/mac-binding-project",
				"tests/common/mac",
			}.Intersect (btouch_prefixes).ToArray ();
			var xtro_prefixes = new string [] {
				"tests/xtro-sharpie",
				"src",
				"Make.config",
			};

			SetEnabled (files, mtouch_prefixes, "mtouch", ref IncludeMtouch);
			SetEnabled (files, mmp_prefixes, "mmp", ref IncludeMmpTest);
			SetEnabled (files, bcl_prefixes, "bcl", ref IncludeBcl);
			SetEnabled (files, btouch_prefixes, "btouch", ref IncludeBtouch);
			SetEnabled (files, mac_binding_project, "mac-binding-project", ref IncludeMacBindingProject);
			SetEnabled (files, xtro_prefixes, "xtro", ref IncludeXtro);
		}

		void SetEnabled (IEnumerable<string> files, string [] prefixes, string testname, ref bool value)
		{
			foreach (var file in files) {
				foreach (var prefix in prefixes) {
					if (file.StartsWith (prefix, StringComparison.Ordinal)) {
						value = true;
						MainLog.WriteLine ("Enabled '{0}' tests because the modified file '{1}' matches prefix '{2}'", testname, file, prefix);
						return;
					}
				}
			}
		}

		void SelectTestsByLabel (int pull_request)
		{
			var labels = new HashSet<string> ();
			labels.UnionWith (Harness.Labels);
			if (pull_request > 0)
				labels.UnionWith (GitHub.GetLabels (Harness, pull_request));

			MainLog.WriteLine ("Found {1} label(s) in the pull request #{2}: {0}", string.Join (", ", labels.ToArray ()), labels.Count (), pull_request);

			// disabled by default
			SetEnabled (labels, "mtouch", ref IncludeMtouch);
			SetEnabled (labels, "mmp", ref IncludeMmpTest);
			SetEnabled (labels, "bcl", ref IncludeBcl);
			SetEnabled (labels, "btouch", ref IncludeBtouch);
			SetEnabled (labels, "mac-binding-project", ref IncludeMacBindingProject);
			SetEnabled (labels, "ios-extensions", ref IncludeiOSExtensions);
			SetEnabled (labels, "ios-device", ref IncludeDevice);
			SetEnabled (labels, "xtro", ref IncludeXtro);
			SetEnabled (labels, "mac-32", ref IncludeMac32);
			SetEnabled (labels, "all", ref IncludeAll);

			if (labels.Contains ("build-ios-extensions-tests")) {
				BuildiOSExtensions = true;
				IncludeiOSExtensions = true;
			}

			// enabled by default
			SetEnabled (labels, "ios", ref IncludeiOS);
			SetEnabled (labels, "tvos", ref IncludetvOS);
			SetEnabled (labels, "watchos", ref IncludewatchOS);
			SetEnabled (labels, "mac", ref IncludeMac);
			SetEnabled (labels, "mac-classic", ref IncludeClassicMac);
			SetEnabled (labels, "ios-msbuild", ref IncludeiOSMSBuild);
			SetEnabled (labels, "ios-simulator", ref IncludeSimulator);
			bool inc_permission_tests = Harness.IncludeSystemPermissionTests;
			SetEnabled (labels, "system-permission", ref inc_permission_tests);
			Harness.IncludeSystemPermissionTests = inc_permission_tests;
		}

		void SetEnabled (HashSet<string> labels, string testname, ref bool value)
		{
			if (labels.Contains ("skip-" + testname + "-tests")) {
				MainLog.WriteLine ("Disabled '{0}' tests because the label 'skip-{0}-tests' is set.", testname);
				value = false;
			} else if (labels.Contains ("run-" + testname + "-tests")) {
				MainLog.WriteLine ("Enabled '{0}' tests because the label 'run-{0}-tests' is set.", testname);
				value = true;
			} else if (labels.Contains ("skip-all-tests")) {
				MainLog.WriteLine ("Disabled '{0}' tests because the label 'skip-all-tests' is set.", testname);
				value = false;
			} else if (labels.Contains ("run-all-tests")) {
				MainLog.WriteLine ("Enabled '{0}' tests because the label 'run-all-tests' is set.", testname);
				value = true;
			}
			// respect any default value
		}

		async Task PopulateTasksAsync ()
		{
			// Missing:
			// api-diff

			SelectTests ();

			await LoadSimulatorsAndDevicesAsync ();

			Tasks.AddRange (CreateRunSimulatorTasks ());

			var buildiOSMSBuild = new XBuildTask ()
			{
				Jenkins = this,
				TestProject = new TestProject (Path.GetFullPath (Path.Combine (Harness.RootDirectory, "..", "msbuild", "Xamarin.MacDev.Tasks.sln"))),
				SpecifyPlatform = false,
				SpecifyConfiguration = false,
				Platform = TestPlatform.iOS,
				UseMSBuild = true,
			};
			var nunitExecutioniOSMSBuild = new NUnitExecuteTask (buildiOSMSBuild)
			{
				TestLibrary = Path.Combine (Harness.RootDirectory, "..", "msbuild", "tests", "bin", "Xamarin.iOS.Tasks.Tests.dll"),
				TestExecutable = Path.Combine (Harness.RootDirectory, "..", "packages", "NUnit.Runners.2.6.4", "tools", "nunit-console.exe"),
				WorkingDirectory = Path.Combine (Harness.RootDirectory, "..", "packages", "NUnit.Runners.2.6.4", "tools", "lib"),
				Platform = TestPlatform.iOS,
				TestName = "MSBuild tests",
				Mode = "iOS",
				Timeout = TimeSpan.FromMinutes (60),
				Ignored = !IncludeiOSMSBuild,
			};
			Tasks.Add (nunitExecutioniOSMSBuild);
			
			var buildInstallSources = new XBuildTask ()
			{
				Jenkins = this,
				TestProject = new TestProject (Path.GetFullPath (Path.Combine (Harness.RootDirectory, "..", "tools", "install-source", "InstallSourcesTests", "InstallSourcesTests.csproj"))),
				SpecifyPlatform = false,
				SpecifyConfiguration = false,
				Platform = TestPlatform.iOS,
			};
			buildInstallSources.SolutionPath = Path.GetFullPath (Path.Combine (Harness.RootDirectory, "..", "tools", "install-source", "install-source.sln")); // this is required for nuget restore to be executed
			var nunitExecutionInstallSource = new NUnitExecuteTask (buildInstallSources)
			{
				TestLibrary = Path.Combine (Harness.RootDirectory, "..", "tools", "install-source", "InstallSourcesTests", "bin", "Release", "InstallSourcesTests.dll"),
				TestExecutable = Path.Combine (Harness.RootDirectory, "..", "packages", "NUnit.Runners.2.6.4", "tools", "nunit-console.exe"),
				WorkingDirectory = Path.Combine (Harness.RootDirectory, "..", "packages", "NUnit.Runners.2.6.4", "tools", "lib"),
				Platform = TestPlatform.iOS,
				TestName = "Install Sources tests",
				Mode = "iOS",
				Timeout = TimeSpan.FromMinutes (60),
				Ignored = !IncludeMac && !IncludeSimulator,
			};
			Tasks.Add (nunitExecutionInstallSource);

			foreach (var project in Harness.MacTestProjects) {
				bool ignored = !IncludeMac;
				bool ignored32 = !IncludeMac || !IncludeMac32;
				if (!IncludeMmpTest && project.Path.Contains ("mmptest"))
					ignored = true;

				if (!IsIncluded (project))
					ignored = true;

				var configurations = project.Configurations;
				if (configurations == null)
					configurations = new string [] { "Debug" };
				foreach (var config in configurations) {
					BuildProjectTask build;
					if (project.GenerateVariations) {
						build = new MdtoolTask ();
						build.Platform = TestPlatform.Mac_Classic;
						build.TestProject = project;
					} else {
						build = new XBuildTask ();
						build.Platform = TestPlatform.Mac;
						build.CloneTestProject (project);
					}
					build.Jenkins = this;
					build.SolutionPath = project.SolutionPath;
					build.ProjectConfiguration = config;
					build.ProjectPlatform = project.Platform;
					build.SpecifyPlatform = false;
					build.SpecifyConfiguration = build.ProjectConfiguration != "Debug";
					RunTestTask exec;
					IEnumerable<RunTestTask> execs;
					var ignored_main = ignored;
					if ((ignored32 || !IncludeClassicMac) && project.GenerateVariations) {
						ignored_main = true; // Only if generating variations is the main project is an XM Classic app
						build.RequiresXcode94 = true;
					}
					if (project.IsNUnitProject) {
						var dll = Path.Combine (Path.GetDirectoryName (build.TestProject.Path), project.Xml.GetOutputAssemblyPath (build.ProjectPlatform, build.ProjectConfiguration).Replace ('\\', '/'));
						exec = new NUnitExecuteTask (build) {
							Ignored = ignored_main,
							TestLibrary = dll,
							TestExecutable = Path.Combine (Harness.RootDirectory, "..", "packages", "NUnit.ConsoleRunner.3.5.0", "tools", "nunit3-console.exe"),
							WorkingDirectory = Path.GetDirectoryName (dll),
							Platform = build.Platform,
							TestName = project.Name,
							Timeout = TimeSpan.FromMinutes (120),
							Mode = "macOS",
						};
						execs = new [] { exec };
					} else {
						exec = new MacExecuteTask (build) {
							Ignored = ignored_main,
							BCLTest = project.IsBclTest,
							TestName = project.Name,
							IsUnitTest = true,
						};
						execs = CreateTestVariations (new [] { exec }, (buildTask, test) => new MacExecuteTask (buildTask) { IsUnitTest = true } );
					}
					exec.Variation = configurations.Length > 1 ? config : project.TargetFrameworkFlavor.ToString ();

					Tasks.AddRange (execs);
					foreach (var e in execs) {
						if (project.GenerateVariations) {
							Tasks.Add (CloneExecuteTask (e, project, TestPlatform.Mac_Unified, "-unified", ignored));
							Tasks.Add (CloneExecuteTask (e, project, TestPlatform.Mac_Unified32, "-unified-32", ignored32, true));
							if (project.GenerateFull) {
								Tasks.Add (CloneExecuteTask (e, project, TestPlatform.Mac_UnifiedXM45, "-unifiedXM45", ignored));
								Tasks.Add (CloneExecuteTask (e, project, TestPlatform.Mac_UnifiedXM45_32, "-unifiedXM45-32", ignored32, true));
							}
							if (project.GenerateSystem)
								Tasks.Add (CloneExecuteTask (e, project, TestPlatform.Mac_UnifiedSystem, "-system", ignored));
						}
					}
				}
			}

			var buildMTouch = new MakeTask ()
			{
				Jenkins = this,
				TestProject = new TestProject (Path.GetFullPath (Path.Combine (Harness.RootDirectory, "mtouch", "mtouch.sln"))),
				SpecifyPlatform = false,
				SpecifyConfiguration = false,
				Platform = TestPlatform.iOS,
				Target = "dependencies",
				WorkingDirectory = Path.GetFullPath (Path.Combine (Harness.RootDirectory, "mtouch")),
			};
			var nunitExecutionMTouch = new NUnitExecuteTask (buildMTouch)
			{
				TestLibrary = Path.Combine (Harness.RootDirectory, "mtouch", "bin", "Debug", "mtouch.dll"),
				TestExecutable = Path.Combine (Harness.RootDirectory, "..", "packages", "NUnit.ConsoleRunner.3.5.0", "tools", "nunit3-console.exe"),
				WorkingDirectory = Path.Combine (Harness.RootDirectory, "mtouch", "bin", "Debug"),
				Platform = TestPlatform.iOS,
				TestName = "MTouch tests",
				Timeout = TimeSpan.FromMinutes (120),
				Ignored = !IncludeMtouch,
			};
			Tasks.Add (nunitExecutionMTouch);

			var buildGenerator = new MakeTask {
				Jenkins = this,
				TestProject = new TestProject (Path.GetFullPath (Path.Combine (Harness.RootDirectory, "..", "src", "generator.sln"))),
				SpecifyPlatform = false,
				SpecifyConfiguration = false,
				Platform = TestPlatform.iOS,
				Target = "build-unit-tests",
				WorkingDirectory = Path.GetFullPath (Path.Combine (Harness.RootDirectory, "generator")),
			};
			var runGenerator = new NUnitExecuteTask (buildGenerator) {
				TestLibrary = Path.Combine (Harness.RootDirectory, "generator", "bin", "Debug", "generator-tests.dll"),
				TestExecutable = Path.Combine (Harness.RootDirectory, "..", "packages", "NUnit.ConsoleRunner.3.5.0", "tools", "nunit3-console.exe"),
				WorkingDirectory = Path.Combine (Harness.RootDirectory, "generator", "bin", "Debug"),
				Platform = TestPlatform.iOS,
				TestName = "Generator tests",
				Timeout = TimeSpan.FromMinutes (10),
				Ignored = !IncludeBtouch,
			};
			Tasks.Add (runGenerator);

			var run_mmp = new MakeTask
			{
				Jenkins = this,
				Platform = TestPlatform.Mac,
				TestName = "MMP Regression Tests",
				Target = "all", // -j" + Environment.ProcessorCount,
				WorkingDirectory = Path.Combine (Harness.RootDirectory, "mmptest", "regression"),
				Ignored = !IncludeMmpTest || !IncludeMac,
				Timeout = TimeSpan.FromMinutes (30),
				SupportsParallelExecution = false, // Already doing parallel execution by running "make -jX"
			};
			run_mmp.CompletedTask = new Task (() =>
			{
				foreach (var log in Directory.GetFiles (Path.GetFullPath (run_mmp.WorkingDirectory), "*.log", SearchOption.AllDirectories))
					run_mmp.Logs.AddFile (log, log.Substring (run_mmp.WorkingDirectory.Length + 1));
			});
			run_mmp.Environment.Add ("BUILD_REVISION", "jenkins"); // This will print "@MonkeyWrench: AddFile: <log path>" lines, which we can use to get the log filenames.
			Tasks.Add (run_mmp);

			var runMacBindingProject = new MakeTask
			{
				Jenkins = this,
				Platform = TestPlatform.Mac,
				TestName = "Mac Binding Projects",
				Target = "all",
				WorkingDirectory = Path.Combine (Harness.RootDirectory, "mac-binding-project"),
				Ignored = !IncludeMacBindingProject || !IncludeMac,
				Timeout = TimeSpan.FromMinutes (15),
			};
			Tasks.Add (runMacBindingProject);

			var buildXtroTests = new MakeTask {
				Jenkins = this,
				Platform = TestPlatform.All,
				TestName = "Xtro",
				Target = "wrench",
				WorkingDirectory = Path.Combine (Harness.RootDirectory, "xtro-sharpie"),
				Ignored = !IncludeXtro,
				Timeout = TimeSpan.FromMinutes (15),
			};
			var runXtroReporter = new RunXtroTask (buildXtroTests) {
				Jenkins = this,
				Platform = TestPlatform.Mac,
				TestName = buildXtroTests.TestName,
				Ignored = buildXtroTests.Ignored,
				WorkingDirectory = buildXtroTests.WorkingDirectory,
			};
			Tasks.Add (runXtroReporter);

			Tasks.AddRange (CreateRunDeviceTasks ());
		}

		RunTestTask CloneExecuteTask (RunTestTask task, TestProject original_project, TestPlatform platform, string suffix, bool ignore, bool requiresXcode94 = false)
		{
			var build = new XBuildTask ()
			{
				Platform = platform,
				Jenkins = task.Jenkins,
				ProjectConfiguration = task.ProjectConfiguration,
				ProjectPlatform = task.ProjectPlatform,
				SpecifyPlatform = task.BuildTask.SpecifyPlatform,
				SpecifyConfiguration = task.BuildTask.SpecifyConfiguration,
			};
			var tp = new TestProject (Path.ChangeExtension (AddSuffixToPath (original_project.Path, suffix), "csproj"));
			build.CloneTestProject (tp);
			build.RequiresXcode94 = requiresXcode94;

			var macExec = task as MacExecuteTask;
			if (macExec != null) {
				return new MacExecuteTask (build) {
					Ignored = ignore,
					TestName = task.TestName,
					IsUnitTest = macExec.IsUnitTest,
				};
			}
			var nunit = task as NUnitExecuteTask;
			if (nunit != null) {
				var project = build.TestProject;
				var dll = Path.Combine (Path.GetDirectoryName (project.Path), project.Xml.GetOutputAssemblyPath (build.ProjectPlatform, build.ProjectConfiguration).Replace ('\\', '/'));
				return new NUnitExecuteTask (build) {
					Ignored = ignore,
					TestName = build.TestName,
					TestLibrary = dll,
					TestExecutable = Path.Combine (Harness.RootDirectory, "..", "packages", "NUnit.ConsoleRunner.3.5.0", "tools", "nunit3-console.exe"),
					WorkingDirectory = Path.GetDirectoryName (dll),
					Platform = build.Platform,
					Timeout = TimeSpan.FromMinutes (120),
				};
			}
			throw new NotImplementedException ();
		}

		async Task ExecutePeriodicCommandAsync (Log periodic_loc, CancellationToken cancellation_token)
		{
			//await Task.Delay (Harness.UploadInterval);
			periodic_loc.WriteLine ($"Starting periodic task with interval {Harness.PeriodicCommandInterval.TotalMinutes} minutes.");
			while (!cancellation_token.IsCancellationRequested) {
				var watch = Stopwatch.StartNew ();
				using (var process = new Process ()) {
					process.StartInfo.FileName = Harness.PeriodicCommand;
					process.StartInfo.Arguments = Harness.PeriodicCommandArguments;
					var rv = await process.RunAsync (periodic_loc, cancellation_token: cancellation_token, timeout: TimeSpan.FromMinutes (10));
					if (!rv.Succeeded)
						periodic_loc.WriteLine ($"Periodic command failed with exit code {rv.ExitCode} (Timed out: {rv.TimedOut})");
				}
				var ticksLeft = Harness.PeriodicCommandInterval.Ticks - watch.ElapsedTicks;
				if (ticksLeft < 0)
					ticksLeft = Harness.PeriodicCommandInterval.Ticks;
				var wait = TimeSpan.FromTicks (ticksLeft);
				await Task.Delay (wait, cancellation_token)
						  .ContinueWith (_ => periodic_loc.WriteLine ("Periodic execution cancelled."), TaskContinuationOptions.OnlyOnCanceled)
				          .ContinueWith (_ => { /* I wish I new why this was needed, but the wait will never return complete otherwise (unless cancelled) */ });
			}
			periodic_loc.WriteLine ("Periodic execution completed.");
		}

		public int Run ()
		{
			try {
				Directory.CreateDirectory (LogDirectory);
				Log log = Logs.Create ($"Harness-{Harness.Timestamp}.log", "Harness log");
				if (Harness.InWrench)
					log = Log.CreateAggregatedLog (log, new ConsoleLog ());
				Harness.HarnessLog = MainLog = log;
				Harness.HarnessLog.Timestamp = true;

				var tasks = new List<Task> ();
				if (IsServerMode)
					tasks.Add (RunTestServer ());

				if (Harness.InJenkins) {
					Task.Factory.StartNew (async () => {
						while (true) {
							await Task.Delay (TimeSpan.FromMinutes (10));
							Console.WriteLine ("Still running tests. Please be patient.");
						}
					});
				}

				CancellationTokenSource periodic_cancellation = null; 
				Task periodic_task = null;
				if (!string.IsNullOrEmpty (Harness.PeriodicCommand)) {
					periodic_cancellation = new CancellationTokenSource ();
					var periodic_log = Logs.Create ("PeriodicCommand.log", "Periodic command log");
					periodic_log.Timestamp = true;
					periodic_task = ExecutePeriodicCommandAsync (periodic_log, periodic_cancellation.Token);
				}

				Task.Run (async () =>
				{
					await SimDevice.KillEverythingAsync (MainLog);
					await PopulateTasksAsync ();
					populating = false;
				}).Wait ();
				GenerateReport ();
				BuildTestLibraries ();
				if (!IsServerMode) {
					foreach (var task in Tasks)
						tasks.Add (task.RunAsync ());
				}
				Task.WaitAll (tasks.ToArray ());
				GenerateReport ();

				try {
					periodic_cancellation?.Cancel ();
					periodic_task?.Wait ();
				} catch (AggregateException) {
					// The above can throw a TaskCanceledException, which we'll just ignore.
				}

				return Tasks.Any ((v) => v.Failed || v.Skipped) ? 1 : 0;
			} catch (Exception ex) {
				MainLog.WriteLine ("Unexpected exception: {0}", ex);
				Console.WriteLine ("Unexpected exception: {0}", ex);
				return 2;
			}
		}

		public bool IsServerMode {
			get { return Harness.JenkinsConfiguration == "server"; }
		}

		void BuildTestLibraries ()
		{
			ProcessHelper.ExecuteCommandAsync ("make", $"all -j{Environment.ProcessorCount} -C {StringUtils.Quote (Path.Combine (Harness.RootDirectory, "test-libraries"))}", MainLog, TimeSpan.FromMinutes (10)).Wait ();
		}

		Task RunTestServer ()
		{
			var server = new HttpListener ();

			// Try and find an unused port
			int attemptsLeft = 50;
			int port = 51234; // Try this port first, to try to not vary between runs just because.
			Random r = new Random ((int) DateTime.Now.Ticks);
			while (attemptsLeft-- > 0) {
				var newPort = port != 0 ? port : r.Next (49152, 65535); // The suggested range for dynamic ports is 49152-65535 (IANA)
				server.Prefixes.Clear ();
				server.Prefixes.Add ("http://*:" + newPort + "/");
				try {
					server.Start ();
					port = newPort;
					break;
				} catch (Exception ex) {
					MainLog.WriteLine ("Failed to listen on port {0}: {1}", newPort, ex.Message);
					port = 0;
				}
			}
			MainLog.WriteLine ($"Created server on localhost:{port}");

			var tcs = new TaskCompletionSource<bool> ();
			var thread = new System.Threading.Thread (() =>
			{
				while (server.IsListening) {
					var context = server.GetContext ();
					var request = context.Request;
					var response = context.Response;
					var arguments = System.Web.HttpUtility.ParseQueryString (request.Url.Query);
					try {
						var allTasks = Tasks.SelectMany ((v) =>
						{
							var rv = new List<TestTask> ();
							var runsim = v as AggregatedRunSimulatorTask;
							if (runsim != null)
								rv.AddRange (runsim.Tasks);
							rv.Add (v);
							return rv;
						});
						string serveFile = null;
						switch (request.Url.LocalPath) {
						case "/":
							response.ContentType = System.Net.Mime.MediaTypeNames.Text.Html;
							GenerateReportImpl (response.OutputStream);
							break;
						case "/select":
						case "/deselect":
							response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
							using (var writer = new StreamWriter (response.OutputStream)) {
								foreach (var task in allTasks) {
									bool? is_match = null;
									if (!(task.Ignored || task.NotStarted))
										continue;
									switch (request.Url.Query) {
									case "?all":
										is_match = true;
										break;
									case "?all-device":
										is_match = task is RunDeviceTask;
										break;
									case "?all-simulator":
										is_match = task is RunSimulatorTask;
										break;
									case "?all-ios":
										switch (task.Platform) {
										case TestPlatform.iOS:
										case TestPlatform.iOS_TodayExtension64:
										case TestPlatform.iOS_Unified:
										case TestPlatform.iOS_Unified32:
										case TestPlatform.iOS_Unified64:
											is_match = true;
											break;
										default:
											if (task.Platform.ToString ().StartsWith ("iOS", StringComparison.Ordinal))
												throw new NotImplementedException ();
											break;
										}
										break;
									case "?all-tvos":
										switch (task.Platform) {
										case TestPlatform.tvOS:
											is_match = true;
											break;
										default:
											if (task.Platform.ToString ().StartsWith ("tvOS", StringComparison.Ordinal))
												throw new NotImplementedException ();
											break;
										}
										break;
									case "?all-watchos":
										switch (task.Platform) {
										case TestPlatform.watchOS:
											is_match = true;
											break;
										default:
											if (task.Platform.ToString ().StartsWith ("watchOS", StringComparison.Ordinal))
												throw new NotImplementedException ();
											break;
										}
										break;
									case "?all-mac":
										switch (task.Platform) {
										case TestPlatform.Mac:
										case TestPlatform.Mac_Classic:
										case TestPlatform.Mac_Unified:
										case TestPlatform.Mac_Unified32:
										case TestPlatform.Mac_UnifiedXM45:
										case TestPlatform.Mac_UnifiedXM45_32:
										case TestPlatform.Mac_UnifiedSystem:
											is_match = true;
											break;
										default:
											if (task.Platform.ToString ().StartsWith ("Mac", StringComparison.Ordinal))
												throw new NotImplementedException ();
											break;
										}
										break;
									default:
										writer.WriteLine ("unknown query: {0}", request.Url.Query);
										break;
									}
									if (request.Url.LocalPath == "/select") {
										if (is_match.HasValue && is_match.Value)
											task.Ignored = false;
									} else if (request.Url.LocalPath == "/deselect") {
										if (is_match.HasValue && is_match.Value)
											task.Ignored = true;
									}
								}

								writer.WriteLine ("OK");
							}
							break;
						case "/runalltests":
							response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
							using (var writer = new StreamWriter (response.OutputStream)) {

								// We want to randomize the order the tests are added, so that we don't build first the test for one device, 
								// then for another, since that would not take advantage of running tests on several devices in parallel.
								var rnd = new Random ((int) DateTime.Now.Ticks);
								foreach (var task in Tasks.OrderBy (v => rnd.Next ())) {
									if (task.InProgress || task.Waiting) {
										writer.WriteLine ($"Test '{task.TestName}' is already executing.");
									} else {
										task.Reset ();
										task.RunAsync ();
									}
								}

								writer.WriteLine ("OK");
							}
							break;
						case "/runselected":
							response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
							using (var writer = new StreamWriter (response.OutputStream)) {
								// We want to randomize the order the tests are added, so that we don't build first the test for one device, 
								// then for another, since that would not take advantage of running tests on several devices in parallel.
								var rnd = new Random ((int) DateTime.Now.Ticks);
								foreach (var task in allTasks.Where ((v) => !v.Ignored).OrderBy (v => rnd.Next ())) {
									if (task.InProgress || task.Waiting) {
										writer.WriteLine ($"Test '{task.TestName}' is already executing.");
									} else {
										task.Reset ();
										task.RunAsync ();
										writer.WriteLine ($"Started '{task.TestName}'.");
									}
								}
							}
							break;
						case "/runfailed":
							response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
							using (var writer = new StreamWriter (response.OutputStream)) {
								foreach (var task in allTasks.Where ((v) => v.Failed)) {
									if (task.InProgress || task.Waiting) {
										writer.WriteLine ($"Test '{task.TestName}' is already executing.");
									} else {
										task.Reset ();
										task.RunAsync ();
										writer.WriteLine ($"Started '{task.TestName}'.");
									}
								}
							}
							break;
						case "/stoptest":
						case "/runtest":
							response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
							IEnumerable<TestTask> find_tasks (StreamWriter writer, string ids)
							{
								var id_inputs = ids.Split (',');
								foreach (var id_input in id_inputs) {
									if (int.TryParse (id_input, out var id)) {
										var task = Tasks.FirstOrDefault ((t) => t.ID == id);
										if (task == null)
											task = Tasks.Where ((v) => v is AggregatedRunSimulatorTask).Cast<AggregatedRunSimulatorTask> ().SelectMany ((v) => v.Tasks).FirstOrDefault ((t) => t.ID == id);
										if (task == null) {
											writer.WriteLine ($"Could not find test {id}");
										} else {
											yield return task;
										}
									} else {
										writer.WriteLine ($"Could not parse {arguments ["id"]}");
									}
								}

							}
							using (var writer = new StreamWriter (response.OutputStream)) {
								var id_inputs = arguments ["id"].Split (',');

								var tasks = find_tasks (writer, arguments ["id"]);

								// We want to randomize the order the tests are added, so that we don't build first the test for one device, 
								// then for another, since that would not take advantage of running tests on several devices in parallel.
								var rnd = new Random ((int) DateTime.Now.Ticks);
								tasks = tasks.OrderBy ((v) => rnd.Next ());

								foreach (var task in tasks) {
									switch (request.Url.LocalPath) {
									case "/stoptest":
										if (!task.Waiting) {
											writer.WriteLine ($"Test '{task.TestName}' is not in a waiting state.");
										} else {
											task.Reset ();
											writer.WriteLine ($"OK: {task.ID}");
										}
										break;
									case "/runtest":
										if (task.InProgress || task.Waiting) {
											writer.WriteLine ($"Test '{task.TestName}' is already executing.");
										} else {
											task.Reset ();
											task.RunAsync ();
											writer.WriteLine ($"OK: {task.ID}");
										}
										break;
									default:
										throw new NotImplementedException ();
									}
								}
							}
							break;
						case "/reload-devices":
							GC.KeepAlive (Devices.LoadAsync (DeviceLoadLog, force: true));
							break;
						case "/reload-simulators":
							GC.KeepAlive (Simulators.LoadAsync (SimulatorLoadLog, force: true));
							break;
						case "/quit":
							using (var writer = new StreamWriter (response.OutputStream)) {
								writer.WriteLine ("<!DOCTYPE html>");
								writer.WriteLine ("<html>");
								writer.WriteLine ("<body onload='close ();'>Closing web page...</body>");
								writer.WriteLine ("</html>");
							}
							server.Stop ();
							break;
						case "/favicon.ico":
							var favicon = File.ReadAllBytes (Path.Combine (Harness.RootDirectory, "xharness", "favicon.ico"));
							response.OutputStream.Write (favicon, 0, favicon.Length);
							response.OutputStream.Close ();
							break;
						case "/xharness.css":
						case "/xharness.js":
							serveFile = Path.Combine (Path.GetDirectoryName (System.Reflection.Assembly.GetExecutingAssembly ().Location), request.Url.LocalPath.Substring (1));
							goto default;
						default:
							if (serveFile == null)
								serveFile = Path.Combine (LogDirectory, request.Url.LocalPath.Substring (1));
							var path = serveFile;
							if (File.Exists (path)) {
								var buffer = new byte [4096];
								using (var fs = new FileStream (path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
									int read;
									response.ContentLength64 = fs.Length;
									switch (Path.GetExtension (path).ToLowerInvariant ()) {
									case ".html":
										response.ContentType = System.Net.Mime.MediaTypeNames.Text.Html;
										break;
									case ".css":
										response.ContentType = "text/css";
										break;
									default:
										response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
										break;
									}
									while ((read = fs.Read (buffer, 0, buffer.Length)) > 0)
										response.OutputStream.Write (buffer, 0, read);
								}
							} else {
								response.StatusCode = 404;
								response.OutputStream.WriteByte ((byte) '?');
							}
							break;
						}
					} catch (IOException ioe) {
						Console.WriteLine (ioe.Message);
					} catch (Exception e) {
						Console.WriteLine (e);
					}
					response.Close ();
				}
				tcs.SetResult (true);
			})
			{
				IsBackground = true,
			};
			thread.Start ();

			var url = $"http://localhost:{port}/";
			Console.WriteLine ($"Launching {url} in the system's default browser.");
			Process.Start ("open", url);

			return tcs.Task;
		}

		string GetTestColor (IEnumerable<TestTask> tests)
		{
			if (!tests.Any ())
				return "black";

			var first = tests.First ();
			if (tests.All ((v) => v.ExecutionResult == first.ExecutionResult))
				return GetTestColor (first);
			if (tests.Any ((v) => v.Crashed))
				return "maroon";
			else if (tests.Any ((v) => v.TimedOut))
				return "purple";
			else if (tests.Any ((v) => v.BuildFailure))
				return "darkred";
			else if (tests.Any ((v) => v.Failed))
				return "red";
			else if (tests.Any ((v) => v.NotStarted))
				return "black";
			else if (tests.Any ((v) => v.Ignored))
				return "gray";
			else if (tests.Any ((v) => v.Skipped))
				return "orangered";
			else
				return "black";
		}

		string GetTestColor (TestTask test)
		{
			return GetTestColor (test.LastRun);
		}

		string GetTestColor (TestRun test)
		{
			if (test.NotStarted) {
				return "black";
			} else if (test.InProgress) {
				if (test.Building) {
					return "darkblue";
				} else if (test.Running) {
					return "lightblue";
				} else {
					return "blue";
				}
			} else {
				if (test.Crashed) {
					return "maroon";
				} else if (test.HarnessException) {
					return "orange";
				} else if (test.TimedOut) {
					return "purple";
				} else if (test.BuildFailure) {
					return "darkred";
				} else if (test.Failed) {
					return "red";
				} else if (test.Succeeded) {
					return "green";
				} else if (test.BuildSucceeded) {
					return "yellowgreen";
				} else if (test.Ignored) {
					return "gray";
				} else if (test.Waiting) {
					return "darkgray";
				} else if (test.Skipped) {
					return "orangered";
				} else {
					return "pink";
				}
			}
		}

		object report_lock = new object ();
		public void GenerateReport ()
		{
			try {
				lock (report_lock) {
					var report = Path.Combine (LogDirectory, "index.html");
					var tmpreport = Path.Combine (LogDirectory, $"index-{Harness.Timestamp}.tmp.html");
					var tmpmarkdown = string.IsNullOrEmpty (Harness.MarkdownSummaryPath) ? string.Empty : (Harness.MarkdownSummaryPath + $".{Harness.Timestamp}.tmp");
					using (var stream = new FileStream (tmpreport, FileMode.CreateNew, FileAccess.ReadWrite)) {
						using (var markdown_writer = (string.IsNullOrEmpty (tmpmarkdown) ? null : new StreamWriter (tmpmarkdown))) {
							GenerateReportImpl (stream, markdown_writer);
						}
					}

					if (File.Exists (report))
						File.Delete (report);
					File.Move (tmpreport, report);
					if (!string.IsNullOrEmpty (tmpmarkdown)) {
						if (File.Exists (Harness.MarkdownSummaryPath))
							File.Delete (Harness.MarkdownSummaryPath);
						File.Move (tmpmarkdown, Harness.MarkdownSummaryPath);
					}

					foreach (var file in new string [] { "xharness.js", "xharness.css" })
						File.Copy (Path.Combine (Path.GetDirectoryName (System.Reflection.Assembly.GetExecutingAssembly ().Location), file), Path.Combine (LogDirectory, file), true);
				}
			} catch (Exception e) {
				this.MainLog.WriteLine ("Failed to write log: {0}", e);
			}
		}

		void GenerateReportImpl (Stream stream, StreamWriter markdown_summary = null)
		{
			var id_counter = 0;

			var allSimulatorTasks = new List<RunSimulatorTask> ();
			var allExecuteTasks = new List<MacExecuteTask> ();
			var allNUnitTasks = new List<NUnitExecuteTask> ();
			var allMakeTasks = new List<MakeTask> ();
			var allDeviceTasks = new List<RunDeviceTask> ();
			foreach (var task in Tasks) {
				var aggregated = task as AggregatedRunSimulatorTask;
				if (aggregated != null) {
					allSimulatorTasks.AddRange (aggregated.Tasks);
					continue;
				}

				var execute = task as MacExecuteTask;
				if (execute != null) {
					allExecuteTasks.Add (execute);
					continue;
				}

				var nunit = task as NUnitExecuteTask;
				if (nunit != null) {
					allNUnitTasks.Add (nunit);
					continue;
				}

				var make = task as MakeTask;
				if (make != null) {
					allMakeTasks.Add (make);
					continue;
				}

				var run_device = task as RunDeviceTask;
				if (run_device != null) {
					allDeviceTasks.Add (run_device);
					continue;
				}

				throw new NotImplementedException ();
			}

			var allTasks = new List<TestTask> ();
			if (!populating) {
				allTasks.AddRange (allExecuteTasks);
				allTasks.AddRange (allSimulatorTasks);
				allTasks.AddRange (allNUnitTasks);
				allTasks.AddRange (allMakeTasks);
				allTasks.AddRange (allDeviceTasks);
			}

			var failedTests = allTasks.Where ((v) => v.Failed);
			var skippedTests = allTasks.Where ((v) => v.Skipped);
			var unfinishedTests = allTasks.Where ((v) => !v.Finished);
			var passedTests = allTasks.Where ((v) => v.Succeeded);
			var runningTests = allTasks.Where ((v) => v.Running && !v.Waiting);
			var buildingTests = allTasks.Where ((v) => v.Building && !v.Waiting);
			var runningQueuedTests = allTasks.Where ((v) => v.Running && v.Waiting);
			var buildingQueuedTests = allTasks.Where ((v) => v.Building && v.Waiting);

			if (markdown_summary != null) {
				markdown_summary.WriteLine ("# Test results");
				markdown_summary.WriteLine ();
				var details = failedTests.Any ();
				if (details) {
					markdown_summary.WriteLine ("<details>");
					markdown_summary.Write ("<summary>");
				}
				if (allTasks.Count == 0) {
					markdown_summary.Write ($"Loading tests...");
				} else if (unfinishedTests.Any ()) {
					var list = new List<string> ();
					var grouped = allTasks.GroupBy ((v) => v.ExecutionResult).OrderBy ((v) => (int) v.Key);
					foreach (var @group in grouped)
						list.Add ($"{@group.Key.ToString ()}: {@group.Count ()}");
					markdown_summary.Write ($"# Test run in progress: ");
					markdown_summary.Write (string.Join (", ", list));
				} else if (failedTests.Any ()) {
					markdown_summary.Write ($"{failedTests.Count ()} tests failed, {skippedTests.Count ()} tests skipped, {passedTests.Count ()} tests passed.");
				} else if (skippedTests.Any ()) {
					markdown_summary.Write ($"{skippedTests.Count ()} tests skipped, {passedTests.Count ()} tests passed.");
				} else if (passedTests.Any ()) {
					markdown_summary.Write ($"# All {passedTests.Count ()} tests passed");
				} else {
					markdown_summary.Write ($"# No tests selected.");
				}
				if (details)
					markdown_summary.Write ("</summary>");
				markdown_summary.WriteLine ();
				markdown_summary.WriteLine ();
				if (failedTests.Any ()) {
					markdown_summary.WriteLine ("## Failed tests");
					markdown_summary.WriteLine ();
					foreach (var t in failedTests) {
						markdown_summary.Write ($" * {t.TestName}");
						if (!string.IsNullOrEmpty (t.Mode))
							markdown_summary.Write ($"/{t.Mode}");
						if (!string.IsNullOrEmpty (t.Variation))
							markdown_summary.Write ($"/{t.Variation}");
						markdown_summary.Write ($": {t.ExecutionResult}");
						if (!string.IsNullOrEmpty (t.FailureMessage))
							markdown_summary.Write ($" ({t.FailureMessage})");
						markdown_summary.WriteLine ();
					}
				}
				if (details)
					markdown_summary.WriteLine ("</details>");
			}

			using (var writer = new StreamWriter (stream)) {
				writer.WriteLine ("<!DOCTYPE html>");
				writer.WriteLine ("<html onkeypress='keyhandler(event)' lang='en'>");
				if (IsServerMode && populating)
					writer.WriteLine ("<meta http-equiv=\"refresh\" content=\"1\">");
				writer.WriteLine ("<head>");
				writer.WriteLine ("<link rel='stylesheet' href='xharness.css'>");
				writer.WriteLine ("<title>Test results</title>");
<<<<<<< HEAD
				writer.WriteLine (@"<script type='text/javascript'>
var ajax_log = null;
function addlog (msg)
{
	if (ajax_log == null)
		ajax_log = document.getElementById ('ajax-log');
	if (ajax_log == null)
		return;
	var newText = msg + ""\n"" + ajax_log.innerText;
	if (newText.length > 1024)
		newText = newText.substring (0, 1024);
	ajax_log.innerText = newText;
}

function toggleLogVisibility (logName)
{
	var button = document.getElementById ('button_' + logName);
	var logs = document.getElementById ('logs_' + logName);
	if (logs.style.display == 'none' && logs.innerText.trim () != '') {
		logs.style.display = 'block';
		button.innerText = '-';
	} else {
		logs.style.display = 'none';
		button.innerText = '+';
	}
}
function toggleContainerVisibility2 (containerName)
{
	var button = document.getElementById ('button_container2_' + containerName);
	var div = document.getElementById ('test_container2_' + containerName);
	if (div.style.display == 'none') {
		div.style.display = 'block';
		button.innerText = '-';
	} else {
		div.style.display = 'none';
		button.innerText = '+';
	}
}
function quit ()
{
	var xhttp = new XMLHttpRequest();
	xhttp.onreadystatechange = function() {
	    if (this.readyState == 4 && this.status == 200) {
	       window.close ();
	    }
	};
	xhttp.open(""GET"", ""quit"", true);
	xhttp.send();
}
function toggleAjaxLogVisibility()
{
	if (ajax_log == null)
		ajax_log = document.getElementById ('ajax-log');
	var button = document.getElementById ('ajax-log-button');
	if (ajax_log.style.display == 'none') {
		ajax_log.style.display = 'block';
		button.innerText = 'Hide log';
	} else {
		ajax_log.style.display = 'none';
		button.innerText = 'Show log';
	}
}
function toggleVisibility (css_class)
{
	var objs = document.getElementsByClassName (css_class);
	
	for (var i = 0; i < objs.length; i++) {
		var obj = objs [i];
		
		var pname = 'original-' + css_class + '-display';
		if (obj.hasOwnProperty (pname)) {
			obj.style.display = obj [pname];
			delete obj [pname];
		} else {
			obj [pname] = obj.style.display;
			obj.style.display = 'none';
		}
	}
}
function keyhandler(event)
{
	switch (String.fromCharCode (event.keyCode)) {
	case ""q"":
	case ""Q"":
		quit ();
		break;
	}
}
function runalltests()
{
	sendrequest (""runalltests"");
}
function runtest(id)
{
	sendrequest (""runtest?id="" + id);
}
function stoptest(id)
{
	sendrequest (""stoptest?id="" + id);
}
function sendrequest(url, callback)
{
	var xhttp = new XMLHttpRequest();
	xhttp.onreadystatechange = function() {
		if (this.readyState == 4) {
			addlog (""Loaded url: "" + url + "" with status code: "" + this.status + ""\nResponse: "" + this.responseText);
			if (callback)
				callback (this.responseText);
		}
	};
	xhttp.open(""GET"", url, true);
	xhttp.send();
	addlog (""Loading url: "" + url);
}
function autorefresh()
{
	var xhttp = new XMLHttpRequest();
	xhttp.onreadystatechange = function() {
	    if (this.readyState == 4) {
			addlog (""Reloaded."");
			var parser = new DOMParser ();
			var r = parser.parseFromString (this.responseText, 'text/html');
			var ar_objs = document.getElementsByClassName (""autorefreshable"");

			for (var i = 0; i < ar_objs.length; i++) {
				var ar_obj = ar_objs [i];
				if (!ar_obj.id || ar_obj.id.length == 0) {
					console.log (""Found object without id"");
					continue;
				}
				
				var new_obj = r.getElementById (ar_obj.id);
				if (new_obj) {
					if (ar_obj.innerHTML != new_obj.innerHTML)
						ar_obj.innerHTML = new_obj.innerHTML;
					if (ar_obj.style.cssText != new_obj.style.cssText) {
						ar_obj.style = new_obj.style;
					}
					
					var evt = ar_obj.getAttribute ('data-onautorefresh');
					if (evt != '') {
						autoshowdetailsmessage (evt);
					}
				} else {
					console.log (""Could not find id "" + ar_obj.id + "" in updated page."");
				}
			}
			setTimeout (autorefresh, 1000);
	    }
	};
	xhttp.open(""GET"", window.location.href, true);
	xhttp.send();
}

function autoshowdetailsmessage (id)
{
	var input_id = 'logs_' + id;
	var message_id = 'button_' + id;
	var input_div = document.getElementById (input_id);
	if (input_div == null)
		return;
	var message_div = document.getElementById (message_id);
	var txt = input_div.innerText.trim ();
	if (txt == '') {
		message_div.style.opacity = 0;
	} else {
		message_div.style.opacity = 1;
		if (input_div.style.display == 'block') {
			message_div.innerText = '-';
		} else {
			message_div.innerText = '+';
		}
	}
}

function oninitialload ()
{
	var autorefreshable = document.getElementsByClassName (""autorefreshable"");
	for (var i = 0; i < autorefreshable.length; i++) {
		var evt = autorefreshable [i].getAttribute (""data-onautorefresh"");
		if (evt != '')
			autoshowdetailsmessage (evt);
	}
}

function toggleAll (show)
{
	var expandable = document.getElementsByClassName ('expander');
	var counter = 0;
	var value = show ? '-' : '+';
	for (var i = 0; i < expandable.length; i++) {
		var div = expandable [i];
		if (div.textContent != value)
			div.textContent = value;
		counter++;
	}
	
	var togglable = document.getElementsByClassName ('togglable');
	counter = 0;
	value = show ? 'block' : 'none';
	for (var i = 0; i < togglable.length; i++) {
		var div = togglable [i];
		if (div.style.display != value) {
			if (show && div.innerText.trim () == '') {
				// don't show nothing
			} else {
				div.style.display = value;
			}
		}
		counter++;
	}
}

");
				if (IsServerMode)
=======
				writer.WriteLine (@"<script type='text/javascript' src='xharness.js'></script>");
				if (IsServerMode) {
					writer.WriteLine (@"<script type='text/javascript'>");
>>>>>>> [xharness] Move js and css to external files.
					writer.WriteLine ("setTimeout (autorefresh, 1000);");
					writer.WriteLine (@"</script>");
				}
				writer.WriteLine ("</script>");
				writer.WriteLine ("</head>");
				writer.WriteLine ("<body onload='oninitialload ();'>");

				if (IsServerMode) {
					writer.WriteLine ("<div id='quit' style='position:absolute; top: 20px; right: 20px;'><a href='javascript:quit()'>Quit</a><br/><a id='ajax-log-button' href='javascript:toggleAjaxLogVisibility ();'>Show log</a></div>");
					writer.WriteLine ("<div id='ajax-log' style='position:absolute; top: 200px; right: 20px; max-width: 100px; display: none;'></div>");
				}

				writer.WriteLine ("<h1>Test results</h1>");

				foreach (var log in Logs)
					writer.WriteLine ("<span id='x{2}' class='autorefreshable'> <a href='{0}' type='text/plain'>{1}</a></span><br />", log.FullPath.Substring (LogDirectory.Length + 1), log.Description, id_counter++);

				var headerColor = "black";
				if (unfinishedTests.Any ()) {
					; // default
				} else if (failedTests.Any ()) {
					headerColor = "red";
				} else if (skippedTests.Any ()) {
					headerColor = "orange";
				} else if (passedTests.Any ()) {
					headerColor = "green";
				} else {
					headerColor = "gray";
				}

				writer.Write ($"<h2 style='color: {headerColor}'>");
				writer.Write ($"<span id='x{id_counter++}' class='autorefreshable'>");
				if (allTasks.Count == 0) {
					writer.Write ($"Loading tests...");
				} else if (unfinishedTests.Any ()) {
					writer.Write ($"Test run in progress (");
					var list = new List<string> ();
					var grouped = allTasks.GroupBy ((v) => v.ExecutionResult).OrderBy ((v) => (int) v.Key);
					foreach (var @group in grouped)
						list.Add ($"<span style='color: {GetTestColor (@group)}'>{@group.Key.ToString ()}: {@group.Count ()}</span>");
					writer.Write (string.Join (", ", list));
					writer.Write (")");
				} else if (failedTests.Any ()) {
					writer.Write ($"{failedTests.Count ()} tests failed, {skippedTests.Count ()} tests skipped, {passedTests.Count ()} tests passed");
				} else if (skippedTests.Any ()) {
					writer.Write ($"{skippedTests.Count ()} tests skipped, {passedTests.Count ()} tests passed");
				} else if (passedTests.Any ()) {
					writer.Write ($"All {passedTests.Count ()} tests passed");
				} else {
					writer.Write ($"No tests selected.");
				}
				writer.WriteLine ("</span>");
				writer.WriteLine ("</h2>");
				if (allTasks.Count > 0) {
					writer.WriteLine ($"<ul id='nav'>");
					if (IsServerMode) {
						writer.WriteLine (@"
	<li>Select
		<ul>
			<li class=""adminitem""><a href='javascript:sendrequest (""select?all"");'>All tests</a></li>
			<li class=""adminitem""><a href='javascript:sendrequest (""select?all-device"");'>All device tests</a></li>
			<li class=""adminitem""><a href='javascript:sendrequest (""select?all-simulator"");'>All simulator tests</a></li>
			<li class=""adminitem""><a href='javascript:sendrequest (""select?all-ios"");'>All iOS tests</a></li>
			<li class=""adminitem""><a href='javascript:sendrequest (""select?all-tvos"");'>All tvOS tests</a></li>
			<li class=""adminitem""><a href='javascript:sendrequest (""select?all-watchos"");'>All watchOS tests</a></li>
			<li class=""adminitem""><a href='javascript:sendrequest (""select?all-mac"");'>All Mac tests</a></li>
		</ul>
	</li>
	<li>Deselect
		<ul>
			<li class=""adminitem""><a href='javascript:sendrequest (""deselect?all"");'>All tests</a></li>
			<li class=""adminitem""><a href='javascript:sendrequest (""deselect?all-device"");'>All device tests</a></li>
			<li class=""adminitem""><a href='javascript:sendrequest (""deselect?all-simulator"");'>All simulator tests</a></li>
			<li class=""adminitem""><a href='javascript:sendrequest (""deselect?all-ios"");'>All iOS tests</a></li>
			<li class=""adminitem""><a href='javascript:sendrequest (""deselect?all-tvos"");'>All tvOS tests</a></li>
			<li class=""adminitem""><a href='javascript:sendrequest (""deselect?all-watchos"");'>All watchOS tests</a></li>
			<li class=""adminitem""><a href='javascript:sendrequest (""deselect?all-mac"");'>All Mac tests</a></li>
		</ul>
	</li>
	<li>Run
		<ul>
			<li class=""adminitem""><a href='javascript:runalltests ();'>All tests</a></li>
			<li class=""adminitem""><a href='javascript:sendrequest (""runselected"");'>All selected tests</a></li>
			<li class=""adminitem""><a href='javascript:sendrequest (""runfailed"");'>All failed tests</a></li>
		</ul>
	</li>");
					}
					writer.WriteLine (@"
	<li>Toggle visibility
		<ul>
			<li class=""adminitem""><a href='javascript:toggleAll (true);'>Expand all</a></li>
			<li class=""adminitem""><a href='javascript:toggleAll (false);'>Collapse all</a></li>
			<li class=""adminitem""><a href='javascript:toggleVisibility (""toggleable-ignored"");'>Hide/Show ignored tests</a></li>
		</ul>
	</li>");
					if (IsServerMode) {
						writer.WriteLine (@"
	<li>Reload
		<ul>
			<li class=""adminitem""><a href='javascript:sendrequest (""reload-devices"");'>Devices</a></li>
			<li class=""adminitem""><a href='javascript:sendrequest (""reload-simulators"");'>Simulators</a></li>
		</ul>
	</li>");
					}
					writer.WriteLine ("</ul>");
				}

				writer.WriteLine ("<div id='test-table' style='width: 100%'>");
				if (IsServerMode) {
					writer.WriteLine ("<div id='test-status' style='display: inline-block; margin-left: 100px;' class='autorefreshable'>");
					if (failedTests.Count () == 0) {
						foreach (var group in failedTests.GroupBy ((v) => v.TestName)) {
							var enumerableGroup = group as IEnumerable<TestTask>;
							if (enumerableGroup != null) {
								writer.WriteLine ("<a href='#test_{2}'>{0}</a> ({1})<br />", group.Key, string.Join (", ", enumerableGroup.Select ((v) => string.Format ("<span style='color: {0}'>{1}</span>", GetTestColor (v), string.IsNullOrEmpty (v.Mode) ? v.ExecutionResult.ToString () : v.Mode)).ToArray ()), group.Key.Replace (' ', '-'));
								continue;
							}

							throw new NotImplementedException ();
						}
					}

					if (buildingTests.Any ()) {
						writer.WriteLine ($"<h3>{buildingTests.Count ()} building tests:</h3>");
						foreach (var test in buildingTests) {
							var runTask = test as RunTestTask;
							var buildDuration = string.Empty;
							if (runTask != null)
								buildDuration = runTask.BuildTask.Duration.ToString ();
							writer.WriteLine ($"<a href='#test_{test.TestName}'>{test.TestName} ({test.Mode})</a> {buildDuration}<br />");
						}
					}

					if (runningTests.Any ()) {
						writer.WriteLine ($"<h3>{runningTests.Count ()} running tests:</h3>");
						foreach (var test in runningTests) {
							writer.WriteLine ($"<a href='#test_{test.TestName}'>{test.TestName} ({test.Mode})</a> {test.Duration.ToString ()} {test.ProgressMessage}<br />");
						}
					}

					if (buildingQueuedTests.Any ()) {
						writer.WriteLine ($"<h3>{buildingQueuedTests.Count ()} tests in build queue:</h3>");
						foreach (var test in buildingQueuedTests) {
							writer.WriteLine ($"<a href='#test_{test.TestName}'>{test.TestName} ({test.Mode})</a><br />");
						}
					}

					if (runningQueuedTests.Any ()) {
						writer.WriteLine ($"<h3>{runningQueuedTests.Count ()} tests in run queue:</h3>");
						foreach (var test in runningQueuedTests) {
							writer.WriteLine ($"<a href='#test_{test.TestName}'>{test.TestName} ({test.Mode})</a><br />");
						}
					}

					var resources = device_resources.Values.Concat (new Resource [] { DesktopResource });
					if (resources.Any ()) {
						writer.WriteLine ($"<h3>Devices:</h3>");
						foreach (var dr in resources.OrderBy ((v) => v.Description, StringComparer.OrdinalIgnoreCase)) {
							writer.WriteLine ($"{dr.Description} - {dr.Users}/{dr.MaxConcurrentUsers} users - {dr.QueuedUsers} in queue<br />");
						}
					}
				}
				writer.WriteLine ("</div>");

				writer.WriteLine ("<div id='test-list' style='float:left'>");
				var orderedTasks = allTasks.GroupBy ((TestTask v) => v.TestPath [0]);

				Func<IEnumerable<IGrouping<string, TestTask>>, IEnumerable<IGrouping<string, TestTask>>> sort;
				if (IsServerMode) {
					// In server mode don't take into account anything that can change during a test run
					// when ordering, since it's confusing to have the tests reorder by themselves while
					// you're looking at the web page.
					//orderedTasks = orderedTasks.OrderBy ((v) => v.Key, StringComparer.OrdinalIgnoreCase);
					sort = (s) => s.OrderBy ((v) => v.Key);
				} else {
					// Put failed tests at the top and ignored tests at the end.
					// Then order alphabetically.
					//orderedTasks = orderedTasks.OrderBy ((v) =>
					// {
					//	 if (v.Any ((t) => t.Failed))
					//		 return -1;
					//	 if (v.All ((t) => t.Ignored))
					//		 return 1;
					//	 return 0;
					// }).
					//ThenBy ((v) => v.Key, StringComparer.OrdinalIgnoreCase);
					sort = (s) => s.OrderBy ((v) => {
						if (v.Any ((t) => t.Failed))
							return -1;
						if (v.All ((t) => t.Ignored))
							return 1;
						return 0;
					}).ThenBy ((v) => v.Key, StringComparer.OrdinalIgnoreCase);
				}

				//foreach (var group in orderedTasks)
				RenderTests (writer, allTasks, sort, ref id_counter, 0);
				
				writer.WriteLine ("</div>");
				writer.WriteLine ("</div>");
				writer.WriteLine ("</body>");
				writer.WriteLine ("</html>");
			}
		}

		void RenderTests (StreamWriter writer, IEnumerable<TestTask> tests, Func<IEnumerable<IGrouping<string, TestTask>>, IEnumerable<IGrouping<string, TestTask>>> sort, ref int id_counter, int level)
		{
			if (tests.Count () > 1) {
				if (level > 30)
					throw new NotSupportedException ("This shouldn't happen");
				var grouped = tests.GroupBy ((v) => string.Join ("/", v.TestPath.Take (level + 1)));
				if (sort != null)
					grouped = sort (grouped);
				foreach (var group in grouped) {
					var groupId = group.Key.Replace (' ', '-');
					var ignoredClass = group.All ((v) => v.Ignored) ? "toggleable-ignored" : string.Empty;
					var autoExpand = !IsServerMode && group.Any ((v) => v.Failed);
					var defaultExpander = autoExpand ? "-" : "+";
					var defaultDisplay = autoExpand ? "block" : "none";

					writer.WriteLine ($"<div id='div_{groupId}' class='pdiv {ignoredClass}'>");

					// test header
					writer.Write ($"<span>");
					writer.Write ($"<span id='expander_{groupId}' class='expander toggler' onclick='javascript: toggleContainerVisibility (\"{groupId}\");'>{defaultExpander}</span>");
					writer.Write ($"<span id='test_{groupId}' class='p1 autorefreshable toggler' onclick='javascript: toggleContainerVisibility (\"{groupId}\");'>{group.First ().TestPath [level]}{RenderTextStates (group)}</span>");
					if (IsServerMode)
						writer.Write ($"<span><a class='runall' href='javascript: runtest (\"{string.Join (",", group.Select ((v) => v.ID.ToString ()))}\");'>Run</a></span>");
					writer.WriteLine ($"</span>");

					// test content
					writer.WriteLine ($"<div id='test_container_{groupId}' class='testcontainer togglable' style='display: {defaultDisplay};'>");
					RenderTests (writer, group, sort, ref id_counter, level + 1);
					writer.WriteLine ("</div>"); // test_container_{groupId}

					writer.WriteLine ($"</div>"); // div_{groupId}
				}
			} else if (tests.Count () == 1) {
				var test = tests.First ();
				var runTest = test as RunTestTask;
				writer.WriteLine ($"<div class='test'>");
				if (runTest != null) {
					var pf_id = id_counter++;
					writer.WriteLine ($"Project file: {runTest.BuildTask.ProjectFile} <br />");
					writer.WriteLine ($"Platform: {runTest.BuildTask.ProjectPlatform} Configuration: {runTest.BuildTask.ProjectConfiguration} <br />");
				}
				var test_id = id_counter++;
				var autoExpand = !IsServerMode && test.Failed;
				var defaultExpander = autoExpand ? "&nbsp;" : "+";
				var defaultDisplay = autoExpand ? "block" : "none";

				writer.WriteLine ($"<div id='testruns_{test_id}' class='autorefreshable' data-onautorefresh='{test_id}' style='display: block;'>");
				for (var i = 0; i < test.TestRuns.Count; i++)
					RenderTestRun (writer, test.TestRuns [i], i, test.TestRuns.Count > 1, ref id_counter);
				writer.WriteLine ($"</div>");
				writer.WriteLine ($"</div>");
			}
			
		}

		void RenderTestRun (StreamWriter writer, TestRun test, int index, bool header, ref int id_counter)
		{
			var runTest = test.TestTask as RunTestTask;

			var logs = test.AggregatedLogs.ToList ();

			var autoExpand = !IsServerMode && test.Failed;
			var ignoredClass = test.TestTask.Ignored ? "toggleable-ignored" : string.Empty;
			var defaultExpander = autoExpand ? "&nbsp;" : "+";
			var defaultDisplay = autoExpand ? "block" : "none";

			var log_id = id_counter++;
			writer.Write ($"<span id='testrun_header_{log_id}' class='appendable toggler' onclick='javascript: toggleContainerVisibility (\"{log_id}\");' {(header ? "" : "style='display: none'")} >");
			writer.Write ($"<span id='expander_{log_id}' class='expander'>-</span>");
			writer.Write ($"<span>Test run #{index + 1} (<span style='color: {GetTestColor (test)}'>{test.ExecutionResult.ToString ()}</span>)</span><br/>");
			writer.WriteLine ($"</span>");
			writer.WriteLine ($"<div id='test_container_{log_id}' class='{(header ? "logs togglable" : "")} appendable' style='display: block;{(header ? "margin-left: 20px;" : "")}'>");

<<<<<<< HEAD
							var autoExpand = !IsServerMode && test.Failed;
							var ignoredClass = test.Ignored ? "toggleable-ignored" : string.Empty;
							var defaultExpander = autoExpand ? "&nbsp;" : "+";
							var defaultDisplay = autoExpand ? "block" : "none";

							writer.Write ($"<div class='pdiv {ignoredClass}'>");
							writer.Write ($"<span id='button_{log_id}' class='expander' onclick='javascript: toggleLogVisibility (\"{log_id}\");'>{defaultExpander}</span>");
							writer.Write ($"<span id='x{id_counter++}' class='p3 autorefreshable' onclick='javascript: toggleLogVisibility (\"{log_id}\");'>{title} (<span style='color: {GetTestColor (test)}'>{state}</span>) </span>");
							if (IsServerMode) {
								writer.Write ($" <span id='x{id_counter++}' class='autorefreshable'>");
								if (test.Waiting) {
									writer.Write ($" <a class='runall' href='javascript:stoptest ({test.ID})'>Stop</a> ");
								} else if (test.InProgress) {
									// Stopping is not implemented for tasks that are already executing
								} else {
									writer.Write ($" <a class='runall' href='javascript:runtest ({test.ID})'>Run</a> ");
								}
								writer.Write ("</span> ");
							}
							writer.WriteLine ("</div>");
							writer.WriteLine ($"<div id='logs_{log_id}' class='autorefreshable logs togglable' data-onautorefresh='{log_id}' style='display: {defaultDisplay};'>");

							if (!string.IsNullOrEmpty (test.FailureMessage)) {
								var msg = System.Web.HttpUtility.HtmlEncode (test.FailureMessage).Replace ("\n", "<br />");
								if (test.FailureMessage.Contains ('\n')) {
									writer.WriteLine ($"Failure:<br /> <div style='margin-left: 20px;'>{msg}</div>");
=======
			if (!string.IsNullOrEmpty (test.FailureMessage)) {
				var msg = System.Web.HttpUtility.HtmlEncode (test.FailureMessage).Replace ("\n", "<br />");
				if (test.FailureMessage.Contains ('\n')) {
					writer.WriteLine ($"Failure:<br /> <div style='margin-left: 20px;'>{msg}</div>");
				} else {
					writer.WriteLine ($"Failure: {msg} <br />");
				}
			}
			var progressMessage = test.TestTask.ProgressMessage;
			if (!string.IsNullOrEmpty (progressMessage))
				writer.WriteLine (progressMessage + "<br />");
			if (runTest != null) {
				var buildRun = runTest.BuildTask.TestRuns [index];
				if (buildRun.Duration.Ticks > 0) {
					IEnumerable<IDevice> candidates = (runTest as RunDeviceTask)?.Candidates;
					if (candidates == null)
						candidates = (runTest as RunSimulatorTask)?.Candidates;
					if (candidates != null)
						writer.WriteLine ($"Candidate devices: {string.Join (", ", candidates.Select ((v) => v.Name))} <br />");
					writer.WriteLine ($"Build duration: {buildRun.Duration} <br />");
				}
				if (test.Duration.Ticks > 0)
					writer.WriteLine ($"Run duration: {test.Duration} <br />");
			} else {
				if (test.Duration.Ticks > 0)
					writer.WriteLine ($"Duration: {test.Duration} <br />");
			}
			if (test.Device != null) {
				if (test.CompanionDevice != null) {
					writer.WriteLine ($"Device: {test.Device.Name} ({test.CompanionDevice.Name}) <br />");
				} else {
					writer.WriteLine ($"Device: {test.Device.Name} <br />");
				}
			}

			if (logs.Count () > 0) {
				foreach (var log in logs) {
					log.Flush ();
					string log_type = System.Web.MimeMapping.GetMimeMapping (log.FullPath);
					string log_target;
					switch (log_type) {
					case "text/xml":
						log_target = "_top";
						break;
					default:
						log_target = "_self";
						break;
					}
					writer.WriteLine ("<a href='{0}' type='{2}' target='{3}'>{1}</a><br />", LinkEncode (log.FullPath.Substring (LogDirectory.Length + 1)), log.Description, log_type, log_target);
					if (log.Description == "Test log" || log.Description == "Extension test log" || log.Description == "Execution log") {
						string summary;
						List<string> fails;
						try {
							using (var reader = log.GetReader ()) {
								Tuple<long, object> data;
								if (!log_data.TryGetValue (log, out data) || data.Item1 != reader.BaseStream.Length) {
									summary = string.Empty;
									fails = new List<string> ();
									while (!reader.EndOfStream) {
										string line = reader.ReadLine ()?.Trim ();
										if (line == null)
											continue;
										if (line.StartsWith ("Tests run:", StringComparison.Ordinal)) {
											summary = line;
										} else if (line.StartsWith ("[FAIL]", StringComparison.Ordinal)) {
											fails.Add (line);
										}
									}
>>>>>>> undo
								} else {
									var data_tuple = (Tuple<string, List<string>>) data.Item2;
									summary = data_tuple.Item1;
									fails = data_tuple.Item2;
								}
							}
							if (fails.Count > 0) {
								writer.WriteLine ("<div style='padding-left: 15px;'>");
								foreach (var fail in fails)
									writer.WriteLine ("{0} <br />", System.Web.HttpUtility.HtmlEncode (fail));
								writer.WriteLine ("</div>");
							}
							if (!string.IsNullOrEmpty (summary))
								writer.WriteLine ("<span style='padding-left: 15px;'>{0}</span><br />", summary);
						} catch (Exception ex) {
							writer.WriteLine ("<span style='padding-left: 15px;'>Could not parse log file: {0}</span><br />", System.Web.HttpUtility.HtmlEncode (ex.Message));
						}
					} else if (log.Description == "Build log") {
						HashSet<string> errors;
						try {
							using (var reader = log.GetReader ()) {
								Tuple<long, object> data;
								if (!log_data.TryGetValue (log, out data) || data.Item1 != reader.BaseStream.Length) {
									errors = new HashSet<string> ();
									while (!reader.EndOfStream) {
										string line = reader.ReadLine ()?.Trim ();
										if (line == null)
											continue;
										// Sometimes we put error messages in pull request descriptions
										// Then Jenkins create environment variables containing the pull request descriptions (and other pull request data)
										// So exclude any lines matching 'ghprbPull', to avoid reporting those environment variables as build errors.
										if (line.Contains (": error") && !line.Contains ("ghprbPull"))
											errors.Add (line);
									}
									log_data [log] = new Tuple<long, object> (reader.BaseStream.Length, errors);
								} else {
									errors = (HashSet<string>) data.Item2;
								}
							}
							if (errors.Count > 0) {
								writer.WriteLine ("<div style='padding-left: 15px;'>");
								foreach (var error in errors)
									writer.WriteLine ("{0} <br />", System.Web.HttpUtility.HtmlEncode (error));
								writer.WriteLine ("</div>");
							}
						} catch (Exception ex) {
							writer.WriteLine ("<span style='padding-left: 15px;'>Could not parse log file: {0}</span><br />", System.Web.HttpUtility.HtmlEncode (ex.Message));
						}
					} else if (log.Description == "NUnit results" || log.Description == "XML log") {
						try {
							if (File.Exists (log.FullPath) && new FileInfo (log.FullPath).Length > 0) {
								var doc = new System.Xml.XmlDocument ();
								doc.LoadWithoutNetworkAccess (log.FullPath);
								var failures = doc.SelectNodes ("//test-case[@result='Error' or @result='Failure']").Cast<System.Xml.XmlNode> ().ToArray ();
								if (failures.Length > 0) {
									writer.WriteLine ("<div style='padding-left: 15px;'>");
									writer.WriteLine ("<ul>");
									foreach (var failure in failures) {
										writer.WriteLine ("<li>");
										var test_name = failure.Attributes ["name"]?.Value;
										var message = failure.SelectSingleNode ("failure/message")?.InnerText;
										writer.Write (System.Web.HttpUtility.HtmlEncode (test_name));
										if (!string.IsNullOrEmpty (message)) {
											writer.Write (": ");
											writer.Write (HtmlFormat (message));
										}
										writer.WriteLine ("<br />");
										writer.WriteLine ("</li>");
									}
									writer.WriteLine ("</ul>");
									writer.WriteLine ("</div>");
								}
							}
						} catch (Exception ex) {
							writer.WriteLine ($"<span style='padding-left: 15px;'>Could not parse {log.Description}: {HtmlFormat (ex.Message)}</span><br />");
						}
					}
				}
			}
			writer.WriteLine ("</div>");
		}

		Dictionary<Log, Tuple<long, object>> log_data = new Dictionary<Log, Tuple<long, object>> ();

		static string HtmlFormat (string value)
		{
			var rv = System.Web.HttpUtility.HtmlEncode (value);
			return rv.Replace ("\t", "&nbsp;&nbsp;&nbsp;&nbsp;").Replace ("\n", "<br/>\n");
		}

		static string LinkEncode (string path)
		{
			return System.Web.HttpUtility.UrlEncode (path).Replace ("%2f", "/").Replace ("+", "%20");
		}

		string RenderTextStates (IEnumerable<TestTask> tests)
		{
			// Create a collection of all non-ignored tests in the group (unless all tests were ignored).
			var allIgnored = tests.All ((v) => v.ExecutionResult == TestExecutingResult.Ignored);
			IEnumerable<TestTask> relevantGroup;
			if (allIgnored) {
				relevantGroup = tests;
			} else {
				relevantGroup = tests.Where ((v) => v.ExecutionResult != TestExecutingResult.NotStarted);
			}
			if (!relevantGroup.Any ())
				return string.Empty;
			
			var results = relevantGroup
				.GroupBy ((v) => v.ExecutionResult)
				.Select ((v) => v.First ()) // GroupBy + Select = Distinct (lambda)
				.OrderBy ((v) => v.ID)
				.Select ((v) => $"<span style='color: {GetTestColor (v)}'>{v.ExecutionResult.ToString ()}</span>")
				.ToArray ();
			return " (" + string.Join ("; ", results) + ")";
		}
	}

	class TestRun
	{
		TestTask task;

		public TestTask TestTask {
			get {
				return task;
			}
		}

		Stopwatch stopwatch = new Stopwatch ();

		public DateTime StartTime { get; private set; }
		public DateTime EndTime { get; private set; }

		public void StartWatch ()
		{
			if (StartTime.Ticks == 0)
				StartTime = DateTime.Now;
			stopwatch.Start ();
		}

		public void StopWatch ()
		{
			EndTime = DateTime.Now;
			stopwatch.Stop ();
		}

		public void ResetWatch ()
		{
			// This will reset the duration, but not initial start time.
			// This is to know when a test started executing (StartTime), but also
			// allows us to see for how long it executed without counting build time for instance
			stopwatch.Reset ();
		}

		public TestRun (TestTask task)
		{
			this.task = task;
		}

		public TimeSpan Duration {
			get {
				return stopwatch.Elapsed;
			}
		}

		IEnumerable<Log> aggregated_logs;
		public IEnumerable<Log> AggregatedLogs {
			get {
				return aggregated_logs ?? task.AggregatedLogs;
			}
			set {
				aggregated_logs = value;
			}
		}

		public TestExecutingResult ExecutionResult;
		public string FailureMessage;
		public Logs Logs;
		public Log MainLog;

		public bool NotStarted { get { return (ExecutionResult & TestExecutingResult.StateMask) == TestExecutingResult.NotStarted; } }
		public bool InProgress { get { return (ExecutionResult & TestExecutingResult.InProgress) == TestExecutingResult.InProgress; } }
		public bool Waiting { get { return (ExecutionResult & TestExecutingResult.Waiting) == TestExecutingResult.Waiting; } }
		public bool Finished { get { return (ExecutionResult & TestExecutingResult.Finished) == TestExecutingResult.Finished; } }

		public bool Building { get { return (ExecutionResult & TestExecutingResult.Building) == TestExecutingResult.Building; } }
		public bool Built { get { return (ExecutionResult & TestExecutingResult.Built) == TestExecutingResult.Built; } }
		public bool Running { get { return (ExecutionResult & TestExecutingResult.Running) == TestExecutingResult.Running; } }

		public bool Succeeded { get { return (ExecutionResult & TestExecutingResult.Succeeded) == TestExecutingResult.Succeeded; } }
		public bool BuildSucceeded { get { return (ExecutionResult & TestExecutingResult.BuildSucceeded) == TestExecutingResult.BuildSucceeded; } }
		public bool Failed { get { return (ExecutionResult & TestExecutingResult.Failed) == TestExecutingResult.Failed; } }
		public bool Ignored {
			get { return ExecutionResult == TestExecutingResult.Ignored; }
			set {
				if (ExecutionResult != TestExecutingResult.NotStarted && ExecutionResult != TestExecutingResult.Ignored)
					throw new InvalidOperationException ();
				ExecutionResult = value ? TestExecutingResult.Ignored : TestExecutingResult.NotStarted;
			}
		}
		public bool Skipped { get { return ExecutionResult == TestExecutingResult.Skipped; } }

		public bool Crashed { get { return (ExecutionResult & TestExecutingResult.Crashed) == TestExecutingResult.Crashed; } }
		public bool TimedOut { get { return (ExecutionResult & TestExecutingResult.TimedOut) == TestExecutingResult.TimedOut; } }
		public bool BuildFailure { get { return (ExecutionResult & TestExecutingResult.BuildFailure) == TestExecutingResult.BuildFailure; } }
		public bool HarnessException { get { return (ExecutionResult & TestExecutingResult.HarnessException) == TestExecutingResult.HarnessException; } }

		public IDevice Device;
		public IDevice CompanionDevice;
	}

	abstract class TestTask
	{
		static int counter;
		public readonly int ID = counter++;

		bool? supports_parallel_execution;

		public Jenkins Jenkins;
		public Harness Harness { get { return Jenkins.Harness; } }
		public TestProject TestProject;
		public string ProjectFile { get { return TestProject?.Path; } }
		public string ProjectConfiguration;
		public string ProjectPlatform;
		public Dictionary<string, string> Environment = new Dictionary<string, string> ();
		public bool BuildOnly;

		public Func<Task> Dependency; // a task that's feteched and awaited before this task's ExecuteAsync method
		public Task InitialTask; // a task that's executed before this task's ExecuteAsync method.
		public Task CompletedTask; // a task that's executed after this task's ExecuteAsync method.

		public bool RequiresXcode94;

		// VerifyRun is called in RunInternalAsync/ExecuteAsync to verify that the task can be executed/run.
		// Typically used to fail tasks that don't have an available device, or if there's not enough disk space.
		public virtual Task VerifyRunAsync ()
		{
			return VerifyDiskSpaceAsync ();
		}

		static DriveInfo RootDrive;
		protected Task VerifyDiskSpaceAsync ()
		{
			if (Finished)
				return Task.CompletedTask;

			if (RootDrive == null)
				RootDrive = new DriveInfo ("/");
			var afs = RootDrive.AvailableFreeSpace;
			const long minSpaceRequirement = 1024 * 1024 * 1024; /* 1 GB */
			if (afs < minSpaceRequirement) {
				FailureMessage = $"Not enough space on the root drive '{RootDrive.Name}': {afs / (1024.0 * 1024):#.##} MB left of {minSpaceRequirement / (1024.0 * 1024):#.##} MB required";
				ExecutionResult = TestExecutingResult.Failed;
			}
			return Task.CompletedTask;
		}

		public List<TestRun> TestRuns = new List<TestRun> ();
		public TestRun LastRun {
			get {
				return TestRuns [TestRuns.Count - 1];
			}
		}

		public TestTask ()
		{
			TestRuns.Add (new TestRun (this));
		}

		public void CloneTestProject (TestProject project)
		{
			// Don't build in the original project directory
			// We can build multiple projects in parallel, and if some of those
			// projects have the same project dependencies, then we may end up
			// building the same (dependent) project simultaneously (and they can
			// stomp on eachother).
			// So we clone the project file to a separate directory and build there instead.
			// This is done asynchronously to speed to the initial test load.
			TestProject = project.Clone ();
			InitialTask = TestProject.CreateCopyAsync ();
		}

		public TimeSpan Duration { 
			get {
				return LastRun.Duration;
			}
		}

		public virtual TestExecutingResult ExecutionResult {
			get {
				return LastRun.ExecutionResult;
			}
			set {
				LastRun.ExecutionResult = value;
			}
		}

		public string FailureMessage {
			get { return LastRun.FailureMessage; }
			set {
				LastRun.FailureMessage = value;
				MainLog.WriteLine (LastRun.FailureMessage);
			}
		}

		public virtual string ProgressMessage { get; }

		public bool NotStarted { get { return LastRun.NotStarted; } }
		public bool InProgress { get { return LastRun.InProgress; } }
		public bool Waiting { get { return LastRun.Waiting; } }
		public bool Finished { get { return LastRun.Finished; } }

		public bool Building { get { return LastRun.Building; } }
		public bool Built { get { return LastRun.Built; } }
		public bool Running { get { return LastRun.Running; } }

		public bool Succeeded { get { return LastRun.Succeeded; } }
		public bool Failed { get { return LastRun.Failed; } }
		public bool Ignored {
			get { return LastRun.Ignored; }
			set { LastRun.Ignored = value; }
		}
		public bool Skipped { get { return LastRun.Skipped; } }

		public bool Crashed { get { return LastRun.Crashed; } }
		public bool TimedOut { get { return LastRun.TimedOut; } }
		public bool BuildFailure { get { return LastRun.BuildFailure; } }
		public bool HarnessException { get { return LastRun.HarnessException; } }

		public virtual string Mode { get; set; }
		public virtual string Variation { get; set; }

		protected static string Timestamp {
			get {
				return Harness.Timestamp;
			}
		}

		public bool HasCustomTestName {
			get {
				return test_name != null;
			}
		}

		string test_name;
		public virtual string TestName {
			get {
				if (test_name != null)
					return test_name;
				
				var rv = Path.GetFileNameWithoutExtension (ProjectFile);
				if (rv == null)
					return $"unknown test name ({GetType ().Name}";
				switch (Platform) {
				case TestPlatform.Mac:
				case TestPlatform.Mac_Classic:
					return rv;
				case TestPlatform.Mac_Unified:
					return rv.Substring (0, rv.Length - "-unified".Length);
				case TestPlatform.Mac_Unified32:
					return rv.Substring (0, rv.Length - "-unified-32".Length);
				case TestPlatform.Mac_UnifiedXM45:
					return rv.Substring (0, rv.Length - "-unifiedXM45".Length);
				case TestPlatform.Mac_UnifiedXM45_32:
					return rv.Substring (0, rv.Length - "-unifiedXM45-32".Length);
				case TestPlatform.Mac_UnifiedSystem:
					return rv.Substring (0, rv.Length - "-unifiedSystem".Length);
				default:
					if (rv.EndsWith ("-watchos", StringComparison.Ordinal)) {
						return rv.Substring (0, rv.Length - 8);
					} else if (rv.EndsWith ("-tvos", StringComparison.Ordinal)) {
						return rv.Substring (0, rv.Length - 5);
					} else if (rv.EndsWith ("-unified", StringComparison.Ordinal)) {
						return rv.Substring (0, rv.Length - 8);
					} else if (rv.EndsWith ("-today", StringComparison.Ordinal)) {
						return rv.Substring (0, rv.Length - 6);
					} else {
						return rv;
					}
				}
			}
			set {
				test_name = value;
			}
		}

		string [] testPath;
		public string [] TestPath {
			get {
				if (testPath == null) {
					var rv = new List<string> ();
					rv.Add (TestName);
					if (!string.IsNullOrEmpty (Mode))
						rv.Add (Mode);
					if (!string.IsNullOrEmpty (Variation))
						rv.Add (Variation);
					testPath = rv.ToArray ();
				}
				return testPath;
			}
		}

		public TestPlatform Platform { get; set; }

		public List<Resource> Resources = new List<Resource> ();

		public Log MainLog {
			get {
				if (LastRun.MainLog == null)
					LastRun.MainLog = Logs.Create ($"main-{Timestamp}.log", "Main log");
				return LastRun.MainLog;
			}
		}

		public virtual IEnumerable<Log> AggregatedLogs {
			get {
				return Logs;
			}
		}

		public string LogDirectory {
			get {
				var rv = Path.Combine (Jenkins.LogDirectory, TestName, ID.ToString ());
				Directory.CreateDirectory (rv);
				return rv;
			}
		}

		public Logs Logs {
			get {
				if (LastRun.Logs == null)
					LastRun.Logs = new Logs (LogDirectory);
				return LastRun.Logs;
			}
		}

		Task execute_task;
		async Task RunInternalAsync ()
		{
			if (Finished)
				return;
			
			ExecutionResult = (ExecutionResult & ~TestExecutingResult.StateMask) | TestExecutingResult.InProgress;

			try {
				if (Dependency != null)
 					await Dependency ();

				if (InitialTask != null)
					await InitialTask;
				
				await VerifyRunAsync ();
				if (Finished)
					return;

				LastRun.StartWatch ();

				execute_task = ExecuteAsync ();
				await execute_task;

				if (CompletedTask != null) {
					if (CompletedTask.Status == TaskStatus.Created)
						CompletedTask.Start ();
					await CompletedTask;
				}

				ExecutionResult = (ExecutionResult & ~TestExecutingResult.StateMask) | TestExecutingResult.Finished;
				if ((ExecutionResult & ~TestExecutingResult.StateMask) == 0)
					throw new Exception ("Result not set!");
			} catch (Exception e) {
				using (var log = Logs.Create ($"execution-failure-{Timestamp}.log", "Execution failure")) {
					ExecutionResult = TestExecutingResult.HarnessException;
					FailureMessage = $"Harness exception for '{TestName}': {e}";
					log.WriteLine (FailureMessage);
				}
				PropagateResults ();
			} finally {
				LastRun.Logs?.Dispose ();
				LastRun.StopWatch ();
			}

			Jenkins.GenerateReport ();
		}

		protected virtual void PropagateResults ()
		{
		}

		public virtual void Reset ()
		{
			if (Ignored) {
				ExecutionResult = TestExecutingResult.NotStarted;
			} else if (!NotStarted) {
				LastRun.AggregatedLogs = AggregatedLogs.ToList (); // Capture the current set of aggregated logs
				TestRuns.Add (new TestRun (this));
			}
			execute_task = null;
		}

		public Task RunAsync ()
		{
			if (execute_task == null)
				execute_task = RunInternalAsync ();
			return execute_task;
		}

		protected abstract Task ExecuteAsync ();

		public override string ToString ()
		{
			return ExecutionResult.ToString ();
		}

		protected void SetEnvironmentVariables (Process process)
		{
			var xcodeRoot = RequiresXcode94 ? Harness.Xcode94Root : Harness.XcodeRoot;
			
			switch (Platform) {
			case TestPlatform.iOS:
			case TestPlatform.iOS_Unified:
			case TestPlatform.iOS_Unified32:
			case TestPlatform.iOS_Unified64:
			case TestPlatform.iOS_TodayExtension64:
			case TestPlatform.tvOS:
			case TestPlatform.watchOS:
				process.StartInfo.EnvironmentVariables ["MD_APPLE_SDK_ROOT"] = xcodeRoot;
				process.StartInfo.EnvironmentVariables ["MD_MTOUCH_SDK_ROOT"] = Path.Combine (Harness.IOS_DESTDIR, "Library", "Frameworks", "Xamarin.iOS.framework", "Versions", "Current");
				process.StartInfo.EnvironmentVariables ["TargetFrameworkFallbackSearchPaths"] = Path.Combine (Harness.IOS_DESTDIR, "Library", "Frameworks", "Mono.framework", "External", "xbuild-frameworks");
				process.StartInfo.EnvironmentVariables ["MSBuildExtensionsPathFallbackPathsOverride"] = Path.Combine (Harness.IOS_DESTDIR, "Library", "Frameworks", "Mono.framework", "External", "xbuild");
				break;
			case TestPlatform.Mac:
			case TestPlatform.Mac_Classic:
			case TestPlatform.Mac_Unified:
			case TestPlatform.Mac_Unified32:
			case TestPlatform.Mac_UnifiedXM45:
			case TestPlatform.Mac_UnifiedXM45_32:
			case TestPlatform.Mac_UnifiedSystem:
				process.StartInfo.EnvironmentVariables ["MD_APPLE_SDK_ROOT"] = xcodeRoot;
				process.StartInfo.EnvironmentVariables ["TargetFrameworkFallbackSearchPaths"] = Path.Combine (Harness.MAC_DESTDIR, "Library", "Frameworks", "Mono.framework", "External", "xbuild-frameworks");
				process.StartInfo.EnvironmentVariables ["MSBuildExtensionsPathFallbackPathsOverride"] = Path.Combine (Harness.MAC_DESTDIR, "Library", "Frameworks", "Mono.framework", "External", "xbuild");
				process.StartInfo.EnvironmentVariables ["XamarinMacFrameworkRoot"] = Path.Combine (Harness.MAC_DESTDIR, "Library", "Frameworks", "Xamarin.Mac.framework", "Versions", "Current");
				process.StartInfo.EnvironmentVariables ["XAMMAC_FRAMEWORK_PATH"] = Path.Combine (Harness.MAC_DESTDIR, "Library", "Frameworks", "Xamarin.Mac.framework", "Versions", "Current");
				break;
			case TestPlatform.All:
				// Don't set:
				//     MSBuildExtensionsPath 
				//     TargetFrameworkFallbackSearchPaths
				// because these values used by both XM and XI and we can't set it to two different values at the same time.
				// Any test that depends on these values should not be using 'TestPlatform.All'
				process.StartInfo.EnvironmentVariables ["MD_APPLE_SDK_ROOT"] = xcodeRoot;
				process.StartInfo.EnvironmentVariables ["MD_MTOUCH_SDK_ROOT"] = Path.Combine (Harness.IOS_DESTDIR, "Library", "Frameworks", "Xamarin.iOS.framework", "Versions", "Current");
				process.StartInfo.EnvironmentVariables ["XamarinMacFrameworkRoot"] = Path.Combine (Harness.MAC_DESTDIR, "Library", "Frameworks", "Xamarin.Mac.framework", "Versions", "Current");
				process.StartInfo.EnvironmentVariables ["XAMMAC_FRAMEWORK_PATH"] = Path.Combine (Harness.MAC_DESTDIR, "Library", "Frameworks", "Xamarin.Mac.framework", "Versions", "Current");
				break;
			default:
				throw new NotImplementedException ();
			}

			foreach (var kvp in Environment)
				process.StartInfo.EnvironmentVariables [kvp.Key] = kvp.Value;
		}

		protected void AddWrenchLogFiles (StreamReader stream)
		{
			string line;
			while ((line = stream.ReadLine ()) != null) {
				if (!line.StartsWith ("@MonkeyWrench: ", StringComparison.Ordinal))
					continue;

				var cmd = line.Substring ("@MonkeyWrench:".Length).TrimStart ();
				var colon = cmd.IndexOf (':');
				if (colon <= 0)
					continue;
				var name = cmd.Substring (0, colon);
				switch (name) {
				case "AddFile":
					var src = cmd.Substring (name.Length + 1).Trim ();
					Logs.AddFile (src);
					break;
				default:
					Harness.HarnessLog.WriteLine ("Unknown @MonkeyWrench command in {0}: {1}", TestName, name);
					break;
				}
			}
		}

		protected void LogEvent (Log log, string text, params object[] args)
		{
			Jenkins.MainLog.WriteLine (text, args);
			log.WriteLine (text, args);
		}

		public string GuessFailureReason (Log log)
		{
			try {
				using (var reader = log.GetReader ()) {
					string line;
					var error_msg = new System.Text.RegularExpressions.Regex ("([A-Z][A-Z][0-9][0-9][0-9][0-9]:.*)");
					while ((line = reader.ReadLine ()) != null) {
						var match = error_msg.Match (line);
						if (match.Success)
							return match.Groups [1].Captures [0].Value;
					}
				}
			} catch (Exception e) {
				Harness.Log ("Failed to guess failure reason: {0}", e.Message);
			}

			return null;
		}

		// This method will set (and clear) the Waiting flag correctly while waiting on a resource
		// It will also pause the duration.
		public async Task<IAcquiredResource> NotifyBlockingWaitAsync (Task<IAcquiredResource> task)
		{
			var rv = new BlockingWait ();

			// Stop the timer while we're waiting for a resource
			LastRun.StopWatch ();
			ExecutionResult = ExecutionResult | TestExecutingResult.Waiting;
			rv.Wrapped = await task;
			ExecutionResult = ExecutionResult & ~TestExecutingResult.Waiting;
			LastRun.StartWatch ();
			rv.OnDispose = LastRun.StopWatch;
			return rv;
		}

		public virtual bool SupportsParallelExecution {
			get {
				return supports_parallel_execution ?? true;
			}
			set {
				supports_parallel_execution = value;
			}
		}

		protected Task<IAcquiredResource> NotifyAndAcquireDesktopResourceAsync ()
		{
			return NotifyBlockingWaitAsync ((SupportsParallelExecution ? Jenkins.DesktopResource.AcquireConcurrentAsync () : Jenkins.DesktopResource.AcquireExclusiveAsync ()));
		}

		class BlockingWait : IAcquiredResource, IDisposable
		{
			public IAcquiredResource Wrapped;
			public Action OnDispose;

			public Resource Resource { get { return Wrapped.Resource; } }

			public void Dispose ()
			{
				OnDispose ();
				Wrapped.Dispose ();
			}
		}
	}

	abstract class BuildToolTask : TestTask
	{
		public bool SpecifyPlatform = true;
		public bool SpecifyConfiguration = true;

		public override string Mode {
			get { return Platform.ToString (); }
			set { throw new NotSupportedException (); }
		}

		public virtual Task CleanAsync ()
		{
			Console.WriteLine ("Clean is not implemented for {0}", GetType ().Name);
			return Task.CompletedTask;
		}
	}

	abstract class BuildProjectTask : BuildToolTask
	{
		public string SolutionPath;

		public bool RestoreNugets {
			get {
				return !string.IsNullOrEmpty (SolutionPath);
			}
		}

		public override bool SupportsParallelExecution {
			get {
				return Platform.ToString ().StartsWith ("Mac", StringComparison.Ordinal);
			}
		}

		// This method must be called with the desktop resource acquired
		// (which is why it takes an IAcquiredResources as a parameter without using it in the function itself).
		protected async Task RestoreNugetsAsync (Log log, IAcquiredResource resource)
		{
			if (!RestoreNugets)
				return;

			if (!File.Exists (SolutionPath))
				throw new FileNotFoundException ("Could not find the solution whose nugets to restore.", SolutionPath);

			using (var nuget = new Process ()) {
				nuget.StartInfo.FileName = "/Library/Frameworks/Mono.framework/Versions/Current/Commands/nuget";
				var args = new StringBuilder ();
				args.Append ("restore ");
				args.Append (StringUtils.Quote (SolutionPath));
				nuget.StartInfo.Arguments = args.ToString ();
				SetEnvironmentVariables (nuget);
				LogEvent (log, "Restoring nugets for {0} ({1})", TestName, Mode);

				var timeout = TimeSpan.FromMinutes (15);
				var result = await nuget.RunAsync (log, true, timeout);
				if (result.TimedOut) {
					ExecutionResult = TestExecutingResult.TimedOut;
					log.WriteLine ("Nuget restore timed out after {0} seconds.", timeout.TotalSeconds);
					return;
				} else if (!result.Succeeded) {
					ExecutionResult = TestExecutingResult.Failed;
					return;
				}
			}
		}
	}

	class MdtoolTask : BuildProjectTask
	{
		protected override async Task ExecuteAsync ()
		{
			ExecutionResult = TestExecutingResult.Building;
			using (var resource = await NotifyAndAcquireDesktopResourceAsync ()) {
				var log = Logs.Create ($"build-{Platform}-{Timestamp}.txt", "Build log");
				await RestoreNugetsAsync (log, resource);
				using (var xbuild = new Process ()) {
					xbuild.StartInfo.FileName = "/Applications/Visual Studio.app/Contents/MacOS/vstool";
					var args = new StringBuilder ();
					args.Append ("build ");
					var sln = Path.ChangeExtension (ProjectFile, "sln");
					args.Append (StringUtils.Quote (File.Exists (sln) ? sln : ProjectFile));
					xbuild.StartInfo.Arguments = args.ToString ();
					SetEnvironmentVariables (xbuild);
					LogEvent (log, "Building {0} ({1})", TestName, Mode);
					if (!Harness.DryRun) {
						var timeout = TimeSpan.FromMinutes (5);
						var result = await xbuild.RunAsync (log, true, timeout);
						if (result.TimedOut) {
							ExecutionResult = TestExecutingResult.TimedOut;
							log.WriteLine ("Build timed out after {0} seconds.", timeout.TotalSeconds);
						} else if (result.Succeeded) {
							ExecutionResult = TestExecutingResult.Succeeded;
						} else {
							ExecutionResult = TestExecutingResult.Failed;
						}
					}
					Jenkins.MainLog.WriteLine ("Built {0} ({1})", TestName, Mode);
				}
				log.Dispose ();
			}
		}
	}

	class MakeTask : BuildToolTask
	{
		public string Target;
		public string WorkingDirectory;
		public TimeSpan Timeout = TimeSpan.FromMinutes (5);

		protected override async Task ExecuteAsync ()
		{
			using (var resource = await NotifyAndAcquireDesktopResourceAsync ()) {
				using (var make = new Process ()) {
					make.StartInfo.FileName = "make";
					make.StartInfo.WorkingDirectory = WorkingDirectory;
					make.StartInfo.Arguments = Target;
					SetEnvironmentVariables (make);
					var log = Logs.Create ($"make-{Platform}-{Timestamp}.txt", "Build log");
					log.Timestamp = true;
					LogEvent (log, "Making {0} in {1}", Target, WorkingDirectory);
					if (!Harness.DryRun) {
						var timeout = Timeout;
						var result = await make.RunAsync (log, true, timeout);
						if (result.TimedOut) {
							ExecutionResult = TestExecutingResult.TimedOut;
							log.WriteLine ("Make timed out after {0} seconds.", timeout.TotalSeconds);
						} else if (result.Succeeded) {
							ExecutionResult = TestExecutingResult.Succeeded;
						} else {
							ExecutionResult = TestExecutingResult.Failed;
						}
					}
					using (var reader = log.GetReader ())
						AddWrenchLogFiles (reader);
					Jenkins.MainLog.WriteLine ("Made {0} ({1})", TestName, Mode);
				}
			}
		}
	}

	class XBuildTask : BuildProjectTask
	{
		public bool UseMSBuild;

		protected override async Task ExecuteAsync ()
		{
			using (var resource = await NotifyAndAcquireDesktopResourceAsync ()) {
				var log = Logs.Create ($"build-{Platform}-{Timestamp}.txt", "Build log");

				await RestoreNugetsAsync (log, resource);

				using (var xbuild = new Process ()) {
					xbuild.StartInfo.FileName = Harness.XIBuildPath;
					var args = new StringBuilder ();
					args.Append ("-- ");
					args.Append ("/verbosity:diagnostic ");
					if (SpecifyPlatform)
						args.Append ($"/p:Platform={ProjectPlatform} ");
					if (SpecifyConfiguration)
						args.Append ($"/p:Configuration={ProjectConfiguration} ");
					args.Append (StringUtils.Quote (ProjectFile));
					xbuild.StartInfo.Arguments = args.ToString ();
					SetEnvironmentVariables (xbuild);
					if (UseMSBuild)
						xbuild.StartInfo.EnvironmentVariables ["MSBuildExtensionsPath"] = null;
					LogEvent (log, "Building {0} ({1})", TestName, Mode);
					if (!Harness.DryRun) {
						var timeout = TimeSpan.FromMinutes (60);
						var result = await xbuild.RunAsync (log, true, timeout);
						if (result.TimedOut) {
							ExecutionResult = TestExecutingResult.TimedOut;
							log.WriteLine ("Build timed out after {0} seconds.", timeout.TotalSeconds);
						} else if (result.Succeeded) {
							ExecutionResult = TestExecutingResult.Succeeded;
						} else {
							ExecutionResult = TestExecutingResult.Failed;
						}
					}
					Jenkins.MainLog.WriteLine ("Built {0} ({1})", TestName, Mode);
				}

				log.Dispose ();
			}
		}

		async Task CleanProjectAsync (Log log, string project_file, string project_platform, string project_configuration)
		{
			// Don't require the desktop resource here, this shouldn't be that resource sensitive
			using (var xbuild = new Process ()) {
				xbuild.StartInfo.FileName = Harness.XIBuildPath;
				var args = new StringBuilder ();
				args.Append ("-- ");
				args.Append ("/verbosity:diagnostic ");
				if (project_platform != null)
					args.Append ($"/p:Platform={project_platform} ");
				if (project_configuration != null)
					args.Append ($"/p:Configuration={project_configuration} ");
				args.Append (StringUtils.Quote (project_file)).Append (" ");
				args.Append ("/t:Clean ");
				xbuild.StartInfo.Arguments = args.ToString ();
				SetEnvironmentVariables (xbuild);
				LogEvent (log, "Cleaning {0} ({1}) - {2}", TestName, Mode, project_file);
				var timeout = TimeSpan.FromMinutes (1);
				await xbuild.RunAsync (log, true, timeout);
				log.WriteLine ("Clean timed out after {0} seconds.", timeout.TotalSeconds);
				Jenkins.MainLog.WriteLine ("Cleaned {0} ({1})", TestName, Mode);
			}
		}

		public async override Task CleanAsync ()
		{
			var log = Logs.Create ($"clean-{Platform}-{Timestamp}.txt", "Clean log");
			await CleanProjectAsync (log, ProjectFile, SpecifyPlatform ? ProjectPlatform : null, SpecifyConfiguration ? ProjectConfiguration : null);

			// Iterate over all the project references as well.
			var doc = new System.Xml.XmlDocument ();
			doc.LoadWithoutNetworkAccess (ProjectFile);
			foreach (var pr in doc.GetProjectReferences ()) {
				var path = pr.Replace ('\\', '/');
				await CleanProjectAsync (log, path, SpecifyPlatform ? ProjectPlatform : null, SpecifyConfiguration ? ProjectConfiguration : null);
			}
		}
	}

	class NUnitExecuteTask : RunTestTask
	{
		public string TestLibrary;
		public string TestExecutable;
		public string WorkingDirectory;
		public bool ProduceHtmlReport = true;
		public TimeSpan Timeout = TimeSpan.FromMinutes (10);

		public NUnitExecuteTask (BuildToolTask build_task)
			: base (build_task)
		{
		}

		public bool IsNUnit3 {
			get {
				return Path.GetFileName (TestExecutable) == "nunit3-console.exe";
			}
		}
		public override IEnumerable<Log> AggregatedLogs {
			get {
				return base.AggregatedLogs.Union (BuildTask.Logs);
			}
		}

		public override string Mode {
			get {
				return base.Mode ?? "NUnit";
			}
			set {
				base.Mode = value;
			}
		}

		protected override async Task RunTestAsync ()
		{
			using (var resource = await NotifyAndAcquireDesktopResourceAsync ()) {
				var xmlLog = Logs.CreateFile ($"log-{Timestamp}.xml", "XML log");
				var log = Logs.Create ($"execute-{Timestamp}.txt", "Execution log");
				log.Timestamp = true;
				using (var proc = new Process ()) {

					proc.StartInfo.WorkingDirectory = WorkingDirectory;
					proc.StartInfo.FileName = Harness.XIBuildPath;
					var args = new StringBuilder ();
					args.Append ("-t -- ");
					args.Append (StringUtils.Quote (Path.GetFullPath (TestExecutable))).Append (' ');
					args.Append (StringUtils.Quote (Path.GetFullPath (TestLibrary))).Append (' ');
					if (IsNUnit3) {
						args.Append ("-result=").Append (StringUtils.Quote (xmlLog)).Append (";format=nunit2 ");
						args.Append ("--labels=All ");
					} else {
						args.Append ("-xml=" + StringUtils.Quote (xmlLog)).Append (' ');
						args.Append ("-labels ");
					}
					proc.StartInfo.Arguments = args.ToString ();
					SetEnvironmentVariables (proc);
					foreach (DictionaryEntry de in proc.StartInfo.EnvironmentVariables)
						log.WriteLine ($"export {de.Key}={de.Value}");
					Jenkins.MainLog.WriteLine ("Executing {0} ({1})", TestName, Mode);
					if (!Harness.DryRun) {
						ExecutionResult = TestExecutingResult.Running;
						var result = await proc.RunAsync (log, true, Timeout);
						if (result.TimedOut) {
							FailureMessage = $"Execution timed out after {Timeout.TotalMinutes} minutes.";
							log.WriteLine (FailureMessage);
							ExecutionResult = TestExecutingResult.TimedOut;
						} else if (result.Succeeded) {
							ExecutionResult = TestExecutingResult.Succeeded;
						} else {
							ExecutionResult = TestExecutingResult.Failed;
							FailureMessage = $"Execution failed with exit code {result.ExitCode}";
						}
					}
					Jenkins.MainLog.WriteLine ("Executed {0} ({1})", TestName, Mode);
				}

				if (ProduceHtmlReport) {
					try {
						var output = Logs.Create ($"Log-{Timestamp}.html", "HTML log");
						using (var srt = new StringReader (File.ReadAllText (Path.Combine (Harness.RootDirectory, "HtmlTransform.xslt")))) {
							using (var sri = File.OpenRead (xmlLog)) {
								using (var xrt = System.Xml.XmlReader.Create (srt)) {
									using (var xri = System.Xml.XmlReader.Create (sri)) {
										var xslt = new System.Xml.Xsl.XslCompiledTransform ();
										xslt.Load (xrt);
										using (var xwo = System.Xml.XmlWriter.Create (output, xslt.OutputSettings)) // use OutputSettings of xsl, so it can be output as HTML
										{
											xslt.Transform (xri, xwo);
										}
									}
								}
							}
						}
					} catch (Exception e) {
						log.WriteLine ("Failed to produce HTML report: {0}", e);
					}
				}
			}
		}

		public override void Reset ()
		{
			base.Reset ();
			BuildTask?.Reset ();
		}
	}

	abstract class MacTask : RunTestTask
	{
		public MacTask (BuildToolTask build_task)
			: base (build_task)
		{
		}

		public override string Mode {
			get {
				switch (Platform) {
				case TestPlatform.Mac:
					return "Mac";
				case TestPlatform.Mac_Classic:
					return "Mac Classic";
				case TestPlatform.Mac_Unified:
					return "Mac Unified";
				case TestPlatform.Mac_Unified32:
					return "Mac Unified 32-bit";
				case TestPlatform.Mac_UnifiedXM45:
					return "Mac Unified XM45";
				case TestPlatform.Mac_UnifiedXM45_32:
					return "Mac Unified XM45 32-bit";
				case TestPlatform.Mac_UnifiedSystem:
					return "Mac Unified System";
				default:
					throw new NotImplementedException (Platform.ToString ());
				}
			}
			set {
				throw new NotSupportedException ();
			}
		}
	}

	class MacExecuteTask : MacTask
	{
		public string Path;
		public bool BCLTest;
		public bool IsUnitTest;

		public MacExecuteTask (BuildToolTask build_task)
			: base (build_task)
		{ 
		}

		public override bool SupportsParallelExecution {
			get {
				if (TestName.Contains ("xammac")) {
					// We run the xammac tests in both Debug and Release configurations.
					// These tests are not written to support parallel execution
					// (there are hard coded paths used for instance), so disable
					// parallel execution for these tests.
					return false;
				}
				if (BCLTest) {
					// We run the BCL tests in multiple flavors (Full/Modern),
					// and the BCL tests are not written to support parallel execution,
					// so disable parallel execution for these tests.
					return false;
				}

				return base.SupportsParallelExecution;
			}
		}

		public override IEnumerable<Log> AggregatedLogs {
			get {
				return base.AggregatedLogs.Union (BuildTask.Logs);
			}
		}

		protected override async Task RunTestAsync ()
		{
			var projectDir = System.IO.Path.GetDirectoryName (ProjectFile);
			var name = System.IO.Path.GetFileName (projectDir);
			if (string.Equals ("mac", name, StringComparison.OrdinalIgnoreCase))
				name = System.IO.Path.GetFileName (System.IO.Path.GetDirectoryName (projectDir));
			var suffix = string.Empty;
			switch (Platform) {
			case TestPlatform.Mac_Unified:
				suffix = "-unified";
				break;
			case TestPlatform.Mac_Unified32:
				suffix = "-unified-32";
				break;
			case TestPlatform.Mac_UnifiedXM45:
				suffix = "-unifiedXM45";
				break;
			case TestPlatform.Mac_UnifiedXM45_32:
				suffix = "-unifiedXM45-32";
				break;
			case TestPlatform.Mac_UnifiedSystem:
				suffix = "-unifiedSystem";
				break;
			}
			if (ProjectFile.EndsWith (".sln", StringComparison.Ordinal)) {
				Path = System.IO.Path.Combine (System.IO.Path.GetDirectoryName (ProjectFile), "bin", BuildTask.ProjectPlatform, BuildTask.ProjectConfiguration + suffix, name + ".app", "Contents", "MacOS", name);
			} else {
				var project = new System.Xml.XmlDocument ();
				project.LoadWithoutNetworkAccess (ProjectFile);
				var outputPath = project.GetOutputPath (BuildTask.ProjectPlatform, BuildTask.ProjectConfiguration).Replace ('\\', '/');
				var assemblyName = project.GetAssemblyName ();
				Path = System.IO.Path.Combine (System.IO.Path.GetDirectoryName (ProjectFile), outputPath, assemblyName + ".app", "Contents", "MacOS", assemblyName);
			}

			using (var resource = await NotifyAndAcquireDesktopResourceAsync ()) {
				using (var proc = new Process ()) {
					proc.StartInfo.FileName = Path;
					if (IsUnitTest) {
						var xml = Logs.CreateFile ($"test-{Platform}-{Timestamp}.xml", "NUnit results");
						proc.StartInfo.Arguments = $"-result={StringUtils.Quote (xml)}";
					}
					proc.StartInfo.EnvironmentVariables ["MONO_DEBUG"] = "no-gdb-backtrace";
					Jenkins.MainLog.WriteLine ("Executing {0} ({1})", TestName, Mode);
					var log = Logs.Create ($"execute-{Platform}-{Timestamp}.txt", "Execution log");
					log.Timestamp = true;
					if (!Harness.DryRun) {
						ExecutionResult = TestExecutingResult.Running;

						var snapshot = new CrashReportSnapshot () { Device = false, Harness = Harness, Log = log, Logs = Logs, LogDirectory = LogDirectory };
						await snapshot.StartCaptureAsync ();

						ProcessExecutionResult result = null;
						try {
							var timeout = TimeSpan.FromMinutes (20);

							result = await proc.RunAsync (log, true, timeout);
							if (result.TimedOut) {
								FailureMessage = $"Execution timed out after {timeout.TotalSeconds} seconds.";
								log.WriteLine (FailureMessage);
								ExecutionResult = TestExecutingResult.TimedOut;
							} else if (result.Succeeded) {
								ExecutionResult = TestExecutingResult.Succeeded;
							} else {
								ExecutionResult = TestExecutingResult.Failed;
								FailureMessage = result.ExitCode != 1 ? $"Test run crashed (exit code: {result.ExitCode})." : "Test run failed.";
								log.WriteLine (FailureMessage);
							}
						} finally {
							await snapshot.EndCaptureAsync (TimeSpan.FromSeconds (Succeeded ? 0 : (result?.ExitCode > 1 ? 120 : 5)));
						}
					}
					Jenkins.MainLog.WriteLine ("Executed {0} ({1})", TestName, Mode);
				}
			}
		}
	}

	class RunXtroTask : MacExecuteTask {

		public string WorkingDirectory;

		public RunXtroTask (BuildToolTask build_task) : base (build_task)
		{
		}

		protected override async Task RunTestAsync ()
		{
			var projectDir = System.IO.Path.GetDirectoryName (ProjectFile);
			var name = System.IO.Path.GetFileName (projectDir);

			using (var resource = await NotifyAndAcquireDesktopResourceAsync ()) {
				using (var proc = new Process ()) {
					proc.StartInfo.FileName = "/Library/Frameworks/Mono.framework/Commands/mono";
					var reporter = System.IO.Path.Combine (WorkingDirectory, "xtro-report/bin/Debug/xtro-report.exe");
					var results = System.IO.Path.Combine (Logs.Directory, $"xtro-{Timestamp}");
					proc.StartInfo.Arguments = $"--debug {reporter} {WorkingDirectory} {results}";

					Jenkins.MainLog.WriteLine ("Executing {0} ({1})", TestName, Mode);
					var log = Logs.Create ($"execute-xtro-{Timestamp}.txt", "Execution log");
					log.WriteLine ("{0} {1}", proc.StartInfo.FileName, proc.StartInfo.Arguments);
					if (!Harness.DryRun) {
						ExecutionResult = TestExecutingResult.Running;

						var snapshot = new CrashReportSnapshot () { Device = false, Harness = Harness, Log = log, Logs = Logs, LogDirectory = LogDirectory };
						await snapshot.StartCaptureAsync ();

						try {
							var timeout = TimeSpan.FromMinutes (20);

							var result = await proc.RunAsync (log, true, timeout);
							if (result.TimedOut) {
								FailureMessage = $"Execution timed out after {timeout.TotalSeconds} seconds.";
								log.WriteLine (FailureMessage);
								ExecutionResult = TestExecutingResult.TimedOut;
							} else if (result.Succeeded) {
								ExecutionResult = TestExecutingResult.Succeeded;
							} else {
								ExecutionResult = TestExecutingResult.Failed;
								FailureMessage = result.ExitCode != 1 ? $"Test run crashed (exit code: {result.ExitCode})." : "Test run failed.";
								log.WriteLine (FailureMessage);
							}
						} finally {
							await snapshot.EndCaptureAsync (TimeSpan.FromSeconds (Succeeded ? 0 : 5));
						}
					}
					Jenkins.MainLog.WriteLine ("Executed {0} ({1})", TestName, Mode);

					Logs.AddFile (System.IO.Path.Combine (results, "index.html"), "HTML Report");
				}
			}
		}
	}

	abstract class RunTestTask : TestTask
	{
		public readonly BuildToolTask BuildTask;
		public bool BuildOnly;

		public RunTestTask (BuildToolTask build_task)
		{
			this.BuildTask = build_task;

			Jenkins = build_task.Jenkins;
			TestProject = build_task.TestProject;
			Platform = build_task.Platform;
			ProjectPlatform = build_task.ProjectPlatform;
			ProjectConfiguration = build_task.ProjectConfiguration;
			if (build_task.HasCustomTestName)
				TestName = build_task.TestName;
		}

		public override IEnumerable<Log> AggregatedLogs {
			get {
				var rv = base.AggregatedLogs;
				if (BuildTask != null)
					rv = rv.Union (BuildTask.AggregatedLogs);
				return rv;
			}
		}

		public override TestExecutingResult ExecutionResult {
			get {
				// When building, the result is the build result.
				if ((BuildTask.ExecutionResult & (TestExecutingResult.InProgress | TestExecutingResult.Waiting)) != 0)
					return (BuildTask.ExecutionResult & ~TestExecutingResult.InProgressMask) | TestExecutingResult.Building;
				return base.ExecutionResult;
			}
			set {
				base.ExecutionResult = value;
			}
		}

		public async Task<bool> BuildAsync ()
		{
			if (Finished)
				return true;
			
			await VerifyBuildAsync ();
			if (Finished)
				return BuildTask.Succeeded;

			ExecutionResult = TestExecutingResult.Building;
			await BuildTask.RunAsync ();
			if (!BuildTask.Succeeded) {
				if (BuildTask.TimedOut) {
					ExecutionResult = TestExecutingResult.TimedOut;
				} else {
					ExecutionResult = TestExecutingResult.BuildFailure;
				}
				FailureMessage = BuildTask.FailureMessage;
			} else if (BuildOnly) {
				ExecutionResult = TestExecutingResult.BuildSucceeded;
			} else {
				ExecutionResult = TestExecutingResult.Built;
			}
			return BuildTask.Succeeded;
		}

		protected override async Task ExecuteAsync ()
		{
			if (Finished)
				return;

			await VerifyRunAsync ();
			if (Finished)
				return;

			if (!await BuildAsync ())
				return;

			if (BuildOnly) {
				ExecutionResult = TestExecutingResult.Succeeded;
				return;
			}

			if (Finished)
				return;

			ExecutionResult = TestExecutingResult.Running;
			LastRun.ResetWatch (); // don't count the build time.
			await RunTestAsync ();
		}

		protected abstract Task RunTestAsync ();
		// VerifyBuild is called in BuildAsync to verify that the task can be built.
		// Typically used to fail tasks if there's not enough disk space.
		public virtual Task VerifyBuildAsync ()
		{
			return VerifyDiskSpaceAsync ();
		}

		public override void Reset ()
		{
			base.Reset ();
			BuildTask.Reset ();
		}
	}

	abstract class RunXITask<TDevice> : RunTestTask where TDevice: class, IDevice
	{
		IEnumerable<TDevice> candidates;
		public AppRunnerTarget AppRunnerTarget;

		protected AppRunner runner;
		protected AppRunner additional_runner;

		public IEnumerable<TDevice> Candidates => candidates;

		public AppRunner Runner {
			get {
				return runner;
			}
		}

		public TDevice Device {
			get { return (TDevice) LastRun.Device; }
			protected set { LastRun.Device = value; }
		}

		public TDevice CompanionDevice {
			get { return (TDevice) LastRun.CompanionDevice; }
			protected set { LastRun.CompanionDevice = value; }
		}

		public string BundleIdentifier {
			get { return runner.BundleIdentifier; }
		}

		public RunXITask (BuildToolTask build_task, IEnumerable<TDevice> candidates)
			: base (build_task)
		{
			this.candidates = candidates;
		}

		public override IEnumerable<Log> AggregatedLogs {
			get {
				var rv = base.AggregatedLogs;
				if (runner != null)
					rv = rv.Union (runner.Logs);
				if (additional_runner != null)
					rv = rv.Union (additional_runner.Logs);
				return rv;
			}
		}

		public override string Mode {
			get {
				
				switch (Platform) {
				case TestPlatform.tvOS:
				case TestPlatform.watchOS:
					return Platform.ToString () + " - " + XIMode;
				case TestPlatform.iOS_Unified32:
					return "iOS Unified 32-bits - " + XIMode;
				case TestPlatform.iOS_Unified64:
					return "iOS Unified 64-bits - " + XIMode;
				case TestPlatform.iOS_TodayExtension64:
					return "iOS Unified Today Extension 64-bits - " + XIMode;
				case TestPlatform.iOS_Unified:
					return "iOS Unified - " + XIMode;
				default:
					throw new NotImplementedException ();
				}
			}
			set { throw new NotImplementedException (); }
		}

		public override async Task VerifyRunAsync ()
		{
			await base.VerifyRunAsync ();
			if (Finished)
				return;

			var enumerable = candidates;
			var asyncEnumerable = enumerable as IAsyncEnumerable;
			if (asyncEnumerable != null)
				await asyncEnumerable.ReadyTask;
			if (!enumerable.Any ()) {
				ExecutionResult = TestExecutingResult.Skipped;
				FailureMessage = "No applicable devices found.";
			}
		}

		protected abstract string XIMode { get; }

		public override void Reset ()
		{
			base.Reset ();
			runner = null;
			additional_runner = null;
		}
	}

	class RunDeviceTask : RunXITask<Device>
	{
		string progress_message;
		public override string ProgressMessage {
			get {
				var pm = progress_message;
				if (pm != null)
					return pm;
				return base.ProgressMessage;
			}
		}

		public RunDeviceTask (XBuildTask build_task, IEnumerable<Device> candidates)
			: base (build_task, candidates.OrderBy ((v) => v.DebugSpeed))
		{
			switch (build_task.Platform) {
			case TestPlatform.iOS:
			case TestPlatform.iOS_Unified:
			case TestPlatform.iOS_Unified32:
			case TestPlatform.iOS_Unified64:
				AppRunnerTarget = AppRunnerTarget.Device_iOS;
				break;
			case TestPlatform.iOS_TodayExtension64:
				AppRunnerTarget = AppRunnerTarget.Device_iOS;
				break;
			case TestPlatform.tvOS:
				AppRunnerTarget = AppRunnerTarget.Device_tvOS;
				break;
			case TestPlatform.watchOS:
				AppRunnerTarget = AppRunnerTarget.Device_watchOS;
				break;
			default:
				throw new NotImplementedException ();
			}
		}

		protected override async Task RunTestAsync ()
		{
			Jenkins.MainLog.WriteLine ("Running '{0}' on device (candidates: '{1}')", ProjectFile, string.Join ("', '", Candidates.Select ((v) => v.Name).ToArray ()));

			var uninstall_log = Logs.Create ($"uninstall-{Timestamp}.log", "Uninstall log");
			using (var device_resource = await NotifyBlockingWaitAsync (Jenkins.GetDeviceResources (Candidates).AcquireAnyConcurrentAsync ())) {
				try {
					// Set the device we acquired.
					Device = Candidates.First ((d) => d.UDID == device_resource.Resource.Name);
					if (Platform == TestPlatform.watchOS)
						CompanionDevice = Jenkins.Devices.FindCompanionDevice (Jenkins.DeviceLoadLog, Device);
					Jenkins.MainLog.WriteLine ("Acquired device '{0}' for '{1}'", Device.Name, ProjectFile);

					runner = new AppRunner
					{
						Harness = Harness,
						ProjectFile = ProjectFile,
						Target = AppRunnerTarget,
						LogDirectory = LogDirectory,
						DeviceName = Device.Name,
						CompanionDeviceName = CompanionDevice?.Name,
						Configuration = ProjectConfiguration,
						IsUsb = Device.InterfaceType == "Usb",
					};

					// Sometimes devices can't upgrade (depending on what has changed), so make sure to uninstall any existing apps first.
					runner.MainLog = uninstall_log;
					var uninstall_result = await runner.UninstallAsync ();
					if (!uninstall_result.Succeeded)
						MainLog.WriteLine ($"Pre-run uninstall failed, exit code: {uninstall_result.ExitCode} (this hopefully won't affect the test result)");

					if (Failed)
						return;
					
					// Install the app, and try a few times.
					try {
						ProcessExecutionResult install_result = null;

						var progress_reporter = new CallbackLog ((line) => {
							var idx = line.IndexOf ("PercentComplete:", StringComparison.Ordinal);
							if (idx > -1)
								progress_message = $"Install: {line.Substring (idx + "PercentComplete:".Length + 1)}%";
						});

						for (var i = 0; i < 3; i++) {
							var file_log = Logs.Create ($"install-{Timestamp}.log", i == 0 ? "Install log" : $"Install log #{i + 1}", true);
							var install_log = Log.CreateAggregatedLog (file_log, progress_reporter);
							runner.MainLog = install_log;
							progress_message = null;
							install_result = await runner.InstallAsync ();
							if (install_result.Succeeded)
								break;
						}
						if (!install_result.Succeeded) {
							FailureMessage = $"Install failed, exit code: {install_result.ExitCode}.";
							ExecutionResult = TestExecutingResult.Failed;
						}
					} finally {
						progress_message = null;
					}

					if (Failed)
						return;

					// Run the app
					runner.MainLog = Logs.Create ($"run-{Device.UDID}-{Timestamp}.log", "Run log", true);
					await runner.RunAsync ();

					if (!string.IsNullOrEmpty (runner.FailureMessage))
						FailureMessage = runner.FailureMessage;
					else if (runner.Result != TestExecutingResult.Succeeded)
						FailureMessage = GuessFailureReason (runner.MainLog);

					if (runner.Result == TestExecutingResult.Succeeded && Platform == TestPlatform.iOS_TodayExtension64) {
						// For the today extension, the main app is just a single test.
						// This is because running the today extension will not wake up the device,
						// nor will it close & reopen the today app (but launching the main app
						// will do both of these things, preparing the device for launching the today extension).

						AppRunner todayRunner = new AppRunner
						{
							Harness = Harness,
							ProjectFile = TestProject.GetTodayExtension ().Path,
							Target = AppRunnerTarget,
							LogDirectory = LogDirectory,
							MainLog = Logs.Create ($"extension-run-{Device.UDID}-{Timestamp}.log", "Extension run log", true),
							DeviceName = Device.Name,
							CompanionDeviceName = CompanionDevice?.Name,
							Configuration = ProjectConfiguration,
						};
						additional_runner = todayRunner;
						await todayRunner.RunAsync ();
						foreach (var log in todayRunner.Logs.Where ((v) => !v.Description.StartsWith ("Extension ", StringComparison.Ordinal)))
							log.Description = "Extension " + log.Description [0].ToString ().ToLower () + log.Description.Substring (1);
						ExecutionResult = todayRunner.Result;

						if (!string.IsNullOrEmpty (todayRunner.FailureMessage))
							FailureMessage = todayRunner.FailureMessage;
					} else {
						ExecutionResult = runner.Result;
					}
				} finally {
					// Uninstall again, so that we don't leave junk behind and fill up the device.
					runner.MainLog = uninstall_log;
					var uninstall_result = await runner.UninstallAsync ();
					if (!uninstall_result.Succeeded)
						MainLog.WriteLine ($"Post-run uninstall failed, exit code: {uninstall_result.ExitCode} (this won't affect the test result)");

					// Also clean up after us locally.
					if (Harness.InJenkins || Harness.InWrench || Succeeded)
						await BuildTask.CleanAsync ();
				}
			}
		}

		protected override string XIMode {
			get {
				return "device";
			}
		}
	}

	class RunSimulatorTask : RunXITask<SimDevice>
	{
		public AggregatedRunSimulatorTask AggregatedTask;
		public IAcquiredResource AcquiredResource;

		public SimDevice [] Simulators {
			get {
				if (Device == null) {
					return new SimDevice [] { };
				} else if (CompanionDevice == null) {
					return new SimDevice [] { Device };
				} else {
					return new SimDevice [] { Device, CompanionDevice };
				}
			}
		}

		public RunSimulatorTask (XBuildTask build_task, IEnumerable<SimDevice> candidates = null)
			: base (build_task, candidates)
		{
			var project = Path.GetFileNameWithoutExtension (ProjectFile);
			if (project.EndsWith ("-tvos", StringComparison.Ordinal)) {
				AppRunnerTarget = AppRunnerTarget.Simulator_tvOS;
			} else if (project.EndsWith ("-watchos", StringComparison.Ordinal)) {
				AppRunnerTarget = AppRunnerTarget.Simulator_watchOS;
			} else {
				AppRunnerTarget = AppRunnerTarget.Simulator_iOS;
			}
		}

		public Task SelectSimulatorAsync ()
		{
			if (Finished)
				return Task.FromResult (true);
			
			if (!BuildTask.Succeeded) {
				ExecutionResult = TestExecutingResult.BuildFailure;
				return Task.FromResult (true);
			}

			Device = Candidates.First ();
			if (Platform == TestPlatform.watchOS)
				CompanionDevice = Jenkins.Simulators.FindCompanionDevice (Jenkins.SimulatorLoadLog, Device);

			var clean_state = false;//Platform == TestPlatform.watchOS;
			runner = new AppRunner ()
			{
				Harness = Harness,
				ProjectFile = ProjectFile,
				EnsureCleanSimulatorState = clean_state,
				Target = AppRunnerTarget,
				LogDirectory = LogDirectory,
				MainLog = Logs.Create ($"run-{Device.UDID}-{Timestamp}.log", "Run log", true),
				Configuration = ProjectConfiguration,
			};
			runner.Simulators = Simulators;
			runner.Initialize ();

			return Task.FromResult (true);
		}

		class NondisposedResource : IAcquiredResource
		{
			public IAcquiredResource Wrapped;

			public Resource Resource {
				get {
					return Wrapped.Resource;
				}
			}

			public void Dispose ()
			{
				// Nope, no disposing here.
			}
		}

		Task<IAcquiredResource> AcquireResourceAsync ()
		{
			if (AcquiredResource != null) {
				// We don't own the acquired resource, so wrap it in a class that won't dispose it.
				return Task.FromResult<IAcquiredResource> (new NondisposedResource () { Wrapped = AcquiredResource });
			} else {
				return Jenkins.DesktopResource.AcquireExclusiveAsync ();
			}
		}

		protected override async Task RunTestAsync ()
		{
			Jenkins.MainLog.WriteLine ("Running XI on '{0}' ({2}) for {1}", Device?.Name, ProjectFile, Device?.UDID);

			ExecutionResult = (ExecutionResult & ~TestExecutingResult.InProgressMask) | TestExecutingResult.Running;
			await BuildTask.RunAsync ();
			if (!BuildTask.Succeeded) {
				ExecutionResult = TestExecutingResult.BuildFailure;
				return;
			}
			using (var resource = await NotifyBlockingWaitAsync (AcquireResourceAsync ())) {
				if (runner == null)
					await SelectSimulatorAsync ();
				await runner.RunAsync ();
			}
			ExecutionResult = runner.Result;
		}

		protected override string XIMode {
			get {
				return "simulator";
			}
		}
	}

	// This class groups simulator run tasks according to the
	// simulator they'll run from, so that we minimize switching
	// between different simulators (which is slow).
	class AggregatedRunSimulatorTask : TestTask
	{
		public IEnumerable<RunSimulatorTask> Tasks;

		// Due to parallelization this isn't the same as the sum of the duration for all the build tasks.
		Stopwatch build_timer = new Stopwatch ();
		public TimeSpan BuildDuration { get { return build_timer.Elapsed; } }

		Stopwatch run_timer = new Stopwatch ();
		public TimeSpan RunDuration { get { return run_timer.Elapsed; } }

		public AggregatedRunSimulatorTask (IEnumerable<RunSimulatorTask> tasks)
		{
			this.Tasks = tasks;
			foreach (var task in tasks)
				task.AggregatedTask = this;
		}

		IEnumerable<RunSimulatorTask> GetExecutingTasks ()
		{
			return Tasks.Where ((v) => !v.Ignored && !v.Failed);;
		}

		public async Task PrepareSimulatorsAsync (IEnumerable<RunSimulatorTask> executingTasks = null, IEnumerable<SimDevice> devices = null)
		{
			if (executingTasks == null)
				executingTasks = GetExecutingTasks ();
			
			if (devices == null)
				devices = executingTasks.First ().Simulators;
			
			foreach (var dev in devices)
				await dev.PrepareSimulatorAsync (Jenkins.MainLog, executingTasks.Select ((v) => v.BundleIdentifier).ToArray ());
		}

		protected override void PropagateResults ()
		{
			foreach (var task in Tasks) {
				task.ExecutionResult = ExecutionResult;
				task.FailureMessage = FailureMessage;
			}
		}

		protected override async Task ExecuteAsync ()
		{
			if (Tasks.All ((v) => v.Ignored)) {
				ExecutionResult = TestExecutingResult.Ignored;
				return;
			}

			var executingTasks = new Queue<RunSimulatorTask> (GetExecutingTasks ());
			RunSimulatorTask last_re_executed_task = null;

			var executingTasks = Tasks.Where ((v) => !v.Ignored && !v.Failed);
			if (!executingTasks.Any ()) {
				ExecutionResult = TestExecutingResult.Failed;
				return;
			}

			while (executingTasks.Count > 0) {
				// First build everything. This is required for the run simulator
				// task to properly configure the simulator.
				build_timer.Start ();
				await Task.WhenAll (executingTasks.Select ((v) => v.BuildAsync ()));
				build_timer.Stop ();

				using (var desktop = await NotifyBlockingWaitAsync (Jenkins.DesktopResource.AcquireExclusiveAsync ())) {
					run_timer.Start ();

					// We need to set the dialog permissions for all the apps    
					// before launching the simulator, because once launched
					// the simulator caches the values in-memory.
					foreach (var task in executingTasks)
						await task.SelectSimulatorAsync ();

					var devices = executingTasks.First ().Simulators;
					Jenkins.MainLog.WriteLine ("Selected simulator: {0}", devices.Length > 0 ? devices [0].Name : "none");

					await PrepareSimulatorsAsync (executingTasks, devices);

					while (executingTasks.Count > 0) {
						var task = executingTasks.Peek ();
						task.AcquiredResource = desktop;
						try {
							await task.RunAsync ();
							if (!task.Succeeded && !task.Runner.TestsLaunched && task.Platform == TestPlatform.watchOS && last_re_executed_task != task) {
								// watchOS test run where the tests didn't launch. Try again
								last_re_executed_task = task;
								task.Reset ();
								break;
							}

							// We're done with this test. Remove it from the queue.
							executingTasks.Dequeue ();
						} finally {
							task.AcquiredResource = null;
						}
					}

					foreach (var dev in devices)
						await dev.ShutdownAsync (Jenkins.MainLog);

					await SimDevice.KillEverythingAsync (Jenkins.MainLog);

					run_timer.Stop ();
				}
			}

			if (Tasks.All ((v) => v.Ignored)) {
				ExecutionResult = TestExecutingResult.Ignored;
			} else {
				ExecutionResult = Tasks.Any ((v) => v.Failed) ? TestExecutingResult.Failed : TestExecutingResult.Succeeded;
			}
		}
	}

	// This is a very simple class to manage the general concept of 'resource'.
	// Performance isn't important, so this is very simple.
	// Currently it's only used to make sure everything that happens on the desktop
	// is serialized (Jenkins.DesktopResource), but in the future the idea is to
	// make each connected device a separate resource, which will make it possible
	// to run tests in parallel across devices (and at the same time use the desktop
	// to build the next test project).
	class Resource
	{
		public string Name;
		public string Description;
		ConcurrentQueue<TaskCompletionSource<IAcquiredResource>> queue = new ConcurrentQueue<TaskCompletionSource<IAcquiredResource>> ();
		ConcurrentQueue<TaskCompletionSource<IAcquiredResource>> exclusive_queue = new ConcurrentQueue<TaskCompletionSource<IAcquiredResource>> ();
		int users;
		int max_concurrent_users = 1;
		bool exclusive;

		public int Users => users;
		public int QueuedUsers => queue.Count + exclusive_queue.Count;
		public int MaxConcurrentUsers {
			get {
				return max_concurrent_users;
			}
			set {
				max_concurrent_users = value;
			}
		}

		public Resource (string name, int max_concurrent_users = 1, string description = null)
		{
			this.Name = name;
			this.max_concurrent_users = max_concurrent_users;
			this.Description = description ?? name;
		}

		public Task<IAcquiredResource> AcquireConcurrentAsync ()
		{
			lock (queue) {
				if (!exclusive && users < max_concurrent_users) {
					users++;
					return Task.FromResult<IAcquiredResource> (new AcquiredResource (this));
				} else {
					var tcs = new TaskCompletionSource<IAcquiredResource> (new AcquiredResource (this));
					queue.Enqueue (tcs);
					return tcs.Task;
				}
			}
		}

		public Task<IAcquiredResource> AcquireExclusiveAsync ()
		{
			lock (queue) {
				if (users == 0) {
					users++;
					exclusive = true;
					return Task.FromResult<IAcquiredResource> (new AcquiredResource (this));
				} else {
					var tcs = new TaskCompletionSource<IAcquiredResource> (new AcquiredResource (this));
					exclusive_queue.Enqueue (tcs);
					return tcs.Task;
				}
			}
		}

		void Release ()
		{
			TaskCompletionSource<IAcquiredResource> tcs;

			lock (queue) {
				users--;
				exclusive = false;
				if (queue.TryDequeue (out tcs)) {
					users++;
					tcs.SetResult ((IAcquiredResource) tcs.Task.AsyncState);
				} else if (users == 0 && exclusive_queue.TryDequeue (out tcs)) {
					users++;
					exclusive = true;
					tcs.SetResult ((IAcquiredResource) tcs.Task.AsyncState);
				}
			}
		}

		class AcquiredResource : IAcquiredResource
		{
			Resource resource;

			public AcquiredResource (Resource resource)
			{
				this.resource = resource;
			}

			void IDisposable.Dispose ()
			{
				resource.Release ();
			}

			public Resource Resource { get { return resource; } }
		}
	}

	interface IAcquiredResource : IDisposable
	{
		Resource Resource { get; }
	}

	class Resources
	{
		readonly Resource [] resources;

		public Resources (IEnumerable<Resource> resources)
		{
			this.resources = resources.ToArray ();
		}

		public Task<IAcquiredResource> AcquireAnyConcurrentAsync ()
		{
			if (resources.Length == 0)
				throw new Exception ("No resources");

			if (resources.Length == 1)
				return resources [0].AcquireConcurrentAsync ();

			// We try to acquire every resource
			// When the first one succeeds, we set the result to true
			// We immediately release any other resources we acquire.
			var tcs = new TaskCompletionSource<IAcquiredResource> ();
			for (int i = 0; i < resources.Length; i++) {
				resources [i].AcquireConcurrentAsync ().ContinueWith ((v) =>
				{
					var ar = v.Result;
					if (!tcs.TrySetResult (ar))
						ar.Dispose ();
				});
			}

			return tcs.Task;
		}
	}

	public enum TestPlatform
	{
		None,
		All,

		iOS,
		iOS_Unified,
		iOS_Unified32,
		iOS_Unified64,
		iOS_TodayExtension64,
		tvOS,
		watchOS,

		Mac,
		Mac_Classic,
		Mac_Unified,
		Mac_UnifiedXM45,
		Mac_Unified32,
		Mac_UnifiedXM45_32,
		Mac_UnifiedSystem,
	}

	[Flags]
	public enum TestExecutingResult
	{
		NotStarted = 0,
		InProgress = 0x1,
		Finished   = 0x2,
		Waiting    = 0x4,
		StateMask  = NotStarted + InProgress + Waiting + Finished,

		// In progress state
		Building         =   0x10 + InProgress,
		BuildQueued      =   0x10 + InProgress + Waiting,
		Built            =   0x20 + InProgress,
		Running          =   0x40 + InProgress,
		RunQueued        =   0x40 + InProgress + Waiting,
		InProgressMask   =   0x10 + 0x20 + 0x40,

		// Finished results
		Succeeded        =  0x100 + Finished,
		Failed           =  0x200 + Finished,
		Ignored          =  0x400 + Finished,
		Skipped          =  0x800 + Finished,
		BuildSucceeded   =0x10000 + Finished,

		// Finished & Failed results
		Crashed          = 0x1000 + Failed,
		TimedOut         = 0x2000 + Failed,
		HarnessException = 0x4000 + Failed,
		BuildFailure     = 0x8000 + Failed,
	}
}
