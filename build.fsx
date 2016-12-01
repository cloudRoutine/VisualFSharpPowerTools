// --------------------------------------------------------------------------------------
// FAKE build script 
// --------------------------------------------------------------------------------------

#r @"packages/build/FAKE/tools/FakeLib.dll"

open Fake 
open Fake.Git
open Fake.AssemblyInfoFile
open Fake.ReleaseNotesHelper
open System
open System.IO
open System.Xml
open Fake.Testing

// Information about the project are used
//  - for version and project name in generated AssemblyInfo file
//  - by the generated NuGet package 
//  - to run tests and to publish documentation on GitHub gh-pages
//  - for documentation, you also need to edit info in "docs/tools/generate.fsx"

// The name of the project 
// (used by attributes in AssemblyInfo, name of a NuGet package and directory in 'src')
//let project = "FSharpVSPowerTools"
let project = "FSharp.Editing"

// Short summary of the project
// (used as description in AssemblyInfo and as a short summary for NuGet package)
let summary = "A collection of additional commands for F# in Visual Studio"

// Longer description of the project
// (used as a description for NuGet package; line breaks are automatically cleaned up)
let description = """The core project of Visual F# Power Tools includes IDE-agnostic features intended to be used in different F# IDEs and editors."""
// List of author names (for NuGet package)
let authors = [ "Anh-Dung Phan"; "Vasily Kirichenko"; "Jared Hester"; "Denis Ok" ]
// Tags for your project (for NuGet package)
let tags = "F# fsharp formatting editing highlighting navigation refactoring"

// File system information 
// (<solutionFile>.sln is built during the building process)
let solutionFile  = "FSharpVSPowerTools"

// Git configuration (used for publishing documentation in gh-pages branch)
// The profile where the project is posted 
let gitOwner = "fsprojects"
let gitHome = "https://github.com/" + gitOwner

// The name of the project on GitHub
let gitName = "VisualFSharpPowerTools"
let cloneUrl = "https://github.com/fsprojects/VisualFSharpPowerTools.git"

// Read additional information from the release notes document
Environment.CurrentDirectory <- __SOURCE_DIRECTORY__
let release = parseReleaseNotes (IO.File.ReadAllLines "RELEASE_NOTES.md")

let isAppVeyorBuild = environVar "APPVEYOR" <> null
let buildVersion = sprintf "%s-a%s" release.NugetVersion (DateTime.UtcNow.ToString "yyMMddHHmm")

Target "BuildVersion" (fun _ ->
    Shell.Exec("appveyor", sprintf "UpdateBuild -Version \"%s\"" buildVersion) |> ignore
)

// Generate assembly info files with the right version & up-to-date information
Target "AssemblyInfo" (fun _ ->
  let shared =
    [   Attribute.Product project
        Attribute.Description summary
        Attribute.Version release.AssemblyVersion
        Attribute.FileVersion release.AssemblyVersion ] 

  CreateCSharpAssemblyInfo "src/FSharpVSPowerTools/Properties/AssemblyInfo.cs"
      (Attribute.InternalsVisibleTo "FSharp.Editing.VisualStudio.Tests" :: Attribute.Title "FSharpVSPowerTools" :: shared)

  CreateFSharpAssemblyInfo "src/FSharp.Editing/AssemblyInfo.fs"
      (Attribute.InternalsVisibleTo "FSharp.Editing.Tests" :: Attribute.Title "FSharp.Editing" :: shared)

  CreateFSharpAssemblyInfo "src/FSharp.Editing.VisualStudio/AssemblyInfo.fs"
      (Attribute.InternalsVisibleTo "FSharp.Editing.VisualStudio.Tests" :: Attribute.Title "FSharp.Editing.VisualStudio" :: shared)

  CreateFSharpAssemblyInfo "src/FSharp.Editing.VisualStudio.Tests.v2015/AssemblyInfo.fs"
      (Attribute.InternalsVisibleTo "FSharp.Editing.VisualStudio.Tests" :: Attribute.Title "FSharp.Editing.VisualStudio.v2015" :: shared) 
)

Target "VsixManifest" (fun _ ->
    let manifest = "./src/FSharpVSPowerTools/source.extension.vsixmanifest"
    let doc = new XmlDocument(PreserveWhitespace=true) in
    doc.Load manifest
    doc.GetElementsByTagName("Identity") 
        |> Seq.cast<XmlNode> 
        |> Seq.head 
        |> fun node -> 
            let currentVersion = node.Attributes.GetNamedItem("Version").Value
            node.Attributes.GetNamedItem("Version").Value <- sprintf "%s.%s" currentVersion AppVeyor.AppVeyorEnvironment.BuildNumber
    doc.Save manifest
)

// --------------------------------------------------------------------------------------
// Clean build results

Target "Clean" (fun _ ->
    CleanDirs ["bin"; "temp"; "nuget"]
)

Target "CleanDocs" (fun _ ->
    CleanDirs ["docs/output"]
)

// --------------------------------------------------------------------------------------
// Build library & test project

Target "Build" (fun _ ->
    // We would like to build only one solution
    !! (solutionFile + ".sln")
    |> MSBuildRelease "" "Rebuild"
    |> ignore
)

// Build test projects in Debug mode in order to provide correct paths for multi-project scenarios
Target "BuildTests" (fun _ ->    
    !! "tests/data/**/*.sln"
    |> MSBuildDebug "" "Rebuild"
    |> ignore
)

let count label glob =
    let (fileCount, lineCount) =
        !! glob
        |> Seq.map (fun path -> File.ReadLines path |> Seq.length)
        |> Seq.fold (fun (fileCount, lineCount) lineNum -> (fileCount+1, lineCount + lineNum)) (0, 0)
    printfn "%s - File Count: %i, Line Count: %i." label fileCount lineCount

Target "RunStatistics" (fun _ ->
    count "F# Source" "src/**/*.fs"
    count "C# Source" "src/**/*.cs"
    count "F# Test" "tests/**/*.fs"
    count "C# Test" "tests/**/*.cs"
)

// --------------------------------------------------------------------------------------
// Run the unit tests using test runner

Target "UnitTests" (fun _ ->
    [@"tests/FSharp.Editing.Tests/bin/Release/FSharp.Editing.Tests.dll"]
    |> NUnit3 (fun p ->
        let param =
            { p with
                ShadowCopy = false
                TimeOut = TimeSpan.FromMinutes 20.
                //Framework = NUnit3Runtime.Net45
                Domain = NUnit3DomainModel.MultipleDomainModel 
                //Workers = Some 1
                ResultSpecs = ["TestResults.xml"] }
        if isAppVeyorBuild then { param with Where = "cat != AppVeyorLongRunning" } else param)
)

Target "IntegrationTests" (fun _ ->
    [@"tests/FSharp.Editing.VisualStudio.Tests/bin/Release/FSharp.Editing.VisualStudio.Tests.dll"]
    |> NUnit3 (fun p ->
        let param =
            { p with
                ShadowCopy = false
                TimeOut = TimeSpan.FromMinutes 20.
                //Framework = NUnit3Runtime.Net45
                Domain = NUnit3DomainModel.MultipleDomainModel 
                //Workers = Some 1
                ResultSpecs = ["TestResults.xml"] }
        if isAppVeyorBuild then { param with Where = "cat != AppVeyorLongRunning" } else param)
)

// --------------------------------------------------------------------------------------
// Build a NuGet package

Target "BuildNupkg" (fun _ ->
    NuGet (fun p -> 
        ensureDirectory "nupkg"
        { p with   
            Authors = authors
            Project = project + ".Core"
            Summary = summary
            Description = description
            Version = release.NugetVersion
            ReleaseNotes = String.Join(Environment.NewLine, release.Notes)
            Tags = tags
            OutputPath = "nupkg"
            Publish = false
            Dependencies = [ "FSharp.Compiler.Service", GetPackageVersion "packages" "FSharp.Compiler.Service" ] })
        (project + ".Core.nuspec")
)

// --------------------------------------------------------------------------------------
// Generate the documentation

Target "GenerateDocs" (fun _ ->
    executeFSIWithArgs "docs/tools" "generate.fsx" ["--define:RELEASE"] [] |> ignore
)

// --------------------------------------------------------------------------------------
// Release Scripts

Target "ReleaseDocs" (fun _ ->
    let tempDocsDir = "temp/gh-pages"
    CleanDir tempDocsDir
    Repository.cloneSingleBranch "" cloneUrl "gh-pages" tempDocsDir

    fullclean tempDocsDir
    CopyRecursive "docs/output" tempDocsDir true |> tracefn "%A"
    StageAll tempDocsDir
    Git.Commit.Commit tempDocsDir (sprintf "[skip ci] Update generated documentation for version %s" release.NugetVersion)
    Branches.push tempDocsDir
)

#load "paket-files/build/fsharp/FAKE/modules/Octokit/Octokit.fsx"
open Octokit

let readString prompt echo : string =
    let rec loop cs =
        let key = Console.ReadKey(not echo)
        match key.Key with
        | ConsoleKey.Backspace -> 
            match cs with [] -> loop [] | _::cs -> loop cs
        | ConsoleKey.Enter -> cs
        | _ -> loop (key.KeyChar :: cs)

    printf "%s" prompt
    let input =
        loop []
        |> List.rev
        |> Array.ofList
        |> fun cs -> String cs
    if not echo then printfn ""
    input

#r @"packages/build/Selenium.WebDriver/lib/net40/WebDriver.dll"
#r @"packages/build/canopy/lib/canopy.dll"

open canopy

Target "UploadToGallery" (fun _ ->
    canopy.configuration.chromeDir <- @"./packages/build/Selenium.WebDriver.ChromeDriver/driver"
    start chrome
    let vsixGuid = "136b942e-9f2c-4c0b-8bac-86d774189cff"
    let galleryUrl = sprintf "https://visualstudiogallery.msdn.microsoft.com/%s/edit?newSession=True" vsixGuid

    let username,password =
        let lines = File.ReadAllLines "gallerycredentials.txt"
        lines.[0],lines.[1]

    // log in to msdn
    url galleryUrl    
    "#i0116" << username
    "#i0118" << password
    click "#idSIButton9"
    sleep 5
    // start a new upload session - via hacky form link
    js (sprintf "$('form[action=\"/%s/edit/changeContributionUpload\"]').submit();" vsixGuid) |> ignore

    // select "upload the vsix"    
    let fi = System.IO.FileInfo "bin/FSharpVSPowerTools.vsix"
    
    ".uploadFileInput" << fi.FullName 
    click "#setContributionTypeButton"  
    sleep 15
    click "#uploadButton"
    sleep 15
    quit ()
)

Target "Release" (fun _ ->
    StageAll ""
    Git.Commit.Commit "" (sprintf "Bump version to %s" release.NugetVersion)
    Branches.push ""

    Branches.tag "" release.NugetVersion
    Branches.pushTag "" "origin" release.NugetVersion

    let user = readString "Username: " true
    let pw = readString "Password: " false

    // release on github
    createClient user pw
    |> createDraft gitOwner gitName release.NugetVersion (release.SemVer.PreRelease <> None) release.Notes 
    |> uploadFile "./bin/FSharpVSPowerTools.vsix"
    |> releaseDraft
    |> Async.RunSynchronously
)

Target "ReleaseAll"  DoNothing

// --------------------------------------------------------------------------------------
// Run main targets by default. Invoke 'build <Target>' to override

Target "Main" DoNothing

Target "All" DoNothing

Target "TravisCI" (fun _ -> 
  [ "src/FSharp.Editing/FSharp.Editing.fsproj"
    "tests/FSharp.Editing.Tests/FSharp.Editing.Tests.fsproj"
  ]
  |> MSBuildRelease "" "Rebuild"
  |> ignore
  
  let additionalFiles = 
    ["./packages/FSharp.Core/lib/net40/FSharp.Core.sigdata";
     "./packages/FSharp.Core/lib/net40/FSharp.Core.optdata";
     "./packages/FSharp.Core/lib/net40/FSharp.Core.xml";]
  CopyTo "tests/FSharp.Editing.Tests/bin/Release" additionalFiles

  ["tests/FSharp.Editing.Tests/bin/Release/FSharp.Editing.Tests.dll"]
  |> NUnit3 (fun p ->
    let param =
        { p with
            ShadowCopy = false
            TimeOut = TimeSpan.FromMinutes 20.
            Framework = NUnit3Runtime.Mono40
            Domain = NUnit3DomainModel.MultipleDomainModel 
            Workers = Some 1
            ResultSpecs = ["TestResults.xml"]      
        }
    if Environment.OSVersion.Platform = PlatformID.Win32NT then param else { param with Where = "cat != IgnoreOnUnix" }
  )
)

Target "EditToolRelease" DoNothing

"Clean"
  =?> ("BuildVersion", isAppVeyorBuild)
  ==> "AssemblyInfo"
  =?> ("VsixManifest", isAppVeyorBuild)
  ==> "Build"
  ==> "BuildTests"
  ==> "UnitTests"
  <=> "IntegrationTests"
  ==> "Main"

"Clean"
  ==> "RunStatistics"

"Clean"
  ==> "AssemblyInfo"
  ==> "TravisCI"

"UnitTests"
  ==> "BuildNupkg"
  ==> "EditToolRelease"




"Release"
  ==> "UploadToGallery"
  ?=> "BuildNupkg"
  ==> "ReleaseAll"

"Main"
  ==> "All"

"Main" 
  ==> "CleanDocs"
  ==> "GenerateDocs"
  ==> "ReleaseDocs"
  ==> "Release"

//RunTargetOrDefault "Main"
RunTargetOrDefault "EditToolRelease"
