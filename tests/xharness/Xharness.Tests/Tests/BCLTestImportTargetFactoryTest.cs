using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.DotNet.XHarness.iOS.Shared.TestImporter;
using Microsoft.DotNet.XHarness.iOS.Shared.TestImporter.Templates;
using Moq;
using NUnit.Framework;
using Xharness.TestImporter.Xamarin;

namespace Xharness.Tests.TestImporter.Tests {


	// test the class so that we ensure that we do call the template object and that we are correctly creating the
	// default projects.
	public class BCLTestImportTargetFactoryTest
	{
		string outputdir;
		AssemblyLocator assemblyLocator;
		Mock<ITemplatedProject> template;
		BCLTestImportTargetFactory generator;

		[SetUp]
		public void SetUp ()
		{
			outputdir = Path.GetTempFileName ();
			File.Delete (outputdir);
			Directory.CreateDirectory (outputdir);
			assemblyLocator = new AssemblyLocator ();
			template = new Mock<ITemplatedProject> ();
			generator = new BCLTestImportTargetFactory (outputdir) {
				AssemblyLocator = assemblyLocator,
				TemplatedProject = template.Object
			};
		}

		[TearDown]
		public void TearDown ()
		{
			if (Directory.Exists (outputdir))
				Directory.Delete (outputdir, false);
			template = null;
		}

		[Test]
		public void ConstructorNullOutputDir ()
		{
			Assert.Throws<ArgumentNullException> (() => new BCLTestImportTargetFactory ((string) null));
			Assert.Throws<ArgumentNullException> (() => new BCLTestImportTargetFactory (null, ""));
		}
		
		[Test]
		public void ConstructorNullMonoDir () => 
			Assert.Throws<ArgumentNullException> (() => new BCLTestImportTargetFactory ("", null));

		[Test]
		public void iOSMonoSDKPathGetterTest ()
		{
			assemblyLocator.iOSMonoSDKPath = "/path/to/ios/sdk";
			Assert.AreEqual (assemblyLocator.iOSMonoSDKPath, generator.iOSMonoSDKPath);
		}

		[Test]
		public void iOSMonoSDKPathSetterTest ()
		{
			generator.iOSMonoSDKPath = "/path/to/ios/sdk";
			Assert.AreEqual (generator.iOSMonoSDKPath, assemblyLocator.iOSMonoSDKPath);
		}

		[Test]
		public void MacMonoSDKPathGetterTest ()
		{
			assemblyLocator.MacMonoSDKPath = "/path/to/mac/sdk";
			Assert.AreEqual (assemblyLocator.MacMonoSDKPath, generator.MacMonoSDKPath);
		}

		[Test]
		public void MacMonoSDKPathSetterTest ()
		{
			generator.MacMonoSDKPath = "/path/to/mac/sdk";
			Assert.AreEqual (generator.MacMonoSDKPath, assemblyLocator.MacMonoSDKPath);
		}

		[Test]
		public async Task GenerateTestProjectsAsyncTest ()
		{
			var projects = new GeneratedProjects () {
				( Name: "First project", Path: "", XUnit: false, ExtraArgs: "", Failure: "", TimeoutMultiplier: 1),
				( Name: "Second project", Path: "", XUnit: true, ExtraArgs: "", Failure: "", TimeoutMultiplier: 1),
			};
			var infos = new List<(string Name, string [] Assemblies, string ExtraArgs, double TimeoutMultiplier)> {
				( Name: "First project", Assemblies: new string [] { }, ExtraArgs: "", TimeoutMultiplier: 1),
				( Name: "Second project", Assemblies: new string [] { }, ExtraArgs: "", TimeoutMultiplier: 1),
			};
			template.Setup (t => t.GenerateTestProjectsAsync (It.IsAny<IEnumerable<(string Name, string [] Assemblies, string ExtraArgs, double TimeoutMultiplier)>> (), It.IsAny<Platform> ())).Returns (() => {
				return Task.FromResult (projects);
			});
			var result = await generator.GenerateTestProjectsAsync (infos, Platform.iOS);
			Assert.AreEqual (projects.Count, result.Count);
			template.Verify (t => t.GenerateTestProjectsAsync (It.IsAny<IEnumerable<(string Name, string [] Assemblies, string ExtraArgs, double TimeoutMultiplier)>> (), It.IsAny<Platform> ()));
		}
	}
}
