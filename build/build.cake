#addin "nuget:?package=Cake.FileHelpers&version=3.2.1"
#addin "nuget:?package=Cake.Powershell&version=0.4.8"
#tool "nuget:?package=GitVersion.CommandLine&version=5.0.1"

using System;
using System.Linq;
using System.Text.RegularExpressions;

//////////////////////////////////////////////////////////////////////
// ARGUMENTS
//////////////////////////////////////////////////////////////////////

var target = Argument("target", "Default");

//////////////////////////////////////////////////////////////////////
// VARIABLES
//////////////////////////////////////////////////////////////////////

var baseDir = MakeAbsolute(Directory("../")).ToString();
var buildDir = baseDir + "/build";
var Solution = baseDir + "/src/Uno.SampleCoreApp";
var toolsDir = buildDir + "/tools";
GitVersion versionInfo = null;

//////////////////////////////////////////////////////////////////////
// METHODS
//////////////////////////////////////////////////////////////////////

void VerifyHeaders(bool Replace)
{
	var header = FileReadText("header.txt") + "\r\n";
	bool hasMissing = false;

	Func<IFileSystemInfo, bool> exclude_objDir =
		fileSystemInfo => !fileSystemInfo.Path.Segments.Contains("obj");

	var files = GetFiles(baseDir + "/**/*.cs", exclude_objDir).Where(file => 
	{
		var path = file.ToString();
		return !(path.EndsWith(".g.cs") || path.EndsWith(".i.cs") || System.IO.Path.GetFileName(path).Contains("TemporaryGeneratedFile"));
	});

	Information("\nChecking " + files.Count() + " file header(s)");
	foreach(var file in files)
	{
		var oldContent = FileReadText(file);
		if(oldContent.Contains("// <auto-generated>"))
		{
		   continue;
		}
		var rgx = new Regex("^(//.*\r?\n|\r?\n)*");
		var newContent = header + rgx.Replace(oldContent, "");

		if(!newContent.Equals(oldContent, StringComparison.Ordinal))
		{
			if(Replace)
			{
				Information("\nUpdating " + file + " header...");
				FileWriteText(file, newContent);
			}
			else
			{
				Error("\nWrong/missing header on " + file);
				hasMissing = true;
			}
		}
	}

	if(!Replace && hasMissing)
	{
		throw new Exception("Please run UpdateHeaders.bat or '.\\build.ps1 -target=UpdateHeaders' and commit the changes.");
	}
}

//////////////////////////////////////////////////////////////////////
// DEFAULT TASK
//////////////////////////////////////////////////////////////////////

Task("Build")
	.IsDependentOn("Version")
	.Description("Build all projects and get the assemblies")
	.Does(() =>
{
	Information("\nBuilding Solution");

	var settings = new DotNetCoreBuildSettings
	{
		Configuration = "Release",
	};

	DotNetCoreBuild(Solution, settings);

	var nuGetPackSettings = new NuGetPackSettings
	{
	  Version = versionInfo.SemVer,
	};

	var nugetFilePaths = GetFiles("./*.nuspec");

	NuGetPack(nugetFilePaths, nuGetPackSettings);
});


//////////////////////////////////////////////////////////////////////
// TASK TARGETS
//////////////////////////////////////////////////////////////////////

Task("Default")
	.IsDependentOn("Build");

Task("UpdateHeaders")
	.Description("Updates the headers in *.cs files")
	.Does(() =>
{
	VerifyHeaders(true);
});

Task("Version")
	.Description("Updates target versions")
	.Does(() =>
{
	versionInfo = GitVersion(new GitVersionSettings {
		UpdateAssemblyInfo = true,
		LogFilePath = System.IO.Path.Combine(EnvironmentVariable("BUILD_ARTIFACTSTAGINGDIRECTORY"), "GitVersionLog.txt"),
		UpdateAssemblyInfoFilePath = baseDir + "/build/AssemblyVersion.cs"
	});

	Information($"SemVer: {versionInfo.SemVer} Sha: {versionInfo.Sha}");

	var files = new[] {
		@"..\src\Uno.SourceGeneratorTasks.Dev15.0\Content\Uno.SourceGenerationTasks.targets",
		@"..\src\Uno.SourceGeneratorTasks.Dev15.0\Uno.SourceGeneratorTasks.Dev15.0.csproj",
		@"..\src\Uno.SourceGeneratorTasks.Dev15.0\Tasks\SourceGenerationTask.cs"
	};
	
	foreach(var file in files)
	{
		var text = System.IO.File.ReadAllText(file);
		System.IO.File.WriteAllText(file, text.Replace("v0", "v" + versionInfo.Sha));
	}

	// Update the commit hash for client/server sync validation
	var serverFiles = new[] {
		@"..\src\Uno.SourceGeneratorTasks.Dev15.0\Tasks\SourceGenerationTask.cs",
		@"..\src\Uno.SourceGeneration.Host\Program.cs",
	};
	
	foreach(var file in serverFiles)
	{
		var text = System.IO.File.ReadAllText(file);
		System.IO.File.WriteAllText(file, text.Replace("<developer build>", "v" + versionInfo.Sha));
	}

});

//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////

RunTarget(target);
