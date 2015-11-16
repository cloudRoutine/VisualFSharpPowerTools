﻿module FSharpVSPowerTools.ImplementInterfaceSmartTaggerProvider

open System
open System.ComponentModel.Composition
open Microsoft.VisualStudio.Text
open Microsoft.VisualStudio.Text.Editor
open Microsoft.VisualStudio.Text.Operations
open Microsoft.VisualStudio.Text.Tagging
open Microsoft.VisualStudio.Utilities
open Microsoft.VisualStudio.Shell
open FSharpVSPowerTools.Refactoring
open FSharpVSPowerTools.ProjectSystem
open System.Diagnostics
open Microsoft.VisualStudio.Shell.Interop

//[<Export (typeof<IViewTaggerProvider>)>]
//[<ContentType "F#">]
//[<TagType(typeof<ImplementInterfaceSmartTag>)>]
//type  ImplementInterfaceSmartTaggerProvider[<ImportingConstructor>]
//    ( [<Import(typeof<SVsServiceProvider>)>] 
//        serviceProvider : IServiceProvider,
//        textDocumentFactoryService  :   ITextDocumentFactoryService     ,
//        textEditorFactoryService    :   ITextEditorFactoryService       ,
//        undoHistoryRegistry         :   ITextUndoHistoryRegistry        ,
//        editorOptionsFactory        :   IEditorOptionsFactoryService    ,
//        projectFactory              :   ProjectFactory                  ,
//        vsLanguageService           :   VSLanguageService               ) =
//
//    interface IViewTaggerProvider with
//        member __.CreateTagger (textView, buffer) =
//            maybe {
//                if textView.TextBuffer <> buffer then return! None else
//                let generalOptions = Setting.getGeneralOptions serviceProvider
//                let codeGenOptions = Setting.getCodeGenerationOptions serviceProvider
//                let dte = serviceProvider.GetService<EnvDTE.DTE,SDTE>()
//                if dte.Version = string VisualStudioVersion.VS2015 then return! None else
//                let! doc = textDocumentFactoryService.TryDocumentFromBuffer buffer
//                let implementInterface = 
//                    new ImplementInterface (doc, textView, editorOptionsFactory, undoHistoryRegistry.RegisterHistory buffer, 
//                        vsLanguageService, serviceProvider, projectFactory, Setting.getInterfaceMemberIdentifier codeGenOptions,
//                        Setting.getDefaultMemberBody codeGenOptions)
//                return
//                    new ImplementInterfaceSmartTagger (buffer, implementInterface) :> obj :?> _
//            } |> Option.getOrElse null
//
