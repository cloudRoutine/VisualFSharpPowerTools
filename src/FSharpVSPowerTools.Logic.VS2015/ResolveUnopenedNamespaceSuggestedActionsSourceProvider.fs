﻿namespace FSharpVSPowerTools.Logic.VS2015

open System.ComponentModel.Composition
open Microsoft.VisualStudio.Text
open Microsoft.VisualStudio.Text.Editor
open Microsoft.VisualStudio.Text.Operations
open Microsoft.VisualStudio.Utilities
open FSharpVSPowerTools.ProjectSystem
open FSharpVSPowerTools.Refactoring
open Microsoft.VisualStudio.Shell
open FSharpVSPowerTools
open Microsoft.VisualStudio.Language.Intellisense
open System
open System.Threading.Tasks
open Microsoft.VisualStudio.Imaging.Interop


[<AutoOpen>]
module Utils =

    type Package with
        static member GetService<'ifc> () = Package.GetGlobalService(typeof<'ifc>) :?> 'ifc
        static member GetService<'svs,'ivs> () = Package.GetGlobalService(typeof<'svs>) :?> 'ivs

        static member TryGetService<'ifc>() = 
            match Package.GetGlobalService(typeof<'ifc>) with
            | null -> None
            | svc -> svc :?> 'ifc |> Some

        static member TryGetService<'svs,'ivs>() = 
            match Package.GetGlobalService(typeof<'svs>) with
            | null -> None
            | svc -> svc :?> 'ivs |> Some



[<Export(typeof<ISuggestedActionsSourceProvider>)>]
[<Name "Resolve Unopened Namespaces Suggested Actions">]
[<ContentType "F#">]
[<TextViewRole(PredefinedTextViewRoles.Editable)>]
type ResolveUnopenedNamespaceSuggestedActionsSourceProvider [<ImportingConstructor>] //() =
//    [<Import; DefaultValue>]
//    val mutable FSharpVsLanguageService: VSLanguageService
//
//    [<Import; DefaultValue>]
//    val mutable TextDocumentFactoryService: ITextDocumentFactoryService
//
//    [<Import(typeof<SVsServiceProvider>); DefaultValue>]
//    val mutable ServiceProvider: IServiceProvider
//
//    [<Import; DefaultValue>]
//    val mutable UndoHistoryRegistry: ITextUndoHistoryRegistry
//
//    [<Import; DefaultValue>]
//    val mutable ProjectFactory: ProjectFactory

   (FSharpVsLanguageService: VSLanguageService,
    TextDocumentFactoryService: ITextDocumentFactoryService,
    UndoHistoryRegistry: ITextUndoHistoryRegistry,
    ProjectFactory: ProjectFactory) =



    interface ISuggestedActionsSourceProvider with
        member x.CreateSuggestedActionsSource(textView: ITextView, buffer: ITextBuffer): ISuggestedActionsSource = 
            if textView.TextBuffer <> buffer then null
            else
                let generalOptions = Setting.getGeneralOptions () //x.ServiceProvider
                if generalOptions == null || not generalOptions.ResolveUnopenedNamespacesEnabled then null
                else
                    match TextDocumentFactoryService.TryGetTextDocument(buffer) with
                    | true, doc -> 
                        let resolver = 
                            new UnopenedNamespaceResolver(doc, textView, UndoHistoryRegistry.RegisterHistory(buffer),
                                                           FSharpVsLanguageService, ProjectFactory)
                    
                        new ResolveUnopenedNamespaceSuggestedActionsSource(resolver) :> _
                    | _ -> null

and ResolveUnopenedNamespaceSuggestedActionsSource (resolver: UnopenedNamespaceResolver) as self =
    let actionsChanged = Event<_,_>()
    do resolver.Updated.Add (fun _ -> actionsChanged.Trigger (self, EventArgs.Empty))
    interface ISuggestedActionsSource with
        member __.Dispose() = (resolver :> IDisposable).Dispose()
        member __.GetSuggestedActions (_requestedActionCategories, _range, _ct) = 
            match resolver.CurrentWord, resolver.Suggestions with
            | None, _
            | _, [] -> 
                Seq.empty
            | Some _, suggestions ->
                suggestions
                |> List.map (fun xs ->
                    xs
                    |> List.map (fun s ->
                         { new ISuggestedAction with
                               member __.DisplayText = s.Text
                               member __.Dispose() = ()
                               member __.GetActionSetsAsync _ct = Task.FromResult <| seq []
                               member __.GetPreviewAsync _ct = Task.FromResult null
                               member __.HasActionSets = false
                               member __.HasPreview = false
                               member __.IconAutomationText = null
                               member __.IconMoniker = //ImageMoniker(Guid=Guid "{ae27a6b0-e345-4288-96df-5eaf394ee369}", Id=90)  // Unchecked.defaultof<_> //KnownMonikers.IntellisenseLightBulb
                                   // if s.NeedsIcon then ImageMoniker(Guid=Guid "{ae27a6b0-e345-4288-96df-5eaf394ee369}", Id=90)
                                   Unchecked.defaultof<_>
                               member __.InputGestureText = null
                               member __.Invoke _ct = s.Invoke()
                               member __.TryGetTelemetryId _telemetryId = false })
                     |> fun xs -> SuggestedActionSet xs) :> _

        member __.HasSuggestedActionsAsync (_requestedCategories, _range, _ct) = 
            Task.FromResult (Option.isSome resolver.CurrentWord && resolver.Suggestions |> List.isEmpty |> not)

        [<CLIEvent>]
        member __.SuggestedActionsChanged: IEvent<EventHandler<EventArgs>, EventArgs> = actionsChanged.Publish
        member __.TryGetTelemetryId telemetryId = telemetryId <- Guid.Empty; false