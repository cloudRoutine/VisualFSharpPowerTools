﻿namespace FSharpVSPowerTools.Reference

open FSharpVSPowerTools
open EnvDTE80
open Microsoft.VisualStudio.Shell
open System
open EnvDTE
open FSharpVSPowerTools.ProjectSystem
open System.IO
open VSLangProj
open System.ComponentModel.Design
open System.Text
open System.Runtime.Versioning

type FsiReferenceCommand(dte2: DTE2, mcs: OleMenuCommandService) =
    static let scriptFolderName = "Scripts"
    static let header = 
        String.Join(Environment.NewLine, 
                    "// Warning: generated file; your changes could be lost when a new file is generated.",
                    "#I __SOURCE_DIRECTORY__")
    
    let containsReferenceScript (project: Project) = 
        project.ProjectItems.Item(scriptFolderName) 
        |> Option.ofNull
        |> Option.bind (fun scriptItem -> Option.ofNull scriptItem.ProjectItems)
        |> Option.map (fun projectItems -> 
            projectItems
            |> Seq.cast<ProjectItem>
            |> Seq.exists (fun item -> item.Name.Contains("load-references")))
        |> Option.getOrElse false

    let getActiveProject() =
        let dte = dte2 :?> DTE
        dte.ActiveSolutionProjects :?> obj []
        |> Seq.tryHead
        |> Option.map (fun o -> o :?> Project)

    let getProjectFolder(project: Project) =
        project.Properties.Item("FullPath").Value.ToString()

    let getFullFilePathInScriptFolder project fileName = 
        let scriptFolder = getProjectFolder project </> scriptFolderName
        Directory.CreateDirectory scriptFolder |> ignore
        scriptFolder </> fileName

    let addFileToActiveProject(project: Project, fileName: string, content: string) = 
        if isFSharpProject project then
            let textFile = getFullFilePathInScriptFolder project fileName
            if not (File.Exists textFile) || File.ReadAllText textFile <> content then
                use writer = File.CreateText textFile
                writer.Write content 

            let isProjectDirty = ref false
            let projectFolderScript = 
                project.ProjectItems.Item scriptFolderName
                |> Option.ofNull
                |> Option.getOrTry (fun _ ->
                    isProjectDirty := true
                    project.ProjectItems.AddFolder scriptFolderName)
            projectFolderScript.ProjectItems.Item fileName
            |> Option.ofNull
            |> function
               | None ->
                   isProjectDirty := true
                   projectFolderScript.ProjectItems.AddFromFile textFile |> ignore
               | _ -> ()
            if !isProjectDirty then project.Save()

    let getRelativePath (folder: string) (file: string) =
        let fileUri = Uri file
        // Folders must end in a slash
        let folder =
            if not <| folder.EndsWith (string Path.DirectorySeparatorChar) then
                folder + string Path.DirectorySeparatorChar
            else folder
        let folderUri = Uri folder
        Uri.UnescapeDataString(folderUri.MakeRelativeUri(fileUri).ToString())

    /// Remove the temporary attribute file name generated by F# compiler
    let filterTempAttributeFileName (project: Project) sourceFiles =
        Option.attempt (fun _ ->
            let targetFrameworkMoniker = project.Properties.Item("TargetFrameworkMoniker").Value.ToString()
            sprintf "%s.AssemblyAttributes.fs" targetFrameworkMoniker) 
        |> Option.map (fun tempAttributeFileName -> 
            sourceFiles |> Array.filter (fun fileName -> Path.GetFileNameSafe(fileName) <> tempAttributeFileName))
        |> Option.getOrElse sourceFiles

    let generateLoadScriptContent(project: Project, projectProvider: IProjectProvider, scriptFile: string) =
        let scriptFolder = getProjectFolder project </> scriptFolderName
        let sb = StringBuilder()
        sb.AppendLine(header) |> ignore
        sb.AppendLine(sprintf "#load \"%s\"" scriptFile) |> ignore
        match filterTempAttributeFileName project projectProvider.SourceFiles with
        | [||] -> ()
        | sourceFiles -> 
            sb.Append("#load ") |> ignore
            let relativePaths = sourceFiles |> Array.map (getRelativePath scriptFolder >> sprintf "\"%s\"")
            sb.AppendLine(String.Join(Environment.NewLine + new String(' ', "#load ".Length), relativePaths)) |> ignore
        sb.ToString()   

    let getReferenceAssembliesFolderByVersion (project: Project) =
        Option.attempt (fun _ ->
            let targetFrameworkMoniker = project.Properties.Item("TargetFrameworkMoniker").Value.ToString()
            let frameworkVersion = FrameworkName(targetFrameworkMoniker).Version.ToString()
            let programFiles = 
                match Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) with
                | null -> Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
                | s -> s 
            sprintf @"%s\Reference Assemblies\Microsoft\Framework\.NETFramework\v%s" programFiles frameworkVersion)

    let generateRefs(project: Project, projectProvider: IProjectProvider) =
        let blackList = set [| "FSharp.Core"; "mscorlib" |]
        let whiteList =
            project.Object
            |> tryCast<VSProject>
            |> Option.map (fun vsProject ->
                vsProject.References
                |> Seq.cast<Reference>
                |> Seq.map (fun reference -> reference.Name))
            |> Option.getOrElse Seq.empty
            |> Set.ofSeq

        let scriptFolder = getProjectFolder project </> scriptFolderName
        let referenceAssembliesFolder = getReferenceAssembliesFolderByVersion project

        // We use compiler options to ensure that assembly references are resolved in the right order.
        projectProvider.CompilerOptions
        |> Seq.choose (fun x -> if x.StartsWith("-r:") then Some (x.[3..].Trim(' ', '"')) else None)
        |> Seq.choose (fun assemblyPath ->    
            let assemblyName = Path.GetFileNameWithoutExtension(assemblyPath)        
            if not (blackList.Contains assemblyName) && whiteList.Contains assemblyName then
                let fullPath = Path.GetFullPathSafe(assemblyPath)
                if File.Exists fullPath then
                    let referenceFolder = Path.GetDirectoryName fullPath
                    match referenceAssembliesFolder with
                    | Some referenceAssembliesFolder ->
                        if String.Equals(Path.GetFullPathSafe referenceAssembliesFolder, 
                                         Path.GetFullPathSafe referenceFolder, 
                                         StringComparison.OrdinalIgnoreCase) then
                            Some (Path.GetFileNameSafe fullPath)
                        else
                            Some (getRelativePath scriptFolder fullPath)
                    | None -> 
                            Some (getRelativePath scriptFolder fullPath)
                else None
            else None)
        |> Seq.map (sprintf "#r \"%s\"") 
        |> Seq.toList

    let generateFileContent (refLines: #seq<string>) =
        String.Join(Environment.NewLine, header, String.Join(Environment.NewLine, refLines))

    let getExistingFileRefs project fileName = 
        let filePath = getFullFilePathInScriptFolder project fileName  
        if File.Exists filePath then
            File.ReadLines filePath
            |> Seq.filter (fun (line: string) -> line.StartsWith "#r")
            |> Seq.toList
        else []

    let mergeRefs existing actual =
        // remove refs which are not actual anymore (they have been removed from the project)
        let existing =
            existing |> List.filter (fun existingRef -> List.exists ((=) existingRef) actual)
        // get refs which don't exist in the existing file
        let newExtraRefs =
            actual |> List.filter (fun actualRef -> not <| List.exists ((=) actualRef) existing)
        // concatenate old survived refs and the extra ones
        existing @ newExtraRefs

    let rec createProjectProvider project =
        let getProjectProvider project =
            Some (createProjectProvider project :> IProjectProvider)
        new ProjectProvider(project, getProjectProvider, (fun _ -> ()), id)

    let generateReferenceFiles (project: Project) =
        maybe {
            let! project = Option.ofNull project
            let! currentConfiguration = 
                try Some (project.ConfigurationManager.ActiveConfiguration.ConfigurationName.ToLower())
                with e -> 
                    Logging.logError (fun _ -> sprintf "[FsiReference] Cannot get current configuration name: %O" e)
                    None
            let loadRefsFileName = sprintf "load-references-%s.fsx" currentConfiguration
            use projectProvider = createProjectProvider project
            let actualRefs = generateRefs(project, projectProvider)

            let refs = 
                let existingRefs = getExistingFileRefs project loadRefsFileName
                mergeRefs existingRefs actualRefs

            addFileToActiveProject(project, loadRefsFileName, generateFileContent refs)
            let content = generateLoadScriptContent(project, projectProvider, loadRefsFileName)
            addFileToActiveProject(project, sprintf "load-project-%s.fsx" currentConfiguration, content) }
        |> ignore

    let generateFsiReferences() =
        getActiveProject()
        |> Option.iter (fun project ->
            // Generate script files
            if isFSharpProject project then
                generateReferenceFiles project)

    let onBuildDoneHandler = EnvDTE._dispBuildEvents_OnBuildDoneEventHandler (fun _ _ ->
            Logging.logInfo (fun _ -> "Checking projects after build done...")
            let dte = dte2 :?> DTE
            listFSharpProjectsInSolution dte
            |> Seq.iter (fun project ->
                if containsReferenceScript project then
                    Logging.logInfo (fun _ -> sprintf "Re-generating reference scripts for '%s'..." project.Name)
                    generateReferenceFiles project))

    let events = dte2.Events.BuildEvents
    do events.add_OnBuildDone onBuildDoneHandler

    member __.SetupCommands() =
        let menuCommandID = CommandID(Constants.guidGenerateReferencesForFsiCmdSet, int Constants.cmdidGenerateReferencesForFsi)
        let menuCommand = OleMenuCommand((fun _ _ -> generateFsiReferences()), menuCommandID)
        menuCommand.BeforeQueryStatus.Add (fun _ -> 
            let visibility = getActiveProject() |> Option.map isFSharpProject |> Option.getOrElse false
            menuCommand.Visible <- visibility)
        mcs.AddCommand(menuCommand)

    interface IDisposable with
        member __.Dispose() = 
            events.remove_OnBuildDone onBuildDoneHandler
        
