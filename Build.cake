﻿var target = Argument("target", "NuGet");
var configuration = Argument("configuration", "Release");

string serenityVersion = null;

var nuspecParams = new Dictionary<string, string> {
  { "authors", "Volkan Ceylan" },
  { "owners", "Volkan Ceylan" },
  { "language", "en-US" },
  { "iconUrl", "https://raw.github.com/volkanceylan/Serenity/master/Tools/Images/serenity-logo-128.png" },
  { "licenceUrl", "https://raw.github.com/volkanceylan/Serenity/master/LICENSE.md" },
  { "projectUrl", "http://github.com/volkanceylan/Serenity" },
  { "copyright", "Copyright (c) Volkan Ceylan" },
  { "tags", "Serenity" },
  { "framework", "net45" }
};

var nugetPackages = new List<string>();

Func<string, System.Xml.XmlDocument> loadXml = path => 
{
    var xml = new System.Xml.XmlDocument();
    xml.LoadXml(System.IO.File.ReadAllText(path));
    return xml;
};

Func<string, string, string> getPackageVersion = (project, package) => 
{
    var node = loadXml(@".\" + project + @"\packages.config").SelectSingleNode("//package[@id='" + package + "']/@version");
    if (node == null || node.Value == null)
        throw new InvalidOperationException("Couldn't find version for " + package + " in project " + project);
    return node.Value;
};

Action<string> minimizeJs = filename => {
    StartProcess("./Tools/Node/uglifyjs.cmd", new ProcessSettings 
    {
        Arguments = filename + 
            " -o " + System.IO.Path.ChangeExtension(filename, "min.js") + 
            " --comments --mangle"
    });
};

Action runGitLink = () => {
    StartProcess("./Tools/GitLink/GitLink.exe", new ProcessSettings
    { 
        Arguments = System.IO.Path.GetFullPath(@".\") + " -u https://github.com/volkanceylan/serenity"
    });
};
    
Task("Clean")
    .Does(() =>
{
    CleanDirectories("./Bin");
    CreateDirectory("./Bin");
    CreateDirectory("./Bin/Packages");
    CreateDirectory("./Bin/Temp");
    CleanDirectories("./Serenity.*/**/bin/" + configuration);
});

Task("Restore-NuGet-Packages")
    .IsDependentOn("Clean")
    .Does(context =>
{
    NuGetRestore("./Serenity.sln");
});

Task("Build")
    .IsDependentOn("Restore-NuGet-Packages")
    .Does(context => 
{
    MSBuild("./Serenity.sln", s => {
        s.SetConfiguration(configuration);
    });
    
    var vi = System.Diagnostics.FileVersionInfo.GetVersionInfo("./Serenity.Core/bin/" + configuration + "/Serenity.Core.dll");
    serenityVersion = vi.FileMajorPart + "." + vi.FileMinorPart + "." + vi.FileBuildPart;   
});

Task("Unit-Tests")
    .IsDependentOn("Build")
    .Does(() =>
{
    XUnit2("./Serenity.Test*/**/bin/" + configuration + "/*.Test.dll");
});

Task("Copy-Files")
    .IsDependentOn("Unit-Tests")
    .Does(() =>
{
});

Task("Pack")
    .IsDependentOn("Copy-Files")
    .Does(() =>
{
    if ((target ?? "").ToLowerInvariant() == "nuget-push")
        runGitLink();
});

Task("NuGet")
    .IsDependentOn("Pack")
    .Does(() =>
{
    nuspecParams["configuration"] = configuration;
    nuspecParams["jsonNetVersion"] = getPackageVersion("Serenity.Core", "Newtonsoft.Json");
    nuspecParams["couchbaseNetClientVersion"] = getPackageVersion("Serenity.Caching.Couchbase", "CouchbaseNetClient");
    nuspecParams["stackExchangeRedisVersion"] = getPackageVersion("Serenity.Caching.Redis", "StackExchange.Redis");
    nuspecParams["microsoftAspNetMvcVersion"] = getPackageVersion("Serenity.Web", "Microsoft.AspNet.Mvc");
    nuspecParams["microsoftAspNetRazorVersion"] = getPackageVersion("Serenity.Web", "Microsoft.AspNet.Razor");
    nuspecParams["microsoftAspNetWebPagesVersion"] = getPackageVersion("Serenity.Web", "Microsoft.AspNet.WebPages");
    nuspecParams["microsoftWebInfrastructureVersion"] = getPackageVersion("Serenity.Web", "Microsoft.Web.Infrastructure");
    nuspecParams["saltarelleCompilerVersion"] = getPackageVersion("Serenity.Script.Imports", "Saltarelle.Compiler");
    nuspecParams["saltarellejQueryVersion"] = getPackageVersion("Serenity.Script.Imports", "Saltarelle.jQuery");
    nuspecParams["saltarellejQueryUIVersion"] = getPackageVersion("Serenity.Script.Imports", "Saltarelle.jQuery.UI");
    nuspecParams["saltarelleLinqVersion"] = getPackageVersion("Serenity.Script.Imports", "Saltarelle.Linq");
    nuspecParams["saltarelleRuntimeVersion"] = getPackageVersion("Serenity.Script.Imports", "Saltarelle.Runtime");
    nuspecParams["saltarelleWebVersion"] = getPackageVersion("Serenity.Script.Imports", "Saltarelle.Web");
    nuspecParams["scriptFramework"] = loadXml(@".\Serenity.Script.Imports\packages.config").SelectSingleNode("//package[@id='Saltarelle.Runtime']/@targetFramework").Value;

    Action<string, string> myPack = (s, id) => {
        var nuspec = System.IO.File.ReadAllText("./" + s + "/" + (id ?? s) + ".nuspec");
      
        nuspec = nuspec.Replace("${version}", serenityVersion);
        nuspec = nuspec.Replace("${id}", (id ?? s));
        
        foreach (var p in nuspecParams)
            nuspec = nuspec.Replace("${" + p.Key + "}", p.Value);
          
        var assembly = "./" + s + "/bin/" + configuration + "/" + s + ".dll";
        if (!System.IO.File.Exists(assembly))
            assembly = "./" + s + "/bin/" + configuration + "/" + s + ".exe";
          
        if (System.IO.File.Exists(assembly)) 
        {
            var vi = System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly);
            nuspec = nuspec.Replace("${title}", vi.FileDescription);
            nuspec = nuspec.Replace("${description}", vi.Comments);
        }
      
        System.IO.File.WriteAllText("./Bin/Temp/" + s + ".temp.nuspec", nuspec);
       
        NuGetPack("./Bin/Temp/" + s + ".temp.nuspec", new NuGetPackSettings {
            BasePath = "./" + s + "/bin/" + configuration,
            OutputDirectory = "./Bin/Packages",
            NoPackageAnalysis = true
        });
        
        nugetPackages.Add("./Bin/Packages/" + (id ?? s) + "." + serenityVersion + ".nupkg");
    };
    
    myPack("Serenity.Core", null);
    myPack("Serenity.Caching.Couchbase", null);
    myPack("Serenity.Caching.Redis", null);
    myPack("Serenity.Data", null);
    myPack("Serenity.Data.Entity", null);
    myPack("Serenity.Services", null);
    myPack("Serenity.Testing", null);
    
    myPack("Serenity.Script.UI", "Serenity.Script");

    myPack("Serenity.Web", null);
    myPack("Serenity.CodeGenerator", null);
    
    if (System.IO.Directory.Exists(@"C:\Sandbox\MyNugetFeed")) 
    {
        foreach (var package in nugetPackages)
            System.IO.File.Copy(package, @"C:\Sandbox\MyNugetFeed\" + System.IO.Path.GetFileName(package), true);
            
        foreach (var package in System.IO.Directory.GetFiles(
			System.IO.Path.Combine(System.Environment.GetFolderPath(
				System.Environment.SpecialFolder.LocalApplicationData), @"nuget\cache"), "Seren*.nupkg"))
            System.IO.File.Delete(package);
    }
});

Task("NuGet-Push")
  .IsDependentOn("NuGet")
  .Does(() => 
  {
      foreach (var package in nugetPackages)
      {
          NuGetPush(package, new NuGetPushSettings {
              Source = "https://www.nuget.org/api/v2/package"
          });
      }
  });
  
Task("VSIX")
  .Does(() => 
  {
      
  });

RunTarget(target);