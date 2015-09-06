﻿namespace FSharpVSPowerTools.Linting

open FSharpVSPowerTools
open Microsoft.VisualStudio.Shell
open System.ComponentModel
open System.IO
open System.Runtime.InteropServices
open System.Windows
open System.ComponentModel.Composition
open Microsoft.VisualStudio.OLE.Interop
open System.Collections.Generic
open FSharpLint.Framework.Configuration
open Management
open LintUtils

[<Guid("f0bb4785-e75a-485f-86e8-e382dd5934a4")>]
type LintOptionsPage() =
    inherit UIElementDialogPage()

    let mutable loadedConfigs = LoadedConfigs.Empty

    let lintOptionsPageControl = lazy LintOptionsControlProvider()

    interface ILintOptions with
        member this.UpdateDirectories(_) =
            ()

        member this.GetConfigurationForDirectory(dir) =
            getConfigForDirectory loadedConfigs dir
            
    override this.OnApply(_) = 
        // TODO: Save updates to configuration
        ()

    override this.OnActivate(_) = 
        loadedConfigs <- updateLoadedConfigs loadedConfigs

        let lintOptions =
            match getInitialPath loadedConfigs with
            | Some(path) -> 
                OptionsViewModel(
                    getConfigForDirectory loadedConfigs,
                    getFileHierarchy loadedConfigs, 
                    [], 
                    path) |> Some
            | None -> 
                None

        lintOptionsPageControl.Value.DataContext <- LintViewModel(lintOptions)
            
    override this.Child = 
        let control = lintOptionsPageControl.Value
        control :> UIElement