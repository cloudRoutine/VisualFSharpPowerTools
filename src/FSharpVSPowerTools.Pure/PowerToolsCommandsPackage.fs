namespace FSharpVSPowerTools

open System                                    
open System.ComponentModel.Design              
open System.Runtime.InteropServices            
open Microsoft.VisualStudio                    
open Microsoft.VisualStudio.Shell              
open Microsoft.VisualStudio.Shell.Interop      
open EnvDTE                                    
open EnvDTE80                                  
open FSharpVSPowerTools.Navigation             
open FSharpVSPowerTools.Folders                
open FSharpVSPowerTools.ProjectSystem          
open FSharpVSPowerTools.TaskList               
open System.ComponentModel.Composition         
open System.Diagnostics                        
open Microsoft.VisualStudio.ComponentModelHost 
open FSharpVSPowerTools.Reference              



[<PackageRegistration(UseManagedResourcesOnly = true)>]
[<ProvideMenuResource(resourceID= "Menus.ctmenu", version=1)>]
[<InstalledProductRegistration("#110", "#112", AssemblyVersionInformation.Version, IconResourceID = 400)>]
[<ProvideBindingPath>]
//[<ProvideOptionPage(typeof<GeneralOptionsPage>, Resource.vsPackageTitle, "General", categoryResourceID= 0, pageNameResourceID= 0, supportsAutomation= true, keywordListResourceId= 0)>]
//[<ProvideOptionPage(typeof<FantomasOptionsPage>, Resource.vsPackageTitle, "Formatting", categoryResourceID= 0, pageNameResourceID= 0, supportsAutomation= true, keywordListResourceId= 0)>]
//[<ProvideOptionPage(typeof<CodeGenerationOptionsPage>, Resource.vsPackageTitle, "Code Generation", categoryResourceID= 0, pageNameResourceID= 0, supportsAutomation= true, keywordListResourceId= 0)>]
//[<ProvideOptionPage(typeof<GlobalOptionsPage>, Resource.vsPackageTitle, "Configuration", categoryResourceID= 0, pageNameResourceID= 0, supportsAutomation= true, keywordListResourceId= 0)>]
//[<ProvideOptionPage(typeof<Linting.LintOptionsPage>, Resource.vsPackageTitle, "Lint", categoryResourceID= 0, pageNameResourceID= 0, supportsAutomation= true, keywordListResourceId= 0)>]
//[<ProvideOptionPage(typeof<OutliningOptionsPage>, Resource.vsPackageTitle, "Outlining", categoryResourceID= 0, pageNameResourceID= 0, supportsAutomation= true, keywordListResourceId= 0)>]
[< ProvideService (typeof<IGeneralOptions>)>]   
[< ProvideService (typeof<IFormattingOptions>)>]
[< ProvideService (typeof<ICodeGenerationOptions>)>]
[< ProvideService (typeof<IGlobalOptions>)>]
[< ProvideService (typeof<ILintOptions>)>]
[< Guid "1F699E38-7D87-44F4-BC08-6B1DD5A6F926">]
[< ProvideAutoLoad (VSConstants.UICONTEXT.SolutionExists_string)>]
[< ProvideAutoLoad (VSConstants.UICONTEXT.FSharpProject_string)>]
[< ProvideAutoLoad (VSConstants.UICONTEXT.NoSolution_string)>]
type PowerToolsCommandsPackage () as self =
    inherit Package()

    let mutable  pctCookie = 0u
    let mutable  objectManagerCookie = 0u
    let mutable  library = Unchecked.defaultof<FSharpLibrary>

    static let DTE = 
        Lazy<DTE2> (fun () -> ServiceProvider.GlobalProvider.GetService<DTE2,DTE> ())

    member __.GetDialogPage<'a>() = base.GetDialogPage(typeof<'a>)
    
    member __.GetService<'IVs,'SVs>() = self.GetService(typeof<'SVs>) :?> 'IVs

    override __.Initialize () =
        base.Initialize ()
        VSUtils.ForegroundThreadGuard.BindThread ()
        
        let serviceContainer = self :> IServiceContainer
//        serviceContainer.AddService<IGeneralOptions> 
//            (self.GetDialogPage<IGeneralOptions>()
//            
//            )
//        
        //let generalOptions 
        library <- FSharpLibrary Constants.guidSymbolLibrary
        library.LibraryCapabilities <-  Enum.Parse( typedefof<_LIB_FLAGS2>, _LIB_FLAGS.LF_PROJECT.ToString()) :?> _LIB_FLAGS2
       // library.LibraryCapabilities <-   asEnum<_LIB_FLAGS2>  _LIB_FLAGS.LF_PROJECT

        self.RegisterLibrary ()




    member private __.RegisterLibrary () =
        if objectManagerCookie = 0u then
            let objManager = self.TryGetService<IVsObjectManager2, SVsObjectManager>()
            match  objManager with
            | None -> () 
            | Some objManager -> ErrorHandler.ThrowOnFailure 
                                    (objManager.RegisterSimpleLibrary (library, &objectManagerCookie)) |> ignore

    member private __.UnregisterLibrary () =
        if objectManagerCookie <> 0u then
            let objManager = self.TryGetService<IVsObjectManager2, SVsObjectManager>()
            match  objManager with
            | None -> () 
            | Some objManager -> ErrorHandler.ThrowOnFailure 
                                    (objManager.RegisterSimpleLibrary (library, &objectManagerCookie)) |> ignore

    interface IDisposable with
        member x.Dispose(): unit = 
        //    UnregisterPriorityCommandTarget();
            self.UnregisterLibrary()
//            if (taskListCommentManager != null)
//                (taskListCommentManager as IDisposable).Dispose();
//            if (fsiReferenceMenu != null)
//                (fsiReferenceMenu as IDisposable).Dispose();
//        }
//

